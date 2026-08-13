# preflight — pre-code declaration format

Before every task that writes code, `.claude/preflight/current.md` is produced
in this format and user approval is awaited. Approval is the message where the
user writes `APPROVE` **on its own** (the token appearing inside free text
does not count). The hook enforces this: writing a protected file
(`.cs`, `.asmdef`, `.unity`, `.prefab`, `.asset`) without approval or outside
the manifest is blocked; if this file changes after approval, the approval
lapses and is requested again.

## Scope: writing, not reading

The manifest lists the files **to be written**, and the hook enforces that.
Reading is free (the codemap routes it); "do not open undeclared files" is
not a reading ban.

## Format

The narrative part is ~10 lines (excluding the manifest). The manifest is
machine-read by the hook: under the `## Manifest` heading, each line starts
with `- `, is relative to the project root, and is an exact path with no
globs. The format of this section must not be changed.

```markdown
# Preflight: <task name>

- Task: <one sentence>
- Phase: <production|shipping>
- Enforcement: <the map-health report was in context at session start: yes/no>
- Located: <locate.md step that answered + the system name from index.md>
- Attachment point: <which existing system, and where>
- Map repairs: <STALE/ORPHAN/UNMAPPED lines this task must fix>  ← "-" if none
- Won't change: <things deliberately left untouched>
- Procedures: <ones applied + their one-line outcomes>
- Data source: <data-source decision, if any>
- Editor tasks: <wiring plan — assets Claude writes / Editor script /
  numbered manual steps for the user>  ← "-" if the task has no editor side
- Assumptions: <everything left uncertain + impact if wrong>  ← "-" if empty
- Risks: <one line>

## Manifest
- Assets/Scripts/Gameplay/ExampleSystem.cs
- Assets/Scripts/Gameplay/ExampleData.cs
```

## Rules

- No code is written before approval. If the task grows, the preflight is
  updated and approved **again** (the hook enforces this anyway, since the
  hash lapses).
- `Enforcement: no` stops the task. No map-health report in context means the
  SessionStart hook did not run, which means none of the others did either —
  the gate that this document relies on is not there. A hook that is off cannot
  say so; the absence of its output is the only signal, so it is checked here.
- The Assumptions section is never trimmed to fit a line limit; neither is
  the manifest. If it gets long, split the task.
- Full example: `examples/01-preflight.md`.
