# phase-transition — the production → shipping audit

The phase ladder has exactly two steps, and both are release-grade:
**production** (day zero — there is no prototype phase) and **shipping**
(release hardening — measurement replaces the cost threshold). The phase
line in root `CLAUDE.md` changes only through this procedure; it is never
edited casually mid-task. Input: the trigger (user request or a drift
signal). Output: an audit report + the updated phase line + a
`decisions.md` entry.

## Audit

Runs in full for the transition, and **on demand as a checkpoint** (same
report, phase unchanged) — recommended at milestones such as the
vertical-slice boundary in `scope.md`:

1. Violation scan across all procedures for the code written so far — all
   codemap shards, `crit: K1|K2` files first.
2. Every `OPEN` field in `fingerprint.md` that shipping depends on is
   closed with the user, or the transition is blocked.
3. All postflights are reviewed; an unresolved NO blocks.
4. Codemap and unitymap stamps are current; regenerate if stale.

## Drift guard — staleness is a defect

The user decides the transition; **detecting readiness is the AI's job.**
Every postflight checks for shipping signals (see `gates/postflight.md`):
when the release scope in `scope.md` is content-complete or within roughly
one task of it, the AI must propose running this audit in that same reply.
A content-complete game still sitting in `production` must never survive
another task without a proposal on record.

## Entry criteria — shipping

- Release scope in `scope.md` is content-complete, or the cut list is a
  recorded decision.
- Performance budget defined in the fingerprint (ms/frame, memory ceiling
  per platform) and a profiler baseline captured per target platform —
  from here on a regression against the baseline blocks the change that
  caused it.
- Persistence schema versioned; save migration tested from the oldest
  supported version.
- Content pipeline proven: one content item added end-to-end without a
  code change, or the deviation is a recorded decision.
- No `OPEN` fields left in the fingerprint; no unresolved postflight NOs.

## Output

The report lists each criterion as Y/N with one line of evidence. Any N
blocks the transition — fix it or escalate to the user; the phase line does
not change while an N stands. The completed transition is recorded in
`decisions.md` with the date and the report's summary line.
