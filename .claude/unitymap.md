<!-- stamp: 2026-08-18T12:50Z source-sig:empty scenes:0 prefabs:0 generator:python-fallback status: OK -->
# unitymap — scene and prefab structure

Read this instead of opening a `.unity`/`.prefab` file. Tree indentation is
the GameObject hierarchy; `[...]` lists the components on the object;
`refs:` lists serialized reference slots and whether the Inspector has
something in them. `*` marks a prefab instance.

Staleness: `source-sig` is derived from scene/prefab mtimes. Regenerate with
`python3 .claude/hooks/build_unitymap.py` or, for real type information, the
Unity menu item Tools > unity-dev > Export unitymap.
