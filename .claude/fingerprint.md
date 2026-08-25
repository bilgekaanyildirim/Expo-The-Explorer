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
  - which Day the player is on → **`player_profile.json`**
    (`PlayerProfile.CurrentDayIndex`, added 2026-08-19 by decisions.md D-012). It is
    a POSITION in the resolved Day catalog, not a Day file's own `dayIndex` (that
    only decides sort order). Runtime mirror: `GameState.CurrentDayIndex`, whose
    single writer stays `GameManager` (day advance, the loaded-and-clamped value in
    `Awake`, and the completed-day exit to the main screen). Readers:
    `GameManager.CurrentDay` and `MainScreenView` (display only, from the file).
  - lives → runtime `GameState.Lives`, persisted in **`player_profile.json`**
    (`PlayerProfile.Lives`, added 2026-08-19 by decisions.md D-014). Single writer:
    `LivesManager` — life loss, both paid Continues, the free retry/abandon refill,
    and the load itself via `ApplyPersistedLives` (clamped 1..MaxLives). `MaxLives`
    is not persisted: nothing varies it. Readers: `GameOverPopupView`,
    `DayLifecycleManager.StarCount`, and `LivesView` through `HudWalletSource`.
  - a NEW player's opening coin balance → **`GameConfig`** (`startingSoftMoney`,
    1000 in `GameConfig.asset`, added 2026-08-20 by decisions.md D-026). Not on
    `EconomyConfig`: that asset is the per-ticket tip math and is unreachable from
    the main screen, while `GameConfig` is the one config both scene roots already
    hold. Exactly one reader, `GameSession`, which hands it to
    `PlayerProfileStore.NewPlayer` as the `Load` FALLBACK — so it applies only to a
    player with no readable save, reaches the wallet through the ordinary
    `ApplyPersistedBalances` call (Wallet stays the single writer of a balance), and
    never tops up a saved profile. The current balance itself is NOT authored
    anywhere: it lives in `player_profile.json` and at runtime in
    `GameState.SoftMoney`.
  - a day's STAR RATING — the two score thresholds and the two failure penalties →
    **`StarScoreConfig`** (`Assets/Data/StarScoreConfig.asset`, added 2026-08-24 by
    decisions.md D-060). Exactly one reader, `DayLifecycleManager`, which holds the RULE
    (code, so it is testable) while the asset holds the numbers (content, per the root
    CLAUDE.md invariant). Not on `GameConfig`: that asset owns `GemsPerStar`, which is
    what a star is WORTH once earned — a different question from what earns one.
  - the DAY'S TOTAL TICKET SECONDS — the score's denominator → **the Day JSON file**
    (`runtime.ticketSequence` + `runtime.ticketRuntime`), summed on demand by
    `DayDefinition.TotalTicketSeconds`. Deliberately NOT stored: a Day already states
    its length twice (`TicketsRequiredForDay` and a sequence of exactly that many
    entries), and a third hand-typed total would be the one free to disagree. The
    override-vs-patience rule behind each entry's limit lives in exactly one place,
    `ResolvedTicketEntry.TimeLimitSecondsWith`, read by both `TicketEntryFactory` (the
    ticket the player plays) and that sum (the score they are graded on).
  - a delivery's SAVED SECONDS → the `Ticket` itself (`RemainingSeconds`), carried to the
    receipt on `DeliveryPayoutResult` by `EconomyCalculator` — the same instance the tip
    tier was read from, so the money paid and the time scored cannot disagree. It is also
    the last moment that number exists; the slot refills immediately after.
  - meta props the player has BOUGHT → **`player_profile.json`**
    (`PlayerProfile.OwnedMetaItemIds`, added v4 by decisions.md D-020), as qualified
    `"<location>.<item>"` keys whose format's single authority is
    `MetaCatalog.OwnershipKey`. Purchases ONLY: whether a location is unlocked and
    whether a Day-unlocked prop is on screen are DERIVED from `CurrentDayIndex` against an
    authored index (D-015/D-017) and are deliberately never stored, because a second copy
    starts lying the moment the catalog is re-authored. No single writer yet — nothing
    writes it as of Adım 4; the writer arrives with `ProfileSaver` in Adım 5.
  - a meta prop's price / position / art / unlock day → **`MetaCatalog` asset**
    (decisions.md D-015…D-019). Readers: `MetaResolver`, `MetaPurchase`,
    `MetaCatalogValidator`, and the Meta Editor window. Nothing else may hold a price.
  - everything else (SoftMoney/Gems, board grid state, tray contents)
    → OPEN. `.claude/economy-plan.md` step 1 is where the Wallet
    single-writer question is being settled. (Xp/Level dropped off this list
    with the XP system's removal, decisions.md D-009.)
- **Scale magnitudes (n):** OPEN <how many units/bullets/tiles/UI elements at once —
  the cost model's n comes from here>. Known fixed points: 3 active ticket slots
  (`GameState.TicketSlotCount`), board grid 6x5 = 30 cells as a starting point
  (parametric, per ExpoTheExplorer/CLAUDE.md Section 4), upcoming-ticket
  lookahead default 10.
- **Persistence:** the **wallet** (SoftMoney + Gems), **the day index** and **lives**, in
  `player_profile.json` under `Application.persistentDataPath`
  (`.claude/economy-plan.md` Adım 4, 2026-08-18; `CurrentDayIndex` added by
  decisions.md D-012 and `Lives` by D-014, both 2026-08-19; D-012 closes
  DaySystem_Roadmap Q4). Authority for
  the starting balances is that file, not `GameState`'s constructor, which now only
  supplies the 0/0 a brand-new player gets. With two scenes and no persistent one,
  this file is also **the hand-off between them**: the main screen reads it directly
  because there is no `GameState` in that scene.
  Write path — four places, all in `GameManager`: `GameState.DayCompleted` →
  `SaveProfile`; `RetryCompletedDay` (corrects a figure already banked);
  `AdvanceToNextDay` (the new index, so quitting mid-day cannot relaunch onto an
  already-beaten day); and `ReturnToMainScreenAbandoningDay`, which writes the
  REVERTED wallet. That last one is the one place a **failed** attempt reaches disk,
  and it narrows the older "a failed day never writes" rule rather than dropping it:
  it can only ever record money already SPENT (revert takes the earnings back), and
  it exists because walking out of an attempt would otherwise refund a Gem-paid
  Continue. Read path: `GameManager.Awake` → `PlayerProfileStore.Load` →
  `Wallet.ApplyPersistedBalances` for the balances (the wallet is their single
  writer, so even loading goes through it) and `ResolveStartingDayIndex` for the
  index, which CLAMPS it against the parsed Day catalog — an index past the last
  authored Day would otherwise resolve `CurrentDay` to null and throw on the first
  ticket. Second reader, never a second writer: `MainScreenView.Start`.
  Schema: `PlayerProfileStore.CurrentVersion = 4`, stamped on every Save. `Load`
  accepts versions 1..CurrentVersion and refuses 0 (unversioned, meaning unknown)
  or anything newer. v2 shipped with no migration code — a v1 file simply lacks
  `CurrentDayIndex` and reads it as 0 = Day 0, where a v1 player always started.
  **v3 is the first version that needed a real one**: an older file lacks `Lives`,
  would read 0, and 0 lives is a dead player rather than a fresh one, so
  `UpgradeToCurrent` fills it from `GameState.DefaultStartingLives` and touches
  nothing the file already carried. `Defaults()` is its sibling for a player with no
  file at all: zero money, FULL lives. `MaxLives` is deliberately NOT persisted —
  nothing varies it. **Lives are written by two ordering-sensitive paths**:
  abandoning a failed day refills BEFORE saving (it is only reachable at 0 lives, so
  saving first writes an unplayable file), and `RetryCompletedDay` saves LAST for the
  same reason.
  Runtime authority for all four values stays `GameState`; a ScriptableObject home
  was designed and rejected as too costly for the gain (decisions.md D-014).
- **Network model:** none
- **Performance budget:** OPEN <per target platform: target frame rate → ms/frame,
  memory ceiling, load-time ceiling — the cost model's "frame budget" and the
  shipping phase's measurements read from here>
- **Target platform/version:** mobile (touch), Unity 6000.3.16f1. Exact device
  tier / OS floor: OPEN.
