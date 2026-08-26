#if UNITY_EDITOR || DEVELOPMENT_BUILD
using ExpoTheExplorer.Data;
using ExpoTheExplorer.Session;
using UnityEngine;

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
    }
}
#endif
