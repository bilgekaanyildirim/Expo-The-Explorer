#!/usr/bin/env python3
"""init_project.py — installs the project skeleton from the templates.

Installs:
  <project>/CLAUDE.md              (if absent; left untouched if present)
  <project>/.gitignore             (if absent; if present, unity-dev lines are appended)
  <project>/.claude/rules/{ui,gameplay,data}.md   (if absent)
  <project>/.claude/shards.json    (if absent — the single source of the path->shard map)
  <project>/.claude/settings.json  (if absent; if present, settings.json.unity-dev.new is written — merge by hand)
  <project>/.claude/unity-dev.json (the guard's enforcement marker — COMMIT THIS)
  <project>/.claude/preflight/     (empty dir — runtime approval state, gitignored)
  <project>/.claude/agents/Explore.md  (if absent — overrides the built-in Explore)
  <project>/.claude/hooks/         (hook + map-builder scripts, always refreshed, +x)
  <project>/.claude/templates/Editor/UnityMapExporter.cs   (staged, NOT compiled)
  <project>/.claude/{scope.md, fingerprint.md, blueprint.md, decisions.md}  (if absent)

Platform: bash, python3 and a sha256 tool are hard requirements, checked before
anything is written. A hook command that cannot execute is a non-blocking error
in Claude Code — the tool call proceeds — so a missing shell does not degrade
the gate, it removes it. macOS, Linux, or Windows via WSL.

settings.json is the PRIMARY and ONLY hook registration point (SessionStart +
PreToolUse + PostToolUse + UserPromptSubmit + Stop): settings hooks run in
every session and inside subagents, unlike SKILL.md frontmatter hooks, which
run only while the skill is active. Project-settings hooks require accepting
the workspace trust dialog once.

The Editor exporter is STAGED, not installed into Assets/: it is a .cs file, so
putting it under Assets/ is a protected write that belongs in a preflight
manifest like any other script.

Usage: python3 init_project.py <project_root>
After installation: verify with /hooks and /memory, then run
scripts/test_enforcement.sh; do not start the first task before it passes.
"""
import os
import shutil
import stat
import subprocess
import sys

SKILL_DIR = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
TPL = os.path.join(SKILL_DIR, "templates")
SCRIPTS = os.path.join(SKILL_DIR, "scripts")

# Copied into .claude/hooks/ on every run so an upgraded skill upgrades the project.
HOOK_FILES = (
    "preflight_guard.sh", "preflight_approve.sh", "codemap_guard.sh",
    "map_drift_notice.sh", "mark_map_stale.py",
    "shards.py", "maps.py", "unityparse.py",
    "build_codemap.py", "build_unitymap.py", "build_assetmap.py", "build_index.py",
    "check_blueprint.py", "session_context.py", "subagent_context.py", "refresh_maps.py",
)

DECISIONS_HEADER = """# decisions — architectural decision record (ADR)

<!-- One line per decision. `affects:` is not optional: a decision nobody can
     trace to code is a decision nobody can revisit. check_blueprint.py and the
     postflight both read it.

     D-001: <decision> — <rationale> — affects: <system(s) / path(s)> — <date>
-->
"""


MIN_CLAUDE_VERSION = (2, 1, 195)   # hyphen exact-match in hook matchers landed here


def check_platform() -> list:
    """Hard requirements. Returns a list of failures; empty means good to go.

    These are checked BEFORE anything is installed. A half-installed project is
    worse than an uninstalled one: settings.json would register hooks whose
    commands cannot run, and Claude Code treats an unrunnable hook command as a
    non-blocking error — the tool call goes through. The gate would be visible
    in /hooks and absent in fact.
    """
    problems = []
    if not shutil.which("bash"):
        problems.append("bash not found. unity-dev's PreToolUse gate is a bash script. "
                        "On Windows use WSL, or install Git Bash and put it on PATH.")
    if not shutil.which("python3"):
        problems.append("python3 not found on PATH. The map builders and the SessionStart "
                        "hook are invoked as `python3`. On Windows, `python3` usually does "
                        "not exist even when Python does — use WSL.")
    if not (shutil.which("sha256sum") or shutil.which("shasum")):
        problems.append("neither sha256sum nor shasum found. The approval token is a "
                        "sha256 of the preflight; without one the gate denies every write.")
    return problems


def claude_version() -> tuple:
    """(major, minor, patch) of the installed CLI, or () if it cannot be read."""
    try:
        out = subprocess.run(["claude", "--version"], capture_output=True, text=True,
                             timeout=15).stdout
    except Exception:
        return ()
    digits = ""
    for ch in out:
        if ch.isdigit() or ch == ".":
            digits += ch
        elif digits:
            break
    parts = [p for p in digits.split(".") if p != ""]
    try:
        return tuple(int(p) for p in parts[:3])
    except ValueError:
        return ()


def hooks_disabled_in() -> list:
    """Settings files that switch enforcement off wholesale.

    disableAllHooks respects the managed hierarchy, so a value in user, project
    or local settings cannot disable MANAGED hooks — but unity-dev's hooks are
    project hooks, and any of those three scopes kills them. When they are off
    no hook can report that they are off, which is why this is checked here and
    asserted by test_enforcement.sh rather than reported at runtime.
    """
    import json
    hit = []
    candidates = [
        os.path.expanduser("~/.claude/settings.json"),
        os.path.join(ROOT_HINT[0], ".claude", "settings.json") if ROOT_HINT else "",
        os.path.join(ROOT_HINT[0], ".claude", "settings.local.json") if ROOT_HINT else "",
    ]
    for path in candidates:
        if not path or not os.path.isfile(path):
            continue
        try:
            with open(path, encoding="utf-8") as f:
                if json.load(f).get("disableAllHooks") is True:
                    hit.append(path)
        except Exception:
            continue
    return hit


ROOT_HINT: list = []


def copy_if_absent(src: str, dst: str, log: list) -> None:
    if os.path.exists(dst):
        log.append(f"  skipped (exists): {dst}")
        return
    os.makedirs(os.path.dirname(dst), exist_ok=True)
    shutil.copyfile(src, dst)
    log.append(f"  installed       : {dst}")


def main() -> int:
    if len(sys.argv) < 2:
        print("Usage: python3 init_project.py <project_root>")
        return 1

    problems = check_platform()
    if problems:
        print("unity-dev init_project ABORTED — unmet platform requirements:")
        for p in problems:
            print(f"  ERROR: {p}")
        print("\nNothing was installed. A project whose hooks cannot execute looks")
        print("enforced in /hooks and is not; refusing to create that state.")
        return 1

    root = os.path.abspath(sys.argv[1])
    os.makedirs(root, exist_ok=True)
    ROOT_HINT[:] = [root]
    log: list = []

    # 1) Root files
    copy_if_absent(os.path.join(TPL, "CLAUDE.md.template"), os.path.join(root, "CLAUDE.md"), log)

    gi = os.path.join(root, ".gitignore")
    with open(os.path.join(TPL, "gitignore.template"), encoding="utf-8") as f:
        gi_tpl = f.read()
    if not os.path.exists(gi):
        with open(gi, "w", encoding="utf-8") as f:
            f.write(gi_tpl)
        log.append(f"  installed       : {gi}")
    else:
        with open(gi, encoding="utf-8") as f:
            cur = f.read()
        if ".claude/preflight/" not in cur:
            with open(gi, "a", encoding="utf-8") as f:
                f.write("\n# unity-dev skill\n.claude/preflight/\n"
                        ".claude/settings.local.json\n.claude/hooks/__pycache__/\n")
            log.append(f"  appended        : {gi} (unity-dev state exclusions)")
        else:
            log.append(f"  skipped (exists): {gi}")

    # 2) .claude skeleton
    cd = os.path.join(root, ".claude")
    for name in ("ui.md", "gameplay.md", "data.md"):
        copy_if_absent(os.path.join(TPL, "rules", name), os.path.join(cd, "rules", name), log)
    copy_if_absent(os.path.join(TPL, "scope.template.md"), os.path.join(cd, "scope.md"), log)
    copy_if_absent(os.path.join(TPL, "fingerprint.template.md"), os.path.join(cd, "fingerprint.md"), log)
    copy_if_absent(os.path.join(TPL, "blueprint.template.md"), os.path.join(cd, "blueprint.md"), log)
    copy_if_absent(os.path.join(TPL, "shards.json.template"), os.path.join(cd, "shards.json"), log)

    dec = os.path.join(cd, "decisions.md")
    if not os.path.exists(dec):
        os.makedirs(cd, exist_ok=True)
        with open(dec, "w", encoding="utf-8") as f:
            f.write(DECISIONS_HEADER)
        log.append(f"  installed       : {dec}")

    # The enforcement marker is a COMMITTED file, not the gitignored preflight
    # directory: the marker has to survive a clone, or the gate arrives disarmed.
    copy_if_absent(os.path.join(TPL, "unity-dev.json.template"),
                   os.path.join(cd, "unity-dev.json"), log)

    os.makedirs(os.path.join(cd, "preflight"), exist_ok=True)
    log.append(f"  installed       : {os.path.join(cd, 'preflight')}/ (runtime approval state)")

    # Overrides the built-in Explore agent, which skips CLAUDE.md and would
    # otherwise answer "where is X" with a repo-wide scan. Must be a project
    # agent: a plugin ships it as `unity-dev:Explore`, which does not override.
    copy_if_absent(os.path.join(TPL, "agents", "Explore.md"),
                   os.path.join(cd, "agents", "Explore.md"), log)

    # 3) Hook + map-builder scripts (+x). settings.json's hook commands point here.
    hooks = os.path.join(cd, "hooks")
    os.makedirs(hooks, exist_ok=True)
    for name in HOOK_FILES:
        dst = os.path.join(hooks, name)
        shutil.copyfile(os.path.join(SCRIPTS, name), dst)
        os.chmod(dst, os.stat(dst).st_mode | stat.S_IXUSR | stat.S_IXGRP | stat.S_IXOTH)
    log.append(f"  installed       : {hooks}/ ({len(HOOK_FILES)} scripts, +x, refreshed on every run)")

    # 4) Editor exporter — STAGED only; installing it under Assets/ is a preflight write.
    staged = os.path.join(cd, "templates", "Editor", "UnityMapExporter.cs")
    os.makedirs(os.path.dirname(staged), exist_ok=True)
    shutil.copyfile(os.path.join(TPL, "Editor", "UnityMapExporter.cs"), staged)
    log.append(f"  staged          : {staged} (copy to Assets/Editor/ via a preflight manifest)")

    # 5) settings.json — never overwrite if present
    sj = os.path.join(cd, "settings.json")
    src = os.path.join(TPL, "settings.json.template")
    if not os.path.exists(sj):
        shutil.copyfile(src, sj)
        log.append(f"  installed       : {sj}")
    else:
        new = sj + ".unity-dev.new"
        shutil.copyfile(src, new)
        log.append(f"  WARNING         : {sj} already exists; template written as {new} — merge by hand.")

    # 6) First map pass so locate.md has something to read on task one
    try:
        subprocess.run([sys.executable, os.path.join(hooks, "refresh_maps.py"), root],
                       capture_output=True, text=True, timeout=120)
        log.append("  built           : first pass of codemap / unitymap / assetmap / index")
    except Exception as exc:
        log.append(f"  NOTE            : first map pass skipped ({type(exc).__name__})")

    # 7) Post-install warnings — conditions no hook can report on at runtime
    warn: list = []
    off = hooks_disabled_in()
    if off:
        warn.append("disableAllHooks is true in: " + ", ".join(off)
                    + " — every unity-dev hook is off, including the one that would "
                      "tell you so. Remove it before the first task.")
    ver = claude_version()
    if ver and ver < MIN_CLAUDE_VERSION:
        warn.append("Claude Code {} is older than the minimum {}; hook matchers with "
                    "hyphens fall back to unanchored regex on this version."
                    .format(".".join(map(str, ver)), ".".join(map(str, MIN_CLAUDE_VERSION))))

    print("unity-dev init_project done:")
    print("\n".join(log))
    for w in warn:
        print(f"  WARNING         : {w}")
    print("\nCommit .claude/unity-dev.json, .claude/settings.json, .claude/hooks/ and")
    print(".claude/agents/. The marker file is what arms the gate in a fresh clone.")
    print("\nNext steps (do not skip):")
    print("  1. Accept the workspace trust dialog, then verify all seven hook events")
    print("     with /hooks: SessionStart, PreToolUse x2, PostToolUse, UserPromptSubmit,")
    print("     SubagentStart, FileChanged, Stop")
    print("  2. Verify path-scoped rules loading with /memory, and confirm in a fresh")
    print("     session with /context that .claude/rules/*.md are NOT all preloaded")
    print("  3. Run scripts/test_enforcement.sh (guard + approve + map regression suite)")
    print("  4. Canary in-session: ask for a .cs without a preflight -> must be blocked")
    print("  5. Optional: install the Editor exporter by declaring")
    print("     Assets/Editor/UnityMapExporter.cs in a preflight manifest and copying")
    print("     .claude/templates/Editor/UnityMapExporter.cs into it with the Write tool")
    return 0


if __name__ == "__main__":
    sys.exit(main())
