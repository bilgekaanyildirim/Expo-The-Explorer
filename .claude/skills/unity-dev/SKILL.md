---
name: unity-dev
description: >-
  Use for ALL of Unity game development — bootstrapping a project from scratch,
  adding new systems or features, writing C# gameplay/UI/data code, bug fixes,
  content and level work, refactoring, optimization, and phase transitions.
  Whenever the user mentions a Unity game, a C# script, a MonoBehaviour, a
  prefab, scene, ScriptableObject, or a game mechanic — even without saying
  "skill" or "unity-dev" — this skill must be used. When a new game idea is
  described, bootstrap it with this skill as well. It provides decision
  discipline: a cost model, preflight/postflight gates, and codemap routing.
---

# unity-dev

Decision discipline for building Unity games from scratch, at professional
quality and optimized. This skill carries no canned answers; it carries the
calculation that leads to the answer.

## Eight principles (one-line versions)

1. **Mechanism > rule list** — decisions are produced from the cost model below, not from memorized rules.
2. **Distillation** — an expensive source (web, MCP, code scan) is read once, distilled, and from then on everything goes through the file.
3. **Declarative > imperative** — say "this must be known", don't impose step-by-step scripts.
4. **Stop on uncertainty** — guessing on undefined information is forbidden; produce an OPEN QUESTION or declare the assumption in the preflight.
5. **No prototype mode** — quality is release-grade from day zero. Phases change only the proof standard for optimization (computed threshold → recorded measurement), never the quality bar; over-engineering is policed by `abstraction-level.md`, not by a lax phase.
6. **Enforcement via hooks** — preflight and codemap are not instructions, they
   are hook guarantees. Hooks live in the project's `.claude/settings.json`
   (installed by `init_project.py`) — never in this file's frontmatter:
   settings hooks run in every session and inside subagents; frontmatter hooks
   only run while the skill is active, which is a gap.
7. **Never build on unverified enforcement** — `scripts/test_enforcement.sh`
   must pass, and `/hooks` must list all seven hook events, before the first
   task. Two silent-failure modes are checked by observation, not by a hook,
   because a disabled hook cannot report itself:
   - **No map-health report in this context** means the SessionStart hook did
     not run — `disableAllHooks` is set somewhere, or the hooks were never
     installed. Enforcement is off; say so and write no code until it is back.
   - **No `.claude/unity-dev.json`** means the guard treats this checkout as an
     ordinary project and allows every protected write. It is the committed
     marker that arms the gate; a clone without it is unarmed.
8. **A map that lies is worse than no map** — every map carries a status, and
   a `DEGRADED`/`STALE`/`UNMAPPED` line on the path a task needs is repaired
   as part of that task, declared in its preflight.

## The subagent boundary

A subagent is inside the gate, not outside it: settings hooks fire on its tool
calls too, so a subagent cannot write a file the manifest does not name. What it
can do is read differently — the built-in `Explore` and `Plan` agents skip the
`CLAUDE.md` hierarchy by design, so a "where does X live" question handed to one
of them is answered by scanning, not by locate.

Two things close that, and both are installed by `init_project.py`:

- the `SubagentStart` hook states the map order and the current map health to
  **every** agent, including the built-ins, which no other mechanism reaches;
- `.claude/agents/Explore.md` overrides the built-in `Explore` with a
  map-first locator whose output is in `locate.md` format.

The consequence for a task: delegated searching counts as searching. When the
postflight says "located, not scanned", it covers what a subagent did on the
task's behalf, and the locate step the subagent stopped at is reported with it.

## Loading channels and reading order

| Source | When |
|---|---|
| Project-root `CLAUDE.md` (invariants + phase + fingerprint summary) | Already loaded — every session |
| Map-health report (SessionStart hook) | Already in context — every session, and re-injected after every compaction (SessionStart also fires with `source: compact`). Its **absence** means enforcement is off — see principle 7 |
| Editor-drift notice (`[maps] … changed on disk`) | Alongside a prompt, when scenes/prefabs/assets changed outside a map refresh |
| This file (router + cost model) | When the skill triggers |
| `procedures/locate.md` | **Step 0 of every task** |
| `.claude/index.md` | Step 1 of locate — always, before any other map |
| `procedures/<name>.md` | When the router selects it |
| `.claude/blueprint.md` | Step 2 of locate; and whenever a task adds a file/object/scene/prefab |
| `.claude/codemap-<shard>.md` | Step 3 of locate |
| `.claude/unitymap.md`, `.claude/assetmap.md` | Step 4 of locate — editor side / asset side only |
| `.claude/fingerprint.md` | When a procedure needs it |
| `engine-facts/<topic>.md` | When a decision rests on an engine fact |
| `gates/preflight.md`, `gates/postflight.md` | Task start / task end |
| `examples/` | When unsure about a format |

Open only what locate + the router select. A file already read this session
stays in context — never re-read it unless its staleness stamp may have
changed. Do not open arbitrary files "just to check". A repo-wide `grep`/`Glob`
before locate steps 1–4 were tried is a procedure violation, not a shortcut.

## Hard behavior rules

- **Never write** to a file not declared in the preflight. Manifest = files
  to be written. Reading is free but routed through `locate.md`; do not open
  arbitrary files "just to check".
- Protected types — `.cs`, `.asmdef`, `.unity`, `.prefab`, `.asset` — are
  never written without approval. Approval is produced only by the
  user sending `APPROVE` as a standalone message; Claude never writes to
  `.claude/preflight/approved` under any circumstances (the hook blocks it
  anyway).
- C# changes are made only with the Edit/Write tools; writing files via Bash
  (`>`, `sed -i`, `tee`, `mv`, `cp`) is forbidden.
- MCP calls are made only for map generation/updates; MCP is off during code
  tasks.
- Every new file lands where the folder layout in `.claude/blueprint.md`
  says it does. If it has no place there, the blueprint is updated first
  (declared in the preflight) — files are never parked "temporarily".
- Postflight is never skipped. At the end of every task that wrote code, an
  audit is produced in the `gates/postflight.md` format.
- When code is written, the codemap line is written **in the same turn**
  (schema below). The PostToolUse hook names the file whose line is missing
  or unfinished; that message is acted on in the same turn, not deferred.
- A map is never "fixed" by deleting the inconvenient line. Markers are
  cleared by repairing the line's content, and `STALE`/`ORPHAN`/`MOVED` are
  never written by hand.
- If a procedure cannot produce an answer, the output is an OPEN QUESTION;
  guessing is forbidden.

## The four maps

| Map | Layer it answers | Written by |
|---|---|---|
| `.claude/index.md` | system → shard, entry files, scenes, prefabs, data | `build_index.py` (a join; never invents a name) |
| `.claude/codemap-<shard>.md` | code: files, API, dependencies, criticality | the AI; `build_codemap.py` audits it |
| `.claude/unitymap.md` | scene/prefab tree, components, unassigned refs | `build_unitymap.py`, or the Unity Editor exporter |
| `.claude/assetmap.md` | `.asset` inventory, load surface, assemblies | `build_assetmap.py` |

`.claude/blueprint.md` is the plan the four maps are checked against;
`check_blueprint.py` reports the drift and the postflight quotes it.

## Codemap line schema (this is the single place it is defined)

```
[STALE|ORPHAN|MOVED ]<path> | <role, 3-6 words> | sys: <system> | api: <sig1; sig2> | dep: <a,b> | used: <c,d> | crit: K1|K2|K3 | note: <- or short note> | h:<sha8>
```

- `sys`: the owning system, exactly as blueprint.md names it. This is what
  makes `index.md` and locate step 1 work; `sys: ?` is an unfinished line.
- `crit`: K1 = core (game won't boot if broken), K2 = system, K3 = leaf/content.
- An empty field is written as `-`; fields are never omitted.
- **Script-owned fields — never hand-written:** the leading marker and `h:`.
  `dep?:` is a script *draft* of the dependency field; confirming it (and
  renaming it to `dep:`) is the AI's job.
- Marker meanings: `STALE` = the file changed after the line was written;
  `ORPHAN` = the file is gone; `MOVED` = the file now belongs to another
  shard. Repair the semantic fields, then delete the marker word — the
  script re-adds it only if the content moves again.
- The file header carries a staleness stamp:
  `<!-- stamp: <git-hash> <ISO-date> status: OK|DEGRADED n stale, m orphan, k missing-role -->`.
  `OK` is earned, not stamped by default.

**Shard definition lives in `.claude/shards.json`** — path pattern → shard,
first match wins. It is the single source: `build_codemap.py` and
`build_index.py` read it, and this file does not repeat the mapping. Default
shards: `editor`, `ui`, `gameplay`, `content`, `core`.

## ROUTER — step 0, then task type → procedures + shard

**Step 0, every task type without exception:** run `procedures/locate.md`.
Its output names the shard, so the "Codemap shard" column below is a check on
the result, not a substitute for it.

| Task type | Mandatory procedures | Conditional | Codemap shard |
|---|---|---|---|
| bootstrap (new game) | `locate.md`, `bootstrap.md` | — | none (no code yet) |
| new system | `locate.md`, `data-source.md`, `ownership.md`, `reference-binding.md` | `abstraction-level.md`; `scene-structure.md` if it has scene presence | shard from locate + core |
| feature addition | `locate.md`, `data-source.md`, `recompute-timing.md` | `reference-binding.md`; `scene-structure.md` if a new object/prefab appears | shard from locate |
| bug fix | `locate.md` (root-cause analysis is free-form after it) | `recompute-timing.md`, `data-source.md` if relevant | shard from locate |
| content / level | `locate.md`, `data-source.md` | `scene-structure.md` for new scenes/prefabs | shard from locate; assets via `assetmap.md` |
| refactor | `locate.md`, `abstraction-level.md`, `ownership.md` | — | shard from locate + core |
| phase-transition audit | `phase-transition.md` | — | all |

Convention routing is not the router's job — the path-scoped rules under
`.claude/rules/` load automatically when a matching file is touched, and they
are re-loaded after a compaction (`InstructionsLoaded` fires with
`load_reason: compact`). What they are is **lazy**: a rule does not reach
context until something matching its `paths:` is touched. So a rule that has to
bind a decision made *before* the first matching file is opened — a data
authority, a dependency direction — belongs in the preflight as well. The rest
stay where they are; copying every rule into the preflight only makes the gate
longer to read.

**Phase → proof standard** (two phases; both are release-grade):

| Phase | Standard |
|---|---|
| production (day zero → release candidate) | Procedures + cost threshold applied in full; the hook denies violations. |
| shipping (release hardening) | Measurement replaces the threshold: every optimization carries a profiler number; baseline regressions block. |

There is no prototype phase. A new project starts in `production` and the
first task is already written at release quality; the move to `shipping`
runs only through `phase-transition.md`.

**Shard selection:** resolved from `.claude/shards.json`, not from memory. If
a touched path matches no pattern, the catch-all shard takes it and the
mismatch is noted in the postflight — a path that keeps landing in the
catch-all is a missing `shards.json` entry, not a fact of life.

## COST MODEL

Decisions are produced from this calculation, not from memory. It is
engine-agnostic; it contains no engine API names — Unity-specific numeric
facts live in `engine-facts/`.

**Formula:** `Cost = Frequency × Scale(n) × UnitCost`

**Frequency orders of magnitude** (rough multiplier):

| Order | Typical | Multiplier |
|---|---|---|
| frame | ~60/s | ×60 |
| physics step | ~50/s | ×50 |
| event | <1/s | ×1 |
| load | 1 per scene | ×0.01 |
| editor | 0 at runtime | ×0 |

**Scale classes:** constant O(1) / linear O(n) / quadratic O(n²). `n` is not
guessed; it is read from the scale section of `fingerprint.md`. If it's not
there, it is an OPEN QUESTION.

**Unit-cost ordering** (cheapest to most expensive; assume each tier ≈ ×10):
memory read → arithmetic → virtual call → engine query → allocation →
serialization → I/O.

**Decision threshold:** If the computed cost ratio between two designs is
**<10×** and the more expensive one's absolute load is below ~1% of the frame
budget (the budget is the performance-budget field of `fingerprint.md`; if it
is OPEN, that is an OPEN QUESTION), choose the **more readable** one. If the ratio is **≥10×** and the
work runs at frame/physics frequency, choose the **cheaper** one. In the zone
between, the decision is written into the preflight with its rationale.

**Phase rule:** the threshold applies in full from day zero (production). In
shipping, measurement replaces the threshold. The structural prohibitions —
content is never embedded in code, no dual authority is created — are
absolute in every phase.

**Evidence rule:** No optimization is applied without a numeric argument (via
this formula) or a profiler measurement. An unmeasured suggestion is presented
only with the label "hypothesis".

## Procedures

Opened from `procedures/` by decision type: `locate.md` (**step 0** — where
the task lives, before anything is read), `bootstrap.md` (setup from
scratch), `data-source.md` (where a piece of information is read from),
`recompute-timing.md` (when a computation runs), `reference-binding.md` (how a
reference is obtained), `ownership.md` (who writes/reads a piece of data),
`abstraction-level.md` (justification for abstraction), `scene-structure.md`
(the editor-side form of an entity: scene object / prefab / variant /
additive scene, plus the wiring handoff), `phase-transition.md` (entry
criteria and audit for moving between phases). The router above decides
which ones to open.

## Map tooling

| Script | Does |
|---|---|
| `init_project.py` | installs the skeleton, hooks, shards.json, the enforcement marker and the `Explore` agent; stages the Editor exporter. Refuses to install without bash + python3 + sha256 |
| `refresh_maps.py` | the Stop hook: codemap → unitymap → assetmap → index, in that order; then clears the drift marker |
| `session_context.py` | the SessionStart hook: map health into context, and the `watchPaths` list that makes editor-side drift detectable |
| `subagent_context.py` | the SubagentStart hook: the map order and current map health for every subagent, built-ins included |
| `mark_map_stale.py` | the FileChanged hook: records a scene/prefab/asset changed on disk to `.claude/map-drift`; never rebuilds a map |
| `map_drift_notice.sh` | a UserPromptSubmit hook: reports that drift on the next prompt (FileChanged itself cannot reach Claude) |
| `build_codemap.py` | audits code lines: hashes, `STALE`/`ORPHAN`/`MOVED`, `dep?:` drafts |
| `build_unitymap.py` | scene/prefab tree from YAML (fallback); the Editor exporter is the real path |
| `build_assetmap.py` | `.asset` types, prefabs, `Resources/`, assemblies |
| `build_index.py` | joins blueprint + codemap + assetmap into `index.md` |
| `check_blueprint.py` | blueprint vs disk vs codemap; exits 1 on ERROR — the postflight quotes it |
| `test_enforcement.sh` | the regression suite; must pass before the first task |

For installation and hook placement, see the docstring at the top of
`scripts/init_project.py`; for format examples, see the `examples/` folder.
