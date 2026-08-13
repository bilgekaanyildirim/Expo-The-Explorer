#!/usr/bin/env python3
"""session_context.py — SessionStart hook: map health, injected once per session.

The skill tells Claude to trust the maps. This is what makes that trust
checkable: at session start it states, in facts and not in orders, which maps
are degraded and which are older than the files they describe. Roughly 1 KB of
context buys back every scan that a silently stale map would have caused.

SessionStart fires on `startup`, `resume`, `clear`, `compact` and `fork`, so the
report is refreshed after a compaction too — the health facts never go stale in
context while the conversation runs.

Output is JSON with hookSpecificOutput.additionalContext (documented for
SessionStart). It is capped well under the 10,000-character hook limit, and it
is written as statements — a hook that issues instructions reads as injected
prompt text, which is not what this is for.

It also publishes `watchPaths`: the scenes, prefabs and .asset files whose
change outside this session is what makes unitymap/assetmap lie. Claude Code
then fires FileChanged for them, and mark_map_stale.py records the drift.

build_report() is shared with subagent_context.py so the two hooks cannot drift
apart in what they claim about the same project.

Usage: python3 session_context.py [project_root]   (reads the hook payload on stdin)
"""
import json
import os
import re
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))

MAX_CHARS = 2000

# A Unity project can hold thousands of prefabs; watching all of them would cost
# more than the drift it detects. The cap is reported, never applied silently.
MAX_WATCH_PATHS = 500
WATCH_SUFFIXES = (".unity", ".prefab", ".asset")


def project_root() -> str:
    args = [a for a in sys.argv[1:] if not a.startswith("--")]
    if args:
        return os.path.abspath(args[0])
    env = os.environ.get("CLAUDE_PROJECT_DIR")
    if env:
        return os.path.abspath(env)
    try:
        payload = json.loads(sys.stdin.read() or "{}")
        if payload.get("cwd"):
            return os.path.abspath(payload["cwd"])
    except Exception:
        pass
    return os.getcwd()


def first_line(path):
    try:
        with open(path, encoding="utf-8") as f:
            return f.readline().strip()
    except OSError:
        return ""


def watch_paths(root: str):
    """(absolute paths to watch, how many were left out by the cap)."""
    try:
        from unityparse import walk_assets   # already skips Library/Temp/obj/Logs
        found = sorted(walk_assets(root, WATCH_SUFFIXES))
    except Exception:
        return [], 0
    if len(found) <= MAX_WATCH_PATHS:
        return found, 0
    return found[:MAX_WATCH_PATHS], len(found) - MAX_WATCH_PATHS


def build_report(root: str):
    """Map-health facts as a list of lines, or None when this is not a unity-dev project."""
    cdir = os.path.join(root, ".claude")
    if not os.path.isdir(cdir):
        return None

    out = []

    # codemaps
    cm = []
    for fn in sorted(os.listdir(cdir)):
        if fn.startswith("codemap-") and fn.endswith(".md"):
            stamp = first_line(os.path.join(cdir, fn))
            m = re.search(r"status:\s*(.*?)\s*-->", stamp)
            status = m.group(1) if m else "no status field (pre-v2 stamp)"
            n = sum(1 for l in open(os.path.join(cdir, fn), encoding="utf-8")
                    if "|" in l and not l.startswith(("<!--", "#")))
            cm.append(f"  - {fn[:-3]}: {status} ({n} lines)")
    out += ["- codemaps:"] + (cm or ["  - none built yet"])

    # index
    idx = os.path.join(cdir, "index.md")
    if os.path.isfile(idx):
        s = first_line(idx)
        m = re.search(r"systems:(\d+)\s+unmapped:(\d+)\s+unassigned-files:(\d+)", s)
        out.append(f"- index.md: {m.group(1)} systems, {m.group(2)} unmapped, "
                   f"{m.group(3)} files with sys: ?" if m else "- index.md: present")
    else:
        out.append("- index.md: absent — locate.md step 1 has nothing to read")

    # unitymap freshness measured against the files it describes
    um = os.path.join(cdir, "unitymap.md")
    if os.path.isfile(um):
        stamp = first_line(um)
        m = re.search(r"source-sig:(\w+)", stamp)
        try:
            from build_unitymap import source_sig
            from unityparse import walk_assets
            cur = source_sig(sorted(walk_assets(root, (".unity", ".prefab"))))
            fresh = "matches the scenes/prefabs on disk" if m and m.group(1) == cur \
                else "does NOT match the scenes/prefabs on disk (regeneration would change it)"
        except Exception:
            fresh = "freshness not computed"
        extra = re.search(r"status:\s*(.*?)\s*-->", stamp)
        out.append(f"- unitymap.md: {fresh}" + (f"; {extra.group(1)}" if extra else ""))
    else:
        out.append("- unitymap.md: absent — scene/prefab questions would cost a raw YAML read")

    am = os.path.join(cdir, "assetmap.md")
    if os.path.isfile(am):
        s = first_line(am)
        counts = re.findall(r"(assets|prefabs|scenes|asmdefs):(\d+)", s)
        out.append("- assetmap.md: " + (", ".join(f"{n} {k}" for k, n in counts) or "present"))
    else:
        out.append("- assetmap.md: absent — data-source.md has no asset inventory to read")

    # blueprint consistency
    try:
        from check_blueprint import main as bp_main
        import io
        import contextlib
        buf = io.StringIO()
        argv = sys.argv
        sys.argv = ["check_blueprint.py", root]
        with contextlib.redirect_stdout(buf):
            bp_main()
        sys.argv = argv
        tail = [l for l in buf.getvalue().splitlines() if "error(s)" in l]
        errs = [l.replace("[blueprint] ", "") for l in buf.getvalue().splitlines()
                if l.startswith("[blueprint] ERROR")][:3]
        out.append("- blueprint check: " + (tail[0].replace("[blueprint] ", "") if tail else "not run"))
        out += [f"  - {e}" for e in errs]
    except Exception:
        out.append("- blueprint check: not run")

    # enforcement state
    if os.path.isfile(os.path.join(cdir, "unity-dev.json")):
        pre = os.path.join(cdir, "preflight")
        cur = os.path.isfile(os.path.join(pre, "current.md"))
        apr = os.path.isfile(os.path.join(pre, "approved"))
        out.append(f"- preflight: current.md {'present' if cur else 'absent'}, "
                   f"approval token {'present' if apr else 'absent'} "
                   f"(approval is session-scoped and is re-checked by the guard)")
    else:
        out.append("- enforcement marker .claude/unity-dev.json is absent: the PreToolUse "
                   "guard treats this checkout as an ordinary project and allows protected "
                   "writes. Running init_project.py restores it.")

    # editor-side drift recorded since the last map refresh
    drift = os.path.join(cdir, "map-drift")
    if os.path.isfile(drift):
        try:
            with open(drift, encoding="utf-8") as f:
                n = sum(1 for _ in f)
            out.append(f"- map-drift: {n} scene/prefab/asset change(s) recorded outside a "
                       f"map refresh; unitymap.md and assetmap.md may not reflect them")
        except OSError:
            pass

    return out


def emit(lines, event, extra=None, limit=MAX_CHARS):
    text = "\n".join(lines)
    if len(text) > limit:
        text = text[:limit - 3] + "..."
    payload = {"hookEventName": event, "additionalContext": text}
    if extra:
        payload.update(extra)
    print(json.dumps({"hookSpecificOutput": payload}))


def main() -> int:
    root = project_root()
    report = build_report(root)
    if report is None:
        return 0   # not a unity-dev project; stay silent

    watch, dropped = watch_paths(root)
    if dropped:
        report.append(f"- watching {len(watch)} of {len(watch) + dropped} scene/prefab/asset "
                      f"files for editor-side changes; {dropped} are not watched (cap)")

    header = ["unity-dev map health at session start (facts, not instructions):"]
    emit(header + report, "SessionStart", {"watchPaths": watch} if watch else None)
    return 0


if __name__ == "__main__":
    sys.exit(main())
