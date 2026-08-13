#!/usr/bin/env python3
"""mark_map_stale.py — FileChanged hook: record editor-side drift, nothing more.

The gap this closes. refresh_maps.py runs on Stop, so the maps are correct at
the end of every turn Claude takes. They are not correct while the user is in
the Unity Editor: adding an object to a scene, wiring a component, editing an
.asset changes the ground truth without Claude noticing, and the next turn
reasons from a map that quietly stopped being true.

The watch list comes from session_context.py's `watchPaths` (absolute paths to
the scenes, prefabs and .asset files). The FileChanged matcher is not used for
it: that field builds its watch list from literal filenames in the working
directory and accepts only letters, digits, `_` and `|`, which cannot express a
path.

What this hook does NOT do:
  - It does not regenerate any map. That is expensive, and a hook that writes
    maps behind the user's back is exactly the surprise the skill avoids.
  - It does not talk to Claude. FileChanged has no decision control and its
    stderr goes to the user only. The notice reaches Claude one step later,
    through map_drift_notice.sh on UserPromptSubmit.

So it appends one line per changed file to .claude/map-drift, de-duplicated by
path, and refresh_maps.py deletes that file once the maps are actually rebuilt.

Always exits 0: a drift record that fails is not a reason to disturb anything.
"""
import json
import os
import sys

MAX_LINES = 200   # a bulk import should not turn the marker into a log file


def main() -> int:
    try:
        payload = json.loads(sys.stdin.read() or "{}")
    except Exception:
        return 0

    path = payload.get("file_path") or ""
    event = payload.get("event") or "change"
    if not path:
        return 0

    root = (os.environ.get("CLAUDE_PROJECT_DIR")
            or (sys.argv[1] if len(sys.argv) > 1 else "")
            or payload.get("cwd") or os.getcwd())
    root = os.path.abspath(root)
    cdir = os.path.join(root, ".claude")
    if not os.path.isdir(cdir):
        return 0

    rel = os.path.relpath(path, root).replace("\\", "/")
    if rel.startswith(".."):
        return 0   # outside the project; not ours to map

    marker = os.path.join(cdir, "map-drift")
    seen = {}
    try:
        with open(marker, encoding="utf-8") as f:
            for line in f:
                p, _, e = line.strip().partition("\t")
                if p:
                    seen[p] = e or "change"
    except OSError:
        pass

    seen[rel] = event
    items = list(seen.items())[-MAX_LINES:]

    try:
        with open(marker, "w", encoding="utf-8") as f:
            for p, e in items:
                f.write(f"{p}\t{e}\n")
    except OSError:
        pass
    return 0


if __name__ == "__main__":
    sys.exit(main())
