using System;
using System.Collections.Generic;
using ExpoTheExplorer.Bootstrap;
using ExpoTheExplorer.Data;
using ExpoTheExplorer.Systems.PowerupSystem;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace ExpoTheExplorer.UI
{
    // The day scene's powerup bar (GDD Section 5.2, .claude/powerup-plan.md Adım 2):
    // the three buttons the prototype drew as grey circles, each showing how many
    // charges of that powerup the player still holds.
    //
    // IT SPENDS, AND WHEN IT CANNOT, IT SENDS THE PLAYER SHOPPING (D-105, the user's
    // decision on 2026-08-27). The rule here used to be "it never sells": buying was the
    // main screen's job because opening a store mid-day suspends the very time pressure a
    // powerup exists to relieve. The reversal answers that objection rather than dropping
    // it -- the day is HELD STILL while the shop is up, so the pressure is paused, not
    // suspended. There is still no price and no Gem icon on this bar; what an empty powerup
    // gained is a destination.
    //
    // The empty state is drawn by the "Add" badge the author placed inside each button,
    // switched on exactly when that powerup is at zero. A badge rather than a second button
    // because the tap target the player aims at is the powerup itself: a full one is spent,
    // an empty one opens the shop. One control, two meanings, and the badge is what says
    // which one is live.
    //
    // IT BUILDS NOTHING. The panel, the three buttons and their `Count` labels are
    // authored by hand in SampleScene, and every reference below is dragged in. A setup
    // script that re-created this hierarchy was considered and rejected: the author
    // already laid it out, so a generator would be a second authority over the same
    // objects and would overwrite their arrangement the first time it ran.
    public class PowerupBarView : MonoBehaviour
    {
        // Serialized and dragged, never searched for -- a runtime lookup for a scene
        // reference is ruled out project-wide. GameManager rather than HudWalletSource
        // because that seam exists for widgets shown on BOTH screens (the wallet, the
        // keys); this bar is a day-scene thing with exactly one source, the same
        // reasoning D-064 applied to the heart row.
        [SerializeField] private GameManager gameManager;

        // Three named blocks rather than an array with a type dropdown, and the scene is
        // the reason: its sibling order is AutoCollect, LeakCleaner, TimerReset, which is
        // NOT the enum's order. An array would quietly pair the middle button with the
        // middle enum value and put the wrong count under the wrong icon -- a mistake
        // that looks like a balancing oddity rather than a wiring error. Named fields
        // cannot be mis-ordered.
        [Tooltip("GDD 5.2 #1 — Auto-Collect. Drag AutoCollectButton and its Count label.")]
        [SerializeField] private PowerupButton autoCollect;

        [Tooltip("GDD 5.2 #2 — Time Reset. Drag TimerResetButton and its Count label.")]
        [SerializeField] private PowerupButton timeReset;

        [Tooltip("GDD 5.2 #3 — Noise Clear. This is the scene's LeakCleanerButton: same thing, the noise pool is what leaks in from upcoming tickets.")]
        [SerializeField] private PowerupButton noiseClear;

        // OPTIONAL, and the whole feature degrades to the old behaviour without it: no shop
        // means an empty powerup goes back to being simply dim and unpressable, which is
        // what this bar did before D-105. That is the right failure for a forgotten drag --
        // a day is still fully playable, one convenience is just unreachable.
        //
        // The day scene's own shop, dragged in. Not the main screen's: a scene reference
        // cannot cross a scene load, and nothing here searches for one (D-028).
        [Tooltip("Optional. The day scene's PowerupShopView, opened when a powerup at 0 charges is pressed. Unwired, an empty powerup is simply dim.")]
        [SerializeField] private PowerupShopView shop;

        // The two tutorial popups, as PREFABS since D-116 -- they were assembled from
        // constants in C# until the user asked to be able to restyle them, which a panel
        // built in code cannot be. Both are OPTIONAL and both fail the same way every other
        // reference on this tutorial does: an unwired one costs its lesson and leaves the day
        // fully playable. Build them with ExpoTheExplorer > Build Tutorial Powerup Popups,
        // then drag the two assets in here.
        [Tooltip("Optional. Assets/Prefabs/UI/TutorialPowerupIntro.prefab — the panel that introduces one powerup. Unwired, that lesson is skipped.")]
        [SerializeField] private TutorialPowerupIntroView introPrefab;

        [Tooltip("Optional. Assets/Prefabs/UI/TutorialPowerupSpotlight.prefab — the frame and sentence shown while a powerup must be pressed. Unwired, the press is still required but unmarked.")]
        [SerializeField] private TutorialPowerupSpotlightView spotlightPrefab;

        // Cached rather than re-read from gameManager on every refresh, for the reason
        // LivesView caches GameState: EventBus removes by delegate equality on a specific
        // instance, so the object this unsubscribes from in OnDestroy must be the object
        // it subscribed to. GameSession is built once in GameManager.Awake and never
        // swapped, so caching is safe and re-walking the property would not be.
        private PowerupManager manager;

        // The tutorial's powerup panel while it is up, or null. Held so a step boundary that
        // is not a dismissal (a retry, an abort) can take it down.
        private TutorialPowerupIntroView introView;

        // The frame drawn around the one powerup the tutorial is asking the player to press,
        // or null. Held for the same reason the panel is, and torn down through its own
        // Dismiss because it puts objects on the BUTTON's hierarchy, not only on itself.
        private TutorialPowerupSpotlightView spotlight;

        // Paired with the fields above once, so every loop below reads one list instead
        // of repeating the three-way spelling. Built in Start rather than being a static
        // table because the entries carry this instance's scene references.
        private readonly List<(PowerupType Type, PowerupButton Ui)> slots = new();

        // Start(), not Awake() -- Unity does not order Awake across GameObjects and
        // GameManager.Awake is what builds the session. Every HUD view in this project
        // binds from Start for exactly this reason; moving it earlier reads a null.
        private void Start()
        {
            if (gameManager == null)
            {
                Debug.LogError(
                    $"{nameof(PowerupBarView)} on '{name}' has no {nameof(GameManager)} wired, so the powerup " +
                    "counts will never update and the buttons will do nothing. Drag the scene's GameManager " +
                    "into the Game Manager field.",
                    this);
                return;
            }

            CollectSlots();

            manager = gameManager.PowerupManager;
            if (manager == null)
            {
                // NOT an error, and not hidden either. A null manager means no PowerupConfig
                // was wired, which GameManager has already reported as an error of its own --
                // repeating it here would be noise. The panel stays visible because the author
                // placed it: silently removing someone's UI is a worse surprise than an inert
                // row, and Refresh below leaves it reading zero and untouchable, which is an
                // honest picture of "you have no powerups".
                Debug.Log(
                    $"{nameof(PowerupBarView)} on '{name}': this session has no powerup stock (no PowerupConfig " +
                    "wired on GameManager), so the bar stays inert.",
                    this);
                RefreshAll();
                return;
            }

            foreach (var (type, ui) in slots)
            {
                // Captured per iteration on purpose: `type` is the loop variable and a
                // lambda closing over a shared one would give all three buttons the last
                // powerup. C# 5+ makes foreach variables per-iteration so this is already
                // safe, but the local says so out loud to the next reader.
                var pressed = type;
                ui.Button.onClick.AddListener(() => OnPressed(pressed));
            }

            manager.ChargesChanged.Subscribe(OnChargesChanged);

            // A DAY CHANGING is what changes a lock, and it is deliberately NOT read off the
            // tutorial's step event: ArmTutorial fires nothing at all on a Day that authors no
            // lesson, which is most of them, so a bar listening only to that would keep
            // yesterday's locks for the rest of the game. AdvanceToNextDay reuses this scene
            // rather than reloading it, so Start does not run again either -- this event is
            // the only honest signal.
            if (gameManager.Session != null) gameManager.Session.State.CurrentDayIndexChanged.Subscribe(OnDayIndexChanged);

            // Subscribed here rather than at the moment the shop is opened, so a shop closed
            // by any route -- its close button, a second press, something else taking it
            // down -- lets the day run again. Unsubscribing in OnDestroy is what keeps a
            // re-enabled view from stacking a second handler.
            if (shop != null) shop.Closed += OnShopClosed;

            RefreshAll();

            // Subscribe, then sync -- the same shape WorldTrayView uses, and for the same
            // reason: a step boundary is a mid-day event, but the tutorial may already be on
            // this step by the time Start runs.
            gameManager.TutorialStepChanged += OnTutorialStepChanged;
            OnTutorialStepChanged();
        }

        // This view builds BOTH powerup steps for the reason the target tray builds a move
        // step's spotlight: it is the object that already holds what they need. The icon is
        // the sprite on a live button and the target of a forced press is a live button, and
        // nothing else in the project knows where those are.
        //
        // Tear down first, then build, and both halves are unconditional: a step boundary can
        // move from a panel to a press, from a press to nothing, or (on a retry or an abort)
        // out of the middle of either. Asking "did the thing I am holding still apply" for
        // each one separately is what makes every one of those transitions the same code.
        private void OnTutorialStepChanged()
        {
            var tutorial = gameManager.Tutorial;
            var introduced = tutorial?.IntroducedPowerup;
            var required = tutorial?.RequiredPowerup;

            if (introView != null && introduced == null)
            {
                Destroy(introView.gameObject);
                introView = null;
            }

            if (spotlight != null && required == null)
            {
                spotlight.Dismiss();
                spotlight = null;
            }

            if (introduced != null && introView == null) BuildIntro(GameManager.ToPowerupType(introduced.Value));
            if (required != null && spotlight == null) BuildSpotlight(GameManager.ToPowerupType(required.Value), tutorial.CurrentMessage);
        }

        // Every failure here SKIPS the step rather than leaving it blocking the day. That is
        // the standing rule for this tutorial: an unwired reference costs a lesson, never a
        // playable day.
        private void BuildIntro(PowerupType type)
        {
            var config = gameManager.PowerupConfig;

            if (config == null || introPrefab == null)
            {
                Debug.LogWarning(
                    $"{nameof(PowerupBarView)} on '{name}': the powerup tutorial panel cannot be shown (" +
                    $"{(config == null ? $"no {nameof(PowerupConfig)} on {nameof(GameManager)}" : "no Intro Prefab wired")}" +
                    "), so that step is skipped rather than left blocking the day.", this);
                gameManager.Tutorial.NotifyReadingFinished();
                return;
            }

            // Instantiated with NO parent on purpose: the prefab's root carries its own
            // Screen Space - Overlay canvas (D-086), and parenting it under this bar's
            // Screen Space - Camera canvas is exactly how it would end up drawn beneath the
            // world sprites.
            introView = TutorialPowerupIntroView.Create(
                Instantiate(introPrefab),
                config.For(type),
                IconFor(type),
                OnIntroDismissed);

            if (introView == null)
            {
                Debug.LogWarning(
                    $"{nameof(PowerupBarView)} on '{name}': the powerup tutorial panel could not be shown for {type}, " +
                    "so that step is skipped rather than left blocking the day.", this);
                gameManager.Tutorial.NotifyReadingFinished();
            }
        }

        // A forced press against a button that is not wired is the one failure that would be
        // a genuine softlock -- the gates refuse everything else for the whole of an armed
        // step, so there would be nothing left to do. Completing the step is the escape, and
        // it is the same shape the panel's failures take.
        private void BuildSpotlight(PowerupType type, string message)
        {
            var button = ButtonFor(type);
            if (button == null)
            {
                Debug.LogWarning(
                    $"{nameof(PowerupBarView)} on '{name}': the tutorial asks the player to press {type}, but that " +
                    "block has no Button wired, so nothing could be pressed. Skipping that step rather than locking " +
                    "the day. Drag its button in.", this);
                gameManager.Tutorial.NotifyPowerupUsed(GameManager.ToTutorialPowerup(type));
                return;
            }

            // Unwired, the lesson still RUNS -- the press is still required and still the only
            // thing permitted. What is lost is the mark on the button, which is a worse
            // lesson but not a broken one, so this does not skip the step the way the panel's
            // failures do: there the missing piece was the only way to continue.
            if (spotlightPrefab == null)
            {
                Debug.LogWarning(
                    $"{nameof(PowerupBarView)} on '{name}': no Spotlight Prefab wired, so the player is asked to press " +
                    $"{type} with nothing marking it. Drag the prefab in.", this);
                return;
            }

            spotlight = TutorialPowerupSpotlightView.Create(
                Instantiate(spotlightPrefab),
                (RectTransform)button.transform,
                message);
        }

        private void OnIntroDismissed()
        {
            introView = null;
            gameManager.Tutorial?.NotifyReadingFinished();
        }

        // Read-only, and the only thing this view exposes about its buttons. The three
        // button fields stay private: a caller has no business reaching the Button itself,
        // which is what spends a charge.
        private Sprite IconFor(PowerupType type)
        {
            foreach (var (slotType, ui) in slots)
            {
                if (slotType != type) continue;
                return ui?.Button != null ? ui.Button.image != null ? ui.Button.image.sprite : null : null;
            }

            return null;
        }

        // The live button for one powerup, and it stays PRIVATE like the icon above: this is
        // for the tutorial's own spotlight, which needs somewhere to draw a frame. A caller
        // outside this class has no business reaching the Button itself, which is what spends
        // a charge.
        private Button ButtonFor(PowerupType type)
        {
            foreach (var (slotType, ui) in slots)
            {
                if (slotType == type) return ui?.Button;
            }

            return null;
        }

        // FindFont lived here until D-116 and is gone with the runtime-built popups. It
        // borrowed a TMP font off a count label so a panel assembled in code could match the
        // game's type with nothing wired by hand. An authored prefab carries its own fonts,
        // which is the whole point of it being authored.

        private void OnDestroy()
        {
            // Guarded because Start returns early on a missing GameManager, and OnDestroy
            // runs regardless of how far Start got.
            if (manager != null) manager.ChargesChanged.Unsubscribe(OnChargesChanged);
            if (gameManager != null && gameManager.Session != null)
            {
                gameManager.Session.State.CurrentDayIndexChanged.Unsubscribe(OnDayIndexChanged);
            }
            if (gameManager != null) gameManager.TutorialStepChanged -= OnTutorialStepChanged;
            if (shop != null) shop.Closed -= OnShopClosed;

            // Through Dismiss rather than Destroy: this one put a frame on the BUTTON's
            // hierarchy, which is not necessarily going away with this view -- an object that
            // is merely disabled and re-enabled would come back to a frame from last time.
            if (spotlight != null) spotlight.Dismiss();
            spotlight = null;

            // A pause this view is still holding dies with it, the same guard the settings
            // menu carries. Usually redundant -- the scene load takes the GameState too --
            // but this object can be destroyed on its own, and a leaked holder would freeze
            // the day with nothing on screen able to let go of it.
            if (gameManager != null) gameManager.ReleasePause(this);

            // The listeners go too. The scene is usually being torn down anyway, but this
            // view is also legal to disable and re-enable, and a second Start would
            // otherwise stack a second listener and spend two charges per press.
            foreach (var (_, ui) in slots)
            {
                if (ui?.Button != null) ui.Button.onClick.RemoveAllListeners();
            }
            slots.Clear();
        }

        // The one place a press means two different things, decided from the LIVE charge
        // count rather than from what the last Render drew: the count can move between the
        // two (a day completing, a debug grant, a purchase in the shop that is already open)
        // and a stale read would spend a charge the player meant to buy, or the reverse.
        private void OnPressed(PowerupType type)
        {
            if (manager == null) return;

            // FIRST, before the day-liveness gate and before the shop: a powerup nobody has
            // been taught cannot be spent and cannot be shopped for. Render already dims the
            // button, so this is the half that holds if anything re-enables it -- the same
            // belt-and-braces the empty state has.
            if (IsLocked(type)) return;

            // The tutorial's third gate, asked at press time rather than by disabling the
            // other two buttons -- the same choice D-069 made for keys and D-105 for the
            // shop. While a forced press is being asked for, this refuses every powerup but
            // the one named; the rest of the time it refuses nothing.
            if (!gameManager.CanUsePowerup(type)) return;

            var isForcedByTutorial = gameManager.Tutorial?.RequiredPowerup == GameManager.ToTutorialPowerup(type);

            if (manager.ChargesOf(type) > 0)
            {
                // Unchanged: TryUse decides on its own whether there was work to do, and a
                // press with nothing to collect costs no charge (GDD 5.2).
                manager.TryUse(type);
            }
            else if (!isForcedByTutorial)
            {
                OpenShop();
                return;
            }

            // The step completes on the PRESS, not on the effect. An effect with nothing to
            // do refuses and costs no charge, and a step that waited for a successful one
            // would leave the player pressing a button that will not advance with no way to
            // find out why. The empty-charge branch above lands here for the same reason: the
            // tutorial tops the stock up before asking, so zero here means someone authored a
            // floor of zero -- a lesson worth losing, not a day worth locking.
            if (isForcedByTutorial) gameManager.Tutorial.NotifyPowerupUsed(GameManager.ToTutorialPowerup(type));
        }

        // Freezes the day, THEN opens -- and only if the shop actually came up. The other
        // order is the bug this shape prevents: a pause taken against a shop that could not
        // open (no panel wired) has nothing on screen to lift it, and the day stays stopped
        // for the rest of the attempt with no way back.
        private void OpenShop()
        {
            if (shop == null) return;

            // The same gate a USE passes (GameManager.CanUsePowerups). Buying under the Game
            // Over popup, over the day-complete receipt or mid-tutorial is the same mistake
            // as spending there, and all three already hold the clock for their own reasons
            // -- a second holder over the top of them would be one more thing to let go of.
            if (!gameManager.CanUsePowerups()) return;

            if (!shop.Open()) return;

            gameManager.HoldPause(this);
        }

        // Releasing a hold that was never taken is a no-op, so this needs no memory of
        // whether THIS view is the reason the shop was up.
        private void OnShopClosed() => gameManager.ReleasePause(this);

        // Only the row that changed is redrawn. The event carries its own type precisely
        // so a bar does not have to re-render three labels because one of them moved.
        private void OnChargesChanged((PowerupType Type, int Charges) change)
        {
            foreach (var (type, ui) in slots)
            {
                if (type == change.Type) Render(ui, change.Charges, ShopIsReachable, IsLocked(type), LockLabelFor(type));
            }
        }

        // BOTH halves, because a shop with no manager behind it is a shop this bar cannot
        // send anyone to: Start returns before the click listeners are attached when there
        // is no PowerupManager, so a button left live there would look pressable and do
        // nothing at all -- the one outcome worse than a dim button.
        private bool ShopIsReachable => shop != null && manager != null;

        // LOCKED UNTIL TAUGHT. The rule itself lives on PowerupSettings; this only supplies
        // the day, which is the half a config cannot know. The AUTHORED index rather than the
        // catalog position, because that is what the lesson's own schedule is compared
        // against -- two notions of "which day" is how a lock opens one day early.
        //
        // Fails OPEN on every uncertainty (no config, no session, no Day resolved): an
        // unwired reference costing a powerup its whole existence is far worse than one
        // costing a lock, and this sits on the HUD of every day in the game.
        private bool IsLocked(PowerupType type)
        {
            var config = gameManager != null ? gameManager.PowerupConfig : null;

            // Through Session rather than a GameManager property, because Session is the
            // seam both scene roots share -- the same route MetaShopView takes to read the
            // day on the main screen.
            var day = gameManager != null && gameManager.Session != null ? gameManager.Session.CurrentDay : null;
            if (config == null || day == null) return false;

            return !config.For(type).IsUnlockedOnDay(day.DayIndex);
        }

        private string LockLabelFor(PowerupType type)
        {
            var config = gameManager != null ? gameManager.PowerupConfig : null;
            return config != null ? config.LockLabelFor(config.For(type)) : string.Empty;
        }

        // The whole bar, because a new Day can unlock any of the three and the event says
        // only that the day moved. Three buttons once per Day is nothing.
        private void OnDayIndexChanged(int _) => RefreshAll();

        private void RefreshAll()
        {
            foreach (var (type, ui) in slots)
            {
                Render(ui, manager?.ChargesOf(type) ?? 0, ShopIsReachable, IsLocked(type), LockLabelFor(type));
            }
        }

        // Dimming is keyed on the CHARGE COUNT, deliberately not on PowerupManager.CanUse.
        // CanUse also asks whether an effect is registered, and effects arrive in Adım 4-6
        // of the plan -- gating on it today would draw all three buttons dead from the
        // moment this bar ships, which reads as broken rather than as empty. In the day
        // scene the two answers converge anyway once the effects are wired, because that
        // is the scene that registers them.
        //
        // `interactable = false` used to be what made "pressing an empty powerup does
        // nothing" true without a branch in the click handler. Since D-105 an empty powerup
        // has somewhere to go, so it stays LIVE whenever a shop is wired -- and the branch
        // that used to be absent now lives in OnPressed, which is the only honest place for
        // it once one control means two things.
        //
        // With no shop wired the old rule returns exactly: dim, unpressable, no popup. That
        // is still the right shape for a missing charge, which costs a convenience, and not
        // the NoKeysPopupView shape, which covers being locked out of playing at all.
        //
        // The badge is switched purely on the count, deliberately not on "the shop is
        // reachable": it says THIS POWERUP IS EMPTY, which is true either way, and a badge
        // that also encoded whether a reference was dragged would make a wiring mistake look
        // like a full stock.
        private static void Render(PowerupButton ui, int charges, bool shopIsReachable, bool locked, string lockLabel)
        {
            if (ui == null) return;

            if (ui.LockOverlay != null) ui.LockOverlay.SetActive(locked);
            if (ui.LockLabel != null) ui.LockLabel.text = lockLabel;

            // Off while locked, so the blocker is the only thing on the button rather than a
            // badge sitting on top of a powerup the player cannot reach.
            if (ui.Background != null) ui.Background.SetActive(!locked);

            // A LOCKED powerup shows neither its count nor its "Add" badge, and that is the
            // point rather than tidiness: a number on something unusable is noise, and an
            // invitation to buy it is worse than noise. The charges are still there and still
            // real -- they simply come back into view on the day it unlocks.
            if (ui.CountLabel != null)
            {
                ui.CountLabel.gameObject.SetActive(!locked);

                // A plain number, not "x3": the label's formatting belongs to whoever styled
                // the button, and a prefix authored here would fight it.
                ui.CountLabel.text = charges.ToString();
            }

            if (ui.EmptyBadge != null) ui.EmptyBadge.SetActive(!locked && charges <= 0);

            // Dim and unpressable while locked, whatever the stock or the shop says. The press
            // is refused in OnPressed too -- this is the half the player can see, that one is
            // the half that holds if something re-enables the button.
            if (ui.Button != null) ui.Button.interactable = !locked && (charges > 0 || shopIsReachable);
        }

        // Reports every unwired half separately rather than bailing on the first one, so
        // one round of dragging fixes everything the author missed instead of one field
        // per play-test. A block that is wired but incomplete (button without label, or
        // the reverse) is still USED for the half that exists -- Render null-checks both --
        // because a missing label should not also cost the button.
        private void CollectSlots()
        {
            Add(PowerupType.AutoCollect, autoCollect, nameof(autoCollect));
            Add(PowerupType.TimeReset, timeReset, nameof(timeReset));
            Add(PowerupType.NoiseClear, noiseClear, nameof(noiseClear));

            void Add(PowerupType type, PowerupButton ui, string fieldName)
            {
                if (ui == null || ui.Button == null)
                {
                    Debug.LogError(
                        $"{nameof(PowerupBarView)} on '{name}': the '{fieldName}' block has no Button, so that " +
                        "powerup cannot be used or counted. Drag its button in.",
                        this);
                    return;
                }

                if (ui.CountLabel == null)
                {
                    Debug.LogWarning(
                        $"{nameof(PowerupBarView)} on '{name}': the '{fieldName}' block has no Count label, so its " +
                        "remaining charges are invisible. The button still works.",
                        this);
                }

                // Warned about only when there IS a shop to open, so a scene that never
                // adopted D-105 stays quiet. The badge is the only sign an empty powerup is
                // now a way into the shop rather than a dead control, which is why a missing
                // drag is worth a line even though nothing breaks without it.
                if (ui.EmptyBadge == null && shop != null)
                {
                    Debug.LogWarning(
                        $"{nameof(PowerupBarView)} on '{name}': the '{fieldName}' block has no Empty Badge, so nothing " +
                        "marks it when it runs out. Pressing it still opens the shop. Drag its inactive 'Add' child in.",
                        this);
                }

                slots.Add((type, ui));
            }
        }
    }

    // One powerup's two scene objects. A [Serializable] class rather than two flat fields
    // per powerup so the Inspector groups them under the powerup they belong to -- six
    // loose fields in a row is exactly how a button ends up paired with another
    // powerup's label.
    [Serializable]
    public class PowerupButton
    {
        [SerializeField] private Button button;

        [Tooltip("The TMP label inside the button that shows how many charges are left.")]
        [SerializeField] private TMP_Text countLabel;

        // The author's inactive "Add" child, switched on exactly while this powerup is at
        // zero. A GameObject rather than an Image or a CanvasGroup so whatever the author
        // built in there -- a plus, a badge, a whole little group -- comes and goes whole,
        // and so this class needs no opinion about how the empty state is DRAWN.
        [Tooltip("Optional. The inactive 'Add' object inside the button, shown when this powerup hits 0 charges.")]
        [SerializeField] private GameObject emptyBadge;

        // The author's inactive lock child, switched on while this powerup has not been
        // taught yet. Exactly the shape emptyBadge above already has, and for the same
        // reason: a GameObject so whatever is built in there -- a padlock, a tint, a whole
        // little group -- comes and goes whole, and this class needs no opinion about how
        // "locked" is DRAWN.
        //
        // OPTIONAL, and its absence costs only the marking: the lock still applies. That is
        // the safe direction -- a locked powerup that looks pressable is a smaller wrong than
        // an unlocked one that should not be.
        [Tooltip("Optional. The inactive lock object inside the button, shown until the Day that introduces this powerup. The lock applies whether or not this is wired.")]
        [SerializeField] private GameObject lockOverlay;

        [Tooltip("Optional. The label inside the lock object, filled with the Day this powerup unlocks on (wording comes from PowerupConfig's Lock Label Format).")]
        [SerializeField] private TMP_Text lockLabel;

        // The button's own `Background` child, hidden while locked so the blocker is the only
        // thing on the button. It needs a field for the dullest possible reason: Render can
        // only switch objects it holds a reference to, and this one had never been introduced
        // to the code, so it went on showing through every lock.
        //
        // The powerup ICON is deliberately NOT hidden with it. That one is the Button's own
        // targetGraphic (IconFor reads Button.image.sprite), the blocker sits on top of it
        // anyway, and hiding a third thing nobody asked about would be a guess.
        [Tooltip("Optional. The button's Background child, hidden while this powerup is locked. Unwired, the background simply keeps showing.")]
        [SerializeField] private GameObject background;

        public Button Button => button;
        public TMP_Text CountLabel => countLabel;
        public GameObject EmptyBadge => emptyBadge;
        public GameObject LockOverlay => lockOverlay;
        public TMP_Text LockLabel => lockLabel;
        public GameObject Background => background;
    }
}
