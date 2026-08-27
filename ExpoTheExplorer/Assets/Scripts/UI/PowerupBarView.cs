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

        // Cached rather than re-read from gameManager on every refresh, for the reason
        // LivesView caches GameState: EventBus removes by delegate equality on a specific
        // instance, so the object this unsubscribes from in OnDestroy must be the object
        // it subscribed to. GameSession is built once in GameManager.Awake and never
        // swapped, so caching is safe and re-walking the property would not be.
        private PowerupManager manager;

        // The tutorial's powerup panel while it is up, or null. Held so a step boundary that
        // is not a dismissal (a retry, an abort) can take it down.
        private TutorialPowerupIntroView introView;

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

        // This view builds the powerup-intro step for the reason the target tray builds a
        // move step's spotlight: it is the object that already holds what the step needs.
        // The three icons are the sprites on the three live buttons, and nothing else in the
        // project knows where those are.
        private void OnTutorialStepChanged()
        {
            var holdingForReading = gameManager.Tutorial != null && gameManager.Tutorial.IsHoldingForReading;

            // The step moved on without the panel being dismissed -- a retry, an abort, the
            // day scene going away. Take it down rather than leaving a modal over a game
            // that is running again.
            if (!holdingForReading)
            {
                if (introView != null) Destroy(introView.gameObject);
                introView = null;
                return;
            }

            if (introView != null) return;

            // Borrowed off a count label rather than serialized, so the panel matches the
            // game's own type with nothing wired by hand -- same trick the step message uses
            // on the ticket card.
            var font = FindFont();
            if (font == null)
            {
                Debug.LogWarning(
                    $"{nameof(PowerupBarView)} on '{name}': no TextMeshPro font could be borrowed from the powerup " +
                    "count labels, so the powerup tutorial panel cannot be drawn. Skipping that step.", this);
                gameManager.Tutorial.NotifyReadingFinished();
                return;
            }

            introView = TutorialPowerupIntroView.Create(
                gameManager.PowerupConfig,
                GetComponentInParent<Canvas>(),
                font,
                IconFor,
                OnIntroDismissed);

            if (introView == null)
            {
                Debug.LogWarning(
                    $"{nameof(PowerupBarView)} on '{name}': the powerup tutorial panel could not be built (no " +
                    $"{nameof(PowerupConfig)} wired?), so that step is skipped rather than left blocking the day.", this);
                gameManager.Tutorial.NotifyReadingFinished();
            }
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

        private TMP_FontAsset FindFont()
        {
            foreach (var (_, ui) in slots)
            {
                if (ui?.CountLabel != null && ui.CountLabel.font != null) return ui.CountLabel.font;
            }

            return null;
        }

        private void OnDestroy()
        {
            // Guarded because Start returns early on a missing GameManager, and OnDestroy
            // runs regardless of how far Start got.
            if (manager != null) manager.ChargesChanged.Unsubscribe(OnChargesChanged);
            if (gameManager != null) gameManager.TutorialStepChanged -= OnTutorialStepChanged;
            if (shop != null) shop.Closed -= OnShopClosed;

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

            if (manager.ChargesOf(type) > 0)
            {
                // Unchanged: TryUse decides on its own whether there was work to do, and a
                // press with nothing to collect costs no charge (GDD 5.2).
                manager.TryUse(type);
                return;
            }

            OpenShop();
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
                if (type == change.Type) Render(ui, change.Charges, ShopIsReachable);
            }
        }

        // BOTH halves, because a shop with no manager behind it is a shop this bar cannot
        // send anyone to: Start returns before the click listeners are attached when there
        // is no PowerupManager, so a button left live there would look pressable and do
        // nothing at all -- the one outcome worse than a dim button.
        private bool ShopIsReachable => shop != null && manager != null;

        private void RefreshAll()
        {
            foreach (var (type, ui) in slots)
            {
                Render(ui, manager?.ChargesOf(type) ?? 0, ShopIsReachable);
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
        private static void Render(PowerupButton ui, int charges, bool shopIsReachable)
        {
            if (ui == null) return;

            // A plain number, not "x3": the label's formatting belongs to whoever styled
            // the button, and a prefix authored here would fight it.
            if (ui.CountLabel != null) ui.CountLabel.text = charges.ToString();
            if (ui.EmptyBadge != null) ui.EmptyBadge.SetActive(charges <= 0);
            if (ui.Button != null) ui.Button.interactable = charges > 0 || shopIsReachable;
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

        public Button Button => button;
        public TMP_Text CountLabel => countLabel;
        public GameObject EmptyBadge => emptyBadge;
    }
}
