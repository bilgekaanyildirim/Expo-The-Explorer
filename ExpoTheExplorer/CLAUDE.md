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
-> When tray is full, a batch check runs -> If correct: ticket delivered (Money + XP + Tip)
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
- Board is a fixed grid, starting point **6x5 = 30 cells** (not locked — keep this parametric, don't hardcode it).
- Two pools: **Required pool** (minimum set needed to complete active tickets — at least one ticket must always be completable) + **Noise pool** (items leaking from upcoming tickets; the main source of difficulty).
- The required pool must spawn **before** noise items.
- Upper bound = grid capacity. If no empty cell is available, the spawn request queues and fills the first cell that opens up.
- Noise items don't expire or disappear on their own — they stay on the board until the player uses them.

### Interaction
- Drag & drop is the primary interaction: player drags an item from the board and drops it into the tray.
- Since this is mobile and touch targets are small, drop zones/hitboxes should be generous (larger than the visual item bounds) and give clear visual feedback during drag (e.g. highlight the valid drop area, snap-back animation on invalid drop).
### Lives System
- A wrong delivery or a ticket timing out reduces lives.
- When lives run out: **the day ends**, the player replays the day, but difficulty is scaled down slightly on retry (exact parameters not yet locked — see Open Questions).
- Players can spend Gems to refill lives and continue the current day ("continue" mechanic).
- **On retry, XP earned during that day attempt is lost:** any Level XP gained in the failed day is discarded (never committed to the persistent profile) and the day is replayed from the start. This is distinct from the Gem "continue" mechanic — continuing keeps the day alive (so XP earned still counts once the day completes successfully); only a full life-loss retry wipes that attempt's XP. See Progression below.

### Time Limit
- Time is **per-ticket**, each ticket has its own countdown.
- On timeout: lives decrease + the ticket is cancelled outright (no tray scatter, since the ticket itself is gone).

### Customer Patience System
- 3 patience types, shown via border color (red = impatient, green = patient, cream/neutral = normal), **fixed for the ticket's lifetime**.
- Tip decay curve is **stepped** (not linear) — drops suddenly at time thresholds, then holds flat.
- Thresholds are **dynamic**: they scale with ticket complexity (item count), not fixed seconds.

### Speed Bonus
- 3 tiers: Lightning / Fast / Standard, each with a different tip multiplier.
- Formula: `Total Tip = Base Tip x Speed Tier Multiplier x Patience Decay Coefficient`
- Tier thresholds are also dynamic (scale with item count).

### Progression
- 3 resources: **Soft Money** (earned per delivery), **Gem** (hard currency — powerup purchases + continue), **XP/Level** (persistent, meta-progression, does not reset per session).
- Persistence is **conditional on successfully completing the day**: XP earned during a day only commits to the player's permanent profile once that day is completed. If the day ends in a life-loss retry, that attempt's XP is discarded (see Lives System above) — it never touches the persistent total.
- **Only one game mode exists: Daily Goal Mode.** There is no Endless Mode — a prior design draft mentioned one; it has been removed from scope entirely. Do not build, reference, or leave hooks for an endless/survival mode.

### Day Complete Popup / Star Rating
- Shown when `GameState.DayCompleted` fires. It's a receipt-style breakdown of the day, not a new currency: "Orders delivered" = the day's summed `BaseTip` (guaranteed per-item value), "Tips" = the summed speed/patience multiplier bonus on top of that (`TotalTip - BaseTip`), "Total" = both combined, which already equals the SoftMoney gained that day.
- 3-star rating: `Total` compared against the current Day's authored thresholds.
- Star thresholds are **per-day**, authored in the same Day JSON as `ticketSequence`/`boardTimeline` (`star1Threshold`/`star2Threshold`/`star3Threshold`) — not a single global config — since they're expected to scale with day difficulty.
- "Orders failed" counts a life loss from either cause (wrong delivery or ticket timeout) via `GameManager.HandleLifeLoss`. It currently carries **no score penalty** — failed orders don't subtract from `Total`. Revisit if/when a penalty formula is designed.
- "Go Back" has nowhere to navigate yet (single-scene project, no menu system) — left `interactable = false`, same as `GameOverPopupView`'s disabled Main Menu button. Wire it once a menu/day-select scene exists.

### Powerup System — DEFERRED, NOT CURRENTLY BEING BUILT
- Design is locked (kept below for reference) but implementation is **out of scope for the current development phase**. Do not create PowerupSystem code, UI, or wiring unless the user explicitly reopens this scope.
- 3 fixed powerups: (1) Auto-Collect — auto-places required-pool items into the correct trays, (2) Time Reset — refreshes active ticket timers, (3) Noise Clear — temporarily fades noise items / highlights required-pool items.
- Earned via: meta-progression (level-up/event rewards) + Gem purchases. Exact numbers not locked — moot for now since this system isn't being built yet.

## 4. Open Questions — Ask Before Touching These

These parameters aren't locked yet. If an implementation needs one of these values, write it as a **placeholder/config value** (don't embed a magic number) and flag it to the user rather than guessing:

- Difficulty scale-down on life loss: which parameter (noise ratio / time / ticket frequency) drops by how much?
- Powerup economy: how many powerups per level-up, which events reward them, Gem cost, daily use cap. **Deferred, not a current blocker** — Powerup System isn't being implemented right now (see Section 3), so this only needs an answer whenever that scope reopens.
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
      Systems/
        TicketSystem/
        BoardDistribution/   // required pool + noise pool
        TraySystem/
        EconomySystem/       // tip/speed/patience formulas
        PowerupSystem/
        LivesSystem/
        ProgressionSystem/   // XP/Level/Gem/SoftMoney
      UI/               // display + input only, reactively bound to state
      Data/             // ScriptableObject configs (food types, modifications, level thresholds)
    Tests/
      EditMode/         // unit tests for Core and Systems
  ```e 
- Balancing numbers should live in **ScriptableObject configs** or JSON, not hardcoded in scripts — so a designer can tune them from the Unity Inspector.

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