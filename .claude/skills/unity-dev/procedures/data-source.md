# data-source — where a piece of information is read from

Decision type: the source of a value. Option space: **bake** (compute at
build/authoring time, write into data) / **derive from authority** (compute at
runtime from the single source) / **runtime query** (ask the engine/scene).

## What must be known

- Who is the **authority** for this information? Read from the authorities
  section of `fingerprint.md`. If it's not defined there → STOP, produce an
  OPEN QUESTION. No data decision is made without an authority.
- How often does the information change? (never / per level / rarely in-game
  / continuously)
- How many places read it, and at what frequency? (input to the cost model)

## Decision logic

- Information that never changes, or is fixed per level → **bake**. Nothing
  that can be computed at authoring time is recomputed at frame frequency.
- Information derivable from the authority and rarely changing → **derive +
  update on event** (see `recompute-timing.md`).
- A runtime query is a **last resort**: only when the information genuinely
  comes into existence at runtime. An engine query at frame frequency is two
  tiers more expensive in the cost model — if it crosses the threshold, the
  design changes.

## Forbidden outcomes

- Holding the same information in two places (dual authority). If the outcome
  requires it, the decision is wrong; go back to `ownership.md`.
- Embedding content data in code (numbers, curves, tables, text). Content
  goes into data (asset/JSON/SO); code reads it.

## Boundary case (not an exit)

If the situation does not fit this trio, do not force the tree: compute with
the cost model and write the rationale into the preflight. A boundary case
is never silent — undeclared deviation from the option space is a procedure
violation.
