#!/usr/bin/env python3
"""build_assetmap.py — asset-layer inventory into .claude/assetmap.md.

The codemap covers code and the unitymap covers scene structure; this covers
the fourth layer: what actually sits under `Assets/` and how it gets loaded.

It answers, without a directory walk:
  - which `.asset` files exist and which ScriptableObject class each one is
  - which prefabs exist and which are variants
  - what is inside `Resources/` and `StreamingAssets/` — the string-loaded,
    always-shipped surface the cost model has to price
  - where the assembly boundaries (.asmdef) are, and what each covers
  - which Addressables group files exist

`data-source.md` and `scene-structure.md` both need this list; without it they
run on guesses.

Lines starting with `>> note:` are preserved across regeneration.

Usage: python3 build_assetmap.py [project_root] [--quiet] [--if-stale]
  --if-stale  exit without writing when the stamp's source-sig already matches
              the asset files on disk (cheap enough for the Stop hook).
"""
import json
import os
import sys
from datetime import datetime, timezone

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from unityparse import CLS_PREFAB_INSTANCE, guid_index, parse_docs, walk_assets  # noqa: E402

MAX_PER_SECTION = 200


def project_root() -> str:
    args = [a for a in sys.argv[1:] if not a.startswith("--")]
    if args:
        return os.path.abspath(args[0])
    return os.path.abspath(os.environ.get("CLAUDE_PROJECT_DIR", os.getcwd()))


def rel(root, p):
    return os.path.relpath(p, root).replace("\\", "/")


def so_type(path, guids):
    """ScriptableObject class behind a .asset file, via its m_Script guid."""
    for d in parse_docs(path):
        s = d.ref1("m_Script")
        if s and s[1]:
            sp = guids.get(s[1])
            if sp:
                base = os.path.basename(sp)
                return (base[:-3] if base.endswith(".cs") else base), sp
            return f"guid:{s[1][:8]}", "?"
    return "-", "-"


def prefab_source(path, guids):
    for d in parse_docs(path):
        if d.cls == CLS_PREFAB_INSTANCE or d.ref1("m_SourcePrefab"):
            s = d.ref1("m_SourcePrefab")
            if s and s[1]:
                return guids.get(s[1], f"guid:{s[1][:8]}")
    return None


def prefab_scripts(path, guids):
    names = []
    for d in parse_docs(path):
        s = d.ref1("m_Script")
        if s and s[1] and s[1] in guids:
            b = os.path.basename(guids[s[1]])
            names.append(b[:-3] if b.endswith(".cs") else b)
    seen, out = set(), []
    for n in names:
        if n not in seen:
            seen.add(n)
            out.append(n)
    return out[:8]


def section(title, rows, empty="- (none)"):
    out = [f"## {title}", ""]
    if rows:
        out.extend(rows[:MAX_PER_SECTION])
        if len(rows) > MAX_PER_SECTION:
            out.append(f"- ... {len(rows) - MAX_PER_SECTION} more")
    else:
        out.append(empty)
    out.append("")
    return out


def special_folder(root, name):
    """Every folder called <name> under Assets, with its file count."""
    hits = []
    base = os.path.join(root, "Assets")
    if not os.path.isdir(base):
        return hits
    for dirpath, dirnames, _files in os.walk(base):
        dirnames[:] = sorted(d for d in dirnames if d not in ("Library", "Temp", ".git"))
        if os.path.basename(dirpath) == name:
            n = sum(len(fs) for _d, _s, fs in os.walk(dirpath))
            hits.append((rel(root, dirpath), n))
            dirnames[:] = []
    return hits


TRACKED = (".asset", ".prefab", ".unity", ".asmdef")


def source_sig(root: str) -> str:
    import hashlib
    files = sorted(walk_assets(root, TRACKED))
    if not files:
        return "empty"
    blob = "".join(f"{p}:{int(os.path.getmtime(p))}" for p in files)
    return hashlib.sha256(blob.encode()).hexdigest()[:12]


def main() -> int:
    root = project_root()
    quiet = "--quiet" in sys.argv
    dest0 = os.path.join(root, ".claude", "assetmap.md")
    sig = source_sig(root)
    if "--if-stale" in sys.argv and os.path.isfile(dest0):
        with open(dest0, encoding="utf-8") as f:
            if f"source-sig:{sig}" in f.readline():
                return 0
    guids = guid_index(root)

    asmdefs = []
    for p in walk_assets(root, (".asmdef",)):
        try:
            with open(p, encoding="utf-8", errors="replace") as f:
                d = json.load(f)
        except (OSError, ValueError):
            d = {}
        refs = ",".join(str(x) for x in (d.get("references") or [])[:6]) or "-"
        only = ",".join(str(x) for x in (d.get("includePlatforms") or [])) or "all"
        asmdefs.append(f"- {rel(root, p)} | name: {d.get('name', '?')} | refs: {refs} | "
                       f"platforms: {only} | covers: {os.path.dirname(rel(root, p))}/**")

    assets = []
    for p in walk_assets(root, (".asset",)):
        t, sp = so_type(p, guids)
        assets.append(f"- {rel(root, p)} | type: {t} | script: {sp}")

    prefabs = []
    for p in walk_assets(root, (".prefab",)):
        src = prefab_source(p, guids)
        r = rel(root, p)
        v = f"variant-of: {src}" if src and src != r else "variant-of: -"
        prefabs.append(f"- {r} | {v} | scripts: {', '.join(prefab_scripts(p, guids)) or '-'}")

    scenes = [f"- {rel(root, p)}" for p in walk_assets(root, (".unity",))]

    loaded = []
    for folder in ("Resources", "StreamingAssets"):
        for path, n in special_folder(root, folder):
            loaded.append(f"- {path} | {n} file(s) | loaded by string at runtime; ships in every build")
    groups = [f"- {rel(root, p)}" for p in walk_assets(root, (".asset",))
              if "AddressableAssetsData" in rel(root, p)]
    for g in groups:
        loaded.append(g + " | addressables group")

    dest = os.path.join(root, ".claude", "assetmap.md")
    kept_notes = []
    if os.path.isfile(dest):
        with open(dest, encoding="utf-8") as f:
            kept_notes = [l.rstrip() for l in f if l.startswith(">> note:")]

    total = len(asmdefs) + len(assets) + len(prefabs) + len(scenes)
    head = [
        f"<!-- stamp: {datetime.now(timezone.utc).strftime('%Y-%m-%dT%H:%MZ')} source-sig:{sig} "
        f"assets:{len(assets)} prefabs:{len(prefabs)} scenes:{len(scenes)} asmdefs:{len(asmdefs)} -->",
        "# assetmap — asset inventory (data, prefabs, load surface, assemblies)",
        "",
        "Regenerate with `python3 .claude/hooks/build_assetmap.py`. `data-source.md`",
        "reads the ScriptableObject section before deciding where a value lives;",
        "the cost model reads the load-surface section before pricing a load.",
        "",
    ]
    body = []
    body += section("Assemblies (.asmdef) — the real compile boundary", asmdefs)
    body += section("ScriptableObject assets", assets)
    body += section("Prefabs", prefabs)
    body += section("Scenes", scenes)
    body += section("Runtime load surface", loaded,
                    empty="- (none) — nothing is string-loaded; every asset is a direct reference")
    if kept_notes:
        body += ["## Preserved notes", "", *kept_notes, ""]

    os.makedirs(os.path.dirname(dest), exist_ok=True)
    with open(dest, "w", encoding="utf-8") as f:
        f.write("\n".join(head + body).rstrip() + "\n")

    if not quiet:
        print(f"[assetmap] {len(assets)} .asset, {len(prefabs)} prefab, {len(scenes)} scene, "
              f"{len(asmdefs)} asmdef indexed ({total} entries).")
    return 0


if __name__ == "__main__":
    sys.exit(main())
