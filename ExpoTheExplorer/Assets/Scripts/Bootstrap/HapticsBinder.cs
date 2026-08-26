using ExpoTheExplorer.Core;
using ExpoTheExplorer.Data;
using ExpoTheExplorer.Session;
using ExpoTheExplorer.Systems.HapticsSystem;
using Lofelt.NiceVibrations;
using UnityEngine;

namespace ExpoTheExplorer.Bootstrap
{
    // The scene's haptics surface, and the ONLY file in this project that knows Nice
    // Vibrations exists. Everything above it speaks in HapticMoment; the translation
    // to the vendor's enum is the switch at the bottom. Swapping the vendor is
    // therefore a one-file change, which is the whole point of the arrangement.
    //
    // It lives in Bootstrap rather than in Scripts/Systems/HapticsSystem for a
    // mechanical reason the blueprint already writes down: it needs GameManager,
    // GameManager has no asmdef and so lands in Assembly-CSharp, and an asmdef
    // assembly cannot reference a predefined one. The testable half of the system --
    // HapticsService -- is the part that made it into the asmdef.
    //
    // ONE COMPONENT, TWO MODES. In the day scene `gameManager` is wired and this
    // subscribes to the session's events. On the main screen there is no GameState at
    // all, so `gameManager` is left EMPTY and the component is nothing but a play
    // surface that MetaShopView and MetaGroundsView call directly. An empty field
    // there is a valid setup, not a mistake, which is why it is not validated.
    //
    // Nothing here vibrates in the editor: Nice Vibrations only reaches real hardware
    // on an iOS/Android build (a connected gamepad aside). Verification means a device.
    public class HapticsBinder : MonoBehaviour
    {
        [Tooltip("Which haptic each moment plays. Required -- without it this component does nothing.")]
        [SerializeField] private HapticConfig config;

        [Tooltip("OPTIONAL. Wire it in the day scene to feel deliveries, life loss and game over. Leave it EMPTY on the main screen, which has no GameState; purchase haptics are called directly there.")]
        [SerializeField] private GameManager gameManager;

        [Tooltip("OPTIONAL. Whichever component provides this scene's session -- GameManager here, MainScreenRoot on the menu. Only used to read the player's haptics switch; left empty, haptics are always on.")]
        [SerializeField] private SessionHost sessionHost;

        private HapticsService service;

        // Cached rather than re-read from gameManager on every use, for the same
        // reason LivesView caches it: GameManager builds its session once in Awake
        // and never swaps the State afterwards, so the object subscribed to in Start
        // is the object unsubscribed from in OnDestroy -- and EventBus removes by
        // delegate equality against a specific instance.
        private GameState state;

        // LivesChanged carries the new count and nothing about WHY. A drop is a
        // mistake; a rise is the new day's refill or a paid Continue, and neither of
        // those should punch the player in the hand.
        private int lastLives;

        private void Awake()
        {
            service = new HapticsService(config);

            if (config == null)
            {
                Debug.LogError(
                    $"{nameof(HapticsBinder)} on '{name}' in scene '{gameObject.scene.name}' has no {nameof(HapticConfig)}. " +
                    "Nothing in this scene will vibrate; everything else keeps working.",
                    this);
            }
        }

        // Start(), not Awake(): GameManager.Awake is what assigns State and Unity does
        // not order Awake across GameObjects. Every other view in this project binds
        // from Start for exactly this reason.
        private void Start()
        {
            if (gameManager == null) return;

            state = gameManager.State;
            if (state == null)
            {
                Debug.LogError(
                    $"{nameof(HapticsBinder)} on '{name}': the wired {nameof(GameManager)} has no State. " +
                    "Gameplay haptics are off for this scene.",
                    this);
                return;
            }

            lastLives = state.Lives;

            state.TicketDelivered.Subscribe(OnTicketDelivered);
            state.LivesChanged.Subscribe(OnLivesChanged);
            state.LivesDepleted.Subscribe(OnLivesDepleted);
        }

        private void OnDestroy()
        {
            if (state == null) return;

            state.TicketDelivered.Unsubscribe(OnTicketDelivered);
            state.LivesChanged.Unsubscribe(OnLivesChanged);
            state.LivesDepleted.Unsubscribe(OnLivesDepleted);
        }

        // The one entry point for everything that is not an event: the drag handler,
        // the reward flight's stars, and the shop. Null-tolerant so a caller never has
        // to know whether this scene wired a config.
        public void Request(HapticMoment moment) => service?.Request(moment);

        // Where the once-per-frame rule is applied. LateUpdate rather than Update so
        // that every system publishing during the frame has already had its say --
        // the cost of that is one frame of latency, about 16 ms, which is below what
        // a hand can tell apart from immediate.
        private void LateUpdate()
        {
            if (service == null) return;
            if (!service.TryTakePending(out var preset)) return;

            // THE PLAYER'S SWITCH, and this is the only place it is read (D-094). Checked
            // HERE rather than in Request, which is what makes it a mute rather than a
            // second set of rules: every request still reaches the service, so the
            // once-per-frame coalescing and the priority ordering behave identically
            // whether the switch is on or off. Flip it back mid-day and the very next
            // moment plays, with nothing to re-arm.
            //
            // AFTER TryTakePending on purpose. Returning before it would leave the pending
            // moment sitting in the service, and the first moment after switching haptics
            // back on would be a stale buzz for something that happened minutes ago.
            //
            // Fails ON: an unwired sessionHost, or a scene whose host has not built its
            // session yet, both read as "allowed". A forgotten drag that leaves haptics
            // always on is a nuisance; one that leaves the game silently unable to
            // vibrate looks exactly like the vendor being broken on that device.
            if (sessionHost != null && sessionHost.Session != null && !sessionHost.Session.HapticsEnabled) return;

            HapticPatterns.PlayPreset(ToPresetType(preset));
        }

        private void OnTicketDelivered((int SlotIndex, Ticket Ticket) _) =>
            service.Request(HapticMoment.OrderDelivered);

        private void OnLivesChanged(int lives)
        {
            if (lives < lastLives) service.Request(HapticMoment.LifeLost);
            lastLives = lives;
        }

        // Published by LivesManager immediately after the LivesChanged that emptied
        // the bar, so both moments queue in the same frame and GameOver's higher
        // priority is what decides which one is felt. That is the coalescer doing its
        // job, not a conflict to resolve here.
        private void OnLivesDepleted(int _) => service.Request(HapticMoment.GameOver);

        // Written out rather than cast, even though the two enums happen to line up
        // today. A cast would keep compiling and start playing the wrong haptic the
        // day Nice Vibrations renumbers its presets; this stops compiling instead.
        private static HapticPatterns.PresetType ToPresetType(HapticPreset preset) => preset switch
        {
            HapticPreset.Selection => HapticPatterns.PresetType.Selection,
            HapticPreset.Success => HapticPatterns.PresetType.Success,
            HapticPreset.Warning => HapticPatterns.PresetType.Warning,
            HapticPreset.Failure => HapticPatterns.PresetType.Failure,
            HapticPreset.LightImpact => HapticPatterns.PresetType.LightImpact,
            HapticPreset.MediumImpact => HapticPatterns.PresetType.MediumImpact,
            HapticPreset.HeavyImpact => HapticPatterns.PresetType.HeavyImpact,
            HapticPreset.RigidImpact => HapticPatterns.PresetType.RigidImpact,
            HapticPreset.SoftImpact => HapticPatterns.PresetType.SoftImpact,
            _ => HapticPatterns.PresetType.None,
        };
    }
}
