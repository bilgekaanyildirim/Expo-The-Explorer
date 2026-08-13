# scene-structure — where a thing lives in the editor

Decision type: the editor-side form of a new entity. Option space: **scene
object** (unique, lives in one scene) / **prefab** (reused or spawned) /
**prefab variant** (existing prefab + overrides) / **nested prefab**
(composition of prefabs) / **additive scene** (independent chunk loaded on
demand). The result is recorded in `.claude/blueprint.md` (scene/prefab
inventories); an entity that exists only in the editor, with no blueprint
line, is a procedure violation.

## What must be known

- **What already exists.** Read `.claude/unitymap.md` (hierarchy, components,
  prefab instances, variant-of lines) and `.claude/assetmap.md` (the prefab
  inventory). Deciding "new prefab" without looking at the existing ones is
  how a duplicate gets created where a variant was available.
- Instance count and origin: how many exist, and are they placed at
  authoring time or spawned at runtime? (spawned → prefab, no exception)
- Reuse across scenes? (yes → prefab; copy-pasting objects between scenes
  is a forbidden outcome)
- Is it a variation of an existing prefab? (differs only in serialized
  values/art → variant, never a duplicate)
- Lifecycle: does it survive scene loads? (persistent → the boot scene or
  an explicit persistence decision, recorded in the blueprint)
- Is it a large chunk with its own loading story (world region, minigame)?
  → additive scene; the cost model prices its load frequency.

## Decision logic

- Runtime-spawned, multi-instance, or cross-scene → **prefab**; it enters
  the blueprint's prefab inventory with its owning system.
- Differs from an existing prefab only by data → **variant**. Structural
  difference → new prefab; shared internals → **nested prefab**.
- Unique, single-scene, authoring-placed → **scene object**; it still
  follows the blueprint's hierarchy conventions.
- Independent chunk with its own load timing → **additive scene**, added
  to the scene inventory with its load trigger.

## Wiring handoff — who attaches what

Every editor-side step is declared in the preflight's `Editor tasks` line,
as one of three paths:

1. **Asset authoring** — SO/`.asset` files: Claude writes them directly
   (manifest-gated).
2. **Editor script** — prefab/scene construction as a menu-item Editor
   script under `Assets/Editor/`. The preferred path: reproducible,
   reviewable, no hand-edited YAML.
3. **Manual step list** — a numbered list of editor steps for the user,
   when a script isn't worth it. The postflight verifies the result via a
   regenerated unitymap.

Hand-editing `.unity`/`.prefab` YAML is the last resort and must be
justified in the preflight.

After any of the three, the unitymap is regenerated and read back — the
postflight's editor item is a check against that file, not a memory of what
was intended. With `Assets/Editor/UnityMapExporter.cs` installed, the menu
item `Tools > unity-dev > Export unitymap` produces it with real type
information; otherwise the python fallback runs from the Stop hook whenever
a scene or prefab changed on disk.

## Forbidden outcomes

- A prefab duplicated where a variant would do.
- Content data living on a scene object after `data-source.md` placed it
  in an asset (dual authority).
- Editor work done but undeclared (silent wiring).

## Boundary case (not an exit)

If an entity fits none of the options (fully procedural content, generated
meshes), the runtime construction path *is* the design: record in the
blueprint who constructs it and when, and run the construction frequency
through the cost model.
