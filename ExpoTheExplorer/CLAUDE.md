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
- Ticket card info hierarchy, top to bottom: customer (photo + name) + remaining time -> base dish image -> modification list (+/- icons) -> side + drink -> time bar -> border color (patience type, stays fixed for the ticket's lifetime).
- **A customer is a name AND a face, and both are drawn once per ticket** (`decisions.md` D-139). The photo is picked from `TicketGenerationConfig.customerPortraits` (the sprites in `Assets/Art/Characters`) at the moment the ticket is built, beside the name, and is fixed for that ticket's lifetime exactly as the name is — it lives on `Ticket`, never in the view, because `TicketCardView` rebuilds its content on every ticket change and a face chosen there could change under the same customer.
- **Names and faces are unpaired, on purpose.** `names.json` holds hundreds of `{id, name, gender}` entries and the portrait list holds a couple of dozen sprites; there is no id joining them, so each is its own uniform draw and two cards may show the same face. **Gender is not matched** — the portraits carry no metadata. If gender-matched faces are ever wanted, that is per-photo authoring that does not exist yet, not a tweak to the draw.
- **The photo goes INSIDE `CustomerPhotoFrame`, it does not replace it.** `TicketCardView` writes only `.sprite` and `.enabled` and never touches colour, so the frame keeps its authored tint and the portrait's transparency lets it show through. Wire the field to a **white child Image** inside the frame; pointing it at the frame's own Image works but swaps the frame out for the photo and tints the face.
- **No Day may author a ticket's face.** `customerNameOverride` exists in Day JSON and has deliberately no portrait counterpart — an override nobody sets is a second authority waiting to drift from the draw.

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
- **A life-loss retry forfeits everything that attempt earned, and refunds nothing it spent.** The day's SoftMoney is rolled back to `max(0, dayStartBalance - spentThisDay)`, so a paid Continue stays paid even though the attempt it bought is being thrown away — and a player who funded that Continue with money earned the same day can finish the day poorer than they started it, though never in debt. **Both ways out of a lost day WRITE, and both cost one key** (`decisions.md` D-068) — and since **D-135** the same is true of the two ways out of a day that is merely *going badly*, taken from the settings menu, because giving up is giving up whichever button does it. The retry path wrote nothing until then, on the reasoning that nothing about a failed day was ever permanent; a key spend *is* permanent, and leaving it unwritten would let a player press Retry, force-quit, and get the key back — making retries free. The write is deliberately **last**, after `DayRetried` has already rolled the wallet back, so what reaches disk is the rolled-back figure — identical to what **abandoning** the attempt for the main screen writes (`decisions.md` D-012). No new money is banked on either path; only the key becomes real. `decisions.md` D-010. (An earlier rule forfeited the attempt's XP instead; the XP/Level system is gone, D-009.)

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
- **The wallet, the day index, the keys, the powerup charges and the owned meta props persist between sessions; nothing else does.** SoftMoney, Gems, `CurrentDayIndex`, `Keys`/`LastKeyRegenUtcTicks` (D-066), the three powerup charge counts (v8, GDD 5.2) and `OwnedMetaItemIds` are saved to `player_profile.json` (`Application.persistentDataPath`) and restored on launch, so the main screen's Play opens the Day the player left off on (`decisions.md` D-012, which closes `docs/DaySystem_Roadmap.md` Q4). A saved index is **clamped** against the authored Day catalog on load — an index past the last Day would otherwise leave `CurrentDay` null and throw on the first ticket.
- **Lives do NOT persist. Every day opens at 3.** (`decisions.md` D-064, replacing D-014, which had saved them at v3.) Lives are a **per-day allowance**: a mistake is paid for inside the day that made it and never carried forward. That includes a *successful* advance to the next day — `AdvanceToNextDay` used to skip `LivesManager` deliberately and now calls `RefillForNewDay` like every other day-start path, because splitting the behaviour by ROUTE (menu-then-Play giving 3 hearts while "Next Day" kept a half-empty row) is a difference no player could see a reason for. `LivesManager.RefillForNewDay` is the only way lives go back up; there is no load path.
- **Lives are drawn as HEART SPRITES, in the day scene only** (D-064). The row is three slots, each an Image carrying the empty heart with a filled-heart CHILD on top; code toggles **only the children**, since switching a slot off would take its filled child with it. It is added to the shared HUD Canvas **instance** in `SampleScene`, not to the prefab asset — which is what keeps hearts off the main screen with no mode branch in code, and is also the hazard: **never press Apply All / Apply Added GameObject on that instance**, or the row lands in the prefab and appears on the menu.
- **KEYS gate playing, and they are a different resource from lives** (`decisions.md` D-065, plan in `.claude/key-plan.md`). A key is permission to play a day at all: **cap 5, +1 every 30 real minutes** (the clock runs while the game is closed), and **spent when the player GIVES UP ON A DAY** — Retry or Main Menu, from the Game Over popup *or* from the settings menu (D-068, widened by **D-135**). So a paid Continue costs none (it stays inside the day), `RetryCompletedDay` costs none (**that day was won, not given up — the user restated this rule directly**), and *starting* a day is a gate rather than a price.
- **The charge is stated by the caller, not inferred from `GameState.IsAwaitingContinue`** (D-135). It used to read that flag, and the flag could only ever answer "was the day lost?" — so the settings menu, which opens only while the day is still RUNNING, walked out and restarted for free. `ReturnToMainScreenAbandoningDay` now charges unconditionally (every caller of a method by that name is a surrender) and `RetryDay` takes a **required** `givingUpOnAttempt` parameter with no default, since a defaulted `false` would make the next caller silently free — the same failure the flag could hide. The debug menu's retry passes `false` and moves no economy.
- **No button is ever disabled for want of a key.** The check happens **at click time** and opens the out-of-keys popup instead (`NoKeysPopupView`, D-069): a greyed-out button does not tell the player why, and the popup can offer both ways forward (wait for the timer, or 40 Gems for a full refill). Three buttons ask it: Play on the main screen, Retry on the Game Over popup, and — since D-135 — **Yes on the settings menu's retry confirmation**, which is the moment that commits, not the button that opens the question. **Neither Main Menu is ever gated** — blocking the only exit from a day at zero keys is a softlock, so their spend floors at zero and the player still leaves. The settings menu's refusal deliberately does **not** close the menu: it returns before releasing the pause, so the day stays frozen behind the explanation while the player waits out the countdown, buys a refill, or backs out. That requires the popup's canvas to sort **above** the settings canvas (`Popup Canvas` 300 vs `SettingsCanvas` 200, raised by D-135) — otherwise the explanation opens behind the panel it is explaining.
- **The key gate FAILS OPEN, and its close button is mandatory.** An unwired `noKeysPopup` field means the gate is simply skipped, never that the player is locked out: a forgotten drag costing an uncharged key is far better than one that makes the game unplayable and looks exactly like an economy bug (`NoKeysPopupView` logs loudly at `Start` so it stays findable). The popup only closes itself when a key arrives, so it must always carry a close button — otherwise a player with no keys and not enough Gems is stuck in it for up to a full regen interval. At 0 keys the player waits or pays **40 Gems, which fills to the cap** rather than adding a fixed amount. Keys persist; lives do not. Neither reads the other. The count is a private field on `KeyManager`, deliberately **not** on `GameState`: a key outlives the day, and a private field makes the single-writer rule a compile error instead of a comment. This system holds the project's **only** dependency on wall-clock time, injected as a `Func<DateTime>` so every time rule is testable — if something else ever needs the clock, it takes that same seam rather than opening its own.
- **Money becomes permanent only when a day is completed.** The save happens on `GameState.DayCompleted`, so quitting mid-day loses that day's earnings — the same "a day attempt is atomic" contract the in-memory revert enforces. Three other writes exist and none of them banks a failed day's income: `RetryCompletedDay` corrects a figure a completed day already banked; `AdvanceToNextDay` persists the new day index (not new money); and **leaving a failed attempt for the main screen writes the REVERTED wallet** — earnings taken back, spending kept (`GameManager.ReturnToMainScreenAbandoningDay`, `decisions.md` D-012). That last one narrows the old "a failed day never writes" rule rather than dropping it: without it, walking out would silently refund a Gem-paid Continue, making paid Continues free for anyone who ends up abandoning.
- **The save file carries a schema version** (`PlayerProfileStore.CurrentVersion`, now **8** — the three powerup charge fields landed with GDD 5.2; `Keys` + `LastKeyRegenUtcTicks` landed with D-066 at v7), stamped on every write, as the root CLAUDE.md invariant requires. v3 brought the project's first real migration — `Lives`, the first field where absent-reads-as-0 was not survivable — and **v6 removed that field again** (D-064), which is the only schema change so far that DROPS one. A removal needs no upgrade branch: `JsonUtility` ignores a JSON key with no matching field, so a v3–v5 file's saved life count is simply dropped on read. The version is still bumped, because the number is what tells a reader the file's shape. When you add a field, check whether 0 is safe — if it is not, it belongs in `UpgradeToCurrent`, not in a field initializer. When 0 is unsafe **and** the correct default is content the store cannot see (keys, whose cap lives on `KeyConfig`), the store writes a **`-1` "absent" marker** and the owning system resolves it — that keeps `PlayerProfileStore` ignorant of what the payload means, which is the property that lets it stay a pure file boundary. Loading accepts versions 1 up to this build's and refuses both an unversioned file (version 0 — its numbers could mean anything, and it is indistinguishable from a real 0 balance) and one from a newer build. Adding a profile field means bumping the version; an older file simply lacks it and reads it as 0, so check that 0 is a safe default for whatever you add.
- **v4 added `OwnedMetaItemIds`** (`decisions.md` D-020): the meta props the player has BOUGHT, as qualified `"<location>.<item>"` keys. It is the first REFERENCE-typed field, and that changes the rule above rather than following it: the safe default is an empty list, not a zero, and a reference can arrive null from any version — including a current one, which `UpgradeToCurrent` returns early on. So it is normalized on **every** load, outside the version gate, and `PlayerProfileStore.Normalize` is where that lives. It stores **purchases only**: whether a location is unlocked and whether a Day-unlocked prop is on screen stay derived from the day index (`decisions.md` D-015/D-017) and must never be written here, because a second copy starts lying the moment the catalog is re-authored.
- **Only one game mode exists: Daily Goal Mode.** There is no Endless Mode — a prior design draft mentioned one; it has been removed from scope entirely. Do not build, reference, or leave hooks for an endless/survival mode.

### Day Complete Popup / Star Rating
- **The star score is shown as a fill bar the three stars stand on** (`decisions.md` D-061). The bar is scaled `0..ThreeStarScore`, so **full means three stars** — a bar that kept filling past the last star would communicate nothing. The 1-star notch is the bar's LEFT EDGE (it is the completion floor, earned by finishing, so it is lit while the bar is still empty), the 2-star notch sits at `TwoStarScore / ThreeStarScore`, the 3-star notch is the right edge.
- **NOTHING in the game moves a star** (`decisions.md` D-062). D-061 stood each star on its own threshold along the bar, positioning three markers from the config at runtime; the user rejected that on sight — three 100px stars spread across a 450px bar stop reading as a three-star rating. A star's position is the scene's business, full stop. If the thresholds are ever wanted on the bar again, the honest form is small notch TICKS positioned from the config: marks may move, stars may not.
- **The bar's fill paces the star seating**: `DayRewardFlightView` waits for the fill to cross each star's threshold instead of counting a fixed interval, so "my speed earned that star" survives the reversal above — the star pops at the moment it is due, sitting exactly where the scene put it. That is timing, not movement. The gem-per-star payout is untouched. Everything degrades safely: with no bar wired, the stars keep the old fixed-interval rhythm and nothing else changes.
- Shown when `GameState.DayCompleted` fires. **It also shows the star score as its three terms** — time bonus, mistakes, net — as integer percents, because a hidden score leaves a player who lost a star unable to tell which half of the day cost it (D-060). Those three text rows are optional serialized fields: unwired they stay blank and `Start` logs it once, so the receipt keeps working before the scene is wired. It's a receipt-style breakdown of the day, not a new currency: "Orders delivered" = the day's summed **Order Value** (the guaranteed food price of everything delivered), "Tips" = the day's summed **Tip**, "Total" = both combined, which already equals the SoftMoney gained that day. Under this model the Tips row can never go negative — the speed multiplier only ever scales the tip up from its base, and nothing eats into Order Value.
- **3-star rating is a SCORE built from the day's own ticket clock, minus what mistakes cost** (`decisions.md` D-060, replacing D-008's "one star per life still held"):

  `StarScore = clamp01(SavedSeconds / TotalTicketSeconds - wrongDeliveries x WrongDeliveryPenalty - timeouts x TimeoutPenalty)`

  where `SavedSeconds` is the seconds still on each **delivered** ticket's clock, summed, and `TotalTicketSeconds` is every authored ticket's own time limit for that Day, summed. **3 stars at `>= ThreeStarScore`, 2 at `>= TwoStarScore`, and completing the day at all is worth 1.** All four numbers live on **`StarScoreConfig`** (`Assets/Data/StarScoreConfig.asset`) — the rule is code (`DayLifecycleManager.StarCount`, testable), the numbers are content.
- **`TotalTicketSeconds` is ticket clock, never wall clock, and the two must never be compared.** Three slots run concurrently, so a day whose tickets total 390s is over in well under half that — subtracting a wall-clock play time from it would score every player 3 stars. Measuring seconds handed back per ticket is what makes the score independent of how many slots happen to be busy, and it needs no per-Day calibration.
- **The two failure causes cost different amounts, deliberately.** A timeout has already forfeited its ticket's entire share of the day's clock (it contributes 0 saved seconds while its limit stays in the denominator), so its explicit penalty is the smaller one — the remainder of its bill, not the bill. A wrong delivery costs almost no time, so its penalty carries the whole cost. Starting values: 0.20 wrong delivery, 0.05 timeout, thresholds 0.55 / 0.30. **These are estimates, not balanced numbers** — retune them from real play, which is what the popup's net-score row is for.
- Speed now affects the RATING as well as the money. What has not changed: a day finished with 3+ failures (only reachable by paying to continue, which refills lives but deliberately does not reset the failure count) still scores 0, and the ceiling is still 3.
- Authored per-day score thresholds (`star1Threshold`/`star2Threshold`/`star3Threshold`) were **removed** back at D-008 and are not coming back under D-060 either — they were hand-tuned numbers that had to be re-balanced for every Day, and both shipped Days had been sitting at 0/0/0, silently awarding 3 stars for any result.
- "Orders failed" counts a life loss from either cause (wrong delivery or ticket timeout). Both still funnel through one place, but through **two named entry points** — `GameManager.HandleTicketTimeout` and `HandleWrongDelivery` — because the star score charges them differently (D-060). The split lives there and nowhere else: `TicketSlotManager` and `TrayManager` still take a plain `Action` and know nothing about failure causes. A failure costs **stars, never money**: it does not subtract from `Total`.
- "Go Back" exits to the **main screen** and persists the NEXT day, so Play there picks up where the popup left off (`decisions.md` D-012). It deliberately does not run `AdvanceToNextDay`'s board/tray/slot reset: the scene is about to be destroyed, so only the index has to survive. "Next Day" on the **last** authored Day falls back to the same exit instead of hiding the popup and leaving the player on a finished day with no UI.
- A "Retry" button (always interactable, even at 3 stars) lets the player redo a day that already succeeded, for a better star score. This is a **voluntary redo, not a free bonus round**: `GameManager.RetryCompletedDay` reverts the wallet to the day-start snapshot the same way a failed day does (`Wallet.RevertToDayStart`, earnings taken back and spending not refunded), so replaying can't stack extra income on top of what the day already paid out. It also re-saves, because the completed day had already banked the higher figure. Lives refill in full, same as the life-loss `RetryDay` path — which now applies the identical money rule, so the two paths differ only in whether the day succeeded first.

### Main Screen / Scene Flow
- **Two scenes, both loaded Single: `MainScreen` (build index 0, where the game opens) and `SampleScene` (the day).** Navigation goes through `SceneFlow`; there is deliberately **no persistent/boot scene**, so each scene builds itself from scratch and `GameManager` keeps its "Awake builds the whole Day" shape and its hand-wired Inspector references untouched. `decisions.md` D-012.
- **The save file is the hand-off between the two scenes.** There is no `GameState` on the main screen, so `MainScreenView` reads `player_profile.json` directly — as a **reader only**. `GameManager` stays the single writer of both balances and the day index.
- The main screen is a **navigation shell on purpose**: the day number, the two balances, and Play. Meta content (day-select map, upgrades, shop) is not designed yet — build the design first, then hang it here.
- Three exits from a day, all in the two popups: Day Complete's "Go Back" (persists the next day), Day Complete's "Next Day" when no next Day exists, and Game Over's "Main Menu" (abandons and settles the attempt). None of them calls `Hide()` — the scene load destroys the popup.
- **Play does not open a day until the player has bought their first meta prop** (`decisions.md` D-090). The gate is the same shape as the key gate: checked **at click time**, never by disabling the button, and a blocked press points the first-run arrow at the store with its own line of text. The condition is **derived** — owning nothing — so there is no profile flag and no save version to bump, and it is the same state the first-run tutorial reads. It **fails open** in every uncertain case (no session, unwired reference): this sits on the only path into the game, so a wrong answer in the blocking direction is an unplayable build.
- The scene **was** generated once by a `MainScreenSceneBuilder` menu step. That builder, and the other eleven one-shot scene/prefab builders under `Assets/Scripts/UI/Editor/`, were **deleted on 2026-08-30** (audit item R-02, the user's call): they had built everything they were going to build, they were the project's only copy-paste cluster, and they carried ~5k lines in `Assembly-CSharp`. **The scenes and prefabs on disk are now the only copy.** Restyle them freely; there is no builder to re-run and none to keep in step. If one is ever lost, it is rebuilt by hand — or recovered from git, where the builders remain in the history.

### HUD — one prefab, both scenes
- **The HUD Canvas is a single prefab (`Assets/Prefabs/UI/HudCanvas.prefab`) instanced in both scenes**, showing coins, gems and lives in each (`decisions.md` D-013). Edit the prefab, not the instances.
- **`HudWalletSource` on the prefab root is the only place that decides where the HUD's numbers come from.** Day scene → the live `GameState` via `GameManager`, reactive as before. Main screen → the save file, read once, because that scene has no `GameManager` by design. Do not add a second null-check-and-fall-back to any view; extend the source instead.
- The `GameManager` is a **`[SerializeField]` on the HUD instance**, not a runtime lookup. Because **a prefab asset cannot store a scene reference**, that field is a prefab **override** in the day scene and empty in the prefab itself. **Never "Apply All" on the HUD prefab from the day scene** — Unity will not push a scene reference into an asset, so the override is dropped and the HUD quietly stops following the live wallet for the rest of the day. A `HudCanvasPrefabSetup` menu step used to set the field per scene; it was deleted with the other builders on 2026-08-30, so **the repair is now a manual drag** — select the HUD Canvas instance in the day scene and drag `GameManager` into `HudWalletSource`'s field. `HudWalletSource` logs which mode it resolved to on every scene load, and that log is now the whole early-warning system, because an empty field is not an error (it is how the main screen works). The views' own `walletSource` references are prefab-internal and need no override anywhere.
- Resolution is **lazy, on the first view's `Start`, never in an `Awake`** — `GameManager` assigns `State` in its own `Awake` and Unity does not order `Awake` across GameObjects. The views' Start-not-Awake rule is load-bearing; don't move it.
- **The HUD views do not know that two data sources exist.** `HudWalletSource` forwards three `EventBus<int>`s (`SoftMoneyChanged`/`GemsChanged`/`LivesChanged`) — live, it re-publishes `GameState`'s events; on the main screen they never fire, which is correct because nothing there can change a value. Each view is one subscription plus one render. Do not add a mode branch back into a view; extend the source instead.
- The wallet is displayed **once**: `MainScreenView` owns the day number and the Play button only, and no longer carries coins/gems labels.

### Powerup System — IN SCOPE since 2026-08-25
- **The user reopened this scope.** The "deferred, do not build" rule that stood here is gone; the step-by-step plan is `.claude/powerup-plan.md` and the design of record is GDD Section 5.2, which was rewritten in the same turn.
- 3 fixed powerups: (1) Auto-Collect — auto-places required-pool items into the correct trays, (2) Time Reset — **refills the clock of the ONE active ticket closest to running out**, (3) Noise Clear — **REMOVES from the board every item no active ticket needs.**
- **Time Reset rescues one order, not the board** (`decisions.md` D-147, the user's decision 2026-08-31, replacing "refreshes active ticket timers"). It refills a single ticket, back to **that ticket's own authored `TimeLimitSeconds`** — never a flat "+N seconds", which would rewrite per-Day patience authoring from a powerup. Refilling all three made it the powerup with no wrong moment to press: three clocks for one charge, best value whenever the board happened to be busy, and never a decision. One clock makes it a rescue you spend on the order you are about to lose.
- **"Closest to running out" is the emptiest BAR, not the fewest seconds** — the smallest fraction of a ticket's own limit still left. An Impatient ticket holding 20 of its 45 seconds (0.44) is in less trouble than a Patient one holding 40 of its 150 (0.27). That fraction is what this project already means by urgency everywhere the player can see it: it colours the timer bar, it sets the tip tier (see Tip Tiers), and it fires the tutorial's deferred trigger. The rule is spelled **once**, in `PowerupEffects.MostUrgentActiveTicketSlot`, and both the powerup and the tutorial read it — so the lesson can never dim the screen down to a ticket the press will not save.
- **Noise Clear is not a visual effect and has no duration** (user's correction, 2026-08-25 — an earlier design, and an earlier version of this file, said it faded noise for a few seconds). **Since D-120 the cleared items DO fall off the board and fade, and that does not weaken this rule — it is the same rule seen from the other side.** The items are gone from the model the instant the powerup runs, the charge is spent on the press, and nothing in the game waits for the animation; what falls is a corpse. If a future change ever makes the board's state depend on that animation finishing, it has re-introduced the design this bullet exists to forbid. It mutates the board: instant, permanent, and it frees cells. That reverses its standing among the three — it is now plausibly the STRONGEST, so its authored price and starting stock need re-tuning. **The rule is by COUNT** (`decisions.md` D-118, the user's decision 2026-08-28): the board is cleared down to how many of each item the active tickets STILL need, and every copy beyond that goes. Five burgers against two wanted leaves two. **This reversed the original rule**, which was by identity — it kept every copy of a wanted food, on the reasoning that a player calls a third burger "too many" rather than "not needed". The user's answer is that "too many" is exactly what this powerup exists to clear. **The count is tray-aware:** a ticket wanting two colas with one already in its tray still wants one more, so one stays on the board — the same `OutstandingFor` definition Auto-Collect uses, so the two powerups cannot disagree about what is owed. Counts **sum across slots** (three tickets each wanting a burger keep three) and modifications are counted separately, since `RequiredItemKey` carries them. Emptied cells immediately backfill from `BoardGrid`'s pending-spawn queue, so clearing also lets required items that could not fit finally land — and a backfilled item is deliberately not re-examined, so the board can end up holding more than the budget allowed.
- **A powerup that had nothing to do does not cost a charge.** All three can be pressed at a moment with no work available (no items on the board, no active tickets); losing a scarce resource for nothing is the opposite of what this system promises.
- **Auto-Collect opens no second delivery path.** It runs the ordinary drag-and-drop accept route (`WorldTrayView.TryAcceptDrop`), so filling a tray triggers the same batch check as a human drop. A data-only implementation would leave the tray visually empty — tray visuals come entirely from reparenting the dragged object.
- **It shares that route but not the tutorial's gate** (D-115). `TryAcceptAutoCollectDrop` passes `respectTutorialGate: false`; a finger's drop passes `true`. The tutorial's tray gate exists to constrain the PLAYER, and a powerup's own machinery is not the player — without this, the lesson that *forces* Auto-Collect blocked it, and the press advanced the step having collected nothing. Safe because `CanUsePowerup` already refuses every powerup during a forced move and behind a panel, so the exemption is only reachable while a forced-press step names that powerup.
- **Acquired two ways: the authored starting stock, and Gem purchase** through `Wallet`, which stays the single writer of Gems. (The original design granted them on level-up and via "events"; there is no level system and no event system.)
- **There is NO day-completion grant.** It existed from 2026-08-25 to 2026-08-28 and was removed on the user's instruction (`decisions.md` D-115): `PowerupManager.GrantForDayCompleted` paid a per-type authored amount every time a day was won, and that amount had been **0 on all three powerups** since `PowerupConfig.asset` was first tuned — an earn path in name only, promising a reward no player ever received. The method, the `chargesPerDayCompleted` field and the call in `OnDayCompleted` are gone. Re-adding it is a design decision, not a bug fix.
- **A powerup is LOCKED until the Day that introduces it** (`decisions.md` D-117, the user's decision 2026-08-28). Locked means unusable AND unbuyable: pressing something nobody has explained wastes a charge, and selling it takes real Gems for something with no effect, so a lock that only dimmed the HUD button would be a half-rule. It is marked with an authored lock object carrying the Day it opens on — the same optional-serialized-field shape the "Add" badge uses, and unwired the lock still **applies**, it is simply unmarked. A locked powerup hides its charge count and its Add badge; **its charges are never confiscated** and become usable the day it unlocks.
- **The rule is `PowerupSettings.IsUnlockedOnDay(dayIndex)`, spelled once**, because neither side owns both halves: the asset has the schedule and not the day, each screen has the day and not the schedule. **An unscheduled powerup (negative `tutorialIntroDayIndex`) is UNLOCKED** — reading "nobody scheduled a lesson" as "never unlocks" would let clearing a schedule silently delete a powerup from the game. It fails open on every uncertainty (no config, no Day resolved) for the same reason the key gate and the first-run Play gate do.
- The lock's wording is `PowerupConfig.lockLabelFormat` (`"Day {0}"`), not a string in code. **It says "Day", not "Lvl"** — this game has no levels (see Progression), and the number is `introDayIndex + 1`, the same +1 `SettingsPopupView` and `MainScreenView` apply because players count days from one.
- The tutorial tops a powerup's stock up to an authored floor when it introduces that powerup (`PowerupManager.EnsureAtLeast`, D-115). That is a **teaching guarantee, not an earn path** — a forced press against an empty stock would open the shop instead of teaching anything. It is a floor rather than an addition, so replaying an introduction Day grants nothing.
- **Buying happens on BOTH screens, and in the day scene the day stops while it does** (`decisions.md` D-105, the user's decision on 2026-08-27). This replaces the older rule — "buying happens on the main screen only; the day scene only spends", and "pressing a powerup with no charges does nothing at all" — and it replaces it by answering that rule's own argument rather than dropping it: a store mid-service was banned because it *suspends the time pressure a powerup exists to relieve*, and the day is now **held still** behind the shop, so the pressure is paused rather than suspended.
- **There is still no buy button on the day HUD. The empty powerup IS the button.** A powerup at 0 charges lights the inactive "Add" badge authored inside it and, when pressed, opens the same `PowerupShopView` the main screen uses — one component, both screens, no second shop. The bar has no price and no Gem icon of its own; what an empty powerup gained is a destination. The shop panel is still its own thing rather than a tab inside `MetaShopView` (that file owns props and SoftMoney and belongs to MetaSystem).
- **Opening it is gated on `GameManager.CanUsePowerups`** — the same gate a *use* passes — so the shop refuses under the Game Over popup, over the day-complete receipt, and mid-tutorial.
- **`GameManager` is the single writer of `GameState.IsPaused`** (`HoldPause`/`ReleasePause`, over a set of holders). Two panels can freeze a running day now — the settings menu and this shop — and a view that assigns the flag itself would let whichever closes first start the day running under the other. Never write `IsPaused` from a view.
- **Freezing the day stops the clock, not a finger.** The day-scene shop carries a full-screen raycast-catching backdrop, because the board sits in the part of the screen the sheet does not cover and `BoardItemDragHandler` routes through the `EventSystem`. The backdrop deliberately does not close on tap.
- The split costs nothing architecturally: the stock lives on `GameSession`, which **both** scene roots build (`GameManager` and `MainScreenRoot`, both `SessionHost`s). Effects are registered only by the day scene's root, so a use button placed on the main screen by mistake cannot spend a charge — `TryUse` finds no effect and returns false.
- **Stock persists** (`player_profile.json`, like keys — unlike lives, which reset every day). A failed day neither refunds a spent charge nor banks a granted one: the same "a day attempt is atomic" contract the wallet follows.
- The stock's single writer is `PowerupManager`, and the count lives in its own private field rather than on `GameState` — the same reasoning as `KeyManager`: a charge outlives the day, and a private field makes the single-writer rule a compile error instead of a comment.
- Every number is authored on `PowerupConfig`: **starting stock, Gem cost, and the tutorial's three schedule fields** (which Day introduces this powerup, whether its forced press is immediate or waits for a ticket to go critical, and the charge floor the lesson guarantees). **No cooldown or per-day use cap** — the stock is the limit. This list has been wrong twice by carrying dead entries, so it is worth saying what left it: `clarityDuration`/`clarityDimAlpha` went when Noise Clear stopped being a fade (2026-08-25), and `chargesPerDayCompleted` went with the day-completion grant (D-115).
- **The deferred lesson's threshold is NOT authored here.** Time Reset is taught when a ticket falls to `EconomyConfig.CriticalRatio` — the same number that turns the timer bar red and drops the tip tier, and the single authority for it (see Tip Tiers). A copy on `PowerupConfig` existed for one day and was removed (D-115): two copies could drift, and a lesson firing at a moment the bar does not mark teaches against what the player can see.

## 4. Open Questions — Ask Before Touching These

These parameters aren't locked yet. If an implementation needs one of these values, write it as a **placeholder/config value** (don't embed a magic number) and flag it to the user rather than guessing:

- Difficulty scale-down on life loss: which parameter (noise ratio / time / ticket frequency) drops by how much?
- ~~Powerup economy: what grants powerups now that level-up is gone, which events reward them, Gem cost, daily use cap.~~ **Resolved 2026-08-25** and moved into Section 3: day completion is the earn trigger, Gem purchase is the second source, there is no daily use cap, and the amounts are authored on `PowerupConfig` rather than locked here. What is still genuinely open is only the BALANCE of those authored numbers — tune them from play, the way the star-score penalties are tuned.
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