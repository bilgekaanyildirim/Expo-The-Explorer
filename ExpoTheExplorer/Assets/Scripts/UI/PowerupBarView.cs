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
    // IT SPENDS; IT NEVER SELLS. Buying is the main screen's job (Adım 3) because
    // opening a store mid-day suspends the very time pressure a powerup exists to
    // relieve. There is deliberately no price, no Gem icon and no purchase path here --
    // if a charge is missing, the answer is on the menu, not in the middle of service.
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

        // Cached rather than re-read from gameManager on every refresh, for the reason
        // LivesView caches GameState: EventBus removes by delegate equality on a specific
        // instance, so the object this unsubscribes from in OnDestroy must be the object
        // it subscribed to. GameSession is built once in GameManager.Awake and never
        // swapped, so caching is safe and re-walking the property would not be.
        private PowerupManager manager;

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
                ui.Button.onClick.AddListener(() => manager.TryUse(pressed));
            }

            manager.ChargesChanged.Subscribe(OnChargesChanged);
            RefreshAll();
        }

        private void OnDestroy()
        {
            // Guarded because Start returns early on a missing GameManager, and OnDestroy
            // runs regardless of how far Start got.
            if (manager != null) manager.ChargesChanged.Unsubscribe(OnChargesChanged);

            // The listeners go too. The scene is usually being torn down anyway, but this
            // view is also legal to disable and re-enable, and a second Start would
            // otherwise stack a second listener and spend two charges per press.
            foreach (var (_, ui) in slots)
            {
                if (ui?.Button != null) ui.Button.onClick.RemoveAllListeners();
            }
            slots.Clear();
        }

        // Only the row that changed is redrawn. The event carries its own type precisely
        // so a bar does not have to re-render three labels because one of them moved.
        private void OnChargesChanged((PowerupType Type, int Charges) change)
        {
            foreach (var (type, ui) in slots)
            {
                if (type == change.Type) Render(ui, change.Charges);
            }
        }

        private void RefreshAll()
        {
            foreach (var (type, ui) in slots)
            {
                Render(ui, manager?.ChargesOf(type) ?? 0);
            }
        }

        // Dimming is keyed on the CHARGE COUNT, deliberately not on PowerupManager.CanUse.
        // CanUse also asks whether an effect is registered, and effects arrive in Adım 4-6
        // of the plan -- gating on it today would draw all three buttons dead from the
        // moment this bar ships, which reads as broken rather than as empty. In the day
        // scene the two answers converge anyway once the effects are wired, because that
        // is the scene that registers them.
        //
        // `interactable = false` is what makes "pressing an empty powerup does nothing"
        // true without a single branch in the click handler: UGUI simply does not raise
        // onClick, and the button takes its own disabled tint. That is also why there is
        // no popup here, unlike NoKeysPopupView -- an empty key locks a player out of
        // playing, an empty charge only costs them a convenience.
        private static void Render(PowerupButton ui, int charges)
        {
            if (ui == null) return;

            // A plain number, not "x3": the label's formatting belongs to whoever styled
            // the button, and a prefix authored here would fight it.
            if (ui.CountLabel != null) ui.CountLabel.text = charges.ToString();
            if (ui.Button != null) ui.Button.interactable = charges > 0;
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

        public Button Button => button;
        public TMP_Text CountLabel => countLabel;
    }
}
