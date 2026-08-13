#!/usr/bin/env python3
"""build_unitymap.py — distills scene/prefab STRUCTURE into .claude/unitymap.md.

The map answers, without opening a single `.unity` file:
  - which GameObject carries which component, and where it sits in the tree
  - which serialized reference slots are still unassigned (NULL)
  - which objects are prefab instances, and of what
  - which prefabs are variants of which
  - where a Missing Script is silently sitting

This is the FALLBACK path: it reads the YAML directly, so it sees serialized
field names but not their declared types. The preferred path is the Editor
exporter (templates/Editor/UnityMapExporter.cs, installed at
Assets/Editor/UnityMapExporter.cs), which has real type information from
AssetDatabase and writes the same file in the same format. Whichever ran last
wins; the stamp records which one it was.

Lines starting with `>> note:` are preserved across regeneration.

Usage: python3 build_unitymap.py [project_root] [--quiet] [--if-stale]
  --if-stale  exit without writing when the stamp's source-sig already matches
              the scenes/prefabs on disk (cheap enough for the Stop hook).
"""
import os
import re
import sys
from datetime import datetime, timezone

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from unityparse import (  # noqa: E402
    CLS_PREFAB_INSTANCE, build_scene_graph, children_of, component_label,
    go_name, guid_index, hierarchy_roots, parse_docs, serialized_refs, walk_assets)

MAX_OBJECTS_PER_ASSET = 120   # a map that costs more than the file it replaces is not a map
RE_NAME_OVERRIDE = re.compile(r"propertyPath:\s*m_Name\s*\n\s*value:\s*(.*)")


def project_root() -> str:
    args = [a for a in sys.argv[1:] if not a.startswith("--")]
    if args:
        return os.path.abspath(args[0])
    return os.path.abspath(os.environ.get("CLAUDE_PROJECT_DIR", os.getcwd()))


def source_sig(targets) -> str:
    import hashlib
    if not targets:
        return "empty"
    blob = "".join(f"{t}:{int(os.path.getmtime(t))}" for t in targets)
    return hashlib.sha256(blob.encode()).hexdigest()[:12]


def instance_name(doc, guids):
    m = RE_NAME_OVERRIDE.search(doc.raw.get("m_Modification", ""))
    if m and m.group(1).strip():
        return m.group(1).strip()
    src = doc.ref1("m_SourcePrefab")
    if src and src[1]:
        p = guids.get(src[1])
        if p:
            return os.path.basename(p).rsplit(".", 1)[0]
    return "PrefabInstance"


def source_prefab_path(doc, guids):
    src = doc.ref1("m_SourcePrefab")
    if src and src[1]:
        return guids.get(src[1], f"guid:{src[1][:8]}")
    return "?"


def render_asset(path, rel, guids, findings, used_scripts):
    docs = parse_docs(path)
    by_fid, gameobjects, transforms, components_of = build_scene_graph(docs)
    lines, budget = [], [MAX_OBJECTS_PER_ASSET]

    prefab_instances = [d for d in docs if d.cls == CLS_PREFAB_INSTANCE]
    # A stripped transform belongs to a prefab instance, not to this file's tree.
    instance_parent = {}
    for pi in prefab_instances:
        m = re.search(r"m_TransformParent:\s*\{fileID:\s*(-?\d+)", pi.raw.get("m_Modification", "")
                      + str(pi.props.get("m_Modification", "")))
        instance_parent[pi.fid] = m.group(1) if m else "0"

    def walk(tr, depth):
        if budget[0] <= 0:
            return
        go = gameobjects.get(str(tr.ref1("m_GameObject")[0])) if tr.ref1("m_GameObject") else None
        if go is None:
            return
        budget[0] -= 1
        comps, refbits = [], []
        for c in components_of.get(go.fid, []):
            label = component_label(c, guids)
            comps.append(label)
            if label.startswith("MISSING SCRIPT"):
                findings.append(f"MISSING SCRIPT | {rel} | {go_name(go)}")
            else:
                script = c.ref1("m_Script")
                if script and script[1] and script[1] in guids:
                    used_scripts.add((label, guids[script[1]]))
            for field, state in serialized_refs(c):
                refbits.append(f"{field}={state}")
                if state == "NULL":
                    findings.append(f"UNASSIGNED | {rel} | {go_name(go)}.{label}.{field}")
        active = "" if go.props.get("m_IsActive", "1") != "0" else " [inactive]"
        tail = f"  refs: {', '.join(refbits)}" if refbits else ""
        lines.append(f"{'  ' * depth}- {go_name(go)}{active}  [{', '.join(comps) or '-'}]{tail}")
        for ch in children_of(tr, transforms):
            walk(ch, depth + 1)

    for root_tr in hierarchy_roots(transforms):
        walk(root_tr, 0)

    for pi in prefab_instances:
        if budget[0] <= 0:
            break
        budget[0] -= 1
        lines.append(f"- * {instance_name(pi, guids)}  (prefab instance of {source_prefab_path(pi, guids)})")

    total = len(gameobjects) + len(prefab_instances)
    if budget[0] <= 0 and total > MAX_OBJECTS_PER_ASSET:
        lines.append(f"- ... {total - MAX_OBJECTS_PER_ASSET} more object(s) not listed; "
                     f"regenerate with the Editor exporter for the full tree")
    return lines, total


def variant_of(path, guids):
    """A prefab whose root object points at another prefab is a variant of it."""
    for d in parse_docs(path):
        src = d.ref1("m_SourcePrefab")
        if src and src[1]:
            return guids.get(src[1], f"guid:{src[1][:8]}")
    return None


def main() -> int:
    root = project_root()
    quiet = "--quiet" in sys.argv
    guids = guid_index(root)
    targets = sorted(walk_assets(root, (".unity", ".prefab")))
    scenes = [t for t in targets if t.endswith(".unity")]
    prefabs = [t for t in targets if t.endswith(".prefab")]

    out_dir = os.path.join(root, ".claude")
    os.makedirs(out_dir, exist_ok=True)
    dest = os.path.join(out_dir, "unitymap.md")

    sig = source_sig(targets)
    if "--if-stale" in sys.argv and os.path.isfile(dest):
        with open(dest, encoding="utf-8") as f:
            head0 = f.readline()
        if f"source-sig:{sig}" in head0:
            return 0          # nothing on disk moved; leave the file (and its generator stamp) alone

    kept_notes = []
    if os.path.isfile(dest):
        with open(dest, encoding="utf-8") as f:
            kept_notes = [l.rstrip() for l in f if l.startswith(">> note:")]

    findings, body, used_scripts = [], [], set()
    for t in targets:
        rel = os.path.relpath(t, root).replace("\\", "/")
        kind = "SCENE" if t.endswith(".unity") else "PREFAB"
        header = f"## {kind} {rel}"
        if kind == "PREFAB":
            v = variant_of(t, guids)
            if v and v != rel:
                header += f"   variant-of: {v}"
        lines, total = render_asset(t, rel, guids, findings, used_scripts)
        body.append(header + f"   ({total} object(s))")
        body.extend(lines or ["- (empty)"])
        body.append("")

    missing = sum(1 for f in findings if f.startswith("MISSING SCRIPT"))
    unassigned = sum(1 for f in findings if f.startswith("UNASSIGNED"))
    status = "OK" if not missing else f"DEGRADED {missing} missing-script"
    stamp = (f"<!-- stamp: {datetime.now(timezone.utc).strftime('%Y-%m-%dT%H:%MZ')} "
             f"source-sig:{sig} scenes:{len(scenes)} prefabs:{len(prefabs)} "
             f"generator:python-fallback status: {status} -->")

    head = [
        stamp,
        "# unitymap — scene and prefab structure",
        "",
        "Read this instead of opening a `.unity`/`.prefab` file. Tree indentation is",
        "the GameObject hierarchy; `[...]` lists the components on the object;",
        "`refs:` lists serialized reference slots and whether the Inspector has",
        "something in them. `*` marks a prefab instance.",
        "",
        "Staleness: `source-sig` is derived from scene/prefab mtimes. Regenerate with",
        "`python3 .claude/hooks/build_unitymap.py` or, for real type information, the",
        "Unity menu item Tools > unity-dev > Export unitymap.",
        "",
    ]
    if findings:
        head.append("## Findings")
        head.extend(f"- {x}" for x in sorted(set(findings))[:60])
        if len(set(findings)) > 60:
            head.append(f"- ... {len(set(findings)) - 60} more")
        head.append("")

    tail = []
    if used_scripts:
        tail += ["## Script index — component name -> codemap path", ""]
        tail += [f"- {name} | {path}" for name, path in sorted(used_scripts)]
        tail.append("")
    if kept_notes:
        tail += ["## Preserved notes", *kept_notes, ""]

    with open(dest, "w", encoding="utf-8") as f:
        f.write("\n".join(head + body + tail).rstrip() + "\n")

    if not quiet:
        print(f"[unitymap] {len(scenes)} scene(s), {len(prefabs)} prefab(s); "
              f"{missing} missing script(s), {unassigned} unassigned reference slot(s).")
    return 0


if __name__ == "__main__":
    sys.exit(main())
