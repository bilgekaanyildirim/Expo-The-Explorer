# fingerprint — project profile

<!-- The AI drafts it, the USER VERIFIES it (mandatory — this file is about
     the project's intent). Any unanswered field is written as OPEN; progress
     does not stop, and the field surfaces as an assumption in the preflight
     of the first task that touches it. The summary is copied into the root
     CLAUDE.md. -->

- **Space model:** OPEN <2D/3D; grid or free; is the tile/unit size fixed>
- **Determinism:** OPEN <is replay/lockstep/sync required; float tolerance>
- **Data authorities:** partially answered — only entries a task has actually
  established are listed; everything else is OPEN.
  - board-distribution balancing (noise-leak, guaranteed-ticket budget,
    arrival-weighted lottery decay, urgency threshold) → **the Day JSON file**
    (`runtime.boardDistribution`). Readers: `DayCatalogParser` →
    `DayDefinition.BoardDistribution` → `GameManager` → `BoardDistributor`.
    `BoardDistributionConfig.asset` is NOT an authority — it is a Day-Editor
    seed for new Days. Established by decisions.md D-004 (2026-08-17).
  - ticket-generation balancing (side/drink inclusion, modification count,
    main-dish weights) → OPEN, but currently split: authoring-time values live
    per Day in `editorMeta`, runtime values (time limits, upcoming-queue size)
    still come from `TicketGenerationConfig.asset`. Scheduled to be resolved by
    `.claude/day-config-plan.md` steps 3–4.
  - a food item's price — the Order Value half of a delivery's payout → **the
    `FoodItemConfig` asset itself** (`basePrice`, added 2026-08-18). One
    authority per food and no parallel price table; the tip RATE stays on
    `EconomyConfig` while the price never does. No reader yet —
    `EconomyCalculator` picks it up in `.claude/economy-plan.md` Adım 6's
    formula slice. Established by the pricing decision in
    ExpoTheExplorer/CLAUDE.md "Food Pricing / Order Value" (GDD v0.8/v0.9).
  - the tip tier thresholds — the two remaining-time ratios that decide BOTH
    the timer bar's colour and which of three tip rates a delivery earns →
    **`EconomyConfig`** (`warningRatio`/`criticalRatio`, moved there 2026-08-18
    from `TicketCardVisualsConfig` on the user's instruction, because they
    decide money). Readers: `EconomyCalculator.ResolveTipTier` and
    `TicketCardsView.TimerFillColorFor` (the latter via
    `GameManager.EconomyConfig`) — two readers, one authored value, so the
    colour on screen cannot drift from the tip paid. The three rates live
    beside them. `TicketCardVisualsConfig.timerSegmentSeconds` is NOT an
    economy authority: it spaces cosmetic tick marks only.
  - everything else (SoftMoney/Gems, lives, board grid state, tray contents)
    → OPEN. `.claude/economy-plan.md` step 1 is where the Wallet
    single-writer question is being settled. (Xp/Level dropped off this list
    with the XP system's removal, decisions.md D-009.)
- **Scale magnitudes (n):** OPEN <how many units/bullets/tiles/UI elements at once —
  the cost model's n comes from here>. Known fixed points: 3 active ticket slots
  (`GameState.TicketSlotCount`), board grid 6x5 = 30 cells as a starting point
  (parametric, per ExpoTheExplorer/CLAUDE.md Section 4), upcoming-ticket
  lookahead default 10.
- **Persistence:** answered, and the answer is **nothing persists**. Xp/Level was
  the only state ever written to disk and it is gone (decisions.md D-009), so no
  code path reads or writes `player_profile.json` any more; every session starts
  from `GameState`'s constructor. `PlayerProfileStore` still exists as the
  load/save boundary (`Application.persistentDataPath`) with `PlayerProfile`
  empty. Whatever re-opens persistence owes a version number with its first
  field — the root CLAUDE.md invariant requires one and none was ever written.
  Candidates: `CurrentDayIndex` (DaySystem_Roadmap Q4), SoftMoney/Gems
  (`.claude/economy-plan.md` step 4).
- **Network model:** none
- **Performance budget:** OPEN <per target platform: target frame rate → ms/frame,
  memory ceiling, load-time ceiling — the cost model's "frame budget" and the
  shipping phase's measurements read from here>
- **Target platform/version:** mobile (touch), Unity 6000.3.16f1. Exact device
  tier / OS floor: OPEN.
