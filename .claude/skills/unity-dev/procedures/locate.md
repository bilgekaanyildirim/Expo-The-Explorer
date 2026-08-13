# locate — turning a request into a file set

Decision type: **where** a task lives, before anything is read in full. Runs
as step 0 of every task, including bug fixes and content work. Its output
feeds the preflight's `Attachment point` and `Manifest` lines directly.

The maps exist so that a request never costs a full scan. Skipping this
procedure and reaching for `grep`/`Glob` is the single most expensive
mistake available in this skill.

## Fixed search order — stop at the first step that answers

1. `.claude/index.md` — system/feature name → shard, entry files, scenes,
   data folder. One screen. Read it first, every time.
2. `.claude/blueprint.md` — the system line, its dependency arrows, the
   scene and prefab inventories. Answers "what does this touch".
3. `.claude/codemap-<shard>.md` — the file lines: `sys:` narrows to the
   system, `api:` gives the signatures, `dep:`/`used:` give the call graph,
   `crit:` gives the blast radius.
4. `.claude/unitymap.md` — only when the answer is editor-side: which object
   carries the component, which serialized reference is unassigned.
   `.claude/assetmap.md` — only when the answer is an asset: which `.asset`
   files exist for a type, what lives under `Resources/`.
5. `grep`/`Glob` — **last resort**, and only inside the paths step 3
   narrowed to. A repo-wide search is never step 5's first move.

## What must be known before the search ends

- The **target files** (to be read) and the **manifest files** (to be
  written) — these are different sets; the manifest is a subset or a
  superset, never assumed equal.
- The **owning system** for every target file (`sys:` field). A file whose
  system cannot be named is an unmapped file: fix its codemap line first.
- The **attachment point**: the existing system this work hangs off, and the
  precise place in it (file + member, or scene object + component).

## Output format

```
Locate: <request in a few words>
- Found at step: <1..5>            ← 5 means the maps failed; say so out loud
- System: <name from index/blueprint>
- Read: <paths>                     ← what will be opened
- Write: <paths>                    ← becomes the preflight manifest
- Editor side: <scene/prefab/asset, or ->
- Unresolved: <what no map could answer>
```

## Map degradation is a finding, not an excuse

If a map is marked `DEGRADED`, `STALE`, `ORPHAN`, `MOVED` or `UNMAPPED` on
the path this task needs, repairing that line **is part of the task** and is
declared in the preflight. Working around a wrong map silently is a
procedure violation; the next task inherits the same wrong map.

## Forbidden outcomes

- A repo-wide `grep`/`Glob` before steps 1–4 were tried.
- Opening a file "just to check" that no map pointed at.
- A preflight whose `Attachment point` is a guess rather than a located line.

## Boundary case (not an exit)

If steps 1–4 genuinely cannot answer — a brand-new system with no
attachment point, or a project whose maps were never built — say which step
failed, run the narrowest possible search, and record the gap: a new
`index.md` row, a blueprint system line, or a codemap repair. The map is
better after the task than before it.
