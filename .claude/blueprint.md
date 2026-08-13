# blueprint — architecture plan: systems, scenes, prefabs, folders

<!-- The AI drafts it at bootstrap, the USER approves it. After that it
     changes only through preflight-declared tasks. This file is the single
     authority for "what goes where" — codemap, unitymap and assetmap all
     hang off it, and index.md is built by joining them to it.

     It is MACHINE-CHECKED: `python3 .claude/hooks/check_blueprint.py`
     compares every section below against the disk and against the codemap
     `sys:` fields, and the postflight quotes its output. A system name here
     is therefore a contract, not a label — spell it in `sys:` exactly as it
     is spelled here. -->

## Systems and dependencies

<!-- system → system arrows, ONE-directional. A bidirectional arrow is an
     ownership problem: apply procedures/ownership.md before writing it
     down. One line per system: name — responsibility — depends-on. -->
- <System — one-line responsibility — depends on: a, b>
- DayEditor — custom EditorWindow tooling for authoring Day JSON content (ticket sequence, board timeline, retry variants) — depends on: -
- BoardUI — runtime board grid rendering + drag/drop (BoardView, board-visual config assets) — depends on: -

## Scene inventory

<!-- Every scene, starting with the boot/persistent scene. One line each:
     name — role — load mode (single | additive + trigger) — what lives
     in it. -->
- <Boot — persistent systems, never unloaded — single — GameManager, Audio>

## Prefab inventory

<!-- Every prefab: name — owning system — variant-of (or -) — where it is
     instantiated from (authoring | spawner). scene-structure.md decides
     what becomes a prefab. -->
- <EnemyGrunt — CombatSystem — variant-of: - — spawned by WaveSpawner>

## Hierarchy conventions

<!-- Per-scene root objects and naming. Keep runtime-moved objects shallow. -->
- <root objects: --Systems--, --World--, --UI-- ; naming: PascalCase, no spaces>

## Folder layout

<!-- Canonical tree. It and `.claude/shards.json` describe the same folders
     from two sides: change one, change the other in the same task, or
     every file lands in the catch-all shard. A new file with no place in
     this tree is a blueprint update FIRST, a file second.
     check_blueprint.py reports folders on disk that no line here covers. -->
```
Assets/
  Scripts/Core/        ← .cs: game flow, save, shared services (codemap: core)
  Scripts/Gameplay/    ← .cs: mechanics, systems (codemap: gameplay)
  Scripts/UI/          ← .cs: UI code (codemap: ui)
  Scripts/Content/     ← .cs: content-driving code (codemap: content)
  Editor/              ← editor-only tools and construction scripts (codemap: editor)
  Data/<Type>/         ← ScriptableObject assets, by content type (assetmap)
  Prefabs/<System>/    ← prefabs, grouped by owning system (assetmap + unitymap)
  Scenes/              ← .unity files (mirrors the scene inventory)
  Art/  Audio/         ← imported assets, by type
```

<!-- `Data/` holds assets, not scripts, so it is inventoried by assetmap.md,
     not by a codemap shard. `Resources/` and `StreamingAssets/` are absent
     on purpose: everything in them ships in every build and is loaded by
     string. Adding one is an architectural decision — it goes to
     decisions.md with its `affects:` field, not into this tree quietly. -->
