<!-- stamp: 2026-08-19T09:07Z source-sig:empty scenes:0 prefabs:0 generator:python-fallback status: OK -->
# unitymap — scene and prefab structure

> **UNMAPPED — this map is empty and that is a TOOLING BUG, not an empty project.**
> `build_unitymap.py` resolves scenes under `<project_root>/Assets`, but this repo nests the
> Unity project one level down at `ExpoTheExplorer/Assets`, so the walker finds nothing and
> stamps `scenes:0` while three canvases and four scenes exist on disk. The same root cause is
> why `check_blueprint.py` prints "no Assets/ directory yet" and `assetmap.md` reports 0 prefabs.
> Do not trust this file for scene questions until that is fixed — parse the `.unity` directly,
> or run the generator with `ExpoTheExplorer` as its project-root argument (its output paths are
> then relative to that root, which does not match the codemap's `ExpoTheExplorer/Assets/...`
> style, so it is not a drop-in fix). Recorded 2026-08-19 while doing D-013; the generator fix is
> a separate task with its own blast radius.

Read this instead of opening a `.unity`/`.prefab` file. Tree indentation is
the GameObject hierarchy; `[...]` lists the components on the object;
`refs:` lists serialized reference slots and whether the Inspector has
something in them. `*` marks a prefab instance.

Staleness: `source-sig` is derived from scene/prefab mtimes. Regenerate with
`python3 .claude/hooks/build_unitymap.py` or, for real type information, the
Unity menu item Tools > unity-dev > Export unitymap.
