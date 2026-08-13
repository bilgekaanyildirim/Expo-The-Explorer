#!/usr/bin/env python3
"""build_index.py — one screen: system -> where it lives. Writes .claude/index.md.

This is step 1 of procedures/locate.md and the reason a task does not need a
repo scan. It is a JOIN, not a source: blueprint.md supplies the system names,
the codemap `sys:` fields supply the files, and assetmap/blueprint supply the
scenes, prefabs and data folders. Anything it cannot join is printed as
UNMAPPED rather than guessed — the script never invents a system name.

Usage: python3 build_index.py [project_root] [--quiet]
"""
import os
import sys
from datetime import date

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from maps import read_assetmap, read_blueprint, read_codemaps  # noqa: E402

CRIT_ORDER = {"K1": 0, "K2": 1, "K3": 2}


def project_root() -> str:
    args = [a for a in sys.argv[1:] if not a.startswith("--")]
    if args:
        return os.path.abspath(args[0])
    return os.path.abspath(os.environ.get("CLAUDE_PROJECT_DIR", os.getcwd()))


def entry_files(lines, limit=2):
    ranked = sorted(lines, key=lambda l: (CRIT_ORDER.get(l.crit, 3), l.path))
    shown = [l.path for l in ranked[:limit]]
    extra = len(ranked) - len(shown)
    return ", ".join(shown) + (f" (+{extra})" if extra > 0 else "") if shown else "-"


def cell(v):
    return v if v else "-"


def main() -> int:
    root = project_root()
    quiet = "--quiet" in sys.argv
    bp = read_blueprint(root)
    codemaps = read_codemaps(root)

    by_sys, unknown_sys, unassigned = {}, {}, []
    for shard, _stamp, lines in codemaps:
        for l in lines:
            if l.marker:
                continue          # a flagged line does not get to define where a system lives
            name = l.sys
            if not name or name == "?":
                unassigned.append(l)
            elif name in bp["systems"]:
                by_sys.setdefault(name, []).append(l)
            else:
                unknown_sys.setdefault(name, []).append(l)

    # asset type -> owning system, via the script that backs the ScriptableObject
    sys_of_path = {l.path: l.sys for _s, _st, ls in codemaps for l in ls if l.sys and l.sys != "?"}
    data_dirs = {}
    for section, line in read_assetmap(root):
        if not section.startswith("ScriptableObject"):
            continue
        parts = [p.strip() for p in line.split("|")]
        script = next((p[len("script:"):].strip() for p in parts if p.startswith("script:")), "")
        owner = sys_of_path.get(script)
        if owner:
            data_dirs.setdefault(owner, set()).add(os.path.dirname(parts[0]))

    rows = []
    for name in sorted(bp["systems"]):
        lines = by_sys.get(name, [])
        shards = ", ".join(sorted({l.shard for l in lines})) if lines else "-"
        scenes = ", ".join(s["name"] for s in bp["scenes"] if name.lower() in s["raw"].lower())
        prefabs = ", ".join(p["name"] for p in bp["prefabs"] if p["system"] == name)
        data = ", ".join(sorted(data_dirs.get(name, [])))
        status = "OK" if lines else "UNMAPPED — blueprint system with no code"
        rows.append(f"| {name} | {shards} | {entry_files(lines)} | {cell(scenes)} | "
                    f"{cell(prefabs)} | {cell(data)} | {status} |")

    unmapped_n = sum(1 for r in rows if "UNMAPPED" in r) + len(unknown_sys)
    head = [
        f"<!-- stamp: {date.today().isoformat()} systems:{len(bp['systems'])} "
        f"unmapped:{unmapped_n} unassigned-files:{len(unassigned)} -->",
        "# index — system to location, one screen",
        "",
        "Step 1 of `procedures/locate.md`: read this before anything else, and only",
        "descend into blueprint/codemap/unitymap for what this table points at.",
        "Regenerate with `python3 .claude/hooks/build_index.py`; it joins existing",
        "maps and never invents a name.",
        "",
        "| system | shard(s) | entry files | scenes | prefabs | data | status |",
        "|---|---|---|---|---|---|---|",
    ]
    if not bp["exists"]:
        head = head[:2] + ["", "blueprint.md is missing — run bootstrap.md before relying on this file.", ""]
        rows = []
    body = rows or ["| - | - | - | - | - | - | no systems declared in blueprint.md |"]

    gaps = ["", "## Gaps"]
    if unknown_sys:
        for name, ls in sorted(unknown_sys.items()):
            gaps.append(f"- UNKNOWN-SYSTEM `{name}` — {len(ls)} codemap line(s) claim it, "
                        f"blueprint.md has no such system line")
    if unassigned:
        gaps.append(f"- `sys: ?` on {len(unassigned)} codemap line(s) — "
                    f"first: {', '.join(l.path for l in unassigned[:3])}")
    flagged = [l for _s, _st, ls in codemaps for l in ls if l.marker]
    if flagged:
        gaps.append(f"- {len(flagged)} flagged codemap line(s) excluded from this table "
                    f"({', '.join(sorted({l.marker for l in flagged}))})")
    if len(gaps) == 2:
        gaps.append("- none")

    dest = os.path.join(root, ".claude", "index.md")
    os.makedirs(os.path.dirname(dest), exist_ok=True)
    with open(dest, "w", encoding="utf-8") as f:
        f.write("\n".join(head + body + gaps).rstrip() + "\n")

    if not quiet:
        print(f"[index] {len(bp['systems'])} system(s), {unmapped_n} unmapped, "
              f"{len(unassigned)} file(s) with sys: ? -> .claude/index.md")
    return 0


if __name__ == "__main__":
    sys.exit(main())
