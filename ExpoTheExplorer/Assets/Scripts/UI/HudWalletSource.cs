using ExpoTheExplorer.Bootstrap;
using ExpoTheExplorer.Core;
using ExpoTheExplorer.Systems.ProgressionSystem;
using UnityEngine;

namespace ExpoTheExplorer.UI
{
    // The single answer to "where does THIS scene's HUD read its numbers from"
    // (decisions.md D-013). It exists because one HUD Canvas prefab now lives in
    // both scenes, and the two scenes have different answers:
    //
    //   day scene   -> the live GameState, via the GameManager wired below.
    //                  Reactive, unchanged.
    //   main screen -> the save file, read once, because the field is empty there.
    //                  That scene has no GameManager by design (D-012), which is
    //                  what keeps each scene able to build itself from scratch.
    //
    // The three HUD views reference THIS instead of a GameManager, so the rule lives
    // in one place rather than being copy-pasted (and drifting) across three views,
    // and the profile is read once per scene load instead of three times.
    //
    // Strictly a READER. Nothing here writes a balance -- Wallet remains the single
    // writer, and this class never even holds one long enough to mutate it.
    public class HudWalletSource : MonoBehaviour
    {
        // Wired per scene, on the prefab INSTANCE, because a prefab asset cannot
        // store a scene reference -- so this is necessarily a prefab override in the
        // day scene and empty in the prefab itself. HudCanvasPrefabSetup step 2 sets
        // it for whichever scene you run it on, rather than leaving it to a manual
        // drag that is easy to forget.
        //
        // KNOWN FAILURE MODE, and the reason ResolvedFrom is logged below: hitting
        // "Apply All" on the prefab from the day scene can drop this override, since
        // Unity will not push a scene reference into a prefab asset. The symptom is
        // quiet -- the HUD stops following the live wallet and sits on the save
        // file's numbers for the whole day -- so the console line is the tell. An
        // earlier draft resolved this with FindAnyObjectByType precisely to dodge
        // that, and it was changed to a serialized field on the user's instruction
        // so the binding is visible and theirs to control.
        [SerializeField] private GameManager gameManager;

        private PlayerProfile profile;
        private bool resolved;

        // Forwarded change events, so a view never has to know which mode it is in
        // (D-014). In the day scene these re-publish GameState's own events; on the
        // main screen they simply never fire, which is not a gap -- nothing there can
        // earn, spend or lose anything, so one render at Start is the whole truth.
        //
        // Separate buses rather than one "something changed" signal: each carries the
        // real new value, so SoftMoneyView and GemsView need no lookup at all. Lives
        // is one bus fed by BOTH of GameState's lives events, because the label it
        // drives always renders "current/max" and cannot act on half of that pair.
        public EventBus<int> SoftMoneyChanged { get; } = new();
        public EventBus<int> GemsChanged { get; } = new();
        public EventBus<int> LivesChanged { get; } = new();

        public int SoftMoney
        {
            get
            {
                Resolve();
                return gameManager != null ? gameManager.State.SoftMoney : profile.SoftMoney;
            }
        }

        public int Gems
        {
            get
            {
                Resolve();
                return gameManager != null ? gameManager.State.Gems : profile.Gems;
            }
        }

        // Lives are persisted since D-014, so off the day scene this reads the SAVED
        // count -- the player's real remaining lives, not the placeholder constant it
        // returned before. That change is the whole reason the main screen's lives
        // readout stopped being decorative.
        public int Lives
        {
            get
            {
                Resolve();
                return gameManager != null ? gameManager.State.Lives : profile.Lives;
            }
        }

        // Still the constant off the day scene: MaxLives is deliberately not persisted
        // because nothing varies it (see PlayerProfile). The day an upgrade raises it,
        // it becomes a saved field and this line follows Lives above.
        public int MaxLives
        {
            get
            {
                Resolve();
                return gameManager != null ? gameManager.State.MaxLives : GameState.DefaultStartingLives;
            }
        }

        // Symmetric with the forwarding in Resolve. Both GameState and this component
        // die with the scene today, so nothing actually leaks -- this exists so the
        // pairing is visible and stays correct if either lifetime ever changes.
        // EventBus removes by delegate equality (target + method), so the method-group
        // expressions here match the ones subscribed above.
        private void OnDestroy()
        {
            if (gameManager == null) return;

            var state = gameManager.State;
            if (state == null) return;

            state.SoftMoneyChanged.Unsubscribe(SoftMoneyChanged.Publish);
            state.GemsChanged.Unsubscribe(GemsChanged.Publish);
            state.LivesChanged.Unsubscribe(LivesChanged.Publish);
            state.MaxLivesChanged.Unsubscribe(LivesChanged.Publish);
        }

        // Lazy, and never from Awake: GameManager assigns State in its own Awake, and
        // Unity does not order Awake across GameObjects. Every caller is a view's
        // Start(), by which point all Awakes have run -- the same guarantee those views
        // already relied on when they read gameManager.State directly. Deferring the
        // profile read here also keeps it off the main screen's Awake frame.
        private void Resolve()
        {
            if (resolved) return;
            resolved = true;

            if (gameManager == null)
            {
                profile = new PlayerProfileStore().Load();
            }
            else
            {
                // Forward, don't re-implement: GameState's setters already publish only
                // on an actual change, so this adds no filtering of its own.
                var state = gameManager.State;
                state.SoftMoneyChanged.Subscribe(SoftMoneyChanged.Publish);
                state.GemsChanged.Subscribe(GemsChanged.Publish);
                state.LivesChanged.Subscribe(LivesChanged.Publish);
                state.MaxLivesChanged.Subscribe(LivesChanged.Publish);
            }

            // Says which of the two modes this instance picked, once per scene load.
            // Not noise: it is the only visible difference between "correctly reading
            // the save file on the menu" and "silently stopped following the live
            // wallet because an override was lost", which look identical on screen.
            Debug.Log(
                $"{nameof(HudWalletSource)} on '{name}' in scene '{gameObject.scene.name}': " +
                (gameManager != null
                    ? "live GameState (GameManager wired)."
                    : "save file (no GameManager wired -- expected on the main screen, a lost prefab override anywhere else)."),
                this);
        }
    }
}
