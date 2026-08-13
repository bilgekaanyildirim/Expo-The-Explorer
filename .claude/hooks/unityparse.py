#!/usr/bin/env python3
"""unityparse.py — minimal Unity YAML reader shared by the map builders.

Unity serializes scenes and prefabs as a stream of documents:

    --- !u!1 &1234567
    GameObject:
      m_Component:
      - component: {fileID: 1234568}
      m_Name: GameManager

This module reads that stream without a YAML dependency. It is deliberately
shallow: it resolves the object graph (who owns whom, which component sits on
which object, which reference slots are empty) and nothing else. Anything that
needs real type information belongs in the Editor exporter
(templates/Editor/UnityMapExporter.cs), for which this is the fallback.
"""
import os
import re

SKIP_DIRS = ("Library", "Temp", "obj", "Logs", "UserSettings", "Build", "Builds", ".git")

# Unity class ids used by name below
CLS_GAMEOBJECT = 1
CLS_TRANSFORM = 4
CLS_MONOBEHAVIOUR = 114
CLS_RECTTRANSFORM = 224
CLS_PREFAB_INSTANCE = 1001

RE_DOC = re.compile(r"^---\s+!u!(\d+)\s+&(\d+)")
RE_KEY = re.compile(r"^  (\w+):\s*(.*)$")
RE_REF = re.compile(r"\{fileID:\s*(-?\d+)(?:,\s*guid:\s*([0-9a-fA-F]+))?[^}]*\}")


# Keys whose nested block is kept verbatim (prefab overrides carry the instance name there)
RAW_KEYS = ("m_Modification",)


class Doc:
    __slots__ = ("cls", "fid", "type_name", "props", "refs", "raw")

    def __init__(self, cls, fid):
        self.cls = cls
        self.fid = fid
        self.type_name = ""
        self.props = {}    # key -> scalar text (may be "")
        self.refs = {}     # key -> [(fileID, guid|None), ...]
        self.raw = {}      # key -> nested block text, only for RAW_KEYS

    def ref1(self, key):
        r = self.refs.get(key)
        return r[0] if r else None


def walk_assets(root: str, suffixes):
    base = os.path.join(root, "Assets")
    if not os.path.isdir(base):
        return
    for dirpath, dirnames, filenames in os.walk(base):
        dirnames[:] = sorted(d for d in dirnames if d not in SKIP_DIRS)
        for fn in sorted(filenames):
            if fn.endswith(suffixes):
                yield os.path.join(dirpath, fn)


def guid_index(root: str):
    """guid -> project-relative asset path, from every .meta under Assets."""
    out = {}
    for meta in walk_assets(root, (".meta",)):
        try:
            with open(meta, encoding="utf-8", errors="replace") as f:
                for line in f:
                    m = re.match(r"guid:\s*([0-9a-fA-F]+)", line)
                    if m:
                        rel = os.path.relpath(meta[:-5], root).replace("\\", "/")
                        out[m.group(1)] = rel
                        break
        except OSError:
            pass
    return out


def parse_docs(path: str):
    """Returns [Doc]. Only documents that start with the `--- !u!N &id` header."""
    docs, cur, cur_key = [], None, None
    try:
        fh = open(path, encoding="utf-8", errors="replace")
    except OSError:
        return docs
    with fh as f:
        for line in f:
            line = line.rstrip("\n")
            m = RE_DOC.match(line)
            if m:
                cur = Doc(int(m.group(1)), m.group(2))
                docs.append(cur)
                cur_key = None
                continue
            if cur is None:
                continue
            if not cur.type_name and line and not line.startswith(" ") and line.endswith(":"):
                cur.type_name = line[:-1]
                continue
            k = RE_KEY.match(line)
            if k:
                cur_key = k.group(1)
                cur.props[cur_key] = k.group(2).strip()
                found = RE_REF.findall(k.group(2))
            elif cur_key is not None and line.startswith("  "):
                found = RE_REF.findall(line)   # continuation of the current key (list items, nested maps)
                if cur_key in RAW_KEYS:
                    cur.raw[cur_key] = cur.raw.get(cur_key, "") + line + "\n"
            else:
                continue
            if found:
                cur.refs.setdefault(cur_key, []).extend(
                    (int(fid), (g or None)) for fid, g in found)
    return docs


def build_scene_graph(docs):
    """Returns (by_fid, gameobjects, transforms, components_of).

    components_of: GameObject fileID -> [component Doc]
    """
    by_fid = {d.fid: d for d in docs}
    gameobjects = {d.fid: d for d in docs if d.cls == CLS_GAMEOBJECT}
    transforms = {d.fid: d for d in docs
                  if d.cls in (CLS_TRANSFORM, CLS_RECTTRANSFORM)}
    components_of = {}
    for d in docs:
        if d.cls == CLS_GAMEOBJECT:
            continue
        go = d.ref1("m_GameObject")
        if go:
            components_of.setdefault(str(go[0]), []).append(d)
    return by_fid, gameobjects, transforms, components_of


def hierarchy_roots(transforms):
    """Root transforms (no father) in file order."""
    return [t for t in transforms.values()
            if not (t.ref1("m_Father") and t.ref1("m_Father")[0] != 0)]


def children_of(transform, transforms):
    out = []
    for fid, _g in transform.refs.get("m_Children", []):
        c = transforms.get(str(fid))
        if c is not None:
            out.append(c)
    return out


def go_name(go_doc):
    return (go_doc.props.get("m_Name") or "?").strip() if go_doc else "?"


def component_label(doc, guids, codemap_paths=None):
    """Human label for a component doc: real name for built-ins, script name for MonoBehaviour."""
    if doc.cls != CLS_MONOBEHAVIOUR:
        return doc.type_name or f"class{doc.cls}"
    script = doc.ref1("m_Script")
    if not script or (script[1] is None and script[0] == 0):
        return "MISSING SCRIPT"
    guid = script[1]
    path = guids.get(guid or "")
    if not path:
        return f"MISSING SCRIPT (guid:{(guid or '?')[:8]})"
    name = os.path.basename(path)
    return name[:-3] if name.endswith(".cs") else name


SKIP_REF_KEYS = {
    "m_GameObject", "m_Script", "m_Father", "m_Children", "m_Component",
    "m_CorrespondingSourceObject", "m_PrefabInstance", "m_PrefabAsset",
    "m_TransformParent", "m_SourcePrefab", "m_ObjectHideFlags",
}


def serialized_refs(doc):
    """[(field, 'set'|'NULL')] for the reference slots the Inspector exposes."""
    out = []
    for key, refs in doc.refs.items():
        if key in SKIP_REF_KEYS or key.startswith("m_"):
            continue
        assigned = any(fid != 0 or guid for fid, guid in refs)
        out.append((key, "set" if assigned else "NULL"))
    return sorted(out)
