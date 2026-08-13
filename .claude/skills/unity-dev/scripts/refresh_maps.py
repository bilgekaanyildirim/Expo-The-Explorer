#!/usr/bin/env python3
"""refresh_maps.py — Stop hook: bring every map back in step, in order.

Order matters and hooks inside one group are not ordered, so all four run in a
single process instead of four hook entries:

  1. build_codemap   — code lines, hashes, STALE/ORPHAN/MOVED markers
  2. build_unitymap  — scene/prefab structure, but only if a scene or prefab
                       actually changed on disk (--if-stale)
  3. build_assetmap  — asset inventory, same staleness guard
  4. build_index     — the join; it must run last, on fresh inputs

Output is one line per map that had something to say. Always exits 0: the Stop
hook is a repair pass, not a gate, and a non-zero exit here would fight the
turn instead of fixing the map.

Usage: python3 refresh_maps.py [project_root]
"""
import io
import contextlib
import os
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)


def project_root() -> str:
    args = [a for a in sys.argv[1:] if not a.startswith("--")]
    if args:
        return os.path.abspath(args[0])
    return os.path.abspath(os.environ.get("CLAUDE_PROJECT_DIR", os.getcwd()))


def run(module_name, argv, root):
    """Run one builder's main() with a controlled argv; never let it break the turn."""
    buf = io.StringIO()
    saved = sys.argv
    sys.argv = [module_name + ".py", root] + argv
    try:
        mod = __import__(module_name)
        with contextlib.redirect_stdout(buf):
            mod.main()
    except Exception as exc:                      # a broken map is reported, not raised
        return f"[{module_name}] skipped: {type(exc).__name__}: {exc}"
    finally:
        sys.argv = saved
    return buf.getvalue().strip()


def main() -> int:
    root = project_root()
    if not os.path.isdir(os.path.join(root, ".claude")):
        return 0                                  # not a unity-dev project
    print(run("build_codemap", [], root))          # always: it is the turn's own output
    run("build_unitymap", ["--if-stale", "--quiet"], root)
    run("build_assetmap", ["--if-stale", "--quiet"], root)
    idx = run("build_index", [], root)
    if idx and "0 unmapped, 0 file(s) with sys: ?" not in idx:
        print(idx)                                 # only when the join left something dangling

    # The editor-side drift marker is cleared here and only here: the maps just
    # caught up with the files it was pointing at. Clearing it anywhere earlier
    # would hide the drift from the turn that needed to know about it.
    try:
        os.remove(os.path.join(root, ".claude", "map-drift"))
    except OSError:
        pass
    return 0


if __name__ == "__main__":
    sys.exit(main())
