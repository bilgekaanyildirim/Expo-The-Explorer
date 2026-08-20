# blueprint — architecture plan: systems, scenes, prefabs, folders

<!-- The AI drafts it at bootstrap, the USER approves it. After that it
     changes only through preflight-declared tasks. This file is the single
     authority for "what goes where" — codemap, unitymap and assetmap all
     hang off it, and index.md is built by joining them to it.

     It is MACHINE-CHECKED: `python3 .claude/hooks/check_blueprint.py`
     compares every section below against the disk and against the codemap
     `sys:` fields, and the postflight quotes its output. A system name here
     is therefore a contract, not a label — spell it in `sys:` exactly as it
     is spelled here. -->

## Systems and dependencies

<!-- system → system arrows, ONE-directional. A bidirectional arrow is an
     ownership problem: apply procedures/ownership.md before writing it
     down. One line per system: name — responsibility — depends-on. -->
- <System — one-line responsibility — depends on: a, b>
- Bootstrap — central runtime composition root / game flow orchestration (GameManager) — depends on: DaySystem, BoardDistribution, TicketSystem
- DaySystem — Day content authoring/parsing/playback (ticket-sequence rolling, Day Start board replay, JSON schema, validation) — depends on: TicketSystem
- BoardDistribution — live required-pool + noise-pool board food spawning (probabilistic guaranteed-ticket selection) — depends on: -
- TicketSystem — active-slot ticket lifecycle (assignment/delivery/cancellation) + ticket generation — depends on: EconomySystem
<!-- TicketSystem -> EconomySystem, added 2026-08-18: the ticket card's timer bar
     reads EconomyConfig's two tier ratios (they colour the bar AND pick the tip
     tier, so one authored value has to serve both). One-directional --
     EconomySystem references nothing in TicketSystem. -->
- DayEditor — custom EditorWindow tooling for authoring Day JSON content (ticket sequence, Day Start board timeline) — depends on: DaySystem, TicketSystem
- BoardUI — runtime board grid rendering + drag/drop (BoardView, board-visual config assets) — depends on: -
- EconomySystem — delivery payout formula (order value from food prices + a tip stepped through three tiers keyed on remaining-time ratio) + its balancing config — depends on: -
- ProgressionSystem — the single writer of SoftMoney/Gems (`Wallet`), the player-profile save boundary (JSON load/save + fallback on missing/corrupt file, currently unwired and carrying no fields), and the owning system of the wallet HUD (SoftMoneyView/GemsView) — depends on: -
- LivesSystem — life loss on wrong delivery/timeout + paid continue (SoftMoney or Gems) — depends on: ProgressionSystem
<!-- LivesSystem -> ProgressionSystem, added 2026-08-18 (economy-plan Adım 1):
     the two paid-Continue prices are charged through Wallet, because GameState's
     balance setters are internal to ProgressionSystem now. One-directional --
     ProgressionSystem references nothing in LivesSystem. -->
- DayLifecycle — per-day receipt bookkeeping (base tip / bonus tip / failed orders) feeding the Day Complete popup — depends on: EconomySystem
- TraySystem — per-slot tray contents, batched order validation, scatter-back-to-board — depends on: -
- MainScreen — the main-screen presentation (day + wallet readout, Play) and the scene it lives in — depends on: ProgressionSystem, Bootstrap, MetaSystem
<!-- MainScreen, added 2026-08-19 (decisions.md D-012): the meta side's navigation
     shell. -> ProgressionSystem because it READS the persisted profile (day index +
     both balances) to display them; there is no GameState in that scene, so the save
     file is the hand-off between the two scenes. It never writes, so ProgressionSystem
     keeps its single writer. -> Bootstrap is the SceneFlow arrow only (Scripts/Core,
     sys: Bootstrap): scene navigation is boot-level flow, not a screen's business.
     Both one-directional -- neither system references MainScreen. The day scene's
     exits back to here go through GameManager, which owns what must survive the
     scene: the persisted day index, and settling an abandoned attempt. -->
- MetaSystem — the expo grounds the player decorates between days: a per-location catalog of props (bought with SoftMoney, or appearing at an authored Day), which of them the player owns, and the purchase rules — depends on: -
<!-- MetaSystem, added 2026-08-20 (decisions.md D-015): the meta CONTENT the main
     screen was deliberately left empty for ("build the design first, then hang it
     here", ExpoTheExplorer/CLAUDE.md).

     It depends on NOTHING. D-015 planned a -> ProgressionSystem arrow, on the grounds
     that a purchase spends through `Wallet`; writing the rules (D-019) showed that was
     unnecessary. `MetaResolver` and `MetaPurchase` take the owned-key set and the balance
     as PARAMETERS, so the money and the owned-item list keep the single writers they
     already have in ProgressionSystem, and the screen's composition root joins the two --
     the same shape GameManager uses to join systems. The assembly references
     `ExpoTheExplorer.Data` and no system at all, which is what makes every rule reachable
     from a test with no MonoBehaviour and no scene.

     There is deliberately NO arrow to DaySystem. D-015 had one, because a prop's
     unlock was DERIVED from Day content (the drinks fridge appeared once drinks were
     on the menu), which meant the resolver had to read the parsed Day catalog. D-017
     replaced that with a single authored day index, so MetaSystem needs the player's
     current Day and nothing else -- the whole content-scanning path, and this
     dependency with it, is gone.

     It does NOT own a scene: MainScreen -> MetaSystem, because the expo grounds are
     rendered by the main screen rather than by a third scene -- D-012's two-scene
     shape is untouched. And it reaches into game flow nowhere: the two facts it needs --
     the player's day index and their owned-item keys -- are handed to it as arguments by
     the screen's composition root. Since D-017 the Day catalog is not among them, and
     since D-019 neither is `Wallet`.

     Deliberately NOT an authority on the wallet, the day index or lives -- it reads
     all three and writes only the owned-item list, through the same profile writer
     GameManager uses. Two things it deliberately does NOT persist: whether a
     Day-unlocked prop is open, and whether a location is unlocked. Both are DERIVED by
     comparing CurrentDayIndex against an authored index, so there is no second copy to
     fall out of step with the catalog. -->

## Scene inventory

<!-- Every scene, starting with the boot/persistent scene. One line each:
     name — role — load mode (single | additive + trigger) — what lives
     in it. -->
- MainScreen — the main screen the game launches into, and where a finished or abandoned day returns — single (build index 0) — Camera, EventSystem, Canvas carrying MainScreenView (day + wallet value texts, Play button)
- SampleScene — the day scene: the whole playable game — single (loaded by SceneFlow.LoadDay) — GameManager plus every hand-wired view (board, tray, ticket cards, HUD, the two popups)
<!-- Scene inventory filled in 2026-08-19 (D-012); it was still template text
     because the project had exactly one scene until then. There is deliberately NO
     persistent/boot scene: both loads are Single mode, so each scene builds itself
     from scratch and the save file carries what has to cross between them. That is
     what keeps GameManager's "Awake builds the whole Day" shape and its hand-wired
     Inspector references working untouched. SampleScene keeps Unity's template name
     -- renaming it moves a GUID that Build Settings and its own references point at. -->

## Prefab inventory

<!-- Every prefab: name — owning system — variant-of (or -) — where it is
     instantiated from (authoring | spawner). scene-structure.md decides
     what becomes a prefab. -->
- HudCanvas — ProgressionSystem — variant-of: - — authoring (placed in both scenes by hand / HudCanvasPrefabSetup)
- TicketCard — TicketSystem — variant-of: - — spawned by TicketCardsView
- TicketCard Into — TicketSystem — variant-of: TicketCard — spawned by TicketCardsView
- TicketCard UpDown — TicketSystem — variant-of: TicketCard — spawned by TicketCardsView
<!-- Prefab inventory filled in 2026-08-19 (D-013); it was template text until the
     HUD became the first thing deliberately SHARED between two scenes. HudCanvas is
     filed under ProgressionSystem because the wallet HUD is that system's per
     blueprint, even though it also carries LivesView (sys: LivesSystem) -- one prefab,
     two systems' widgets, and HudWalletSource is the seam. The three TicketCard rows
     were already on disk and unlisted; they are recorded here rather than left for
     the next reader to discover, since assetmap.md cannot see them (see the UNMAPPED
     note in unitymap.md for why the asset/scene tooling reports zero). -->
- <EnemyGrunt — CombatSystem — variant-of: - — spawned by WaveSpawner>

## Hierarchy conventions

<!-- Per-scene root objects and naming. Keep runtime-moved objects shallow. -->
- <root objects: --Systems--, --World--, --UI-- ; naming: PascalCase, no spaces>

## Folder layout

<!-- Canonical tree. It and `.claude/shards.json` describe the same folders
     from two sides: change one, change the other in the same task, or
     every file lands in the catch-all shard. A new file with no place in
     this tree is a blueprint update FIRST, a file second.
     check_blueprint.py reports folders on disk that no line here covers. -->
```
Assets/
  Scripts/Core/        ← .cs: game flow, save, shared services (codemap: core)
  Scripts/Bootstrap/   ← .cs: the day scene's composition root (GameManager). NO asmdef
                           on purpose: it lands in Assembly-CSharp, which every
                           hand-wired view in the scene also lives in
  Scripts/Session/     ← .cs: GameSession — the half of a session that belongs to the
                           PLAYER rather than to a day, shared by both screens
                           (decisions.md D-021). Its own asmdef, so it is testable; it
                           cannot live in Scripts/Core/ because it needs
                           ProgressionSystem, LivesSystem and DaySystem, all of which
                           already reference Core — that would be a cycle
  Scripts/Systems/<System>/ ← .cs: one folder per system in the list above, each
                           with its own asmdef so it is EditMode-testable
                           (codemap: core — shards.json has no Systems pattern,
                           so these land in the catch-all, same as every other
                           system on disk today)
  Scripts/Gameplay/    ← .cs: mechanics, systems (codemap: gameplay)
  Scripts/UI/          ← .cs: UI code (codemap: ui)
  Scripts/<Area>/Editor/ ← editor-only code that must SEE Assembly-CSharp types
                           (codemap: editor — shards.json matches **/Editor/** first)
  Scripts/Content/     ← .cs: content-driving code (codemap: content)
  Editor/              ← editor-only tools and construction scripts (codemap: editor)
  Data/<Type>/         ← ScriptableObject assets, by content type (assetmap)
  Prefabs/<System>/    ← prefabs, grouped by owning system (assetmap + unitymap)
  Scenes/              ← .unity files (mirrors the scene inventory)
  Art/  Audio/         ← imported assets, by type
```

<!-- `Scripts/<Area>/Editor/` exists for one hard constraint, not as a second home
     for editor tooling: `Editor/` carries an asmdef, and an asmdef assembly cannot
     reference a PREDEFINED one. Everything under `Scripts/UI` and
     `Scripts/Bootstrap` has no asmdef, so it lands in Assembly-CSharp — which means
     an editor script that touches those types (MainScreenSceneBuilder and
     MainScreenView, D-012) can only compile in Assembly-CSharp-Editor, i.e. an
     `Editor/` folder OUTSIDE any asmdef. Editor tooling that talks only to asmdef
     code still belongs in `Editor/`. -->

<!-- `Data/` holds assets, not scripts, so it is inventoried by assetmap.md,
     not by a codemap shard. `Resources/` and `StreamingAssets/` are absent
     on purpose: everything in them ships in every build and is loaded by
     string. Adding one is an architectural decision — it goes to
     decisions.md with its `affects:` field, not into this tree quietly. -->
