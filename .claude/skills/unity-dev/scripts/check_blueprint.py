#!/usr/bin/env python3
"""check_blueprint.py — machine check of the architecture plan against reality.

The postflight used to ask the AI whether the blueprint was still in sync. A
self-declaration is not a mechanism; this script is. It reports four classes of
drift:

  (a) folder layout   — folders declared in blueprint.md vs the real Assets/ tree
  (b) scene & prefab  — inventories in blueprint.md vs the .unity/.prefab on disk
  (c) systems         — blueprint system lines vs the codemap `sys:` fields
  (d) arrows          — a dependency cycle between systems (arrows must be one-way)

Exit code: 0 when there is no ERROR, 1 when there is at least one. WARN and
INFO never fail the run — a not-yet-created folder is not a defect.

Usage: python3 check_blueprint.py [project_root] [--quiet]
Quoted verbatim by gates/postflight.md.
"""
import os
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from maps import find_cycles, read_blueprint, read_codemaps  # noqa: E402
from unityparse import walk_assets  # noqa: E402

IGNORED_TOP = {"Art", "Audio", "Plugins", "Settings", "TextMesh Pro", "TutorialInfo",
               "AddressableAssetsData", "XR", "Samples", "ThirdParty", "Editor Default Resources"}


def project_root() -> str:
    args = [a for a in sys.argv[1:] if not a.startswith("--")]
    if args:
        return os.path.abspath(args[0])
    return os.path.abspath(os.environ.get("CLAUDE_PROJECT_DIR", os.getcwd()))


def main() -> int:
    root = project_root()
    quiet = "--quiet" in sys.argv
    bp = read_blueprint(root)
    findings = []

    def add(level, text):
        findings.append((level, text))

    if not bp["exists"]:
        add("ERROR", "blueprint.md is missing — no architecture plan to check against.")
        report(findings, quiet)
        return 1

    # --- (a) folder layout ---------------------------------------------------
    declared = {d.rstrip("/") for d in bp["folders"] if not d.startswith("<")}
    declared_norm = {d if d.startswith("Assets/") else f"Assets/{d}"
                     for d in declared if d not in ("Assets",)}
    assets = os.path.join(root, "Assets")
    if os.path.isdir(assets):
        real_top = {f"Assets/{d}" for d in os.listdir(assets)
                    if os.path.isdir(os.path.join(assets, d)) and d not in IGNORED_TOP
                    and not d.startswith(".")}
        for d in sorted(real_top):
            if not any(x == d or x.startswith(d + "/") for x in declared_norm):
                add("WARN", f"folder on disk but not in the blueprint layout: {d}/")
        for d in sorted(declared_norm):
            if "<" in d or ">" in d:
                continue
            if not os.path.isdir(os.path.join(root, d)):
                add("INFO", f"folder declared in the blueprint, not created yet: {d}/")
    else:
        add("INFO", "no Assets/ directory yet — folder layout not checked.")

    # --- (b) scene & prefab inventories -------------------------------------
    def stem(p):
        return os.path.basename(p).rsplit(".", 1)[0]

    disk_scenes = {stem(p) for p in walk_assets(root, (".unity",))}
    disk_prefabs = {stem(p) for p in walk_assets(root, (".prefab",))}
    bp_scenes = {s["name"] for s in bp["scenes"]}
    bp_prefabs = {p["name"] for p in bp["prefabs"]}

    for n in sorted(disk_scenes - bp_scenes):
        add("ERROR", f"scene on disk with no line in the blueprint scene inventory: {n}.unity")
    for n in sorted(bp_scenes - disk_scenes):
        add("INFO", f"scene planned in the blueprint, not created yet: {n}.unity")
    for n in sorted(disk_prefabs - bp_prefabs):
        add("ERROR", f"prefab on disk with no line in the blueprint prefab inventory: {n}.prefab")
    for n in sorted(bp_prefabs - disk_prefabs):
        add("INFO", f"prefab planned in the blueprint, not created yet: {n}.prefab")

    # --- (c) systems vs codemap sys: ----------------------------------------
    codemaps = read_codemaps(root)
    sys_used, unassigned, flagged = {}, 0, 0
    for _shard, _stamp, lines in codemaps:
        for l in lines:
            if l.marker:
                flagged += 1
            if not l.sys or l.sys == "?":
                unassigned += 1
            else:
                sys_used[l.sys] = sys_used.get(l.sys, 0) + 1
    for name in sorted(set(sys_used) - set(bp["systems"])):
        add("ERROR", f"codemap `sys: {name}` has no system line in the blueprint "
                     f"({sys_used[name]} file(s))")
    for name in sorted(set(bp["systems"]) - set(sys_used)):
        add("WARN", f"blueprint system with no code behind it: {name}")
    if unassigned:
        add("WARN", f"{unassigned} codemap line(s) still carry `sys: ?`")
    if flagged:
        add("WARN", f"{flagged} codemap line(s) carry a STALE/ORPHAN/MOVED marker")

    for p in bp["prefabs"]:
        if p["system"] and p["system"] not in bp["systems"] and not p["system"].startswith("<"):
            add("WARN", f"prefab {p['name']} names owning system `{p['system']}`, "
                        f"which has no system line")

    # --- (d) one-directional arrows -----------------------------------------
    for cyc in find_cycles(bp["systems"]):
        add("ERROR", "dependency cycle between systems: " + " -> ".join(cyc))
    for name, meta in sorted(bp["systems"].items()):
        for d in meta["depends"]:
            if d not in bp["systems"]:
                add("WARN", f"system {name} depends on `{d}`, which has no system line")

    return report(findings, quiet)


def max_findings() -> int:
    """--max-findings N, default 40. 0 means no cap."""
    for i, a in enumerate(sys.argv):
        if a == "--max-findings" and i + 1 < len(sys.argv):
            try:
                return max(0, int(sys.argv[i + 1]))
            except ValueError:
                break
        if a.startswith("--max-findings="):
            try:
                return max(0, int(a.split("=", 1)[1]))
            except ValueError:
                break
    return 40


def report(findings, quiet) -> int:
    errors = sum(1 for lv, _ in findings if lv == "ERROR")
    warns = sum(1 for lv, _ in findings if lv == "WARN")
    if not quiet:
        order = {"ERROR": 0, "WARN": 1, "INFO": 2}
        ordered = sorted(findings, key=lambda f: (order[f[0]], f[1]))
        # Errors sort first, so a cap always keeps the findings that decide the
        # exit code and drops INFO noise. The drop is printed, never silent: on
        # a large project the raw list can outgrow what a postflight can quote.
        cap = max_findings()
        shown = ordered if cap == 0 else ordered[:cap]
        for lv, text in shown:
            print(f"[blueprint] {lv}: {text}")
        if len(shown) < len(ordered):
            print(f"[blueprint] ... {len(ordered) - len(shown)} more finding(s) not shown "
                  f"(--max-findings {cap}); the counts below cover all of them.")
        print(f"[blueprint] {errors} error(s), {warns} warning(s), "
              f"{len(findings) - errors - warns} info.")
    return 1 if errors else 0


if __name__ == "__main__":
    sys.exit(main())
