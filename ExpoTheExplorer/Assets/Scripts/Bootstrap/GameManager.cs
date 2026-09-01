using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using ExpoTheExplorer.Core;
using ExpoTheExplorer.Data;
using ExpoTheExplorer.Systems.BoardDistribution;
using ExpoTheExplorer.Systems.DayLifecycle;
using ExpoTheExplorer.Systems.DaySystem;
using ExpoTheExplorer.Systems.EconomySystem;
using ExpoTheExplorer.Systems.KeySystem;
using ExpoTheExplorer.Systems.LivesSystem;
using ExpoTheExplorer.Systems.MetaSystem;
using ExpoTheExplorer.Systems.PowerupSystem;
using ExpoTheExplorer.Session;
using ExpoTheExplorer.Systems.ProgressionSystem;
using ExpoTheExplorer.Systems.TicketSystem;
using ExpoTheExplorer.Systems.TraySystem;
using ExpoTheExplorer.Systems.Tutorial;
using ExpoTheExplorer.UI;

// The Tutorial assembly mirrors three of Data's and DaySystem's types -- its own step kind,
// its own powerup enum, its own trigger enum -- deliberately, so it can keep an empty
// reference list (see blueprint.md). This class is the ONE place that holds both sides and
// the translation between them; the alias that used to disambiguate TutorialStepKind is gone
// with DaySystem's copy of it (D-115), which no longer exists.
using UnityEngine;
using UnityEngine.EventSystems;

namespace ExpoTheExplorer.Bootstrap
{
    // Thin entry point: builds the central GameState from config and exposes it,
    // then drives the ticket lifecycle, board distribution, and tray delivery.
    // Systems/UI bind to State/TicketSlotManager/TrayManager rather than this
    // class growing gameplay logic itself.
    // SessionHost rather than MonoBehaviour since 5b (decisions.md D-022): the shared HUD
    // binds to "whichever component provides this scene's session", and the day scene's
    // provider is this class. Changing the base class does not change the component's type
    // identity, so nothing serialized in SampleScene is disturbed.
    public class GameManager : SessionHost
    {
        [SerializeField] private GameConfig gameConfig;
        [SerializeField] private TicketGenerationConfig ticketGenerationConfig;
        [SerializeField] private FoodCatalog foodCatalog;
        [SerializeField] private EconomyConfig economyConfig;
        [SerializeField] private LivesConfig livesConfig;

        // Required, like livesConfig beside it (decisions.md D-065). Not optional the way
        // metaCatalog is: without it GameSession cannot build a KeyManager at all, and a
        // null one would push a null check into every future caller instead of failing
        // here, once, where the missing drag actually is.
        [Tooltip("Key economy knobs (cap, regen minutes, refill cost). Create via Create > ExpoTheExplorer > Data > Key Config.")]
        [SerializeField] private KeyConfig keyConfig;

        // Required in practice, but it FAILS OPEN rather than aborting Awake the way
        // keyConfig above does (GDD 5.2, .claude/powerup-plan.md). The difference is what
        // each one costs when missing: without a KeyConfig there is no session at all and
        // the scene cannot run, while without this there is simply no powerup stock -- the
        // day is fully playable, three buttons are just absent. That is the same trade
        // NoKeysPopupView documents: a forgotten drag must never be the thing that makes
        // the game unplayable. It is still LOUD, because an unwired field and a player
        // who has spent all their charges look identical on screen.
        [Tooltip("Powerup stock knobs (starting charges, Gem price, per-day grant, clarity window). Create via Create > ExpoTheExplorer > Data > Powerup Config.")]
        [SerializeField] private PowerupConfig powerupConfig;

        // Optional, and it owns nothing: this class asks for one haptic (a key being
        // spent) and is otherwise unaware haptics exist. The day's other moments reach
        // HapticsBinder through GameState's events, without passing through here.
        [Tooltip("Optional. The scene's HapticsBinder, used only so spending a key can be felt. Unwired changes nothing but that.")]
        [SerializeField] private HapticsBinder haptics;

        // Optional, and the reason it hangs off this class rather than off the tray:
        // the two life-loss routes both end here and NOWHERE else, so this is the one
        // place that knows a life was actually spent -- and since D-078 it also knows
        // on which slot. The tray could only ever see half of it.
        [Tooltip("Optional. The scene's LifeLostHeartView, shown which slot just cost a life so a broken heart can rise from that tray. Unwired, nothing but that animation is missing.")]
        [SerializeField] private LifeLostHeartView lifeLostHeartView;

        // Optional, and the failure is genuinely harmless: with this empty the Auto-Collect
        // powerup finds nothing registered to perform it, so pressing its button refuses
        // and -- because the refusal is honest -- costs the player no charge. The other two
        // powerups are unaffected.
        //
        // It is a UI object rather than a system, and that is forced rather than chosen:
        // placing an item has to go through the tray's ordinary drop path or the tray draws
        // nothing (see AutoCollectRunner), and that path lives in MonoBehaviours.
        [Tooltip("Optional. The scene's AutoCollectRunner. Without it the Auto-Collect powerup does nothing and spends nothing.")]
        [SerializeField] private AutoCollectRunner autoCollectRunner;

        // The animated half of Noise Clear (D-120). OPTIONAL, and the failure is gentler than
        // Auto-Collect's: without it the powerup still CLEARS, through the data-only
        // PowerupEffects.ClearUnneededItems, and the items simply blink out the way they did
        // before. A forgotten drag costs the animation, not the powerup.
        [Tooltip("Optional. The scene's NoiseClearRunner, which drops cleared items off the board. Without it Noise Clear still works, the items just vanish instantly.")]
        [SerializeField] private NoiseClearRunner noiseClearRunner;

        // The popup a Day shows for each food it introduces, before the clock starts. A
        // PREFAB rather than a scene object, because there is one per introduced item and
        // they are shown one after another -- the same shape MetaGroundsView's unlock popup
        // has, and for the same reason.
        //
        // OPTIONAL, and the degraded mode is the day exactly as it played yesterday: an
        // unwired field means the introductions are skipped, never that the day refuses to
        // start. That is the trade every optional reference in this class documents -- a
        // forgotten drag must not be the thing that makes the game unplayable -- and it
        // matters more here than most, because this one holds the clock while it is up.
        // It is still LOUD when a Day actually authored an introduction, since an empty
        // field and a Day that introduces nothing look identical on screen.
        [Tooltip("Optional. Assets/Prefabs/UI/NewItemIntroPopup.prefab — shown at Day Start for each item this Day introduces. Unwired, the introductions are simply skipped.")]
        [SerializeField] private NewItemIntroPopup newItemIntroPopupPrefab;

        // The four numbers behind the day's star rating (decisions.md D-060). Required,
        // unlike metaCatalog below: without it every completed day scores 0 stars and pays
        // no gems, so Awake says so as an ERROR rather than a note.
        [Tooltip("Star thresholds and the two failure penalties. A completed day scores 0 stars while this is empty.")]
        [SerializeField] private StarScoreConfig starScoreConfig;

        // OPTIONAL, and read for exactly one question: will the day the player is about to
        // enter open a Day-unlocked prop? If it does, "Next Day" sends them to the main
        // screen so the celebration (decisions.md D-041) cannot be skipped.
        //
        // Optional rather than required because a day scene must not be broken by a missing
        // reference for what is, from the day's point of view, a routing nicety. But NOT
        // silent: Awake says so once, because an empty field and a catalog with no upcoming
        // unlocks look identical on screen -- the same reason HudWalletSource logs which mode
        // it resolved to.
        [Tooltip("Only used to decide whether finishing this day should return the player to the main screen, so a newly unlocked prop is actually seen. Leave it empty and that routing simply does not happen.")]
        [SerializeField] private MetaCatalog metaCatalog;

        // No boardDistributionConfig field on purpose: board-distribution balancing is
        // authored per Day and arrives with the Day (decisions.md D-004). The shared asset
        // is now Day-Editor-only -- a seed for new Days. Wiring it here again would put a
        // second authority back on the runtime path.

        // Exposed for the ticket card's timer bar: the two ratios that decide
        // where it changes colour are the SAME ones that decide the tip tier, and
        // they live on EconomyConfig because they decide money (CLAUDE.md — Tip
        // Tiers). TicketCardsView reads them through here rather than keeping its
        // own copy next to the bar's colours, so the colour on screen and the tip
        // actually paid can never disagree about where a tier starts.
        public EconomyConfig EconomyConfig => economyConfig;

        // Exposed for the same reason EconomyConfig is: a HUD view needs the authored
        // numbers (here, each powerup's name and description) and reading them through the
        // scene's one composition root beats a second serialized slot on every view.
        public PowerupConfig PowerupConfig => powerupConfig;

        // Everything a PLAYER owns rather than a day: state, wallet, lives, the Day
        // catalog, the day index and the owned meta props. Shared with the main screen
        // (decisions.md D-021), which is why it is a class and not more fields here.
        public override GameSession Session => session;

        private GameSession session;

        // Forwarded, not re-stored. Keeping the property NAMES identical is what let this
        // extraction happen without rewiring a single view in the scene -- roughly a dozen
        // components read GameManager.State, and a rename would have touched all of them
        // for no gain.
        public GameState State => Session?.State;
        public LivesManager LivesManager => Session?.LivesManager;

        // Forwarding property, same shape and same reason as LivesManager above: the
        // session owns it, and a dozen hand-wired views in the scene reach their systems
        // through this component. Step 5's out-of-keys popup reads it too.
        public KeyManager KeyManager => Session?.KeyManager;

        // Forwarding property, same shape and same reason as KeyManager above. NULL when
        // no PowerupConfig was wired (see the field's note) — every reader null-checks,
        // which is the price of failing open and is deliberately paid here rather than in
        // GameSession, where a Debug.LogError would fail every EditMode test that builds a
        // session without one.
        //
        // The day scene is also the ONLY place that registers powerup effects (Adım 4-6 of
        // .claude/powerup-plan.md fill these in). That is what makes the two-screen split
        // safe: the main screen builds the same manager to sell against, registers nothing,
        // and therefore cannot spend a charge even if a use button ended up there.
        public PowerupManager PowerupManager => Session?.PowerupManager;

        // This Day's forced first move while it is unfinished, and null the rest of the time
        // -- which is every Day but the first, and the first Day once the player has made
        // the move. Rebuilt on every day start by ArmTutorial.
        public TutorialDirector Tutorial { get; private set; }

        // Fires whenever the current tutorial step changes -- a Day start arming one, the
        // player finishing a step, the tutorial ending or being aborted. WorldTrayView
        // listens so that whichever tray is the NEW target can raise the spotlight and the
        // old one can tear its own down. It has to be an event rather than a one-time read
        // for two reasons: a RETRY re-arms mid-scene long after every Start has run, and
        // (since D-083) a step boundary is a mid-day event by definition. The trays also
        // check the already-armed case on their own Start, since the first arm happens in
        // Awake -- before anything could have subscribed. Same subscribe-then-sync shape
        // BoardView uses for CellChanged.
        //
        // A plain event, not a GameState EventBus: GameState lives in Core, and putting a
        // Tutorial type on it would point Core at a system that depends on nothing.
        public event Action TutorialStepChanged;

        // The two gates the tutorial imposes, asked by the board item being pressed and by
        // the tray being dropped on. They are null-safe here rather than at the call sites
        // so "no tutorial" is answered in ONE place -- and both read as an ordinary
        // permission check rather than as a tutorial special case.
        public bool IsBoardPickupAllowed(int x, int y) => Tutorial == null || Tutorial.IsPickupAllowed(x, y);

        public bool IsTrayDropAllowed(int slotIndex) => Tutorial == null || Tutorial.IsTrayDropAllowed(slotIndex);

        // Asked before a drop turns into a board-to-board move. Null-safe here like the
        // other two, so a day with no tutorial relocates items exactly as it always has.
        public bool IsBoardRelocationAllowed() => Tutorial == null || Tutorial.IsBoardRelocationAllowed();

        // Whoever is currently holding the day still. GameState.IsPaused is a single bool,
        // and until D-105 a single panel wrote it; now TWO can be up over a running day (the
        // settings menu, and the powerup shop an empty powerup opens). Two views each
        // assigning that bool is exactly the dual-authority the root invariant forbids, and
        // the bug it produces is concrete: close the shop while the settings menu is still
        // open and the day starts running underneath it.
        //
        // A SET of holders rather than a counter, because the failure mode a counter has is
        // silent -- one panel releasing twice takes the pause off someone else's hold, and
        // nothing in the numbers says so. Adding the same holder twice is a no-op here, and
        // releasing one that never held is too. Holders are Objects (the views themselves),
        // so the set also survives a view being destroyed mid-hold: OnDestroy releases, and
        // a leaked entry would keep the day frozen forever, which is why every caller
        // releases from OnDestroy as well as from its close path.
        private readonly HashSet<object> pauseHolders = new();

        // The single writer of GameState.IsPaused since D-105. Everything that freezes a
        // running day goes through this pair; nothing assigns the flag directly.
        public void HoldPause(object holder)
        {
            if (holder == null || State == null) return;

            pauseHolders.Add(holder);
            State.IsPaused = pauseHolders.Count > 0;
        }

        public void ReleasePause(object holder)
        {
            if (holder == null || State == null) return;

            pauseHolders.Remove(holder);
            State.IsPaused = pauseHolders.Count > 0;
        }

        public TicketSlotManager TicketSlotManager { get; private set; }
        public TrayManager TrayManager { get; private set; }
        public DayLifecycleManager DayLifecycleManager { get; private set; }

        private TicketFactory ticketFactory;
        private EconomyCalculator economyCalculator;
        private DayTicketSequenceProvider dayTicketSequenceProvider;

        // (Re)constructed alongside dayTicketSequenceProvider (see
        // RefreshDayTicketSequenceProvider) rather than once for the whole session --
        // its leakedTickets dedup state must not survive into a retried/new Day, or a
        // ticket that already leaked once in the PREVIOUS attempt would wrongly stay
        // "already leaked" (never leak again) in this one. Since D-004 there is a second
        // reason: each Day carries its own balancing, so a distributor built for the
        // previous Day would keep applying that Day's numbers.
        private BoardDistributor boardDistributor;

        // How many more OnTicketAssigned events open the Day and must therefore NOT
        // distribute. Set by ApplyDayStartBoardPreSeed to TicketSlotCount when (and only
        // when) this Day authored its own opening board, counted down by OnTicketAssigned.
        //
        // A COUNTER rather than a bool, because "the opening" is not one event: filling the
        // three slots publishes TicketAssigned three times, and every one of them would
        // otherwise spawn required items on top of the board the designer drew. Counting is
        // safe precisely because the opening fill is synchronous -- ApplyDayStartBoardPreSeed
        // runs immediately before FillEmptySlots/ResetSlotsForNewDay on all four day-start
        // paths, and nothing else can publish a TicketAssigned in between, so the events
        // this eats are exactly the opening ones. It counts assignments, not tickets, so a
        // Day whose sequence is shorter than three still lands on zero (an exhausted
        // sequence publishes a null assignment and that is still an opening event).
        private int openingAssignmentsWithoutDistribution;

        // Still the single writer of SoftMoney/Gems (economy-plan.md Adım 1); it just
        // lives on the session now, because the main screen needs the same one.
        private Wallet wallet => Session.Wallet;

        // No PlayerProfileStore field any more: GameSession composes what is written and
        // owns the only Save (decisions.md D-021). The four call sites below still own the
        // "a day attempt is atomic" contract -- WHEN to save is a game-flow decision and
        // stays here; WHAT gets written is a schema decision and does not.

        // Position in dayCatalog, not a Day's JSON dayIndex (that only decides
        // sort order) -- null until a Day catalog exists (PR-7), so every
        // consumer falls back to the pre-Day-system GameConfig behavior.
        private DayDefinition CurrentDay => Session.CurrentDay;

        // The star score's denominator: every ticket this Day will hand out, its own time
        // limit summed (decisions.md D-060). Asked of the Day rather than tracked here, so
        // it cannot drift from the limits TicketEntryFactory actually gives the tickets.
        // Zero with no Day loaded, which reads as "scored nothing" downstream.
        private float CurrentDayTicketSeconds => CurrentDay == null ? 0f : CurrentDay.TotalTicketSeconds;

        private void Awake()
        {
            // Paired with the identical line in MainScreenRoot.Awake -- see the reasoning
            // there. Short version: the platform default on mobile is 30, and this game is
            // a finger dragging an item, which is the one interaction where the frame rate
            // is the feel. Repeated here because the day scene is opened straight from the
            // Editor all the time, without MainScreen ever loading.
            Application.targetFrameRate = 60;

            EnsurePhysics2DRaycaster();

            // The whole session half of this method in one line (decisions.md D-021):
            // state, wallet + persisted balances, lives + persisted lives, the Day catalog
            // and the clamped day index, with the four ordering rules between them kept in
            // one place instead of duplicated per screen.
            //
            // A missing, corrupt or unversioned file loads as 0/0 (see PlayerProfileStore),
            // which is also what a brand-new player gets.
            // Checked BEFORE the session is built, unlike the optional references below:
            // GameSession cannot construct a KeyManager without it, so the failure would
            // otherwise be a NullReferenceException from inside a constructor rather than
            // a sentence naming the field and the menu that fills it.
            if (keyConfig == null)
            {
                Debug.LogError(
                    $"{nameof(GameManager)} on '{name}' has no {nameof(KeyConfig)} wired, so no session can be " +
                    "built and the day scene will not run. Create the asset via " +
                    "Create > ExpoTheExplorer > Data > Key Config and drag it into the Key Config field.",
                    this);
                return;
            }

            if (powerupConfig == null)
            {
                Debug.LogError(
                    $"{nameof(GameManager)} on '{name}' has no {nameof(PowerupConfig)} wired, so this day has no " +
                    "powerups at all — no charges, no HUD buttons, and a save written from here keeps whatever " +
                    "stock the profile already had rather than zeroing it. Create the asset via " +
                    "Create > ExpoTheExplorer > Data > Powerup Config and drag it into the Powerup Config field.",
                    this);
            }

            session = new GameSession(gameConfig, livesConfig, keyConfig, foodCatalog, powerupConfig);

            if (metaCatalog == null)
            {
                Debug.Log(
                    $"{nameof(GameManager)}: no MetaCatalog wired, so finishing a day will never route to the " +
                    "main screen for a prop unlock. Drag the catalog in if you want unlocks to be shown.", this);
            }

            if (starScoreConfig == null)
            {
                Debug.LogError(
                    $"{nameof(GameManager)}: no {nameof(StarScoreConfig)} wired, so every completed day will score " +
                    "0 stars and pay no gems. Drag Assets/Data/StarScoreConfig.asset into the Star Score Config slot.",
                    this);
            }

            ticketFactory = new TicketFactory(ticketGenerationConfig);
            DayLifecycleManager = new DayLifecycleManager(State, starScoreConfig);

            // The two life-loss paths pass DIFFERENT methods here, and that is the only
            // place the difference is decided (decisions.md D-060): a timeout and a wrong
            // delivery cost the star score different amounts, and neither system should
            // have to know that. TicketSlotManager and TrayManager keep taking a plain
            // Action and stay unaware there are now two of them.
            TicketSlotManager = new TicketSlotManager(State, CreateNextTicket, HandleTicketTimeout);
            TrayManager = new TrayManager(State, slotIndex => TicketSlotManager.DeliverTicket(slotIndex), HandleWrongDelivery);
            economyCalculator = new EconomyCalculator(economyConfig);

            // The catalog parse and the day-index clamp moved into GameSession, which is
            // where their ordering rule lives now. Both still happen BEFORE this line, so
            // the provider and distributor are still built off a resolved CurrentDay.
            RefreshDayTicketSequenceProvider();

            // The day's star budget, handed over on the way in exactly as it is on the
            // three retry/advance paths -- so day one of a session is scored by the same
            // line as every day after it. Nothing else here needs resetting (the state is
            // brand new), which is why this is the only day-start call.
            DayLifecycleManager.ResetForNewDay(CurrentDayTicketSeconds);

            // After TicketSlotManager exists, because the shared gate below reads it.
            RegisterPowerupEffects();

            // Subscribe before the initial fill so the first 3 tickets trigger
            // board playback too, not just later deliveries/cancellations.
            State.TicketAssigned.Subscribe(OnTicketAssigned);
            State.TicketDelivered.Subscribe(OnTicketDelivered);
            State.DayRetried.Subscribe(OnDayRetried);
            State.DayCompleted.Subscribe(OnDayCompleted);
            ApplyDayStartBoardPreSeed();
            TicketSlotManager.FillEmptySlots();
        }

        // The ONE place a powerup is connected to something that can actually perform it,
        // and it is this class for the reason every other system-to-system join is: this is
        // the day's composition root. PowerupSystem stays unaware of tickets, trays and the
        // board; it holds charges and calls a delegate.
        //
        // It is also what makes the two-screen split safe rather than merely tidy. The main
        // screen builds the same PowerupManager -- it needs the counts to sell against --
        // and registers NOTHING here, so a use button that ended up on the menu by mistake
        // cannot spend a charge: TryUse finds no effect and refuses without deducting.
        //
        // Adım 5 and 6 of .claude/powerup-plan.md add one line each. They go behind the
        // same gate; that is the point of writing it once.
        private void RegisterPowerupEffects()
        {
            var powerups = PowerupManager;
            if (powerups == null) return;

            // CanUsePowerup(type), NOT CanUsePowerups(). The difference is one letter and it
            // cost two play-tests (D-115, fixed 2026-08-28): the plural is the SHOP's gate and
            // answers false for every powerup while ANY tutorial step is armed -- so during the
            // lesson that FORCES a press, this lambda short-circuited before the effect was
            // ever reached. It then returned false, correctly spent no charge (GDD 5.2), and
            // the step advanced on the press having done nothing at all, which is exactly what
            // a player reports as "the button works but the powerup doesn't".
            //
            // The singular asks the same two day-liveness questions and then asks the tutorial
            // about THIS powerup, so outside a lesson the two are identical and inside one only
            // the powerup being taught gets through. The button already used the singular; it
            // was the effect behind it that re-asked the wrong question.
            powerups.RegisterEffect(
                PowerupType.TimeReset,
                () => CanUsePowerup(PowerupType.TimeReset) && PowerupEffects.ResetMostUrgentTicketTimer(State));

            // Noise Clear takes the tray contents since D-118, because it now clears down to
            // what the tickets STILL need rather than to what they need in principle -- a cola
            // already in a tray is one the board no longer has to hold. Supplied from here for
            // the reason Auto-Collect's are: this is the composition root that holds both the
            // board and the trays, and the effect takes plain BoardItems so PowerupSystem
            // still needs no TraySystem reference of its own.
            // Two paths, one rule. The runner clears through PlanNoiseClear and drops the
            // items off the board (D-120); without one wired this falls back to the data-only
            // sweep, which removes exactly the same items and simply blinks them out. The
            // fallback is the point: a forgotten drag must cost the animation, not the
            // powerup.
            powerups.RegisterEffect(
                PowerupType.NoiseClear,
                () => CanUsePowerup(PowerupType.NoiseClear)
                    && (noiseClearRunner != null
                        ? noiseClearRunner.Run()
                        : PowerupEffects.ClearUnneededItems(State, TrayContentsSnapshot())));

            // The only one of the three whose effect is not a static call, because placing
            // an item has to go through the tray's ordinary drop path. Registered even when
            // the runner is unwired -- the lambda's null check turns that into an honest
            // "nothing happened", which costs no charge, rather than a missing registration
            // that would look identical from the outside anyway.
            powerups.RegisterEffect(
                PowerupType.AutoCollect,
                () => CanUsePowerup(PowerupType.AutoCollect) && autoCollectRunner != null && autoCollectRunner.Run());
        }

        // Read at the moment the effect runs rather than cached, because a tray's contents
        // change constantly and a stale snapshot would make Noise Clear keep board items for
        // a requirement the player has already satisfied. Three slots, on a button press.
        //
        // Null-safe on TrayManager for the same reason every other powerup path is: the main
        // screen builds no trays, and an effect that is never registered there still has to
        // compile against the same class.
        private IReadOnlyList<BoardItem>[] TrayContentsSnapshot()
        {
            var contents = new IReadOnlyList<BoardItem>[GameState.TicketSlotCount];
            if (TrayManager == null) return contents;

            for (var slot = 0; slot < contents.Length; slot++)
            {
                contents[slot] = TrayManager.GetContents(slot);
            }

            return contents;
        }

        // The shared gate for every powerup: the day has to actually be running.
        //
        // IsAwaitingContinue is the important half. When the last life goes the day freezes
        // and the player is looking at the Continue popup -- spending a charge behind it
        // would be paying for a world they cannot see, and the timers it refilled would be
        // the timers of a day they may be about to abandon. BoardItemDragHandler gates
        // dragging on the identical flag, which is the precedent this follows rather than
        // invents.
        //
        // IsDayComplete is the cheap half: with the day over every slot is empty, so each
        // effect would find nothing to do and refuse on its own anyway. It is named here
        // regardless, so the rule reads as "the day must be live" rather than resting on
        // three separate effects each happening to no-op.
        //
        // Checked at USE time rather than by disabling the buttons, the same choice D-069
        // made for keys: a greyed-out button does not say why, and this state lasts seconds.
        //
        // The tutorial joins that list for a reason the other two share: during the forced
        // first move there is exactly one legal action, and all three powerups act on the
        // board or the tickets. Auto-Collect in particular would sweep the very item the
        // player is being told to drag, leaving a ghost pointing at an empty cell and a step
        // that can no longer be completed.
        //
        // PUBLIC since D-105 so the powerup bar can ask the same question before opening the
        // shop an empty powerup leads to. The three reasons above all apply unchanged to
        // buying: a store opened under the Game Over popup, over the day-complete receipt, or
        // in the middle of a forced first move is the same mistake as spending a charge there
        // -- and the shop additionally FREEZES the day, which those three states are already
        // doing for their own reasons.
        //
        // IsArmed rather than IsActive since D-115: a tutorial step that is merely WAITING
        // for its moment leaves the day running normally, and a day the player is playing
        // normally is one they may spend and buy in. Only an armed step refuses.
        public bool CanUsePowerups() => !State.IsAwaitingContinue && !TicketSlotManager.IsDayComplete
            && (Tutorial == null || !Tutorial.IsArmed);

        // The same question for ONE powerup, and the difference is the whole forced-use step:
        // while the tutorial is asking the player to press Time Reset, Time Reset is the one
        // thing that may be pressed -- CanUsePowerups above answers false for every powerup,
        // including that one, because it is also the question the SHOP asks and no shop opens
        // mid-lesson.
        //
        // Split into two methods rather than given a parameter with a default, because the
        // two callers want genuinely different answers: the bar asks about a specific button,
        // and the shop asks whether buying is possible at all.
        public bool CanUsePowerup(PowerupType type)
        {
            if (State.IsAwaitingContinue || TicketSlotManager.IsDayComplete) return false;
            if (Tutorial == null || !Tutorial.IsArmed) return true;

            return Tutorial.IsPowerupUseAllowed(ToTutorialPowerup(type));
        }

        private void OnDestroy()
        {
            // The rule D-105 set for every pause holder: release from OnDestroy as well as
            // from the close path, so a torn-down scene cannot leave a hold behind. It also
            // takes the popup with it -- a parentless instance outlives its owner until the
            // scene unloads, and during a domain reload in the editor that is long enough to
            // see.
            StopItemIntros();

            State.TicketAssigned.Unsubscribe(OnTicketAssigned);
            State.TicketDelivered.Unsubscribe(OnTicketDelivered);
            State.DayRetried.Unsubscribe(OnDayRetried);
            State.DayCompleted.Unsubscribe(OnDayCompleted);
        }

        // Paused while awaiting Continue (GDD Section 6 — Lives depleted, day
        // over) so a frozen ticket countdown can't keep cancelling tickets and
        // requesting further life loss from LivesManager, which is already a
        // no-op at 0 Lives but would otherwise mask the pause with silent
        // no-ops instead of actually holding time still.
        private void Update()
        {
            if (State.IsAwaitingContinue) return;

            // The settings popup (D-094). Third gate on the same line rather than a
            // condition folded into one of the other two: all three mean "hold the ticket
            // clock", but they are held by different things for different reasons, and a
            // combined condition would make it impossible to tell from a stuck clock which
            // one forgot to let go.
            if (State.IsPaused) return;

            // The tutorial stops the clock for the WHOLE of its run, not only for the steps
            // that ask the player to read (D-097). It started as the reading gate alone, on
            // the reasoning that a panel takes longer than a Patient ticket has; the same
            // thing turned out to be true of the moves. A forced move is TAUGHT, not raced —
            // a player working out which item goes where is reading the board for the first
            // time, while three tickets they did not order drain behind the dim and can time
            // out mid-lesson. Its own condition rather than a second use of IsAwaitingContinue,
            // which means "the Continue popup is up" and is read by four other places.
            //
            // Safe by construction against a tutorial that never ends: every way one stops —
            // the last step completing, an impossible step aborting (EnsureCurrentTutorialStep-
            // IsPossible), Abort() — moves the same field this reads, so a released
            // tutorial is a released clock with nothing extra to remember.
            //
            // IsArmed rather than IsActive since D-115, and that is not a loosening of D-097:
            // every step that used to freeze the clock still freezes it, because every one of
            // them arms the instant it becomes current. What is new is a step that is waiting
            // for a ticket to run down — the deferred Time Reset lesson — and freezing the
            // clock for THAT one would be a deadlock: the trigger it waits for is the clock.
            if (Tutorial != null && Tutorial.IsArmed) return;

            // The day is live again. Anything the Continue hold postponed runs HERE, before
            // the clock starts, so the board settles into the frame the player is looking at
            // rather than having moved while they read a popup (D-099).
            //
            // Reached by reading the flag above rather than by subscribing to a "the day
            // resumed" event, and that is the whole safety argument: the hold is lifted by
            // four callers -- both paid Continues, RetryDay, and SRDebugger's refill -- and a
            // publisher any one of them forgot would strand a slot empty for the rest of the
            // day, with the day then unable to complete. There is no flag to forget here,
            // because it is the same one that gates the return above.
            //
            // Only the TICKET half is still deferred (D-102). A wrong order's scatter used to
            // wait here too, and no longer does: it returns the tray's own items to the board
            // and assigns nothing, so it is safe to play in front of a popup that now waits
            // (D-101) -- and holding it back was what left the final wrong order with no tray
            // animation at all. A deferred CANCELLATION is a different animal: it replaces the
            // ticket and spawns a whole board round, which must not happen on a day that ended.
            TicketSlotManager.ResolveDeferredTimeouts();

            TicketSlotManager.Tick(Time.deltaTime);

            // AFTER the tick, deliberately: the ratio a waiting step is compared against is
            // the one the player can see at the end of this frame, and arming takes effect at
            // the top of the next one. Feeding it before the tick would arm a step against a
            // number a frame older than the bar the player is looking at, and would also put a
            // state change between this method's tutorial gate and the tick it guards.
            NotifyTutorialOfTicketPatience();
        }

        // The deferred trigger's only feed. It hands over the LOWEST remaining fraction among
        // the active tickets, so a step arms on the first ticket to reach its threshold rather
        // than on some particular slot -- which is what "whenever a ticket drops to a third"
        // means.
        //
        // It also hands over WHOSE fraction that was (D-146). The rule above is unchanged --
        // any ticket can arm the step -- but the lesson that follows dims the screen down to
        // the ticket that did it, so the arming moment is the only moment that answer exists:
        // by the time the player reaches for the powerup, a different ticket may well be the
        // one closest to running out. The director stores it and never consults it.
        //
        // Polled rather than event-driven because RemainingSeconds is mutated directly every
        // frame and publishes nothing; TicketCardView's timer bar reads it the same way for
        // the same reason. The cost is three divisions and three comparisons per frame, and
        // only while a step is actually waiting -- every other frame leaves on the first line.
        private void NotifyTutorialOfTicketPatience()
        {
            if (Tutorial == null || !Tutorial.IsAwaitingTrigger) return;

            // THE SAME QUESTION TIME RESET ITSELF ASKS, asked through the same method
            // (D-147). That sharing is the point rather than a tidy-up: the lesson arms on a
            // ticket, dims the screen down to it, and the powerup then refills "the most
            // urgent" -- if these were two loops agreeing by coincidence, one edit would make
            // the lesson point at a ticket the powerup does not save.
            //
            // This loop used to live here and skipped resolved tickets only by way of a null
            // check; the shared helper skips delivered and cancelled ones too, which is a
            // quiet fix rather than a change of intent -- a resolved ticket sitting in the
            // array mid-cascade was never a thing the tutorial should have armed on.
            var slotIndex = PowerupEffects.MostUrgentActiveTicketSlot(State, out var lowestRatio);

            // No active ticket with a clock: nothing to say, and saying float.MaxValue would
            // be a lie the director would (correctly) ignore anyway.
            if (slotIndex < 0) return;

            Tutorial.NotifyTicketPatienceRatio(lowestRatio, slotIndex);
        }

        // Production is order-triggered (GDD Section 4), not a continuous poll --
        // every time a slot's ticket changes (a new arrival, OR a slot going empty
        // once the Day's authored sequence is exhausted), BoardDistributor
        // re-evaluates live what the board needs (required-pool top-up, then
        // noise-pool leaking) from the CURRENT active + upcoming ticket state.
        // Deliberately unconditional on assignment.Ticket being non-null: with
        // GuaranteedTicketCount covering only a few tickets per round, a later
        // slot-emptied event is what finally makes an earlier, not-yet-covered
        // active ticket "earliest" and eligible -- skipping this on a null
        // assignment was a real bug (found via playtest): the last tickets of a
        // finite Day could reach the end of the sequence without ever getting a
        // qualifying round, since no further arrivals remained to trigger one.
        // That slot's tray (if a timeout left it holding orphaned items) is
        // cleared back onto the board either way. No fallback: an authored Day
        // is required (PR-7).
        private void OnTicketAssigned((int SlotIndex, Ticket Ticket) assignment)
        {
            if (CurrentDay == null)
            {
                throw new InvalidOperationException("No Day loaded -- board playback requires an authored Day (PR-7).");
            }

            // The opening fill of a Day that drew its own board distributes NOTHING -- see
            // ApplyDayStartBoardPreSeed. The tray call below still runs: it clears the slot's
            // tray, which has to happen for every assignment whatever the board is doing.
            //
            // Skipping the call outright, rather than letting the distributor run and
            // discarding its spawns, is the point: OnOrderPlaced is what makes a ticket
            // "guaranteed" for the rest of the Day (its selection is sticky), so a
            // suppressed round must not consume the opening tickets' turn at that lottery.
            // They enter it at the first real round instead, which is what lets the Day
            // recover normally the moment a slot resolves.
            if (openingAssignmentsWithoutDistribution > 0)
            {
                openingAssignmentsWithoutDistribution--;
            }
            else
            {
                var activeTickets = State.TicketSlots.Where(t => t != null).ToList();
                var lookaheadCount = Math.Max(GameState.TicketSlotCount, CurrentDay.TicketRuntime.UpcomingQueueSize);
                var upcomingTickets = dayTicketSequenceProvider.PeekUpcoming(lookaheadCount);
                boardDistributor.OnOrderPlaced(activeTickets, upcomingTickets);
            }

            TrayManager.OnTicketAssigned(assignment.SlotIndex);
        }

        // RECORDS the Economy Module's payout (GDD Section 9 — Order Value + the
        // tier's tip) the instant a ticket is delivered, and deliberately does not
        // pay it. TicketDelivered fires with the ticket that just left, still holding
        // its final RemainingSeconds, so the tier is read from that same instance --
        // which is the only moment the delivered ticket's remaining time still exists.
        //
        // The wallet call that used to sit here is gone (D-057): nothing reaches the
        // player's balance until the day is COMPLETED, and the receipt this line feeds
        // is what the day then owes. DayLifecycleManager.RecordDelivery already rounds
        // once per delivery, so its running Total is to the coin the same number the
        // per-delivery EarnSoftMoney calls used to add up to -- moving the payment did
        // not move the amount.
        private void OnTicketDelivered((int SlotIndex, Ticket Ticket) delivery)
        {
            var payout = economyCalculator.CalculatePayout(delivery.Ticket);
            DayLifecycleManager.RecordDelivery(payout);
        }

        // Both life-loss paths (TicketSlotManager's timeout, TrayManager's wrong
        // delivery) funnel through the same method below so the Day Complete popup's
        // "Orders failed" count catches either cause -- there's no other place
        // both funnel through.
        //
        // They now arrive through two named entry points instead of one shared delegate
        // (decisions.md D-060), because the star score charges them different amounts. The
        // split lives HERE and only here: this class is the one that hands each system its
        // delegate, so no system had to learn what kind of failure it causes, and neither
        // can report the wrong kind.
        private void HandleTicketTimeout(int slotIndex) => HandleLifeLoss(DayFailureCause.Timeout, slotIndex);

        private void HandleWrongDelivery(int slotIndex) => HandleLifeLoss(DayFailureCause.WrongDelivery, slotIndex);

        private void HandleLifeLoss(DayFailureCause cause, int slotIndex)
        {
            LivesManager.LoseLife();
            DayLifecycleManager.RecordFailure(cause);

            // AFTER the life is actually gone, never before: a heart flying up for a
            // loss that did not happen would be the one kind of lie this readout must
            // not tell. It is asked on both causes and both are equal here -- to the
            // player a heart is a heart, and which mistake spent it is what the shake
            // or the vanishing card already says.
            //
            // Optional and silent when unwired, the shape `haptics` above established
            // (D-070): a forgotten drag costs the animation, never a life.
            lifeLostHeartView?.Show(slotIndex);
        }

        // The day ended in failure and is being replayed, so this attempt's
        // earnings are taken back and its spending is not (economy-plan.md Adım 2
        // -- "a day attempt is atomic"). Without this the day's income survived a
        // failed day, which made repeatedly losing a day a way to farm money.
        //
        // Wired to DayRetried rather than called from RetryDay directly because
        // the event already exists for exactly this and RetryDay is also reachable
        // from UI. Note a paid Continue never publishes DayRetried, so continuing
        // correctly keeps what the day has earned so far -- only abandoning the
        // attempt gives it up.
        private void OnDayRetried(int _)
        {
            wallet.RevertToDayStart();
        }

        // The day's earnings stop being provisional the instant its goal is
        // reached, so this is the only place a wallet reaches disk on the winning
        // path (economy-plan.md Adım 4). Nothing is written on a failed day
        // precisely because nothing about it is permanent -- which is also why
        // quitting mid-day loses that day's income rather than banking it.
        private void OnDayCompleted(int _)
        {
            pendingReward = new DayRewardPurse(
                DayLifecycleManager.Total,
                DayLifecycleManager.StarCount * gameConfig.GemsPerStar);

            // Still saves, and still saves NOTHING of the reward. What this write banks
            // is everything the day changed that is not money: lives lost, and the day
            // index. Dropping it to "the handover saves" would mean a player who force
            // quits on the popup relaunches with their pre-day lives, which is a worse
            // trade than the ~2s window the money now sits in.
            SaveProfile();
        }

        // What the completed day owes the player but has not handed over yet. Null
        // whenever no day is waiting to pay out, which is every moment except between
        // DayCompleted and the player leaving the finished day.
        //
        // The debt exists because the payout is now a PERFORMANCE (D-057): coins and gems
        // reach the balance as the Day Complete popup's reward flight lands each icon on
        // its HUD counter, so what the player sees arrive and what they actually own are
        // the same event rather than two events that have to be kept in step.
        private DayRewardPurse pendingReward;

        public int PendingRewardSoftMoney => pendingReward?.SoftMoneyRemaining ?? 0;
        public int PendingRewardGems => pendingReward?.GemsRemaining ?? 0;

        // The two hand-over steps the reward flight calls, one per icon that lands. Both
        // go through Wallet like every other balance change in the game -- the purse only
        // decides how much of the debt is allowed out, it never touches a balance itself.
        // Both are clamped by the purse, so a miscounted animation can pay out less than
        // the day earned but never more.
        public void ClaimRewardGems(int count)
        {
            if (pendingReward == null) return;

            wallet.EarnGems(pendingReward.TakeGems(count));
        }

        public void ClaimRewardSoftMoney(int amount)
        {
            if (pendingReward == null) return;

            wallet.EarnSoftMoney(pendingReward.TakeSoftMoney(amount));
        }

        // Called by the reward flight when its last icon has landed. Separate from
        // CommitPendingReward only in that it also writes the file: the exits below
        // already save for their own reasons, and this one has no other reason to.
        public void CompleteRewardHandover()
        {
            if (CommitPendingReward()) SaveProfile();
        }

        // The safety net under the whole animation, and the reason a broken or unwired
        // reward flight cannot cost the player money: every exit from a finished day runs
        // this first, so whatever the flight did not hand over is paid in full before the
        // player can go anywhere. Returns whether anything was actually credited, so the
        // callers that already save do not gain a second write for nothing.
        private bool CommitPendingReward()
        {
            if (pendingReward == null) return false;

            var softMoney = pendingReward.TakeAllSoftMoney();
            var gems = pendingReward.TakeAllGems();
            pendingReward = null;

            wallet.EarnSoftMoney(softMoney);
            wallet.EarnGems(gems);

            return softMoney > 0 || gems > 0;
        }

        // The opposite of committing, for the one exit where the day's result is being
        // thrown away rather than collected: a voluntary redo. The debt is voided instead
        // of paid, which is what stops a finished day being replayed for its reward over
        // and over. Anything the flight already handed over is a real balance by then and
        // is taken back by RevertToDayStart, not by this.
        private void DiscardPendingReward()
        {
            pendingReward = null;
        }

        // Builds the profile from GameState's public getters rather than asking
        // the wallet for its numbers: the wallet owns the RULES for changing
        // balances, GameState holds the values.
        // WHEN to save is game flow and stays here; WHAT gets written is schema and moved
        // to GameSession.Save (decisions.md D-021). The four callers below are unchanged.
        private void SaveProfile() => Session.Save();

        // The key charge for GIVING UP ON A DAY (.claude/key-plan.md step 4, widened by
        // D-135). One place rather than four call sites, because this class cannot be
        // reached by the EditMode suite at all -- it is in the predefined Assembly-CSharp
        // (D-012) -- so the only protection this rule has is that there is exactly one
        // copy of it to read.
        //
        // THE RULE IS "GAVE UP", NOT "LOST". It used to be gated in here on
        // IsAwaitingContinue, which made the settings menu's Retry and Main Menu free:
        // that menu only opens while the day is still RUNNING, so the flag was never set
        // on the way through and walking out mid-day cost nothing. The user's rule is
        // that abandoning an attempt costs a key however it is abandoned, so the gate
        // moved OUT to the callers, each of which knows whether it is a surrender.
        //
        // What that buys beyond the new routes: the flag no longer has to be read before
        // LivesManager.RefillForNewDay clears it. That ordering was the one mistake here
        // that would have broken silently -- the refill clears the flag, this sees false,
        // and giving up quietly becomes free -- and it is now unrepresentable, because
        // there is no flag left to read at the wrong moment.
        //
        // WHAT IS NOT A SURRENDER, and must never call this: RetryCompletedDay. The
        // player finished that day and is replaying it for a better star score; they are
        // giving nothing up, so it costs nothing (the user restated this rule directly
        // when D-135 was specified). The paid Continues are not surrenders either -- they
        // stay INSIDE the day -- and neither is the debug menu's replay.
        //
        // The result is deliberately ignored. At zero keys the player must still be able
        // to leave -- blocking that is a softlock -- so this floors at zero rather than
        // refusing. Step 5's popup is what stops them arriving here with nothing to spend.
        private void SpendKeyForGivingUp()
        {
            KeyManager.TrySpendKey();

            // Requested unconditionally rather than on TrySpendKey's result, and that is
            // deliberate: at zero keys the spend floors and returns false, but walking out
            // of an attempt is exactly when the player most needs to be told the resource
            // is gone. The rule itself (D-068/D-135 -- which exits cost a key, and that the
            // exit is never blocked) is untouched; this line only reports it.
            //
            // The scene is about to be replaced on most routes here, so this can be lost
            // to the load before LateUpdate flushes it. That is a known gap rather than a
            // silent one -- worth a device check, and not worth pre-empting with a special
            // immediate path that would bypass the coalescer for one moment only.
            haptics?.Request(HapticMoment.KeySpent);
        }

        // Day Complete popup's "Go Back". The day is finished, so what the main
        // screen should offer next is the NEXT Day -- that index is persisted here
        // and the scene is then dropped.
        //
        // Deliberately NOT AdvanceToNextDay: that method also clears the board,
        // discards trays and refills slots, which would (a) be thrown away
        // microseconds later as the scene unloads and (b) cascade TicketAssigned
        // through views that are already tearing down. Only the index needs to
        // survive; everything else is rebuilt by Awake in the fresh day scene.
        //
        // No wallet work: this day already completed, so OnDayCompleted banked its
        // earnings, and the next day's baseline is re-taken by ApplyPersistedBalances
        // when the day scene next loads.
        // What "Next Day" actually asks for (decisions.md D-042). Returns true when the
        // player stays in THIS scene on a fresh day, false when the scene is being dropped
        // for the main screen -- the caller only needs to know whether it still has a popup
        // to hide.
        //
        // The whole decision lives here rather than in the popup. That view already asked one
        // question and branched on it; a second question would have made it the place where
        // flow is decided, and flow is this class's job.
        //
        // Two reasons to leave for the main screen, and they resolve in this order:
        //   1. The next day opens a Day-unlocked prop. The player is sent to see it, because
        //      a celebration nobody is present for is not a celebration.
        //   2. There is no next day. The pre-existing fallback, unchanged.
        // Both hand off to ReturnToMainScreenFromCompletedDay, which already persists the
        // next index and loads the scene -- and which deliberately does NOT go through
        // AdvanceToNextDay, since clearing the board and refilling slots microseconds before
        // the scene unloads is either wasted or an event cascade through tearing-down views.
        // That reasoning applies to case 1 exactly as it did to case 2.
        public bool TryContinueIntoNextDay()
        {
            if (NextDayOpensAProp())
            {
                ReturnToMainScreenFromCompletedDay();
                return false;
            }

            if (AdvanceToNextDay()) return true;

            ReturnToMainScreenFromCompletedDay();
            return false;
        }

        // Asks about the location the meta screen will OPEN on, via the catalog overload, not
        // about some location of its own choosing. That is what makes "marched back to the
        // menu and shown nothing" unrepresentable: the celebration only starts from Start,
        // and at that moment the viewed location is the newest unlocked one -- the same one
        // this resolves.
        private bool NextDayOpensAProp()
        {
            if (metaCatalog == null) return false;

            var nextIndex = State.CurrentDayIndex + 1;

            // No next day means nothing opens on it either; the caller's second reason then
            // handles the exit. Checked here so this method never reports an unlock for a day
            // the player cannot reach.
            if (Session.DayCatalog == null || nextIndex >= Session.DayCatalog.Count) return false;

            return MetaResolver.DayUnlocksBetween(
                metaCatalog, Session.OwnedMetaItemIds, State.CurrentDayIndex, nextIndex).Count > 0;
        }

        public void ReturnToMainScreenFromCompletedDay()
        {
            // Before anything else: the day is over and the player is leaving with it,
            // so whatever the reward flight had not handed over yet is paid now. The
            // SaveProfile at the bottom then writes it, which is why this needs no save
            // of its own.
            CommitPendingReward();

            var nextIndex = State.CurrentDayIndex + 1;
            if (Session.DayCatalog != null && nextIndex < Session.DayCatalog.Count)
            {
                // Through the session, which is the single writer of this field since
                // D-092. The bounds test above is kept rather than left to GoToDay's clamp:
                // clamping would silently pin the player to the last Day, while this branch
                // deliberately leaves the index ALONE when there is no next Day.
                Session.GoToDay(nextIndex);
            }

            SaveProfile();
            SceneFlow.LoadMainScreen();
        }

        // "Main Menu" from inside a day: the player walks out of an attempt they are not
        // going to finish. Reached from the Game Over popup (the day was lost) and, since
        // D-135, from the settings menu (the day was still running) -- and the settlement
        // is the same either way, which is the whole reason this is one method. Settled
        // exactly the way the free Retry settles it --
        // RevertToDayStart, so the attempt's earnings are taken back and its
        // spending is not -- and the day index is deliberately left alone, so the
        // main screen still offers this same Day.
        //
        // Unlike a retry this REACHES DISK, which is the one place D-012 changes an
        // older rule ("a failed day never writes"). It has to: there is no later
        // DayCompleted to correct the figure, and without the write a Continue
        // bought with Gems during an attempt the player then abandons would be
        // silently refunded by walking out -- making paid Continues free for anyone
        // who ends up leaving. The write can only ever record money already spent,
        // never money earned, so it cannot bank a failed day's income.
        public void ReturnToMainScreenAbandoningDay()
        {
            // FIRST LINE, AND THAT IS THE CONTRACT (decisions.md D-149). IsAwaitingContinue
            // is the only thing that can still say whether this attempt was LOST or merely
            // given up on, and LivesManager.RefillForNewDay below clears it before
            // refilling (D-103). Read it any lower and every lost day reports itself as a
            // voluntary quit -- silently, and in every single case.
            //
            // Nothing in gameplay listens; this is telemetry's only way to tell a player
            // who died from a player who walked out of a day that was going fine.
            State.DayAttemptEnded.Publish(
                State.IsAwaitingContinue ? DayAttemptEnd.Lost : DayAttemptEnd.GivenUp);

            // Defensive rather than load-bearing: every route here comes from a day that
            // never completed -- the Game Over popup, or the settings menu, which refuses
            // to open once the day is over -- so there is no debt to void. It is here so
            // that "an abandoned day pays nothing" stays true by construction rather than
            // by the reader tracing which events can overlap.
            DiscardPendingReward();

            wallet.RevertToDayStart();

            // Kept, but no longer load-bearing for the SAVE. D-014 made lives persistent
            // and this refill existed to stop a 0 being written -- a save file the player
            // could not play out of: 0 lives on launch, dead before the first ticket,
            // forever. D-064 took lives out of the profile entirely, so the save cannot
            // carry a life count at all and this line's position relative to SaveProfile
            // no longer decides anything.
            //
            // It stays because it is still true of the SCENE: the day is reset here the
            // same way the free Retry resets it, so whatever the player starts next opens
            // on a full bar. What is NO LONGER true is the old note that this is only
            // reached with Lives at 0 -- D-135's settings route arrives mid-day with
            // hearts still on the row -- and the refill is correct for that case too.
            LivesManager.RefillForNewDay();

            // Walking out of an attempt costs a key, exactly as restarting it does: the
            // player gives up on it either way, and charging one route but not the other
            // would only teach them which button is cheaper.
            //
            // UNCONDITIONAL since D-135. Every caller of a method named "abandoning day"
            // is by definition a surrender, so there is no longer a flag here to read at
            // the wrong moment -- and the completed-day exits do not come through here at
            // all, they have ReturnToMainScreenFromCompletedDay.
            SpendKeyForGivingUp();

            SaveProfile();
            SceneFlow.LoadMainScreen();
        }

        // The alternative to the paid Continue flow (GameOverPopupView), and "free" only
        // in the sense that costs no Gems -- it does cost a KEY whenever the caller says
        // the player is giving up (see the parameter below) --
        // abandons the current day attempt and restarts it at the same
        // difficulty (difficulty scale-down on retry is a still-open GDD
        // question, CLAUDE.md Section 4, deliberately not addressed here) --
        // a retry always replays the Day itself. Order matters for the
        // first three calls: Board.Clear() -> ApplyDayStartBoardPreSeed() ->
        // TrayManager.DiscardAllForNewDay() -> TicketSlotManager.
        // ResetSlotsForNewDay() -- the pre-seed must land on the freshly
        // cleared board before slots start refilling and cascading into
        // OnTicketAssigned's own board playback, or a different order
        // reintroduces stale items. LivesManager/DayLifecycleManager are
        // independent of those and of each other.
        //
        // givingUpOnAttempt states whether this restart is a SURRENDER, and it decides the
        // key charge and the save below. It has NO DEFAULT on purpose: a defaulted false
        // would make every future caller silently free, which is the same failure the flag
        // read it replaced could hide. The three callers answer it honestly -- the Game
        // Over popup and the settings menu with true (the player is throwing an attempt
        // away), the debug menu with false (a cheat button has no business moving the
        // economy, and it must stay able to replay a day that was going fine).
        public void RetryDay(bool givingUpOnAttempt)
        {
            // FIRST LINE, for the reason spelled out on the identical call in
            // ReturnToMainScreenAbandoningDay: LivesManager.RefillForNewDay further down
            // clears IsAwaitingContinue before refilling (D-103), so this is the last
            // moment the flag still answers "was this attempt lost?".
            //
            // It deliberately does NOT read givingUpOnAttempt. That parameter answers a
            // different question -- "does this cost a key?" -- and both the Game Over
            // popup and the settings menu pass true for it, because both are surrenders.
            // Only one of them is a loss (decisions.md D-149).
            State.DayAttemptEnded.Publish(
                State.IsAwaitingContinue ? DayAttemptEnd.Lost : DayAttemptEnd.GivenUp);

            var ticketsBeforeRetry = State.TicketsDeliveredToday;

            RefreshDayTicketSequenceProvider();

            State.Board.Clear();
            ApplyDayStartBoardPreSeed();
            TrayManager.DiscardAllForNewDay();
            TicketSlotManager.ResetSlotsForNewDay();
            LivesManager.RefillForNewDay();
            DayLifecycleManager.ResetForNewDay(CurrentDayTicketSeconds);

            // Publishes DayRetried, which OnDayRetried answers with wallet.RevertToDayStart
            // -- so the money is already rolled back by the time the save below runs.
            State.DayRetried.Publish(ticketsBeforeRetry);

            if (!givingUpOnAttempt) return;

            SpendKeyForGivingUp();

            // THIS PATH USED TO WRITE NOTHING, and that was a documented contract: a failed
            // day was never permanent, so there was nothing to persist. The key breaks that
            // -- if the spend is not written, a player can press Retry and force-quit to get
            // the key back, which makes retries free and the whole economy decorative.
            //
            // It is LAST for a reason that is easy to undo by accident: DayRetried above has
            // already reverted the wallet, so what reaches disk is the ROLLED-BACK money --
            // the same figure the abandon path writes. Move this line any earlier and a
            // failed attempt's earnings get banked, which is exactly the "a day attempt is
            // atomic" rule this file exists to hold. Nothing new is banked here; only the
            // key becomes permanent.
            SaveProfile();
        }

        // Voluntary redo of a day that already succeeded (Day Complete
        // popup's Retry button, for a better star score) -- distinct from
        // RetryDay, which is the free life-loss-failure path. Rolls SoftMoney
        // back to the Wallet's day-start snapshot, so replaying for stars can't
        // stack income on top of what the day already paid out. Mirrors
        // RetryDay's reset order otherwise, including a full Lives refill.
        public void RetryCompletedDay()
        {
            // VOIDED, not paid: this attempt's result is being thrown away, so the debt
            // goes with it. Whatever the reward flight already handed over is a real
            // balance by now, and the revert below is what takes that part back -- the
            // two together are why replaying a finished day cannot farm the reward.
            DiscardPendingReward();

            wallet.RevertToDayStart();

            RefreshDayTicketSequenceProvider();

            State.Board.Clear();
            ApplyDayStartBoardPreSeed();
            TrayManager.DiscardAllForNewDay();
            TicketSlotManager.ResetSlotsForNewDay();
            LivesManager.RefillForNewDay();
            DayLifecycleManager.ResetForNewDay(CurrentDayTicketSeconds);

            // Writes the reverted balance back to disk, unlike the failed-day
            // retry path: this day already COMPLETED, so OnDayCompleted has
            // already banked the higher figure and the file would keep paying it
            // out on the next launch if we left it alone.
            //
            // Position no longer matters, and that is worth stating rather than leaving
            // as a silent invitation to move it. D-014 pushed this line LAST because
            // lives were persisted and the refill above had to land in the file first,
            // or a player quitting mid-redo got their half-empty bar back. D-064 removed
            // lives from the profile, so there is nothing left here whose order against
            // the refill can be got wrong. It stays last simply because nothing gains by
            // moving it.
            SaveProfile();
        }

        // Free-win path: the day's goal was hit (GameManager.OnDayCompleted
        // already paused ticket production). Mirrors RetryDay's reset order,
        // LivesManager included, and never publishes DayRetried.
        //
        // THAT INCLUSION IS A REVERSAL, not an oversight (decisions.md D-064). This
        // method used to skip LivesManager on purpose: "Lives are NOT reset on a
        // successful advance, only a failed retry pays that cost". The user's rule is
        // now that every day opens at a full bar, so the cost of a mistake is paid
        // within the day that made it and never carried forward. Leaving the skip in
        // would have split the behaviour by ROUTE rather than by rule -- a player who
        // returned to the menu and pressed Play would get 3 hearts while one who took
        // Next Day from the popup kept a half-empty row, for no reason they could see.
        public bool AdvanceToNextDay()
        {
            var nextIndex = State.CurrentDayIndex + 1;
            if (Session.DayCatalog == null || nextIndex >= Session.DayCatalog.Count)
            {
                // Last authored Day. The caller decides what to show --
                // DayCompletePopupView sends the player to the main screen rather
                // than leaving them on a finished day with the popup gone.
                return false;
            }

            // Ordering is load-bearing twice over: the reward is paid BEFORE
            // CaptureDayStart, so the day the player is advancing into takes a baseline
            // that already includes what they just earned -- snapshot first and their
            // first retry of the new day would revert the reward away.
            CommitPendingReward();

            // Through the session (D-092): it is the single writer of the day index. The
            // early return above already proved nextIndex is in range, so the clamp inside
            // is a no-op here and the behaviour is unchanged.
            Session.GoToDay(nextIndex);
            wallet.CaptureDayStart();

            // Persists the new index, and now also whatever the line above just paid
            // out. Without this write,
            // quitting during the day the player just advanced INTO would relaunch
            // them onto the day they had already beaten.
            SaveProfile();

            RefreshDayTicketSequenceProvider();

            State.Board.Clear();
            ApplyDayStartBoardPreSeed();
            TrayManager.DiscardAllForNewDay();
            TicketSlotManager.ResetSlotsForNewDay();
            LivesManager.RefillForNewDay();
            DayLifecycleManager.ResetForNewDay(CurrentDayTicketSeconds);

            return true;
        }

        // No fallback: an authored Day is required (PR-7) -- until then this
        // throws instead of silently falling back to the old procedural
        // TicketFactory.Create path (removed; TicketFactory itself stays,
        // just for PickRandomCustomerName's cosmetic reuse inside
        // TicketEntryFactory).
        private Ticket CreateNextTicket()
        {
            if (dayTicketSequenceProvider == null)
            {
                throw new InvalidOperationException("No Day loaded -- ticket generation requires an authored Day (PR-7).");
            }

            // A Day's authored sequence is finite and WILL run out mid-day (see
            // TicketSlotManager.AssignTicket) -- null tells it to leave that slot empty
            // instead of trying (and failing) to draw one more.
            return dayTicketSequenceProvider.HasNext ? dayTicketSequenceProvider.NextTicket() : null;
        }

        // Re-pointed every time CurrentDay could have changed (Awake, RetryDay,
        // AdvanceToNextDay) -- a fresh provider per Day/retry-variant, cursor
        // reset to 0, rather than resetting a single long-lived instance. Also
        // (re)constructs boardDistributor for the same reason -- see its field
        // comment for why a stale instance can't carry over.
        private void RefreshDayTicketSequenceProvider()
        {
            dayTicketSequenceProvider = CurrentDay != null
                ? new DayTicketSequenceProvider(CurrentDay.TicketSequence, CurrentDay.TicketRuntime, ticketFactory)
                : null;
            // The tray reader is a lambda, not TrayManager itself: resolved at call
            // time, so it survives being handed over before TrayManager exists and
            // stays correct across a Day rebuild. Null-safe for the same reason
            // TrayContentsSnapshot is — the main screen builds no trays (D-152).
            boardDistributor = CurrentDay != null
                ? new BoardDistributor(
                    State,
                    CurrentDay.BoardDistribution,
                    trayContentsForSlot: slot => TrayManager?.GetContents(slot))
                : null;
        }

        // Applies the Day's triggerStepIndex == -1 board entries once, right
        // after the board is cleared and before any ticket gets assigned into
        // a slot -- everything after this point plays back per-ticket via
        // OnTicketAssigned instead.
        //
        // AND, when this Day authored one, that board is the ENTIRE opening: the ticket fill
        // that follows distributes nothing, so the player sees exactly the arrangement the
        // designer drew instead of it plus three tickets' worth of spawned ingredients (the
        // user's decision, 2026-08-29). Which is why the answer is read from the Day's own
        // timeline rather than from a flag: "this Day opens with an authored board" is a
        // fact about the board, and a flag beside it could disagree with the thing it
        // describes. DayValidator refuses to save such a Day unless one of the tickets on
        // screen can actually be served from it -- see ValidateDayStartBoard, which is the
        // other half of this decision and the reason suppressing the opening is safe.
        //
        // Set unconditionally, including to 0: a Day with no authored board must clear a
        // count a PREVIOUS Day left behind, or advancing from an authored Day to a plain one
        // would silence the plain Day's opening too.
        private void ApplyDayStartBoardPreSeed()
        {
            if (CurrentDay == null) return;
            DayBoardTimelinePlayer.ApplyForStep(State.Board, CurrentDay.BoardTimeline, -1);

            openingAssignmentsWithoutDistribution =
                DayBoardTimelinePlayer.HasEntriesForStep(CurrentDay.BoardTimeline, -1)
                    ? GameState.TicketSlotCount
                    : 0;

            ArmTutorial();

            // After the tutorial is armed, not before, and the order is only about what the
            // player READS: the introduction says "this is a burger", the lesson says "put it
            // there", and a lesson explained before its subject is introduced is backwards.
            // Nothing depends on the order mechanically -- both freeze the clock through
            // different gates and the popup draws on its own canvas above everything.
            ShowItemIntrosForCurrentDay();
        }

        // The popup sequence's pause holder. A dedicated token rather than `this`, because
        // GameManager is the WRITER of the pause set (see HoldPause) and a holder that is
        // also the writer reads as though the day were holding itself still. Its identity is
        // all that matters -- the set is keyed on the object, not on what it is.
        private readonly object itemIntroPauseHolder = new();

        private Coroutine itemIntroRoutine;
        private NewItemIntroPopup activeItemIntroPopup;

        // What this Day introduces, shown before its clock starts. Driven from here rather
        // than from a view for the reason every system-to-system join in this class is: this
        // is the day's composition root, and it is the one object holding both the Day's
        // content and the pause the popup needs.
        //
        // It runs on EVERY day-start path, retries included, which is the same call
        // ArmTutorial makes one line above -- and deliberately so: a retried Day is the Day
        // being played from the top, and an introduction that appears only on the first
        // attempt would be missing precisely for the player who is struggling with it.
        private void ShowItemIntrosForCurrentDay()
        {
            // A previous day's sequence must not survive into this one. All four day-start
            // paths can fire while a popup is still up -- SRDebugger's day jump, a retry
            // taken from the settings menu -- and a leaked hold would freeze the new day
            // behind a popup belonging to the old one. Stopping the coroutine is not enough:
            // the popup is a parentless instance and would sit on screen with nothing left
            // to dismiss it.
            StopItemIntros();

            var intros = CurrentDay?.ItemIntros;
            if (intros == null || intros.Count == 0) return;

            if (newItemIntroPopupPrefab == null)
            {
                // LOUD, because an unwired field and a Day that introduces nothing look
                // identical on screen -- the same reason MetaGroundsView warns about its own
                // unlock popup rather than failing silently.
                Debug.LogWarning(
                    $"{nameof(GameManager)} on '{name}': Day {CurrentDay.DayIndex} introduces {intros.Count} item(s) " +
                    $"but no {nameof(NewItemIntroPopup)} prefab is wired, so the player is shown nothing. Drag " +
                    "Assets/Prefabs/UI/NewItemIntroPopup.prefab into the New Item Intro Popup Prefab field.", this);
                return;
            }

            itemIntroRoutine = StartCoroutine(RunItemIntros(intros));
        }

        // The hold is taken on the FIRST line, which runs synchronously inside StartCoroutine
        // -- before Update can tick even once. Taking it after a yield would let the day run
        // for a frame behind a popup that is about to appear.
        private IEnumerator RunItemIntros(IReadOnlyList<ResolvedItemIntro> intros)
        {
            HoldPause(itemIntroPauseHolder);

            // try/finally, not a release at the end: the release has to survive the sequence
            // being stopped mid-popup, and a stopped coroutine's iterator is disposed, which
            // runs this. StopItemIntros releases as well, and that duplication is deliberate
            // -- ReleasePause is idempotent (a HashSet remove of something absent), so the
            // belt and the braces cost nothing and neither one is load-bearing alone.
            try
            {
                foreach (var intro in intros)
                {
                    if (intro == null) continue;

                    // Parentless: the prefab carries its own Screen Space - Overlay canvas,
                    // and a Canvas nested inside another inherits its parent's RectTransform
                    // rather than the screen's (D-126, the same trap the meta unlock popup
                    // documents).
                    activeItemIntroPopup = Instantiate(newItemIntroPopupPrefab);
                    activeItemIntroPopup.Bind(
                        intro.DisplayName, intro.Message, intro.Sprite, intro.ModificationIsAddition);

                    // Frame by frame rather than on a callback, because this is a coroutine
                    // holding the day still: the wait IS the feature. The null check ends it
                    // if the popup is destroyed under us (the scene unloading mid-sequence),
                    // so the day cannot be frozen by something outside this method's control.
                    while (activeItemIntroPopup != null && !activeItemIntroPopup.IsDismissed)
                    {
                        yield return null;
                    }

                    if (activeItemIntroPopup != null) Destroy(activeItemIntroPopup.gameObject);
                    activeItemIntroPopup = null;
                }
            }
            finally
            {
                ReleasePause(itemIntroPauseHolder);
                itemIntroRoutine = null;
            }
        }

        // Public-shaped cleanup kept private: every caller is inside this class, and the two
        // that exist -- a new day starting, and this object being destroyed -- are the only
        // moments a sequence should end without the player pressing anything.
        private void StopItemIntros()
        {
            if (itemIntroRoutine != null)
            {
                StopCoroutine(itemIntroRoutine);
                itemIntroRoutine = null;
            }

            if (activeItemIntroPopup != null)
            {
                Destroy(activeItemIntroPopup.gameObject);
                activeItemIntroPopup = null;
            }

            ReleasePause(itemIntroPauseHolder);
        }

        // This Day's forced first move, if it authored one. Armed from inside the pre-seed
        // rather than from Awake because all FOUR day-start paths run through there (first
        // load, both retries, and the advance to the next Day) -- one call site instead of
        // four, and it cannot drift out of step with the board it is gating.
        //
        // Rebuilding rather than keeping one instance is what makes the next Day correct:
        // Day 1 authors no tutorial, so this sets the director back to null and every gate
        // opens again. A director left over from Day 0 would silently lock Day 1 to one
        // cell.
        private void ArmTutorial()
        {
            if (Tutorial != null) Tutorial.StepChanged -= OnTutorialStepChanged;
            Tutorial = null;

            var steps = BuildTutorialSteps();
            if (steps.Count == 0) return;

            Tutorial = new TutorialDirector(steps);
            Tutorial.StepChanged += OnTutorialStepChanged;

            // Validated the same way every later step is, through the one method, so the
            // first step gets no special treatment and no second copy of the rule.
            if (!EnsureCurrentTutorialStepIsPossible()) return;

            // Before the announcement, so the bar has the charges in hand by the time it is
            // asked to draw a panel for a powerup the player is about to be told to press.
            GrantChargesForCurrentTutorialStep();

            TutorialStepChanged?.Invoke();
        }

        // THE TWO AUTHORING SIDES MEET HERE AND NOWHERE ELSE (D-115). The Day file owns the
        // forced MOVES -- they describe this Day's board, which the Day file is already the
        // single authority for -- and PowerupConfig owns which Day introduces which powerup,
        // because that is a property of the powerup. Neither can name the other's business,
        // so the two lists cannot disagree; concatenating them is the whole integration.
        //
        // Moves first, powerups after: the moves teach the board, and a powerup that acts on
        // the board would sweep the very item a later move points at.
        private List<TutorialStep> BuildTutorialSteps()
        {
            var steps = new List<TutorialStep>();

            // Translated into the Tutorial system's own step type at this boundary rather
            // than handing it DaySystem's -- that is what keeps the Tutorial assembly's
            // reference list empty, which is what keeps its rules testable with no Day
            // catalog. The same trade PowerupManager already makes with its Func effects.
            var authored = CurrentDay?.Tutorial;
            if (authored != null)
            {
                foreach (var step in authored.Steps)
                {
                    steps.Add(TutorialStep.ForcedMove(
                        step.SourceX, step.SourceY, step.TargetTraySlotIndex, step.Message, step.HighlightModification));
                }
            }

            AppendPowerupTutorialSteps(steps);
            return steps;
        }

        // Two steps per powerup introduced today: the panel, then the forced press. Two
        // rather than one because they are two moments, and for a deferred trigger they are
        // minutes apart -- one step carrying both states would be the state machine this
        // system has refused to become three times now.
        //
        // PowerupTypes.All order, which is the order the bar and the shop already render, so
        // a Day that introduced two powerups would teach them in the order they are shown.
        private void AppendPowerupTutorialSteps(List<TutorialStep> steps)
        {
            if (powerupConfig == null || CurrentDay == null) return;

            foreach (var type in PowerupTypes.All)
            {
                var schedule = powerupConfig.For(type);
                if (!schedule.IntroducedOnDay(CurrentDay.DayIndex)) continue;

                var powerup = ToTutorialPowerup(type);

                // The panel carries no message of its own: it prints the powerup's NAME and
                // DESCRIPTION, which live on the same asset beside this schedule and are read
                // by the view. The instruction sentence belongs to the press, not the panel.
                steps.Add(TutorialStep.PowerupIntro(powerup, string.Empty));

                // The threshold is the ECONOMY's critical ratio, not a number of the
                // tutorial's own: that is the single authority for where a ticket's bar turns
                // red and its tip tier drops (CLAUDE.md, Tip Tiers), so the lesson fires at
                // exactly the moment the player can SEE a ticket go critical. A second copy on
                // PowerupConfig was free to drift from it and was removed. A missing
                // EconomyConfig leaves 0, which simply means the step waits for a ticket at
                // zero rather than throwing -- and it is already an error this day reports for
                // its own reasons, since nothing could be paid out without it.
                //
                // THE SENTENCE IS AUTHORED PER POWERUP AND USUALLY EMPTY. This is the argument
                // D-121 said was "one argument away", and D-146 is the step that wanted it:
                // the two AtDayStart lessons still author nothing and are taught by the arrow
                // alone, while the DEFERRED one has to say what changed on screen, because it
                // fires minutes after its panel was read and the player is looking at a board,
                // not at a tutorial.
                //
                // It carried the powerup's Description until 2026-08-28, which meant the panel
                // said it and then the spotlight said the very same thing again seconds later.
                // That was a duplicate this project had already deleted once: D-117 removed the
                // authored `tutorialUseInstruction` because it was a reworded copy of
                // Description, and pointing the spotlight at Description simply re-created the
                // repetition in the FLOW instead of the DATA. `tutorialUseMessage` avoids both
                // by describing the MOMENT rather than the powerup -- the panel says what Time
                // Reset does, this says that an order is running out of time.
                steps.Add(TutorialStep.PowerupUse(
                    powerup,
                    ToTutorialTrigger(schedule.TutorialUseTrigger),
                    economyConfig != null ? economyConfig.CriticalRatio : 0f,
                    schedule.TutorialUseMessage));
            }
        }

        // The tutorial's charge floor, applied whenever a powerup step ARMS -- both when the
        // panel appears and again when a deferred press finally comes due, which is the only
        // way a player can have spent the panel's charges before being asked to use one.
        //
        // A floor rather than a grant (PowerupManager.EnsureAtLeast): this runs on every
        // day-start path including both retries, so an adding grant would make replaying an
        // introduction Day a charge farm.
        // Not a balance number and deliberately not on the asset: a forced press against an
        // empty stock opens the shop (D-105) and the step could never be completed, so one
        // charge is the mechanic's own floor rather than a dial anyone would tune.
        private const int ChargesNeededForAForcedPress = 1;

        private void GrantChargesForCurrentTutorialStep()
        {
            if (PowerupManager == null || powerupConfig == null) return;
            if (Tutorial == null || !Tutorial.IsArmed) return;

            var step = Tutorial.Current;
            if (step.Kind != TutorialStepKind.PowerupIntro && step.Kind != TutorialStepKind.PowerupUse) return;

            var type = ToPowerupType(step.Powerup);

            // Two different questions, and only one of them is a balance dial. The PANEL hands
            // over the authored number -- how generous the lesson is. The PRESS only has to be
            // POSSIBLE, and "at least one charge or the step cannot be completed" is a rule of
            // the mechanic rather than something to tune, so it is a constant here instead of a
            // second field on the asset. It fires only for a deferred trigger, where the
            // panel's charges can have been spent in the minutes before the moment arrived.
            var minimum = step.Kind == TutorialStepKind.PowerupIntro
                ? powerupConfig.For(type).TutorialCharges
                : ChargesNeededForAForcedPress;

            PowerupManager.EnsureAtLeast(type, minimum);
        }

        // The three translations across the Tutorial assembly's boundary. Written out rather
        // than cast, so a value added on either side without teaching the other about it
        // stops the build instead of quietly becoming whatever number happens to line up --
        // the same stance HapticConfig takes on its mirror of the vendor's preset enum.
        //
        // The two powerup ones are PUBLIC because the powerup bar needs the same translation
        // to turn "which powerup is the tutorial asking for" into a button, and this class is
        // the designated place that holds both sides. A second copy in the view would be a
        // second mapping free to disagree with this one.
        public static TutorialPowerup ToTutorialPowerup(PowerupType type) => type switch
        {
            PowerupType.AutoCollect => TutorialPowerup.AutoCollect,
            PowerupType.TimeReset => TutorialPowerup.TimeReset,
            PowerupType.NoiseClear => TutorialPowerup.NoiseClear,
            _ => throw new ArgumentOutOfRangeException(nameof(type), type, "No TutorialPowerup mirrors this PowerupType."),
        };

        public static PowerupType ToPowerupType(TutorialPowerup powerup) => powerup switch
        {
            TutorialPowerup.AutoCollect => PowerupType.AutoCollect,
            TutorialPowerup.TimeReset => PowerupType.TimeReset,
            TutorialPowerup.NoiseClear => PowerupType.NoiseClear,
            _ => throw new ArgumentOutOfRangeException(nameof(powerup), powerup, "No PowerupType mirrors this TutorialPowerup."),
        };

        private static TutorialTrigger ToTutorialTrigger(PowerupTutorialTrigger trigger) => trigger switch
        {
            PowerupTutorialTrigger.AtDayStart => TutorialTrigger.Immediate,
            PowerupTutorialTrigger.TicketPatienceBelow => TutorialTrigger.TicketPatienceBelow,
            _ => throw new ArgumentOutOfRangeException(nameof(trigger), trigger, "No TutorialTrigger mirrors this PowerupTutorialTrigger."),
        };

        // The softlock guard, and the reason it exists at runtime as well as in DayValidator:
        // a hand-edited Day file never passes through the editor's Save gate at all, and --
        // more importantly for a multi-step tutorial -- the BOARD MOVES between steps. An
        // authoring-time check can only see the Day Start layout, so a later step's item may
        // genuinely be gone by the time its turn arrives. With no item on the source cell
        // there is nothing to pick up and no tray that will accept anything: the day would
        // read as a freeze. Aborting turns that into an ordinary day plus a sentence naming
        // the cell.
        private bool EnsureCurrentTutorialStepIsPossible()
        {
            var step = Tutorial?.Current;
            if (step == null) return false;

            // A step that is not a forced move names no cell, so there is nothing on the
            // board that could make it impossible. Without this it would be checked against
            // its unused (0,0) and abort the whole tutorial the moment that cell is empty.
            // That now covers both powerup kinds as well as the panel it was written for.
            if (step.Kind != TutorialStepKind.ForcedMove) return true;

            if (State.Board.ItemAt(step.SourceX, step.SourceY) != null) return true;

            Debug.LogError(
                $"Day {CurrentDay.DayIndex}'s tutorial expects an item at cell ({step.SourceX}, {step.SourceY}) " +
                "for its next step, but that cell is empty, so the forced move would be impossible. Ending the " +
                "tutorial and running the rest of the day normally. Check the Day's boardTimeline and the order of " +
                "the tutorial steps.", this);
            Tutorial.Abort();
            return false;
        }

        // Each step re-validates as it becomes current, and a step that survives that is
        // announced so the new target tray can raise its spotlight. Abort() also lands here
        // (with Current null), which is exactly right: the trays tear down what they built.
        private void OnTutorialStepChanged()
        {
            if (Tutorial != null && Tutorial.IsActive && !EnsureCurrentTutorialStepIsPossible()) return;

            // Ahead of the announcement for the reason ArmTutorial does it in that order: the
            // bar is about to draw a panel or a spotlight for a powerup, and it should find
            // the charges already there rather than a zero that turns the forced press into a
            // trip to the shop. Harmless on every other step -- it returns on the kind check.
            GrantChargesForCurrentTutorialStep();

            TutorialStepChanged?.Invoke();
        }

        // Lets the same EventSystem that already drives the UGUI Canvas
        // (GraphicRaycaster) also raycast the world-space board item and tray
        // colliders, so drag handlers and drop targets interoperate via the
        // standard pointerDrag mechanism. Added in code rather than hand-edited
        // into the scene file — no manual Editor step needed on Main Camera.
        private void EnsurePhysics2DRaycaster()
        {
            var cam = Camera.main;
            if (cam != null && cam.GetComponent<Physics2DRaycaster>() == null)
            {
                cam.gameObject.AddComponent<Physics2DRaycaster>();
            }
        }
    }
}
