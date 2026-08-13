#!/usr/bin/env python3
"""maps.py — readers for the maps under .claude/.

One place that knows the codemap line schema and the blueprint section shape,
so build_index.py, check_blueprint.py and session_context.py cannot drift
apart from build_codemap.py.
"""
import os
import re

MARKERS = ("STALE", "ORPHAN", "MOVED")
DASH = "—"   # blueprint field separator (em dash)


# ---------------------------------------------------------------- codemap ---

class CodeLine:
    __slots__ = ("shard", "marker", "path", "role", "sys", "api", "dep", "used", "crit", "note", "h")

    def __init__(self, shard, marker, parts):
        self.shard, self.marker = shard, marker
        self.path = parts[0] if parts else ""
        self.role = parts[1] if len(parts) > 1 else ""
        get = lambda pre: next((p[len(pre):].strip() for p in parts if p.startswith(pre)), "")  # noqa: E731
        self.sys = get("sys:")
        self.api = get("api:")
        self.dep = get("dep:") or get("dep?:")
        self.used = get("used:")
        self.crit = get("crit:")
        self.note = get("note:")
        self.h = get("h:")

    @property
    def unfinished(self):
        return self.role == "MISSING-role" or self.sys in ("", "?")


def read_codemaps(root: str):
    """[(shard, stamp_line, [CodeLine])] for every codemap-*.md present."""
    cdir = os.path.join(root, ".claude")
    out = []
    if not os.path.isdir(cdir):
        return out
    for fn in sorted(os.listdir(cdir)):
        if not (fn.startswith("codemap-") and fn.endswith(".md")):
            continue
        shard = fn[len("codemap-"):-3]
        stamp, lines = "", []
        try:
            with open(os.path.join(cdir, fn), encoding="utf-8") as f:
                for raw in f:
                    s = raw.strip()
                    if s.startswith("<!-- stamp:"):
                        stamp = s
                        continue
                    if not s or s.startswith(("#", ">>")) or "|" not in s:
                        continue
                    marker = ""
                    for m in MARKERS:
                        if s.startswith(m + " "):
                            marker, s = m, s[len(m) + 1:]
                            break
                    lines.append(CodeLine(shard, marker, [p.strip() for p in s.split("|")]))
        except OSError:
            continue
        out.append((shard, stamp, lines))
    return out


# -------------------------------------------------------------- blueprint ---

def _fields(line: str):
    return [f.strip() for f in line.lstrip("- ").split(DASH)]


def _is_placeholder(fields):
    """Template stubs and 'nothing here yet' lines are not inventory entries."""
    if not fields or not fields[0]:
        return True
    first = fields[0].strip()
    return first.startswith(("<", "(")) or first in ("-", "TBD", "none", "None")


def read_blueprint(root: str):
    """{'systems': {...}, 'scenes': [...], 'prefabs': [...], 'folders': [...]}"""
    path = os.path.join(root, ".claude", "blueprint.md")
    data = {"systems": {}, "scenes": [], "prefabs": [], "folders": [], "exists": False}
    if not os.path.isfile(path):
        return data
    data["exists"] = True
    section, in_code = "", False
    with open(path, encoding="utf-8") as f:
        for raw in f:
            line = raw.rstrip("\n")
            s = line.strip()
            if s.startswith("```"):
                in_code = not in_code
                continue
            if s.startswith("## "):
                section = s[3:].strip().lower()
                continue
            if in_code and section.startswith("folder"):
                m = re.match(r"^\s*([\w./<>-]+/)", line)
                if m:
                    data["folders"].append(m.group(1))
                continue
            if s.startswith("<!--") or not s.startswith("- "):
                continue
            fields = _fields(s)
            if _is_placeholder(fields):
                continue
            if section.startswith("systems"):
                deps = []
                for f2 in fields[1:]:
                    m = re.match(r"depends on:\s*(.*)", f2, re.I)
                    if m:
                        deps = [d.strip() for d in re.split(r"[,;]", m.group(1))
                                if d.strip() and d.strip() != "-"]
                data["systems"][fields[0]] = {
                    "responsibility": fields[1] if len(fields) > 1 else "",
                    "depends": deps,
                    "raw": s,
                }
            elif section.startswith("scene"):
                data["scenes"].append({"name": fields[0], "raw": s})
            elif section.startswith("prefab"):
                data["prefabs"].append({
                    "name": fields[0],
                    "system": fields[1] if len(fields) > 1 else "",
                    "raw": s,
                })
    return data


def find_cycles(systems):
    """[[a, b, a], ...] — dependency arrows must stay one-directional."""
    graph = {k: [d for d in v["depends"] if d in systems] for k, v in systems.items()}
    cycles, state, stack = [], {}, []

    def visit(n):
        state[n] = 1
        stack.append(n)
        for m in graph.get(n, []):
            if state.get(m) == 1:
                cycles.append(stack[stack.index(m):] + [m])
            elif state.get(m, 0) == 0:
                visit(m)
        stack.pop()
        state[n] = 2

    for n in sorted(graph):
        if state.get(n, 0) == 0:
            visit(n)
    return cycles


# --------------------------------------------------------------- assetmap ---

def read_assetmap(root: str):
    """[(section, line)] pairs from .claude/assetmap.md."""
    path = os.path.join(root, ".claude", "assetmap.md")
    out, section = [], ""
    if not os.path.isfile(path):
        return out
    with open(path, encoding="utf-8") as f:
        for raw in f:
            s = raw.strip()
            if s.startswith("## "):
                section = s[3:].strip()
            elif s.startswith("- ") and section:
                out.append((section, s[2:]))
    return out


def stamp_status(stamp: str) -> str:
    m = re.search(r"status:\s*([^-]*?)\s*-->", stamp)
    return m.group(1).strip() if m else "unknown"
