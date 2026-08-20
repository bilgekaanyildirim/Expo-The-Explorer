# CLAUDE.md — Expo the Explorer

This file is the primary reference Claude Code uses while working on this project.
The rules here are derived from GDD v0.5. GDD source: `docs/Expo_the_Explorer_GDD.docx` (or its markdown export, if you keep one).

**Critical rule:** The GDD is a "living document," and so is this file. When a new design decision is made, update both. Before assuming anything not explicitly stated here, Claude Code should check the **Open Questions** section — if the value is listed there, ask the user instead of inventing a number/parameter.

---

## 1. Project Overview

- **Genre:** Time-management / order-matching, mobile, touch-based
- **Engine:** Unity (C#)
- **Pitch:** Get the right food to the right ticket before time runs out — lose focus and the kitchen eats you alive.
- **Core fantasy:** The player is the bridge between kitchen and customer — the "expo" role.

## 2. Core Gameplay Loop

```
Ticket arrives -> Food spawns onto the board -> Player collects the correct items into the tray
-> When tray is full, a batch check runs -> If correct: ticket delivered (Money + Tip)
-> New ticket arrives -> (loop repeats)

If incorrect: Life lost -> Tray contents scatter back onto the board -> Player re-collects
```

In code, this loop should revolve around a single central `GameState` (see Section 8 — Architecture).

## 3. Locked Rules (marked done in the GDD)

These are fixed design decisions — don't second-guess them during implementation. How they're technically achieved (data structures, algorithms) is still open to Claude Code's judgment.

### Ticket System
- Exactly **3 active ticket slots** at all times (fixed, not a free queue). When a ticket is delivered/cancelled, a new ticket fills the same slot.
- Slots have **no ordinal badge** (an earlier version had one — it was removed; don't re-add it).
- Ticket card info hierarchy, top to bottom: customer + remaining time -> base dish image -> modification list (+/- icons) -> side + drink -> time bar -> border color (patience type, stays fixed for the ticket's lifetime).

### Tray / Validation Mechanic
- **Validation is batched, not instant:** the tray is checked all at once when it reaches the required item count. No feedback is given at the moment an item is placed.
- The tray counter tracks **item count only** (main dish + side + drink). Modifications are checked separately and are not counted toward the tally.
- The tray area is *not* a separate "preview box" — it **is** the tray itself. There's no two-stage "preview -> submit" flow.
- On check: are the items correct AND are the modifications correct? If both pass -> auto-deliver. If either fails -> lose a life, tray contents scatter back to the board.

### Board / Food Distribution
- **Every balancing knob below is authored per Day**, in that Day's JSON under
  `runtime.boardDistribution`, and edited in the Day Editor — not in a shared
  ScriptableObject. There is no global board-distribution asset any more and no
  override layer: each Day carries a complete block, and a Day file missing it is
  refused at load rather than silently defaulted. `decisions.md` D-004 / D-007.
- Board is a fixed grid, starting point **6x5 = 30 cells** (not locked — keep this parametric, don't hardcode it).
- Two pools: **Required pool** (minimum set needed to complete active tickets — at least one ticket must always be completable) + **Noise pool** (items leaking from upcoming tickets; the main source of difficulty).
- The required pool must spawn **before** noise items.
- Upper bound = grid capacity. If no empty cell is available, the spawn request queues and fills the first cell that opens up.
- Noise items don't expire or disappear on their own — they stay on the board until the player uses them.
- **Which tickets the required pool covers each round is probabilistic, not deterministic "earliest N":** a per-round budget (fixed value or Poisson-sampled, per-Day configurable) picks tickets via an arrival-order-weighted lottery — earlier-arrived tickets are favored but not guaranteed. Any active ticket whose remaining time drops below a configurable threshold (default 10s) is guaranteed unconditionally regardless of the lottery, consuming from (and able to exceed) that round's budget. Selection is sticky across rounds — a ticket stays guaranteed once picked, until it's delivered/cancelled — so the guarantee never silently drifts past "at least one ticket" from repeated re-rolling. See `BoardDistributor.SelectGuaranteedTickets`, `decisions.md` D-001.

### Interaction
- Drag & drop is the primary interaction: player drags an item from the board and drops it into the tray.
- Since this is mobile and touch targets are small, drop zones/hitboxes should be generous (larger than the visual item bounds) and give clear visual feedback during drag (e.g. highlight the valid drop area, snap-back animation on invalid drop).
### Lives System
- A wrong delivery or a ticket timing out reduces lives.
- When lives run out: **the day ends**, the player replays the day, but difficulty is scaled down slightly on retry (exact parameters not yet locked — see Open Questions).
- Players can spend Gems to refill lives and continue the current day ("continue" mechanic).
- **A life-loss retry forfeits everything that attempt earned, and refunds nothing it spent.** The day's SoftMoney is rolled back to `max(0, dayStartBalance - spentThisDay)`, so a paid Continue stays paid even though the attempt it bought is being thrown away — and a player who funded that Continue with money earned the same day can finish the day poorer than they started it, though never in debt. Nothing is written to disk on this *retry* path, because nothing about a failed day was ever permanent — but **abandoning** the attempt for the main screen applies the identical rollback and does write it (see Progression, `decisions.md` D-012), so the kept spending survives the player walking out. `decisions.md` D-010. (An earlier rule forfeited the attempt's XP instead; the XP/Level system is gone, D-009.)

### Time Limit
- Time is **per-ticket**, each ticket has its own countdown.
- The three patience time limits are **authored per Day** (`runtime.ticketRuntime`
  in the Day JSON, edited in the Day Editor), so two Days can give the same
  patience type different limits. A single ticket may still override its own
  limit. GDD Section 8's Impatient < Normal < Patient ordering is not enforced —
  the Day Editor warns when a Day breaks it, but still lets you save.
  `decisions.md` D-005.
- On timeout: lives decrease + the ticket is cancelled outright (no tray scatter, since the ticket itself is gone).

### Customer Patience System
- 3 patience types, shown via border color (red = impatient, green = patient, cream/neutral = normal), **fixed for the ticket's lifetime**.
- **Patience does not enter the tip formula.** The stepped tip-decay curve was removed — patience affects money only indirectly, through the time limit it grants (a longer limit means more timer-bar segments before the tip starts stepping down). Since the XP/Level system was removed (`decisions.md` D-009), the per-patience **XP multiplier** that used to be its second effect is gone too: patience now feeds nothing but the ticket's time limit.
- Consequence to keep visible: `EconomyConfig`'s three decay-step curves and `PatienceDecayStep` become dead data/code under this rule — see economy-plan.md step 6.
- The "thresholds scale with item count rather than fixed seconds" principle is **void** as of the timer-bar decay rule below: the tip's only time thresholds are now the bar's own segment ticks, which are wall-clock and identical for every ticket. Item count still shapes a ticket indirectly (more items take longer to gather, so more segments elapse), but nothing scales a threshold BY item count any more.

### Food Pricing / Order Value
- **Every food item carries its own base price**, authored per item in its food config (`FoodItemConfig`) — content data, so it is never hardcoded and never derived from item count. A burger, fries and a cola are not worth the same.
- A delivery pays in two parts, and only the second one varies:

  | Part | How it's computed | Variable? |
  |---|---|---|
  | **Order Value** | sum of the base prices of the ticket's required items | No — a successful delivery always pays it in full |
  | **Tip** | `Order Value x TipRate`, where `TipRate` steps down as the ticket's timer bar drains | Yes — the only variable part |

  `Delivery payout = Order Value + Tip`
- The tip is **proportional to the order's price by construction**: a fast delivery on an expensive order tips more than the same speed on a cheap one. `TipRate` is one global knob on `EconomyConfig` (e.g. 0.2 = an on-time delivery tips 20% of the order) — the price lives per food, the rate does not.
- **Elapsed time scales the tip only** — never Order Value — and the tip floors at 0: a late player can lose the entire tip but never claws back the food's own price. **Patience is not a factor** (see Customer Patience System); the timer bar's drain is the single variable in the tip.
- `EconomyConfig.baseTipPerItem` (a flat value per required item) is **replaced** by this model — item count no longer sets what a delivery is worth, the summed prices do.
- A food item with no authored price is a **content bug** (a free dish), not a valid default — surface it in validation rather than silently paying 0.

### Tip Tiers — thirds of the ticket's own timer
- **The old 3-tier speed bonus (Lightning / Fast / Standard) was removed** — the enum, its three multipliers and its two seconds-per-item thresholds are gone. An earlier GDD marked it locked; the user replaced it (GDD v1.1) because those thresholds were invisible to the player while the timer bar sits right there on the card.
- The replacement is still **three tiers, but keyed on the fraction of the ticket's OWN time limit that is left** — and they are exactly the thresholds that already recolor the timer bar. **The colour the player sees IS the tip tier:**

  | Remaining | Bar | Tip rate |
  |---|---|---|
  | `> WarningRatio` (0.666) | green | `TipRateFull` |
  | `> CriticalRatio` (0.333), `<= WarningRatio` | orange | `TipRateWarning` |
  | `<= CriticalRatio` (0.333) | red | `TipRateCritical` |

  `Tip = Order Value x (that tier's rate)`. Order Value is never touched.
- Because the thresholds are **ratios of each ticket's own limit, they scale per patience type for free**: an Impatient 45s ticket drops a tier every 15s, a Patient 150s one every 50s. This is the concrete mechanism behind "patience affects money only through the time limit it grants".
- **Single authority:** `WarningRatio`/`CriticalRatio` and the three rates all live on **`EconomyConfig`** (user's instruction), because they decide money. `TicketCardsView` reads the ratios from there via `GameManager.EconomyConfig` and keeps only the three *colours* in `TicketCardVisualsConfig` — one number can never let the bar and the wallet disagree about where a tier starts.
- `TicketCardVisualsConfig.TimerSegmentSeconds` is **purely cosmetic** — it only spaces the divider ticks drawn along the bar and has no effect on any payout. Do not wire money to it.
- Nothing here scales with item count. A bigger order reaches a lower tier only because it genuinely takes longer to gather.

### Progression
- 2 resources: **Soft Money** (earned per delivery, as Order Value + Tip — see Food Pricing / Order Value) and **Gem** (hard currency — powerup purchases + continue).
- **There is no XP or player Level.** The system existed and was removed on the user's instruction (`decisions.md` D-009): no experience points, no level curve, no level-up rewards, no level HUD. Do not reintroduce any of it — "level" is not a word that appears in gameplay code (the sequential-content concept is a **Day**, see `docs/DaySystem_Roadmap.md`).
- **The wallet, the day index and lives persist between sessions; nothing else does.** SoftMoney, Gems, `CurrentDayIndex` and `Lives` are saved to `player_profile.json` (`Application.persistentDataPath`) and restored on launch, so the main screen's Play opens the Day the player left off on (`decisions.md` D-012, which closes `docs/DaySystem_Roadmap.md` Q4). **Lives persist too** (`decisions.md` D-014) — they already carried from one day to the next inside a session, since `AdvanceToNextDay` deliberately skips `LivesManager`, and only a failed-day retry or a paid Continue refills; v3 extends that across sessions. Abandoning a failed day for the main screen **refills lives before saving**, because that path is only reachable at 0 lives and persisting 0 would write a save the player cannot play out of. A saved index is **clamped** against the authored Day catalog on load — an index past the last Day would otherwise leave `CurrentDay` null and throw on the first ticket.
- **Money becomes permanent only when a day is completed.** The save happens on `GameState.DayCompleted`, so quitting mid-day loses that day's earnings — the same "a day attempt is atomic" contract the in-memory revert enforces. Three other writes exist and none of them banks a failed day's income: `RetryCompletedDay` corrects a figure a completed day already banked; `AdvanceToNextDay` persists the new day index (not new money); and **leaving a failed attempt for the main screen writes the REVERTED wallet** — earnings taken back, spending kept (`GameManager.ReturnToMainScreenAbandoningDay`, `decisions.md` D-012). That last one narrows the old "a failed day never writes" rule rather than dropping it: without it, walking out would silently refund a Gem-paid Continue, making paid Continues free for anyone who ends up abandoning.
- **The save file carries a schema version** (`PlayerProfileStore.CurrentVersion`, now **3**), stamped on every write, as the root CLAUDE.md invariant requires. v3 brought the project's **first real migration**: `Lives` is the first field where absent-reads-as-0 is not survivable, so `UpgradeToCurrent` fills it for older files and touches nothing they already carried. When you add a field, check whether 0 is safe — if it is not, it belongs in that method, not in a field initializer. Loading accepts versions 1 up to this build's and refuses both an unversioned file (version 0 — its numbers could mean anything, and it is indistinguishable from a real 0 balance) and one from a newer build. Adding a profile field means bumping the version; an older file simply lacks it and reads it as 0, so check that 0 is a safe default for whatever you add.
- **Only one game mode exists: Daily Goal Mode.** There is no Endless Mode — a prior design draft mentioned one; it has been removed from scope entirely. Do not build, reference, or leave hooks for an endless/survival mode.

### Day Complete Popup / Star Rating
- Shown when `GameState.DayCompleted` fires. It's a receipt-style breakdown of the day, not a new currency: "Orders delivered" = the day's summed **Order Value** (the guaranteed food price of everything delivered), "Tips" = the day's summed **Tip**, "Total" = both combined, which already equals the SoftMoney gained that day. Under this model the Tips row can never go negative — the speed multiplier only ever scales the tip up from its base, and nothing eats into Order Value.
- **3-star rating is one star per life still held: no lives lost = 3 stars, one lost = 2, two lost = 1.** Lives start at 3, so 3+ losses in a single day is only reachable by paying Gems to continue — that refills lives but does not reset the day's failure count, since it is still the same attempt, and such a run finishes with 0 stars. Stars therefore measure accuracy only; speed and tip performance affect the money earned, not the rating. See `DayLifecycleManager.StarCount`, `decisions.md` D-008.
- Authored per-day score thresholds (`star1Threshold`/`star2Threshold`/`star3Threshold`) were **removed** with this change — they were hand-tuned numbers that had to be re-balanced for every Day, and both shipped Days had been sitting at 0/0/0, silently awarding 3 stars for any result.
- "Orders failed" counts a life loss from either cause (wrong delivery or ticket timeout) via `GameManager.HandleLifeLoss`. It currently carries **no score penalty** — failed orders don't subtract from `Total`. Revisit if/when a penalty formula is designed.
- "Go Back" exits to the **main screen** and persists the NEXT day, so Play there picks up where the popup left off (`decisions.md` D-012). It deliberately does not run `AdvanceToNextDay`'s board/tray/slot reset: the scene is about to be destroyed, so only the index has to survive. "Next Day" on the **last** authored Day falls back to the same exit instead of hiding the popup and leaving the player on a finished day with no UI.
- A "Retry" button (always interactable, even at 3 stars) lets the player redo a day that already succeeded, for a better star score. This is a **voluntary redo, not a free bonus round**: `GameManager.RetryCompletedDay` reverts the wallet to the day-start snapshot the same way a failed day does (`Wallet.RevertToDayStart`, earnings taken back and spending not refunded), so replaying can't stack extra income on top of what the day already paid out. It also re-saves, because the completed day had already banked the higher figure. Lives refill in full, same as the life-loss `RetryDay` path — which now applies the identical money rule, so the two paths differ only in whether the day succeeded first.

### Main Screen / Scene Flow
- **Two scenes, both loaded Single: `MainScreen` (build index 0, where the game opens) and `SampleScene` (the day).** Navigation goes through `SceneFlow`; there is deliberately **no persistent/boot scene**, so each scene builds itself from scratch and `GameManager` keeps its "Awake builds the whole Day" shape and its hand-wired Inspector references untouched. `decisions.md` D-012.
- **The save file is the hand-off between the two scenes.** There is no `GameState` on the main screen, so `MainScreenView` reads `player_profile.json` directly — as a **reader only**. `GameManager` stays the single writer of both balances and the day index.
- The main screen is a **navigation shell on purpose**: the day number, the two balances, and Play. Meta content (day-select map, upgrades, shop) is not designed yet — build the design first, then hang it here.
- Three exits from a day, all in the two popups: Day Complete's "Go Back" (persists the next day), Day Complete's "Next Day" when no next Day exists, and Game Over's "Main Menu" (abandons and settles the attempt). None of them calls `Hide()` — the scene load destroys the popup.
- The scene was generated by `MainScreenSceneBuilder` (menu: **ExpoTheExplorer > Create MainScreen Scene**), which refuses to overwrite an existing `MainScreen.unity`. Restyle the scene freely; do not expect the builder to reproduce your changes.

### HUD — one prefab, both scenes
- **The HUD Canvas is a single prefab (`Assets/Prefabs/UI/HudCanvas.prefab`) instanced in both scenes**, showing coins, gems and lives in each (`decisions.md` D-013). Edit the prefab, not the instances.
- **`HudWalletSource` on the prefab root is the only place that decides where the HUD's numbers come from.** Day scene → the live `GameState` via `GameManager`, reactive as before. Main screen → the save file, read once, because that scene has no `GameManager` by design. Do not add a second null-check-and-fall-back to any view; extend the source instead.
- The `GameManager` is a **`[SerializeField]` on the HUD instance**, not a runtime lookup. Because **a prefab asset cannot store a scene reference**, that field is a prefab **override** in the day scene and empty in the prefab itself. **Never "Apply All" on the HUD prefab from the day scene** — Unity will not push a scene reference into an asset, so the override is dropped and the HUD quietly stops following the live wallet for the rest of the day. `HudCanvasPrefabSetup` step 2 sets the field per scene, and `HudWalletSource` logs which mode it resolved to on every scene load, because an empty field is not an error (it is how the main screen works). The views' own `walletSource` references are prefab-internal and need no override anywhere.
- Resolution is **lazy, on the first view's `Start`, never in an `Awake`** — `GameManager` assigns `State` in its own `Awake` and Unity does not order `Awake` across GameObjects. The views' Start-not-Awake rule is load-bearing; don't move it.
- **The HUD views do not know that two data sources exist.** `HudWalletSource` forwards three `EventBus<int>`s (`SoftMoneyChanged`/`GemsChanged`/`LivesChanged`) — live, it re-publishes `GameState`'s events; on the main screen they never fire, which is correct because nothing there can change a value. Each view is one subscription plus one render. Do not add a mode branch back into a view; extend the source instead.
- The wallet is displayed **once**: `MainScreenView` owns the day number and the Play button only, and no longer carries coins/gems labels.

### Powerup System — DEFERRED, NOT CURRENTLY BEING BUILT
- Design is locked (kept below for reference) but implementation is **out of scope for the current development phase**. Do not create PowerupSystem code, UI, or wiring unless the user explicitly reopens this scope.
- 3 fixed powerups: (1) Auto-Collect — auto-places required-pool items into the correct trays, (2) Time Reset — refreshes active ticket timers, (3) Noise Clear — temporarily fades noise items / highlights required-pool items.
- Earned via: event rewards + Gem purchases. (The original design also granted them on level-up; there is no level system any more, so that source is void — if powerups are ever built, they need a new earn trigger.) Exact numbers not locked — moot for now since this system isn't being built yet.

## 4. Open Questions — Ask Before Touching These

These parameters aren't locked yet. If an implementation needs one of these values, write it as a **placeholder/config value** (don't embed a magic number) and flag it to the user rather than guessing:

- Difficulty scale-down on life loss: which parameter (noise ratio / time / ticket frequency) drops by how much?
- Powerup economy: what grants powerups now that level-up is gone, which events reward them, Gem cost, daily use cap. **Deferred, not a current blocker** — Powerup System isn't being implemented right now (see Section 3), so this only needs an answer whenever that scope reopens.
- Do modifications change an order's price (should "extra patty" cost the customer more)? Current decision: **no** — price comes from the food item alone. Charging for add-ons is a separate design call.
- What is `TipRate` actually worth? (the single global coefficient in the tip formula — balance during production)
- Should patience carry any weight in the money at all? Current decision: **no**. If it should, the natural form is a flat per-patience tip multiplier, not a decay curve. Note this question got sharper with the XP system's removal: patience now affects nothing but the ticket's time limit, so it is the one design lever with no second effect left.
- Where does the tray fill counter (x/y) sit in the final UI?
- Board grid size (6x5 is a starting point, not locked — **keep this parametric/serializable**, don't hardcode it).

## 5. Architecture Decisions (Unity / C#)

The principles from GDD Section 15 are binding:

- **Central Game State:** Ticket and board state live in a single central state object (e.g. `GameStateManager` / a ScriptableObject + runtime model, or a plain C# model + event bus). UI binds **reactively** to this state — don't put game logic in UI code; UI just reads state and renders.
- **Food Distribution Module:** The required-pool + noise-pool logic should be a separate, **unit-testable** module (no Unity/MonoBehaviour dependency, so it can be tested in isolation). Difficulty tuning will largely run through this module's parameters.
- **Economy Module:** Tip/speed/patience calculations belong in a single "economy" function/class, isolated from `MonoBehaviour` so it can be unit tested independently — this makes balancing iterations fast.
- **Suggested folder structure** (adapt as needed):
  ```
  Assets/
    Scripts/
      Core/           // GameState, event bus, domain models (Unity-agnostic, testable)
      Bootstrap/      // GameManager: composition root wiring every system together
      Systems/
        DaySystem/            // Day content authoring/parsing/playback, ticket sequencing
        DayLifecycle/         // Day completion/star-rating bookkeeping
        TicketSystem/
        BoardDistribution/   // required pool + noise pool
        TraySystem/
        EconomySystem/       // tip/speed/patience formulas
        PowerupSystem/
        LivesSystem/
        ProgressionSystem/   // player profile save boundary (currently unwired)
      UI/               // display + input only, reactively bound to state
      Data/             // ScriptableObject configs (food types, modifications, economy/lives knobs)
    Editor/             // Day Editor tooling (not shipped in builds)
    Tests/
      EditMode/         // unit tests for Core and Systems
  ```
- Balancing numbers should live in **ScriptableObject configs** or JSON, not hardcoded in scripts — so a designer can tune them from the Unity Inspector.
- **Per-Day balancing lives in the Day's own JSON, not in a config asset.** Board
  distribution and the ticket play-time values (patience limits, lookahead depth)
  are read from the Day being played; the generation probabilities that produced
  its `ticketSequence` are kept alongside them in `editorMeta` as a record of how
  that Day was rolled. A config asset is only a **seed** for a new Day — never an
  authority the runtime reads. `TicketGenerationConfig` survives in that role
  because it also owns `namesDatabase`, a TextAsset reference that cannot live in
  JSON; `BoardDistributionConfig` had no such reason left and was deleted.
  `decisions.md` D-004 … D-007.

## 6. Code Style / General Rules

> Fill this in with your own preferences — starting suggestions:
- C# naming: PascalCase (classes/methods), camelCase (private fields).
- When adding a new system, read the relevant GDD section first; if something conflicts with the Locked Rules, stop and ask before proceeding.
- No magic numbers — anything the GDD calls "dynamic" (thresholds, ratios) must be read from config/ScriptableObjects.
- In commit/PR descriptions, note which GDD section you implemented (e.g. "Section 5.2 — Powerup system").

## 7. Keeping This File Updated

When an open question gets resolved: update the GDD **and** move the corresponding item from Section 4 into Section 3 of this file. When handing Claude Code a task after a design decision, explicitly ask it to update CLAUDE.md too ("we locked this down, please update CLAUDE.md").

## 8. Claude Working Rules

- Before modifying architecture, explain the plan briefly.
- Prefer editing existing systems over creating new ones.
- Avoid unnecessary dependencies.
- Keep MonoBehaviours thin.
- Business logic belongs in plain C# classes.
- If a design decision conflicts with the GDD, stop and ask.
- Do not rewrite unrelated files.
- Keep methods small and readable.
- Prefer composition over inheritance.
- When introducing a config value, expose it through a ScriptableObject.