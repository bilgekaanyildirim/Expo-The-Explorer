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
  - everything else (Xp/Level/SoftMoney/Gems, lives, board grid state, tray
    contents, economy curve) → OPEN. `.claude/economy-plan.md` step 1 is where
    the Wallet single-writer question is being settled.
- **Scale magnitudes (n):** OPEN <how many units/bullets/tiles/UI elements at once —
  the cost model's n comes from here>. Known fixed points: 3 active ticket slots
  (`GameState.TicketSlotCount`), board grid 6x5 = 30 cells as a starting point
  (parametric, per ExpoTheExplorer/CLAUDE.md Section 4), upcoming-ticket
  lookahead default 10.
- **Persistence:** partially answered — Xp/Level persist to
  `player_profile.json` via `PlayerProfileStore` (`Application.persistentDataPath`).
  Versioning scheme: OPEN — no version number is written today, which the root
  CLAUDE.md invariant requires. SoftMoney/Gems persistence: OPEN
  (`.claude/economy-plan.md` step 4).
- **Network model:** none
- **Performance budget:** OPEN <per target platform: target frame rate → ms/frame,
  memory ceiling, load-time ceiling — the cost model's "frame budget" and the
  shipping phase's measurements read from here>
- **Target platform/version:** mobile (touch), Unity 6000.3.16f1. Exact device
  tier / OS floor: OPEN.
