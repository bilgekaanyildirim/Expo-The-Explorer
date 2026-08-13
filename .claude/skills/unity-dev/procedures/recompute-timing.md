# recompute-timing — when a computation runs

Decision type: computation timing. Option space: **every frame** / **physics
step** / **on event** (when an input changes) / **on load** (once) / **in
editor** (authoring time).

## What must be known

- How often do the computation's inputs change? (this is the deciding
  question)
- What does the computation cost? (via the cost model: scale × unit cost)
- How much staleness of the result is tolerable? (1 frame? 1 second? none?)

## Decision logic

- If input change frequency < read frequency → compute **when the input
  changes** (on event) and cache the result. Recomputing every frame is
  legitimate only when the inputs genuinely change every frame.
- If the inputs never change after load → **on load**, once.
- If the inputs are known at authoring time → **in editor**; see
  `data-source.md`, bake.
- Computation that interacts with physics aligns to the physics step;
  computation that interacts with visuals aligns to the frame; the two are
  never mixed.

## Common mistake

The "check every frame, act if changed" pattern is usually an expensive
imitation of an event subscription. The code that produces the change is
known (see `ownership.md` — the writer); the writer publishes the change
event, the reader subscribes.

## Boundary case (not an exit)

If the input change frequency cannot be known (external system, uncertain
user input), put the most expensive realistic scenario into the cost model
and declare that scenario in the preflight assumptions; if it crosses the
threshold, produce an OPEN QUESTION.
