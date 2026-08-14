# decisions — architectural decision record (ADR)

<!-- One line per decision. `affects:` is not optional: a decision nobody can
     trace to code is a decision nobody can revisit. check_blueprint.py and the
     postflight both read it.

     D-001: <decision> — <rationale> — affects: <system(s) / path(s)> — <date>
-->

D-001: Reconnect BoardDistributor to runtime for live/dynamic board food-spawn distribution (replacing DayContentGenerator's pre-authored BoardTimeline replay for everything except Day Start, TriggerStepIndex == -1, which stays authored), with guaranteed-ticket selection made probabilistic (a per-round budget, Poisson-sampled or manually fixed, per-Day configurable; picks tickets via an arrival-order-weighted lottery; any active ticket under a configurable remaining-time threshold, default 10s, is unconditionally guaranteed and consumes from — and may exceed — that budget) — rationale: ExpoTheExplorer/CLAUDE.md Section 3 ("Board / Food Distribution"), still marked Locked, already describes this live required-pool/noise-pool system as current design; it was never updated when an earlier, undocumented change (referred to only in code comments as "PR-6") disconnected BoardDistributor from runtime because a live ticket-lookahead buffer over-consumed the Day's fixed-length pre-authored TicketSequence — fixed here via a non-consuming DayTicketSequenceProvider.PeekUpcoming instead of permanently avoiding a live system; the probabilistic-selection layer (vs. the old deterministic "earliest N") is a new design choice made this session, not a restoration of prior behavior — affects: GameManager.cs, DayTicketSequenceProvider.cs, BoardDistributor.cs, BoardDistributionConfig.cs, DayEditorMetaJson (+ DayEditorModel.cs's editor surface for it), DayContentGenerator.cs, DayEditorBoardTimelinePreview.cs, DaySolvabilityChecker.cs, DayValidator.cs — 2026-08-14
