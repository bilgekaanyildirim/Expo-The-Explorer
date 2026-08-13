# abstraction-level — justifying the addition of abstraction

This procedure runs in the opposite direction: the others look for "missing
discipline", this one looks for "over-engineering". The default answer is
**do not add abstraction**; the side adding it owes the justification.

## What must be known

- Repetition count: in how many places does this pattern currently exist?
  (not a guess; counted from the codemap)
- Evidence of change: is there a concrete sign in `scope.md` or
  `decisions.md` that this point will change?

This gate is phase-independent: release-grade quality never means
speculative layers. The burden of proof sits on repetition and recorded
evidence in every phase.

## Decision logic

- Repetition < 2 and no evidence of change → no abstraction. Write concrete
  code.
- Repetition ≥ 3 → extraction is legitimate; choose the narrowest
  abstraction (a function if a function suffices, not an interface).
- "Might be needed later" is not a justification on its own. Adding it when
  it is actually needed is cheaper than carrying the wrong abstraction
  today.
- A generic solution is preferred over a specific one only if it makes no
  difference under the cost model AND it already has at least one existing
  second consumer.

## Common mistake

Adding configurability (parameters, settings, options) is also abstraction
and is subject to the same justification rule. A setting nobody will ever
change is dead weight being carried.

## Boundary case (not an exit)

Extension points that are unavoidable for the genre (e.g. the path for
adding a new content type in a content-driven game) count as justified if
they were written into the system plan at bootstrap; if not, add them to the
plan first (a recorded decision), then abstract. An extension point that
exists only in code, with no plan entry, is a procedure violation.
