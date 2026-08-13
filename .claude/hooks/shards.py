#!/usr/bin/env python3
"""shards.py — shared shard resolution. Reads .claude/shards.json.

The path -> shard mapping is defined in exactly ONE place: the project's
.claude/shards.json (installed by init_project.py from
templates/shards.json.template). build_codemap.py and build_index.py both
import this module; SKILL.md only points at the file. If shards.json is
absent or unreadable, DEFAULT_SHARDS below is used so the tools never fail
closed on a half-installed project.
"""
import json
import os
import re

DEFAULT_SHARDS = [
    {"name": "editor", "patterns": ["**/Editor/**"]},
    {"name": "ui", "patterns": ["**/Scripts/UI/**"]},
    {"name": "gameplay", "patterns": ["**/Scripts/Gameplay/**"]},
    {"name": "content", "patterns": ["**/Scripts/Content/**"]},
    {"name": "core", "patterns": ["**"]},
]


def _glob_to_re(pat: str) -> "re.Pattern":
    """gitignore-ish glob -> regex. ** crosses '/', * does not."""
    out, i, n = [], 0, len(pat)
    while i < n:
        c = pat[i]
        if c == "*":
            if pat.startswith("**/", i):
                out.append("(?:.*/)?")
                i += 3
                continue
            if pat.startswith("**", i):
                out.append(".*")
                i += 2
                continue
            out.append("[^/]*")
            i += 1
            continue
        if c == "?":
            out.append("[^/]")
        elif c in ".^$+{}()[]|\\":
            out.append("\\" + c)
        else:
            out.append(c)
        i += 1
    return re.compile("^" + "".join(out) + "$")


def load_shards(root: str):
    """Returns [(name, [compiled patterns]), ...] in declaration order."""
    cfg = os.path.join(root, ".claude", "shards.json")
    data = None
    if os.path.isfile(cfg):
        try:
            with open(cfg, encoding="utf-8") as f:
                data = json.load(f)
        except (OSError, ValueError):
            data = None
    entries = (data or {}).get("shards") or DEFAULT_SHARDS
    out = []
    for e in entries:
        name = str(e.get("name", "")).strip()
        pats = e.get("patterns") or []
        if name and pats:
            out.append((name, [_glob_to_re(p) for p in pats]))
    return out or [(e["name"], [_glob_to_re(p) for p in e["patterns"]]) for e in DEFAULT_SHARDS]


def shard_names(root: str):
    return [n for n, _ in load_shards(root)]


def shard_of(rel: str, shards) -> str:
    """First matching pattern wins; falls back to the last shard listed."""
    p = rel.replace("\\", "/")
    for name, pats in shards:
        for rx in pats:
            if rx.match(p):
                return name
    return shards[-1][0]
