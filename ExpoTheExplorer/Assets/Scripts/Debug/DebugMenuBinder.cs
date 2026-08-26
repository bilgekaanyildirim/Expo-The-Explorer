#if UNITY_EDITOR || DEVELOPMENT_BUILD
using ExpoTheExplorer.Data;
using ExpoTheExplorer.Session;
using UnityEngine;
using UnityEngine.InputSystem;

namespace ExpoTheExplorer.DebugMenu
{
    // The one bridge between a scene and the SRDebugger cheat panel (decisions.md D-092).
    //
    // WHY THIS EXISTS AT ALL: SROptions is a plain C# object that SRDebugger constructs
    // itself, from a [RuntimeInitializeOnLoadMethod], with no scene presence whatsoever. It
    // therefore cannot reach a GameSession on its own. The two ways to close that are a
    // runtime scene search or a serialized reference, and D-013 already settled which one
    // this project uses -- the panel gets a slot in the Inspector and a human drags the host
    // into it, exactly like every other binding here. Nothing searches the scene.
    //
    // It is a component on the object that ALREADY carries the SessionHost rather than a new
    // prefab or a new scene object: one component and one drag per scene is the smallest
    // thing that works, and a prefab would only add a second place for the reference to be
    // wrong.
    //
    // IT DOES NOT READ THE SESSION HERE, and that is deliberate. SessionHost.Session is null
    // until the host's own Awake has run, and Unity does not order Awake across GameObjects
    // (SessionHost says so in its own note). Caching `host.Session` at bind time would
    // therefore be a race that resolves differently per scene load. Instead the host itself
    // is what gets registered, and the panel dereferences it at the moment a button is
    // actually pressed -- by which point every Awake in the scene is long finished, and a
    // session rebuilt since then is picked up for free.
    //
    // Compiled out of release builds entirely; see the Scripts/Debug/ note in blueprint.md.
    public class DebugMenuBinder : MonoBehaviour
    {
        [SerializeField]
        [Tooltip("The GameManager (day scene) or MainScreenRoot (main screen) on this object. " +
                 "Drag it in -- the panel is inert without it.")]
        private SessionHost host;

        [SerializeField]
        [Tooltip("Optional. Only the 'Unlock All Props' cheat needs it; every other cheat " +
                 "works with this empty.")]
        private MetaCatalog metaCatalog;

        [Header("Opening the panel")]
        [SerializeField]
        [Tooltip("Touch anywhere with this many fingers to open the panel. This is the one " +
                 "that matters on device.")]
        [Min(2)]
        private int fingersToOpen = 3;

        [SerializeField]
        [Tooltip("Opens the panel in the editor and on desktop.")]
        private Key openKey = Key.F1;

        internal SessionHost Host => host;

        internal MetaCatalog Catalog => metaCatalog;

        // OnEnable/OnDisable rather than Awake/OnDestroy so that disabling the object is
        // enough to take the cheats away, and so a scene reload cannot leave the panel
        // holding a destroyed component: the outgoing scene's OnDisable runs before the
        // incoming scene's OnEnable, so the handover is ordered even in Single-mode loads.
        private void OnEnable()
        {
            SROptions.BindDebugMenu(this);
        }

        private void OnDisable()
        {
            SROptions.UnbindDebugMenu(this);
        }

        // WHY THIS OPENER EXISTS, when SRDebugger ships with a triple-tap trigger of its own:
        // that trigger is a tiny UI rect pinned to ONE CORNER of the screen, sized at runtime
        // from screen DPI. Tapping the middle of the screen three times does nothing, which
        // reads exactly like a broken install -- and on a phone, hitting a few-millimetre
        // corner target three times in a row while the game's own UI sits in the same corner
        // is not a gesture anyone wants to rely on for a debug build.
        //
        // A whole-screen multi-finger touch cannot be missed and cannot collide with the
        // game: nothing here is played with three fingers at once. SRDebugger's own trigger
        // and keyboard shortcuts are left enabled -- this is an addition, not a replacement.
        //
        // Frame-frequency work, which the cost model normally makes us justify: it is a
        // handful of memory reads and an int compare per frame, O(1), at the cheapest tier
        // there is, and the whole file is compiled out of release builds. Nothing here is
        // measurable.
        private void Update()
        {
            if (WasOpenKeyPressed() || WasMultiFingerTapStarted()) TogglePanel();
        }

        // Modifier-free on purpose. SRDebugger's own shortcuts are Ctrl+Shift+F1..F4, so a
        // bare `keyboard[openKey].wasPressedThisFrame` would fire on Ctrl+Shift+F1 as well as
        // on F1 -- both openers would run on the same keypress, toggling the panel open and
        // straight back shut. The symptom of that is "the shortcut does nothing", which is
        // the exact complaint this whole opener exists to answer.
        private bool WasOpenKeyPressed()
        {
            var keyboard = Keyboard.current;
            if (keyboard == null || !keyboard[openKey].wasPressedThisFrame) return false;

            return !keyboard.ctrlKey.isPressed
                   && !keyboard.shiftKey.isPressed
                   && !keyboard.altKey.isPressed;
        }

        // Edge-triggered on the frame the finger count REACHES the threshold, so holding
        // three fingers down does not toggle the panel once per frame.
        private bool WasMultiFingerTapStarted()
        {
            var touchscreen = Touchscreen.current;
            if (touchscreen == null) return false;

            var pressed = 0;
            foreach (var touch in touchscreen.touches)
            {
                if (touch.press.isPressed) pressed++;
            }

            var reached = pressed >= fingersToOpen;
            var justReached = reached && !wasAtFingerCount;
            wasAtFingerCount = reached;
            return justReached;
        }

        private bool wasAtFingerCount;

        private static void TogglePanel()
        {
            var service = SRDebug.Instance;
            if (service == null) return;

            if (service.IsDebugPanelVisible)
            {
                service.HideDebugPanel();
                return;
            }

            // requireEntryCode: false because this gesture IS the gate. The entry code exists
            // to stop a player who found the trigger by accident; someone deliberately holding
            // three fingers on a development build is not that person.
            service.ShowDebugPanel(false);
        }
    }
}
#endif
