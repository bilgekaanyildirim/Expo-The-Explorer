# reference-binding — how a reference is obtained

Decision type: how one object gains access to another. Option space:
**serialize** (bind in the editor) / **inject** (the constructing code
provides it) / **service** (from a central registry) / **runtime lookup**
(find it in the scene).

## What must be known

- What is the dependency direction? Does it match the arrow in the system
  plan? A reference against the arrow direction is a plan error — fix the
  plan before writing code.
- What are the two objects' lifecycles? (same prefab? same scene? is one
  persistent?)
- How many times is the reference established? (once? per object? per
  frame?)
- **Can the editor already reach it?** Read `.claude/unitymap.md`: it lists
  every serialized reference slot and whether the Inspector has something in
  it. A slot that exists and is `NULL` means the binding was designed and
  simply not wired — the fix is a wiring step, never a runtime lookup added
  to compensate. This is the most common way a frame-frequency scene query
  gets written for no reason.

## Decision logic

- Within the same prefab/scene, known at authoring time → **serialize**. The
  cheapest and most visible bond.
- The constructing code is known (spawner, factory) → **inject**: the
  constructor hands its construct the dependencies.
- A singleton system with many consumers → **service**; but as the number of
  services grows, hidden dependencies grow — `abstraction-level.md` demands a
  justification.
- **Runtime lookup is the last resort**, legitimate only at load/event
  frequency. A lookup at frame frequency sits at the engine-query tier of
  the cost model and almost always crosses the threshold.

## Forbidden outcomes

- Re-looking up a reference on every use. Even when a lookup is legitimate,
  its result is established once and cached.
- A runtime lookup standing in for an unwired serialized slot. If `unitymap.md`
  shows the slot as `NULL`, the deliverable is the wiring step (a preflight
  `Editor tasks` line), not code that searches the scene.

## Boundary case (not an exit)

If the lifecycles do not overlap (the source lives while the reference
target does not exist), no option is safe → it is a design problem; produce
an OPEN QUESTION. Papering over it with a null check is a procedure
violation, not a solution.
