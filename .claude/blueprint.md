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
- Bootstrap — central runtime composition root / game flow orchestration (GameManager) — depends on: DaySystem, BoardDistribution, TicketSystem, EconomySystem
<!-- EconomySystem added to this line 2026-08-25 (decisions.md D-076/D-077): it was missing
     and had been for a while -- GameManager constructs EconomyCalculator and calls it
     on every delivery, so the arrow was already there in code and only the map denied
     it. Repaired as part of the task that touched that very handler -- and KEPT when that task
     was reverted (D-077), because the arrow was never the receipt's: GameManager still builds and
     calls EconomyCalculator on every delivery. -->

- DaySystem — Day content authoring/parsing/playback (ticket-sequence rolling, Day Start board replay, JSON schema, validation) — depends on: TicketSystem
- BoardDistribution — live required-pool + noise-pool board food spawning (probabilistic guaranteed-ticket selection) — depends on: -
- TicketSystem — active-slot ticket lifecycle (assignment/delivery/cancellation) + ticket generation — depends on: EconomySystem
<!-- TicketSystem -> EconomySystem, added 2026-08-18: the ticket card's timer bar
     reads EconomyConfig's two tier ratios (they colour the bar AND pick the tip
     tier, so one authored value has to serve both). One-directional --
     EconomySystem references nothing in TicketSystem. -->
- DayEditor — custom EditorWindow tooling for authoring Day JSON content (ticket sequence, Day Start board timeline) — depends on: DaySystem, TicketSystem
- DataTooling — editor-only maintenance of the ScriptableObject config assets themselves (re-serializing `Assets/Data` so a `.asset` carries every field its class declares) — depends on: -
<!-- DataTooling, added 2026-08-25 (decisions.md D-075): its own line rather than a
     home inside DayEditor, because it names no game type at all -- it works on
     ScriptableObject and the Assets/Data folder, so filing it under the Day authoring
     tools would imply a dependency it does not have. It exists because the stale-asset
     trap has now been recorded four times (D-004, D-011, D-063, D-074); a system line
     is what lets its codemap entry carry a real `sys:` instead of `?`. -->
- PlayTesting — editor-only jump into any authored Day: sets the saved profile's day POSITION, opens a scene and enters play — depends on: DaySystem, ProgressionSystem
<!-- PlayTesting, added 2026-08-26 (decisions.md D-091): its own line rather than a home
     inside DayEditor, for the reason DataTooling got one -- it authors NOTHING. DayEditor
     writes Day JSON; this reads the finished catalog and writes the SAVE FILE, which is a
     different direction and a different dependency (ProgressionSystem, an arrow DayEditor
     does not have and should not gain). Both arrows are one-directional: neither DaySystem
     nor ProgressionSystem knows this exists, and neither would compile differently if it
     were deleted. -->

- BoardUI — runtime board grid rendering + drag/drop (BoardView, board-visual config assets) — depends on: -
- EconomySystem — delivery payout formula (order value from food prices + a tip stepped through three tiers keyed on remaining-time ratio) + its balancing config — depends on: -
- ProgressionSystem — the single writer of SoftMoney/Gems (`Wallet`), the debt a completed day still owes the player (`DayRewardPurse`), the player-profile save boundary (JSON load/save + fallback on missing/corrupt file, currently unwired and carrying no fields), and the owning system of the wallet HUD (SoftMoneyView/GemsView) and, since D-130, of the HUD's day badge (DayNumberView, which reads the day through the same HudWalletSource) — depends on: -
<!-- DayRewardPurse, added 2026-08-24 (decisions.md D-057): the payout became deferred, so
     between "the day completed" and "the player left the popup" there is an amount that is
     owed but not yet held. It is filed here rather than under DayLifecycle because it is
     currency bookkeeping, and it is safe next to the single-writer invariant precisely
     because it is a DEBT: the numbers in it have never been in the wallet, `Wallet` still
     owns both balances, and GameManager -- the one class holding both -- pairs every Take
     with exactly one Earn. It adds no arrow: it is plain C# and references nothing. -->
- LivesSystem — life loss on wrong delivery/timeout + paid continue (SoftMoney or Gems) — depends on: ProgressionSystem
<!-- LivesSystem -> ProgressionSystem, added 2026-08-18 (economy-plan Adım 1):
     the two paid-Continue prices are charged through Wallet, because GameState's
     balance setters are internal to ProgressionSystem now. One-directional --
     ProgressionSystem references nothing in LivesSystem.

     The arrow SURVIVED decisions.md D-064 even though lives stopped being persisted,
     and that is worth stating: the reference is about the Continue PRICES, not about
     the save file. What D-064 removed is the other direction of coupling -- lives are
     no longer a field on PlayerProfile, so ProgressionSystem carries nothing of theirs.
     Lives are a per-day resource: every day opens at a full bar, including a successful
     advance to the next one, and LivesManager.RefillForNewDay is the single way they
     ever go back up. -->
- KeySystem — the meta resource that gates playing: 5 keys, +1 every 30 real minutes, spent on leaving a lost day, refillable to the cap for 40 Gems — depends on: ProgressionSystem
<!-- KeySystem -> ProgressionSystem, added 2026-08-25 (decisions.md D-065, plan in
     .claude/key-plan.md): the 40-Gem refill is charged through Wallet, the single
     writer of Gems. Identical in shape to the LivesSystem arrow above and for the
     identical reason. One-directional -- ProgressionSystem does not know keys exist.

     KEYS AND LIVES ARE NOT THE SAME RESOURCE and neither reads the other. Lives are
     a per-day allowance that resets every morning and is never saved (D-064); a key
     is permission to play the day at all, is persisted, and refills in real time.
     They ended up adjacent in the HUD, which is the only thing they share.

     This system holds the project's ONLY dependency on wall-clock time. It is
     injected (Func<DateTime>) rather than read from DateTime.UtcNow directly, so the
     regen, cap, offline-accrual and clock-tampering rules are all reachable from a
     test. If a second system ever needs the clock, it takes the same seam rather
     than opening its own.

     The count lives in KeyManager's own private field, NOT on GameState. That is a
     deliberate departure from Lives, whose public setter leaves its single-writer
     rule resting on a comment; here the compiler holds it. The second reason is
     scope: GameState is the central state of a DAY. -->
- PowerupSystem — the STOCK behind GDD 5.2's three powerups: how many charges of each the player holds, buying them with Gems, and what spending one costs — depends on: ProgressionSystem
<!-- PowerupSystem -> ProgressionSystem, added 2026-08-25 (decisions.md D-081; GDD 5.2
     reopened by the user; plan in .claude/powerup-plan.md): the Gem purchase is charged through Wallet, the
     single writer of Gems. Identical in shape to the LivesSystem and KeySystem arrows
     above and for the identical reason. One-directional -- ProgressionSystem does not
     know powerups exist.

     IT DELIBERATELY DOES NOT KNOW WHAT A POWERUP DOES, and that is the whole reason this
     arrow list is one line long instead of four. The three effects need the board, the
     trays and the ticket slots; taking references to them would point this system at
     TraySystem, TicketSystem and BoardUI at once. Instead each effect is handed in as a
     `Func<bool>` by whoever can actually perform it -- the day scene's composition root --
     which is the same shape TrayManager and BoardDistributor already use to stay out of
     each other's assemblies.

     THAT SEAM IS ALSO THE TWO-SCREEN SPLIT, not just tidiness. Buying happens on the MAIN
     SCREEN and spending in the day scene (the user's decision, 2026-08-25). Both screens
     build a GameSession and therefore both hold a PowerupManager -- the menu needs the
     counts to sell against -- but only GameManager registers effects. So a use button
     that ended up on the menu by mistake cannot spend a charge: TryUse finds nothing
     registered and refuses. The rule is enforced by what is absent rather than by a
     scene check.

     The count lives in PowerupManager's own private field, NOT on GameState, for the two
     reasons KeyManager's note gives: a charge outlives the day, and a private field makes
     the single-writer rule a compile error rather than a comment.

     The `Func<bool>` return value is a RULE, not plumbing: false means the effect had no
     work to do, and GDD 5.2 says such a press costs no charge. All three powerups have
     reachable moments with nothing to do (an empty board, no active tickets, a finished
     day), so this is the difference between a convenience and a resource quietly lost.

     D-115 (2026-08-27) GAVE THIS SYSTEM'S CONFIG THE TUTORIAL SCHEDULE -- which Day
     introduces each powerup, and whether its forced press follows the panel at once or waits
     for a ticket to run down. It adds NO arrow: PowerupConfig is data, the Tutorial system
     reads none of it, and GameManager is the one object that holds both sides. The stock
     gained one method, EnsureAtLeast, which is a FLOOR rather than an addition because the
     tutorial re-arms on every day-start path including both retries -- an adding grant would
     have made replaying an introduction Day a charge farm.

     D-115 also DELETED the day-completion earn path (2026-08-28, the user's decision), which is
     why the line above no longer says "earning them by completing a day". GrantForDayCompleted
     paid a per-type authored amount every time a day was won, and that amount had been 0 on all
     three powerups since the asset was first tuned -- an earn path in name only. Two ways in now:
     the starting stock and the Gem purchase. -->

- DayLifecycle — per-day receipt bookkeeping (base tip / bonus tip / failed orders) feeding the Day Complete popup, and since D-057 the popup's payout HANDOVER (`DayRewardFlightView`) — depends on: EconomySystem
<!-- The handover, added 2026-08-24 (decisions.md D-057): a day's earnings are no longer
     credited as they are made, so the Day Complete popup is where the money actually
     changes hands -- stars seat, gems and coins fly to the HUD counters, and each landing
     credits its share. It is filed under DayLifecycle because it is that popup's second
     half, not a new system.

     Its two references -- GameManager and DayRewardPurse -- add no NEW arrow: the popup
     beside it has held a GameManager since it was written, which is this project's
     composition seam for every view, and the purse is a plain data holder. Worth stating
     plainly rather than leaving for the next reader to rediscover: the money authority did
     not move. Wallet is still the single writer, GameManager still owns when a day pays,
     and this view only asks it to release what the purse already owes. -->

- TraySystem — per-slot tray contents, batched order validation, scatter-back-to-board — depends on: -
- Tutorial — teaching the game: the day scene's forced opening (a Day's authored moves, plus a per-powerup introduction and forced press scheduled on PowerupConfig) AND the main screen's first-run welcome and store hint — depends on: -
<!-- D-115 (2026-08-27) split the step list's AUTHORING in two, and the split is what keeps
     this system's arrow list empty on both sides. The Day JSON owns the forced MOVES,
     because they describe THIS Day's board. PowerupConfig owns which Day introduces which
     powerup, because that is a property of the POWERUP. Neither can name the other's
     business -- the Day file's PowerupIntro kind was REMOVED in the same pass -- so the two
     cannot disagree, and GameManager concatenating them in ArmTutorial is the whole
     integration. That is the same trade D-082 made when it rejected a TutorialConfig asset:
     the objection was a SECOND authority over one Day, not an asset as such, and a schedule
     no Day file can contradict is not one.

     THE STRUCTURAL CHANGE IS A THIRD STATE, not the new step kinds. IsActive used to mean
     both "a step is running" and "freeze everything" (D-097 hung the clock on it). Time
     Reset cannot be taught that way: its forced press waits for a ticket to run DOWN, and a
     step that stops the clock while waiting for the clock is a deadlock. So a step can be
     CURRENT but not ARMED -- every gate open, the day running -- and only its trigger closes
     them. IsArmed is now the freeze/gate answer everywhere IsActive used to be, which
     changed nothing for the kinds that arm immediately.

     THE TRIGGER ARRIVES AS A PLAIN FLOAT, which is the only reason the arrow list below is
     still `-`. NotifyTicketPatienceRatio takes a fraction; it does not take a Ticket, and
     this system still does not know what one is. GameManager computes the lowest remaining
     fraction over the three slots and hands the number over, exactly as it hands over cell
     coordinates without this system knowing what a BoardItem is. -->

<!-- Became a step LIST in D-083 (2026-08-26), one turn after it shipped as a single step.
     That is the note D-082 wrote in advance: one authored step meant one step, and the
     second authored moment is what earned the generalisation. It is STILL not a framework
     -- a list and an index, no interface, no state machine class, no per-step subclass.

     THE MAIN SCREEN'S HALF SHARES NO CODE WITH THE DAY SCENE'S, on purpose (D-088). This system's
     name covers both, but MainScreenTutorialView does not use TutorialDirector: that director's
     entire surface is board cells, trays and drop gates, and the main screen has no board, no
     trays and no Day. Reusing it would have meant carrying Day-shaped fields into a screen with
     none of them, so the two flows share a `sys:` and nothing else. The main screen's texts are
     authored in the SCENE rather than in a Day file or a config asset, which is where every
     other player-facing string in this project already lives.

     TutorialStep is this system's OWN type rather than DaySystem's ResolvedTutorialStep,
     which is the one thing protecting the empty reference list below: taking DaySystem's
     type would have been the first entry on it. GameManager translates at the boundary,
     exactly as it hands PowerupManager a Func instead of the systems an effect touches. -->

<!-- Tutorial, added 2026-08-26 (decisions.md D-082; the user's ask: on Day 0 everything
     darkens except one hotdog and one tray, a ghost hotdog loops between them, and the
     player cannot do anything else until they make that move).

     The arrow is `-` and the assembly references NOTHING, which is the whole shape of this
     system. TutorialDirector is handed three plain ints (source cell x/y, target tray
     index) and answers two questions -- may this cell be picked up, may this tray accept --
     plus "is it done". It does not know what a BoardItem is, what a Ticket is, or that
     trays have contents. That is what makes every rule in it reachable from a test with no
     MonoBehaviour, no scene, and no Day catalog, and it is the same trick PowerupSystem
     uses to avoid pointing at the board, the trays and the tickets at once.

     WHERE THE CONTENT LIVES IS THE OTHER HALF: the three numbers are authored per Day in
     the Day JSON (`runtime.tutorial`), because they describe THIS Day's board, which the
     Day file is already the single authority for. A TutorialConfig asset was rejected --
     it would be a second authority for what happens on Day 0, free to name a cell the
     Day's own boardTimeline never fills, with nothing checking the two against each other.
     DayValidator now checks exactly that pairing at authoring time, which is only possible
     because both halves live in the same file.

     IT OWNS NO SCENE PRESENCE AND NO ASSET. The spotlight (the dim, the sorting lifts, the
     looping ghost) is built at runtime by TutorialSpotlightView, which the TARGET
     WorldTrayView creates in its own Start -- that tray is the one object already holding
     both of the ghost's endpoints (a serialized BoardView for the source cell, its own
     transform for the destination), so the effect needs no new scene object, no prefab, no
     art and nothing dragged into an Inspector. The view lives in Scripts/UI (shard ui)
     rather than in this system's folder for the usual reason: it is a MonoBehaviour that
     must see GameManager, so it belongs to Assembly-CSharp and could not compile inside an
     asmdef assembly.

     DELIBERATELY NOT A FRAMEWORK. There is one authored step, so there is no step list, no
     interface and no state machine -- abstraction-level.md's default answer. A second
     tutorial moment is the thing that would justify those, and it does not exist yet. -->

- CelebrationSystem — the confetti a payoff throws: two authored cannons, fired once and gone — depends on: -
<!-- CelebrationSystem, added 2026-08-29 (decisions.md D-131). The arrow is `-` and, like
     HapticsSystem's, is meant to stay `-`: ConfettiView reads no game state, no session and no
     config asset. It is handed a prefab and told to fire, and everything it needs is authored on
     that prefab. The three call sites (DayCompletePopupView once, MetaGroundsView twice) all live
     in Assembly-CSharp above it and point DOWN at it, which is the same shape HapticsBinder gives
     haptics -- so nothing depends on this in the arrow sense either.

     It runs the user's own ParticleSystem, and getting that on screen is the system's whole
     technical content: a ParticleSystem is a world renderer and a Screen Space - Overlay canvas is
     composited after every camera, so particles cannot draw over this game's UI at all (D-126 hit
     the same wall with the tutorial's world-space arrow and gave up the world renderer; this keeps
     it). The bridge is a stage parked far off-origin, an orthographic camera filming only it into
     a transparent RenderTexture, and a RawImage drawing that texture as ordinary UI.

     There is deliberately NO config asset and no look-field in code. Assets/Prefabs/Confetti.prefab
     is the single authority for every number, edited in the ParticleSystem inspector -- which is
     D-127's decision applied again: numbers tuned while looking at the thing they belong to. -->

- HapticsSystem — which game moment plays which haptic, and which one wins when several land in the same frame — depends on: -
<!-- HapticsSystem, added 2026-08-25 (decisions.md D-070). The arrow is `-` and stays `-`:
     this system READS a config asset and nothing else. It does not know GameState, the
     board, or the shop -- the subscribing and the calling both happen ABOVE it, in
     HapticsBinder, which is a scene component in Assembly-CSharp rather than part of this
     assembly. Nothing depends on it either, in the arrow sense: the four views that call
     it (BoardItemDragHandler, DayRewardFlightView, MetaShopView, MetaGroundsView) are all
     Assembly-CSharp too, so no system-to-system arrow is created in either direction.

     THE SPLIT IS THE POINT, and it is the same predefined-assembly constraint the
     `Scripts/<Area>/Editor/` note below is about: HapticsBinder needs GameManager, which
     has no asmdef and therefore lands in Assembly-CSharp, and an asmdef assembly cannot
     reference a predefined one. So the half that could be an asmdef is the half worth
     testing -- HapticsService, pure C#, whose one rule (highest priority per frame wins)
     is what HapticsSystemTests pins. The MonoBehaviour half carries the wiring and the
     vendor call and has no tests, which is the same trade GameManager makes.

     NICE VIBRATIONS IS QUARANTINED IN EXACTLY ONE FILE. HapticsBinder is the only place
     in this project that names `Lofelt.NiceVibrations`; everything above it speaks in
     `HapticMoment`, and `HapticConfig` stores the project's OWN `HapticPreset` mirror
     enum rather than the vendor's. That is why `ExpoTheExplorer.Data` keeps its empty
     reference list -- which in this project is how "data depends on nothing" is said --
     and why swapping the haptics vendor is a one-file change. The mirror costs a nine-arm
     switch, written out rather than cast, so a renumbering upstream stops the build
     instead of quietly playing the wrong haptic.

     THE PER-FRAME COALESCER IS NOT AN OPTIMISATION, it is a correctness fix for this
     project's synchronous EventBus. Publish calls its handlers inline and the handlers
     publish in turn, so several moments routinely land in one frame -- a correct drop
     queues ItemDroppedInTray and then OrderDelivered, the last life queues LifeLost and
     then GameOver. Played as they arrive those read as one smeared buzz. Two moments were
     left out of the table for the same reason rather than added and then lost to it:
     TicketAssigned (a delivery cascades straight into the next ticket's assignment) and
     TraySlotScatterBegin (a scatter only ever happens alongside a life loss).

     It has no scene presence of its own: HapticsBinder is added to an existing object in
     each scene, and Nice Vibrations additionally wants exactly one HapticReceiver per
     scene, the way a scene wants one AudioListener. -->

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
- DebugMenu — development-only cheat and inspection surface on SRDebugger's Options tab: grants currency/keys/powerups, refills lives, jumps to any authored Day, forces a save — depends on: Bootstrap, ProgressionSystem, KeySystem, PowerupSystem, LivesSystem, TicketSystem, MetaSystem
<!-- DebugMenu, added 2026-08-26 (decisions.md D-092). It is a READER-AND-COMMANDER, never
     an owner: every arrow above exists because it CALLS that system's public API, and it
     introduces no writer of its own. That is the whole design constraint -- a cheat menu is
     the most tempting place in a codebase to assign a field directly, and doing so would
     hand every balance a second writer and quietly void the root invariant. Money goes
     through `Wallet`, keys through `KeyManager`, charges through `PowerupManager`, lives
     through `LivesManager`, the day index through `GameSession.GoToDay`.

     It has no assembly of its own AND THAT IS LOAD-BEARING, not laziness: SRDebugger exposes
     options by having you extend its `SROptions` partial class, which lives in
     Assets/StompyRobot/SROptions/ with no asmdef and therefore compiles into
     Assembly-CSharp. A partial's halves must share an assembly, so ours must land there too.
     It costs nothing -- every project asmdef is `autoReferenced: true`, so Assembly-CSharp
     already sees all of them.

     Nothing references it, in either direction: deleting the folder removes the panel and
     changes no other file. The one exception is the day-index owner move that D-092 made in
     GameSession, which stands on its own merits and stays if this folder goes. -->
- Testing — the EditMode test assembly and the reference list that decides what it can see — depends on: -
<!-- Added 2026-08-25 (D-065's turn). NOT a game system, and it is listed here for one
     mechanical reason: `ExpoTheExplorer.Tests.EditMode.asmdef` carried `sys: ?` in the
     codemap, which the schema calls an unfinished line, and its own note argued the `?`
     was honest because the assembly "belongs to no single system". That argument is
     really an argument for a SHARED entry rather than for a question mark — the test
     files themselves stay filed under the system each one tests (LivesSystemTests ->
     LivesSystem, KeySystemTests -> KeySystem); it is only the assembly boundary that is
     common ground.

     The arrow is `-` even though this assembly plainly references a dozen systems, and
     that is the deliberate choice of two bad options. Enumerating them would put a
     SECOND copy of the asmdef's reference list here, which starts lying the first time
     someone updates one and not the other -- and prose ("every testable system") is not
     an option either: check_blueprint.py resolves each arrow against a real system line
     and warned about exactly that when it was tried in this turn. So the truth lives in
     the asmdef, and this entry exists to be a place to write the trap below down.

     THE TRAP IT EXISTS TO MAKE VISIBLE: that reference list is EXPLICIT, so a new
     system's assembly is invisible to the tests until it is added there by hand. That
     is not hypothetical -- it broke the build in this very turn (KeySystemTests could
     not see KeyManager, CS0234/CS0246), and the symptom appears in the TEST file rather
     than anywhere near the new system, which is what makes it hard to place. Adding a
     system whose code needs tests means editing this asmdef in the same task. -->

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

     Deliberately NOT an authority on the wallet or the day index -- it reads both and
     writes only the owned-item list, through the same profile writer GameManager uses.
     Lives used to be a third thing it read; since D-064 they are not in the profile at
     all, so there is nothing there to read. Two things it deliberately does NOT persist: whether a
     Day-unlocked prop is open, and whether a location is unlocked. Both are DERIVED by
     comparing CurrentDayIndex against an authored index, so there is no second copy to
     fall out of step with the catalog. -->

## Scene inventory

<!-- Every scene, starting with the boot/persistent scene. One line each:
     name — role — load mode (single | additive + trigger) — what lives
     in it. -->
- MainScreen — the main screen the game launches into, and where a finished or abandoned day returns — single (build index 0) — Camera, EventSystem, the shared HUD Canvas prefab (wallet ONLY since D-064; it carried lives too from D-013), and a Canvas carrying MainScreenView (Play button captioned "Continue Day X" since D-025, optional Start Over button since D-026), the meta grounds and the meta shop, plus the out-of-keys popup since D-069
- SampleScene — the day scene: the whole playable game — single (loaded by SceneFlow.LoadDay) — GameManager plus every hand-wired view (board, tray, ticket cards, HUD, the two popups, and since D-069 a THIRD: the out-of-keys popup, which sits over the Game Over one), and since D-064 the HEART ROW: LowerPanel/HeartPanel/Heart{1,2,3}/FilledHeart, added to the HUD Canvas prefab INSTANCE rather than to the prefab asset, which is the whole reason the main screen shows no lives; and since D-094 a FOURTH popup on its own SettingsCanvas (built by SettingsPopupSetup, sortingOrder 200) carrying the pause menu — its own canvas rather than the HUD instance, so that an Apply All can never push a day-scene menu onto the main screen
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
- HUDCanvas — ProgressionSystem — variant-of: - — authoring (placed in both scenes by hand / HudCanvasPrefabSetup)
<!-- Spelled HUDCanvas, matching Assets/Prefabs/UI/HUDCanvas.prefab on disk. It read
     "HudCanvas" here until 2026-08-26, which check_blueprint reported as BOTH a prefab on
     disk with no line AND a line with no prefab -- one case mismatch wearing two hats. It
     went unseen because the checker could not find Assets/ at all in this nested repo
     layout until D-092 fixed the map generators' project-root resolution. -->
- NoKeysPopup — KeySystem — variant-of: - — authoring (placed in MainScreen by hand, wired into MainScreenView's optional popup slot)
- PowerupShop — PowerupSystem — variant-of: - — authoring (placed in BOTH scenes by hand)
- TutorialPowerupIntro — Tutorial — variant-of: - — spawned by PowerupBarView
- TutorialPowerupSpotlight — Tutorial — variant-of: - — spawned by PowerupBarView
- TutorialStepHints — Tutorial — variant-of: - — spawned by TutorialSpotlightView
- CelebrationConfetti — CelebrationSystem — variant-of: - — spawned by DayCompletePopupView and MetaGroundsView
<!-- Added 2026-08-29 (decisions.md D-131). THE RIG, not the look: a stage holding an orthographic
     camera and two nested instances of Confetti.prefab, plus a RawImage on its own canvas. Seeded
     by ExpoTheExplorer > Celebration > Build Confetti, which refuses to rebuild it once it exists.

     ITS ROOT IS A PLAIN TRANSFORM, and it is the one prefab here where that is right (compare
     D-126, where a plain root was the bug): it has to hold a WORLD branch parked ten thousand
     units from the origin and a UI branch, and one transform cannot sensibly parent both. They
     are siblings, and ConfettiView detaches the UI half at runtime.

     WHY A CAMERA AND A TEXTURE AT ALL: a ParticleSystem is a world renderer and both celebration
     beats sit under Screen Space - Overlay canvases, which are composited after every camera, so
     the particles can never draw over them. Filming them into a transparent RenderTexture and
     showing that through a RawImage turns the layering into a UI question — which is how the
     user's two rules are both met: the RawImage's canvas is left a root Overlay canvas at 300 on
     the main screen (over everything), and mounted at the receipt popup's own sibling index in
     the day scene (under the receipt, over the rest).

     No GraphicRaycaster and the RawImage is never a raycast target: confetti must not be able to
     eat a tap, and the cheapest guarantee is having nothing in the hierarchy that could. -->
- Confetti — CelebrationSystem — variant-of: - — authoring (nested twice inside CelebrationConfetti)
<!-- The user's hand-tuned ParticleSystem, 2026-08-29, and the confetti itself — everything about
     the look lives here and nothing about it lives in code. It is INSTANCED into the rig rather
     than copied, so tuning this one asset retunes both cannons. Its authored aim is load-bearing
     and was read rather than overridden: the cone points at (0.5, 0.866, 0), up and to the right
     in the screen plane, so the rig's LEFT cannon holder carries no rotation at all and the right
     one is that aim mirrored by 60 degrees about Z.

     A missing Confetti.prefab is fatal to the seeding step rather than substituted, which is the
     honest failure: the step builds plumbing, and plumbing with nothing flowing through it is not
     a celebration. -->
- MetaUnlockPopup — MetaSystem — variant-of: - — spawned by MetaGroundsView
<!-- Added 2026-08-28 (decisions.md D-128). What a DAY-UNLOCKED prop says when it opens: a
     picture of what it brought and a line naming it, both authored per prop on MetaCatalog.
     A purchased prop never shows one -- the fields are drawn only in MetaEditorWindow's
     Day-Unlock branch, which makes that structural rather than a rule to remember.

     IT HOLDS THE CELEBRATION OPEN. MetaGroundsView.CelebrateOne waits on its IsDismissed
     before returning, so the camera stays zoomed on the prop until the player presses the
     button, and dismissing hands off to the next prop's own FocusOn or to the queue ending.
     That is why its dismiss button is the one part that is not optional: a popup with no way
     out would strand the map zoomed in behind a full-screen skip catcher.

     Its ROOT is its Canvas, for the reason every popup prefab here now is (D-126). -->
<!-- Renamed from TutorialStepMessage 2026-08-28 (decisions.md D-126) when it stopped being
     only a message: it now carries the step's sentence AND its two modification arrows, so
     an author can style all three. The step still decides which appear -- highlightModification
     and a non-empty message are INDEPENDENT flags, and burying the arrows in a message-only
     prefab would have tied them together silently.

     ITS ROOT IS A PLAIN TRANSFORM, not a canvas, and that is what lets the world arrow live
     in it: a SpriteRenderer under a Screen Space - Overlay canvas sits at screen coordinates
     and looks broken in the prefab stage. The canvas is a CHILD holding the message, and it
     keeps Overlay for the D-086 reason every tutorial canvas does.

     BOTH ARROWS ARE UI IMAGES ON ITS CANVAS. The item one was a world SpriteRenderer and
     the card one an Image parented outside any Canvas -- which never renders -- so the
     prefab stage showed an empty frame with nothing to click. They still move differently:
     the card arrow is reparented into the ticket card's row and rides it, while the item
     arrow stays on this canvas and is only POSITIONED over the board item, because
     parented to that item it rode along when the player picked the food up.

     ITS ROOT IS THE CANVAS, which is what makes it editable: Unity drives an Overlay
     canvas's rect from the screen and gives a ROOT canvas that treatment in the prefab
     stage, while a NESTED one sits at 0x0 and collapses every child into it. Authoring a
     size does not help -- the Canvas overwrites it. -->
- MainScreenTutorialWelcome — Tutorial — variant-of: - — spawned by MainScreenTutorialView
- MainScreenTutorialStoreHint — Tutorial — variant-of: - — spawned by MainScreenTutorialView
<!-- The main screen's two, added 2026-08-28 (decisions.md D-123). Filed under Tutorial with
     the day scene's three even though D-088 keeps the two FLOWS sharing no code: the system
     line covers both halves, and these are its assets.

     BOTH ROOTS CARRY THEIR OWN OVERLAY CANVAS and are instantiated PARENTLESS, which is not
     tidiness -- a Canvas nested inside another Canvas inherits its parent's RectTransform
     rather than the screen's, and MainScreenTutorialView's own rect is zero-sized. That once
     crushed the store hint to one character per line down a sliver of the screen.

     The store hint's ARROW is authored here but does not stay here: the view reparents it
     onto the store button and sizes it from that button, because only the running layout
     knows where the button ended up. It therefore outlives the hint object and the view
     destroys it explicitly -- the same trap TutorialPowerupSpotlight's frame has. -->
<!-- Both added 2026-08-28 (decisions.md D-116). They are the FIRST tutorial assets in the
     project: every tutorial visual before them was built at runtime and authored nowhere,
     which the files argued for at length. The argument held only while nobody needed to
     restyle them -- a panel assembled from constants in C# cannot be restyled at all, and
     the user asked to own their look. Seeded once by a menu step (TutorialPopupSetup),
     which REFUSED to overwrite an existing asset, so the prefabs belonged to the author
     from the first edit onward. That seeding step and the other eleven builders under
     Assets/Scripts/UI/Editor were deleted on 2026-08-30 (audit R-02): the prefabs on
     disk are now the only copy, and the builders live only in git history.

     THE INTRO PREFAB'S ROOT CARRIES A SCREEN SPACE - OVERLAY CANVAS and must keep it
     (D-086). This scene's InGameCanvas is Screen Space - CAMERA at sortingOrder -1, so a
     panel reparented under it is drawn beneath the world sprites and simply disappears,
     with nothing reporting it. The spotlight prefab deliberately has NO canvas on its
     root: its frame is reparented onto the live powerup button and inherits that button's
     canvas, which is what puts it ON the button rather than over it; only its message
     carries an Overlay canvas of its own.

     Spawned, not placed: PowerupBarView instantiates them, which is also why they are
     prefabs rather than scene objects (scene-structure.md -- runtime-spawned admits no
     exception). Both references are OPTIONAL on that view: an unwired one costs its
     lesson and leaves the day fully playable, the standing rule for this tutorial. -->
<!-- Added 2026-08-27, after the user turned the built shop into a prefab so one asset
     could serve both screens (D-105 put a shop in the day scene as well). The two scenes
     differ only by per-instance edits, which is the point of it being a prefab: the day
     scene REMOVES PowerupShopButton (an empty powerup is the opener there) and overrides
     sessionHost to GameManager; the menu keeps the button and points sessionHost at
     MainScreenRoot. sessionHost is empty in the asset and always will be -- a prefab
     cannot store a scene reference, the same constraint HUDCanvas records above, so a
     forgotten override is a shop that logs an error and refuses to open rather than one
     that silently misbehaves. -->
<!-- The powerup BAR is deliberately NOT here: it lives inside HUDCanvas, whose MainScreen
     instance removes it along with HeartPanel. Both are DAY-scene things kept off the menu
     by instance removal, which is the same trick D-064 used for the hearts and carries the
     same hazard -- an Apply that pushed them into the asset would put a dead powerup bar
     on the main screen. -->

<!-- Added 2026-08-26 during D-092's map repair: the prefab shipped with the out-of-keys
     popup (D-069) and never got an inventory line, which the same blind checker hid. -->

<!-- Carries a THIRD system's widget since D-065 step 3: KeysView (sys: KeySystem) sits
     in the prefab beside the wallet, replacing the old lives readout on the same object.
     That is not a repeat of the two-systems-one-prefab tangle D-064 untangled -- the
     opposite. Hearts left the prefab because they are a DAY-scene thing; keys belong in
     it because they are shown on BOTH screens and a prefab asset cannot hold a scene
     reference, which is precisely what HudWalletSource (the seam) solves. Editing this
     prefab intentionally changes both screens at once -- for keys that is the point. -->
- TicketCard — TicketSystem — variant-of: - — spawned by TicketCardsView
<!-- TicketCard Into was listed here until 2026-08-30. It was a TicketCard variant that
     no scene, prefab or config referenced -- the audit's GUID sweep found zero inbound
     references -- so it was deleted with the other orphan assets (S-03). It is in git
     history if the variant is ever wanted back. -->
- TicketCard UpDown — TicketSystem — variant-of: TicketCard — spawned by TicketCardsView
<!-- Prefab inventory filled in 2026-08-19 (D-013); it was template text until the
     HUD became the first thing deliberately SHARED between two scenes. HudCanvas is
     filed under ProgressionSystem because the wallet HUD is that system's per
     blueprint. It carried LivesView (sys: LivesSystem) as well until D-064 -- one
     prefab, two systems' widgets, with HudWalletSource as the seam -- and now it does
     not: the heart row is a SampleScene-only addition to the INSTANCE, so the prefab
     asset is a single-system thing again and HudWalletSource has no lives members left
     to forward. The hazard that replaces the old one: Apply All / Apply Added GameObject
     on that instance would push the row into the asset and put hearts on the main
     screen, which is the one thing D-064 was asked to prevent. The three TicketCard rows
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
  Scripts/Debug/       ← .cs: the SRDebugger cheat/inspection panel. NO asmdef, for the
                           same mechanical reason as Bootstrap but a different cause: it
                           extends SRDebugger's `SROptions` PARTIAL class, and a partial's
                           halves must share an assembly — SRDebugger's half has no asmdef,
                           so neither may ours. Everything in here is wrapped in
                           `UNITY_EDITOR || DEVELOPMENT_BUILD` and ships in no release build
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
