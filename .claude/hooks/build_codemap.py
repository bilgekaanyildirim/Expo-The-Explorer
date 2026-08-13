#!/usr/bin/env python3
"""build_codemap.py — codemap INTEGRITY tool (not the primary producer).

Contract:
  - Adds MISSING lines for files that have no line yet.
  - Marks lines whose file is gone (ORPHAN), whose file now belongs to another
    shard (MOVED), or whose content changed since the line was written (STALE).
    It NEVER deletes a line and never rewrites the semantic fields the AI wrote
    (role / sys / api / dep / used / crit / note).
  - Owns exactly three mechanical things: the status marker at the start of a
    line, the `h:<sha8>` field at the end of it, and the staleness stamp at the
    top of the file.
  - Output is deterministic and idempotent (a second run on the same state
    produces no diff).

Hash ownership (why there is no mark/clear loop):
  When the file content no longer matches `h:`, this tool marks the line STALE
  *and writes the new hash*. So the mark survives exactly until the AI reviews
  the semantic fields and deletes the `STALE ` prefix itself. The tool never
  re-adds it for the same content.

Line schema (defined in SKILL.md, produced here):
  [STALE|ORPHAN|MOVED ]<path> | <role> | sys: <system> | api: <sigs> | dep: <a,b> |
  used: <c,d> | crit: K1|K2|K3 | note: <text> | h:<sha8>

Shard mapping comes from .claude/shards.json (see shards.py).

Usage: python3 build_codemap.py [project_root]   (default: CLAUDE_PROJECT_DIR or cwd)
Run by the Stop hook at the end of the turn, and by the PostToolUse nudge.
"""
import hashlib
import json
import os
import re
import subprocess
import sys
from datetime import date

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from shards import load_shards, shard_of  # noqa: E402

MARKERS = ("STALE", "ORPHAN", "MOVED")
SKIP_DIRS = ("Library", "Temp", "obj", "Logs", "UserSettings", "Build", "Builds", ".git", ".claude")


def project_root() -> str:
    if len(sys.argv) > 1:
        return os.path.abspath(sys.argv[1])
    return os.path.abspath(os.environ.get("CLAUDE_PROJECT_DIR", os.getcwd()))


def git_hash(root: str) -> str:
    try:
        out = subprocess.run(["git", "rev-parse", "--short", "HEAD"],
                             cwd=root, capture_output=True, text=True, timeout=5)
        return out.stdout.strip() or "no-git"
    except Exception:
        return "no-git"


def sha8(path: str) -> str:
    h = hashlib.sha256()
    try:
        with open(path, "rb") as f:
            for chunk in iter(lambda: f.read(65536), b""):
                h.update(chunk)
    except OSError:
        return ""
    return h.hexdigest()[:8]


RE_DECL = re.compile(
    r"\b(?:public|internal|private|protected)?\s*(?:abstract\s+|sealed\s+|static\s+|partial\s+|readonly\s+)*"
    r"(?:class|struct|interface|enum)\s+(\w+)")
RE_PUBLIC = re.compile(
    r"\bpublic\s+(?:static\s+|virtual\s+|override\s+|async\s+|sealed\s+|new\s+)*"
    r"[\w<>\[\],\s\.]+?\s+(\w+)\s*\(([^)]*)\)")
RE_USING = re.compile(r"^\s*using\s+(?:static\s+)?([\w\.]+)\s*;", re.M)
RE_WORD = re.compile(r"\b([A-Z]\w+)\b")
KEYWORDS = {"if", "for", "while", "switch", "foreach", "get", "set", "lock", "using", "catch", "return", "new"}


def read_text(path: str) -> str:
    try:
        with open(path, encoding="utf-8", errors="replace") as f:
            return f.read()
    except OSError:
        return ""


def public_api(path: str) -> str:
    if path.endswith(".asmdef"):
        try:
            with open(path, encoding="utf-8", errors="replace") as f:
                d = json.load(f)
            return "asmdef " + str(d.get("name", "?"))
        except (OSError, ValueError):
            return "asmdef ?"
    src = read_text(path)
    sigs = []
    for m in RE_PUBLIC.finditer(src):
        name, params = m.group(1), " ".join(m.group(2).split())
        if name in KEYWORDS:
            continue
        sigs.append(f"{name}({params})" if params else f"{name}()")
        if len(sigs) >= 6:
            break
    return "; ".join(sigs) if sigs else "-"


def declared_types(src: str):
    return {m.group(1) for m in RE_DECL.finditer(src)}


def dep_suggestion(path: str, type_index, own_types) -> str:
    """Deterministic DRAFT of the dependency field. The AI confirms it.

    Sources: `using` namespaces that name a project assembly, plus PascalCase
    identifiers in the file that are declared as types elsewhere in the project.
    """
    if path.endswith(".asmdef"):
        try:
            with open(path, encoding="utf-8", errors="replace") as f:
                refs = json.load(f).get("references") or []
            return ",".join(str(r) for r in refs[:6]) or "-"
        except (OSError, ValueError):
            return "-"
    src = read_text(path)
    hits = set()
    for m in RE_WORD.finditer(src):
        t = m.group(1)
        if t in type_index and t not in own_types:
            hits.add(t)
    for m in RE_USING.finditer(src):
        ns = m.group(1).split(".")[0]
        if ns in type_index and ns not in own_types:
            hits.add(ns)
    return ",".join(sorted(hits)[:6]) if hits else "-"


def collect_sources(root: str):
    """Mapped file types: C# scripts and assembly definitions."""
    assets = os.path.join(root, "Assets")
    base = assets if os.path.isdir(assets) else root
    for dirpath, dirnames, filenames in os.walk(base):
        dirnames[:] = sorted(d for d in dirnames if d not in SKIP_DIRS)
        for fn in sorted(filenames):
            if fn.endswith(".meta"):
                continue
            if fn.endswith(".cs") or fn.endswith(".asmdef"):
                yield os.path.relpath(os.path.join(dirpath, fn), root).replace("\\", "/")


def split_marker(line: str):
    for m in MARKERS:
        if line.startswith(m + " "):
            return m, line[len(m) + 1:]
    return "", line


def parse_line(line: str):
    """-> (marker, path, parts) for a map line; (None, None, None) otherwise."""
    stripped = line.strip()
    if not stripped or stripped.startswith(("<!--", "#", ">>")) or "|" not in stripped:
        return None, None, None
    marker, rest = split_marker(stripped)
    parts = [p.strip() for p in rest.split("|")]
    return marker, parts[0], parts


def field_index(parts, prefix):
    for i, p in enumerate(parts):
        if p.startswith(prefix):
            return i
    return -1


def rebuild(marker: str, parts) -> str:
    body = " | ".join(parts)
    return f"{marker} {body}" if marker else body


def main() -> int:
    root = project_root()
    claude_dir = os.path.join(root, ".claude")
    os.makedirs(claude_dir, exist_ok=True)
    shards = load_shards(root)
    names = [n for n, _ in shards]

    on_disk = list(collect_sources(root))
    by_shard = {n: [] for n in names}
    for rel in on_disk:
        by_shard[shard_of(rel, shards)].append(rel)

    type_index, own_types_of = None, {}
    orphan_api = {}   # api signature -> old path; global, so a cross-shard move is still seen
    pending = {}      # shard -> (out_lines, known_paths, counters)

    # --- pass 1: audit the lines that already exist (all shards) ---
    for shard in names:
        cm_file = os.path.join(claude_dir, f"codemap-{shard}.md")
        exists = os.path.isfile(cm_file)
        if not exists and not by_shard[shard]:
            continue  # never create a file for an empty shard

        lines = read_text(cm_file).splitlines() if exists else []
        known = set()
        stale = orphan = missing_role = 0

        out = []
        for line in lines:
            marker, path, parts = parse_line(line)
            if path is None:
                out.append(line)
                continue
            known.add(path)
            abs_path = os.path.join(root, path)

            # sys: is a schema field; insert the placeholder if a legacy line lacks it
            if field_index(parts, "sys:") < 0:
                parts.insert(min(2, len(parts)), "sys: ?")

            hi = field_index(parts, "h:")
            stored = parts[hi][2:].strip() if hi >= 0 else ""

            api_i = field_index(parts, "api:")
            api_val = parts[api_i][4:].strip() if api_i >= 0 else ""

            if not os.path.isfile(abs_path):
                marker = "ORPHAN"                    # the file is gone
                orphan_api.setdefault(api_val, path)
            elif shard_of(path, shards) != shard:
                marker = "MOVED"                     # the file now belongs to another shard
                orphan_api.setdefault(api_val, path)
            else:
                cur = sha8(abs_path)
                if marker in ("ORPHAN", "MOVED"):
                    marker = ""                      # the file is back where this line lives
                if not stored:
                    pass                             # migration: adopt the hash, do not accuse
                elif stored != cur:
                    marker = "STALE"                 # content moved on; the AI clears the marker
                if hi >= 0:
                    parts[hi] = f"h:{cur}"
                else:
                    parts.append(f"h:{cur}")

            if marker == "STALE":
                stale += 1
            elif marker in ("ORPHAN", "MOVED"):
                orphan += 1
            if len(parts) > 1 and parts[1] == "MISSING-role":
                missing_role += 1
            out.append(rebuild(marker, parts))

        pending[shard] = [out, known, stale, orphan, missing_role]

    # --- pass 2: append lines for files that have none yet ---
    added = t_stale = t_orphan = t_missing = 0
    gstamp, today = git_hash(root), date.today().isoformat()
    for shard in names:
        if shard not in pending and not by_shard[shard]:
            continue
        if shard not in pending:
            pending[shard] = [[], set(), 0, 0, 0]
        out, known, stale, orphan, missing_role = pending[shard]

        missing = [p for p in by_shard[shard] if p not in known]
        if missing and type_index is None:
            type_index = set()
            for rel in on_disk:
                if rel.endswith(".cs"):
                    t = declared_types(read_text(os.path.join(root, rel)))
                    own_types_of[rel] = t
                    type_index |= t

        for rel in missing:  # collect_sources is sorted -> deterministic
            abs_path = os.path.join(root, rel)
            api = public_api(abs_path)
            dep = dep_suggestion(abs_path, type_index or set(), own_types_of.get(rel, set()))
            note = "auto-added; AI must complete"
            old = orphan_api.get(api)
            if old and api != "-" and old != rel:
                note += f"; possible rename/move of {old}"
            out.append(
                f"{rel} | MISSING-role | sys: ? | api: {api} | dep?: {dep} | "
                f"used: ? | crit: ? | note: {note} | h:{sha8(abs_path)}"
            )
            added += 1
            missing_role += 1

        if stale or orphan or missing_role:
            status = f"DEGRADED {stale} stale, {orphan} orphan, {missing_role} missing-role"
        else:
            status = "OK"
        stamp = f"<!-- stamp: {gstamp} {today} status: {status} -->"
        if out and out[0].startswith("<!-- stamp:"):
            out[0] = stamp
        else:
            out.insert(0, stamp)

        with open(os.path.join(claude_dir, f"codemap-{shard}.md"), "w", encoding="utf-8") as f:
            f.write("\n".join(out).rstrip() + "\n")
        t_stale += stale
        t_orphan += orphan
        t_missing += missing_role

    if t_stale or t_orphan or t_missing:
        print(f"[codemap] DEGRADED: {t_stale} stale, {t_orphan} orphan/moved, {t_missing} missing-role, "
              f"{added} line(s) auto-added. Repair the flagged lines, then delete the marker word.")
    else:
        print("[codemap] OK; stamp updated.")
    return 0


if __name__ == "__main__":
    sys.exit(main())
