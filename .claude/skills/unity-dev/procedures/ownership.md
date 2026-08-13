# ownership — who writes a piece of data, who reads it

Decision type: data ownership. For every piece of data, one question: **who
is the writer, who are the readers?**

## What must be known

- The data's authority (`fingerprint.md`) — ownership cannot contradict the
  authority.
- Writer candidates. If more than one writer emerges → **STOP**. Code writing
  halts; either the owner is made singular, or write requests flow to the
  single owner as commands.
- The readers and their read frequencies (input to the cost model).
- Is there a network model? If the network field of `fingerprint.md` is
  filled, this procedure's output changes fundamentally: the owner becomes a
  machine + system pair, and the local dual-write ban becomes the
  authority/replica split on the network. If the network field is `OPEN` and
  the system will touch the network → OPEN QUESTION.

## Decision logic

- Single writer + many readers → the natural state; the writer publishes the
  change event.
- If write requests come from multiple places → the requests are commands,
  queued to the owner; the owner resolves ordering and conflicts.
- If a reader "needs" to modify what it reads → it is not a reader; the
  design is partitioned wrongly — go back to the system plan.

## Output

The decision is written to `decisions.md` as one line: data → owner →
readers. Consistency with the arrows in the system plan is checked.

## Boundary case (not an exit)

If ownership genuinely must be shared (e.g. the physics engine and game code
both drive the same transform), this is a known conflict pattern: draw the
boundary explicitly (who drives, when), record it in `decisions.md`, and if
the relevant `engine-facts/` block doesn't exist, produce an OPEN QUESTION.
A shared-ownership boundary left implicit in code is a procedure violation.
