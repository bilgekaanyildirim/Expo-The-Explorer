using ExpoTheExplorer.Bootstrap;
using ExpoTheExplorer.Core;
using ExpoTheExplorer.Session;
using ExpoTheExplorer.Systems.KeySystem;
using ExpoTheExplorer.Systems.ProgressionSystem;
using UnityEngine;
using UnityEngine.Serialization;

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
    // The wallet HUD views reference THIS instead of a GameManager, so the rule lives
    // in one place rather than being copy-pasted (and drifting) across each of them,
    // and the profile is read once per scene load instead of once per view. There were
    // three such views until D-064 took lives out of here; SoftMoneyView and GemsView
    // are what is left, and they are the two the two-mode rule was really about.
    //
    // Strictly a READER. Nothing here writes a balance -- Wallet remains the single
    // writer, and this class never even holds one long enough to mutate it.
    public class HudWalletSource : MonoBehaviour
    {
        // Wired per scene, on the prefab INSTANCE, because a prefab asset cannot
        // store a scene reference -- so this is necessarily a prefab override in the
        // day scene and empty in the prefab itself. A menu step (HudCanvasPrefabSetup)
        // used to set it per scene; the 2026-08-30 audit deleted the one-shot builders,
        // so it IS the manual drag now: select the HUD Canvas instance in the day scene
        // and drop GameManager in here. Forgetting it is not an error and will not log as
        // one -- an empty field is exactly how the main screen works -- which is why the
        // ResolvedFrom line below is the whole early-warning system.
        //
        // KNOWN FAILURE MODE, and the reason ResolvedFrom is logged below: hitting
        // "Apply All" on the prefab from the day scene can drop this override, since
        // Unity will not push a scene reference into a prefab asset. The symptom is
        // quiet -- the HUD stops following the live wallet and sits on the save
        // file's numbers for the whole day -- so the console line is the tell. An
        // earlier draft resolved this with FindAnyObjectByType precisely to dodge
        // that, and it was changed to a serialized field on the user's instruction
        // so the binding is visible and theirs to control.
        // FormerlySerializedAs is LOAD-BEARING, not tidiness. This field was named
        // `gameManager` and SampleScene's prefab instance carries an override under that
        // name; renaming it without this attribute drops the override, and the symptom is
        // precisely the quiet failure described above -- the HUD stops following the live
        // wallet and sits on the save file's numbers for the whole day, with nothing
        // visibly different on screen. The attribute carries the value across, and the
        // reference still type-checks because GameManager IS a SessionHost now.
        //
        // The type widened from GameManager to SessionHost so the main screen can provide
        // a session too (decisions.md D-022). That is what makes the menu HUD live rather
        // than a one-shot read, which a shop spending money requires.
        [FormerlySerializedAs("gameManager")]
        [SerializeField] private SessionHost sessionHost;

        private PlayerProfile profile;
        private bool resolved;

        // PRIVATE on purpose. A public LiveState existed until D-014 and was removed
        // precisely so the three views would stop branching on which mode they were in --
        // reintroducing it as API would undo that. Here it is one null-safe walk down the
        // chain, so the four value getters and Resolve do not each repeat it.
        //
        // Null means "no session in this scene", which is the save-file mode. The double
        // ?. matters: the host may exist while its Session is still unassigned, since a
        // host assigns it in its own Awake and Unity does not order Awake across objects.
        private GameState LiveState => sessionHost?.Session?.State;

        // Forwarded change events, so a view never has to know which mode it is in
        // (D-014). In the day scene these re-publish GameState's own events; on the
        // main screen they simply never fire, which is not a gap -- nothing there can
        // earn or spend anything, so one render at Start is the whole truth.
        //
        // Separate buses rather than one "something changed" signal: each carries the
        // real new value, so SoftMoneyView and GemsView need no lookup at all.
        //
        // There is deliberately no lives bus any more (D-064). Lives stopped being
        // persisted and the heart row became a day-scene object, so this class has
        // neither a second mode to hide for them nor a reader to serve: LivesView
        // binds straight to GameState now. What is left here is exactly what the
        // class name claims -- the wallet.
        public EventBus<int> SoftMoneyChanged { get; } = new();
        public EventBus<int> GemsChanged { get; } = new();

        // Keys are forwarded like the two balances, and unlike lives they genuinely
        // belong here: the key readout lives INSIDE the shared HUD prefab and shows on
        // both screens, which is the exact case this class was built for (D-013). The
        // heart row went the other way in D-064 because it is a day-scene object.
        //
        // Not re-published from GameState like the balances: keys are not on GameState at
        // all (D-065 keeps the count private to KeyManager, so the single-writer rule is a
        // compile error rather than a comment). This forwards KeyManager's own bus.
        public EventBus<int> KeysChanged { get; } = new();

        // The day the player is on, forwarded for the same reason keys are (D-130): the
        // day badge lives INSIDE the shared HUD prefab and shows on both screens, which is
        // the exact case this class was built for. Lives went the other way in D-064
        // because the heart row is a day-scene object that can hold a scene reference of
        // its own; a prefab asset cannot, so this readout has to come through here.
        //
        // Re-published from GameState like the two balances rather than owned here:
        // GameManager stays the single writer of CurrentDayIndex (day advance, the clamped
        // load in Awake, the completed-day exit), and nothing in this file can move it.
        public EventBus<int> CurrentDayIndexChanged { get; } = new();

        public int SoftMoney
        {
            get
            {
                Resolve();
                return LiveState != null ? LiveState.SoftMoney : profile.SoftMoney;
            }
        }

        public int Gems
        {
            get
            {
                Resolve();
                return LiveState != null ? LiveState.Gems : profile.Gems;
            }
        }

        // A CATALOG POSITION, zero-based -- not the number the player reads. The +1 is the
        // view's job, the same split MainScreenView and SettingsPopupView already make, so
        // this getter stays comparable with GameState.CurrentDayIndex and with the profile
        // field it falls back to instead of being a second, off-by-one currency.
        //
        // The save-file branch is not a dead one even though D-022 gave both screens a
        // session: it is what the number falls back to when a session host reference has
        // been lost, and the profile holds the same value GameState was loaded from, so a
        // lost reference shows a stale day rather than Day 1.
        public int CurrentDayIndex
        {
            get
            {
                Resolve();
                return LiveState != null ? LiveState.CurrentDayIndex : profile.CurrentDayIndex;
            }
        }

        // The session's key manager, or null when this scene has no session. Exposed so a
        // view can ask whether keys are readable at all -- see KeysAvailable below --
        // rather than each view repeating the walk down the chain.
        private KeyManager Keys => sessionHost?.Session?.KeyManager;

        // False means "this scene has no session", which since D-022 is a LOST REFERENCE
        // rather than an expected mode: both screens are supposed to have a SessionHost.
        // KeysView uses it to stay silent instead of rendering a number, because the only
        // number it could invent -- 0 -- would tell the player they are out of keys.
        public bool KeysAvailable
        {
            get
            {
                Resolve();
                return Keys != null;
            }
        }

        public int KeyCount
        {
            get
            {
                Resolve();
                return Keys?.Keys ?? 0;
            }
        }

        public int MaxKeys
        {
            get
            {
                Resolve();
                return Keys?.MaxKeys ?? 0;
            }
        }

        // The DISPLAY tick, and it is display-only by design (decisions.md D-065): every
        // gate calls KeyManager.Refresh for itself, so no rule anywhere depends on this
        // running. Its single job is that the number on screen climbs while the player is
        // looking at it -- without it a key earned at minute 30 would appear only when
        // something else happened to ask.
        //
        // ONCE PER SECOND, not per frame. A value that changes every 30 minutes does not
        // need 60 clock reads a second; that is three orders of magnitude of waste for a
        // counter whose smallest visible step is one second. Refresh publishes only on an
        // actual grant, so a quiet second costs one subtraction and nothing else.
        //
        // It lives HERE rather than in KeysView so there is one ticker per scene instead
        // of one per view -- and step 5's out-of-keys popup needs the same freshness.
        private float secondsSinceKeyRefresh;

        private void Update()
        {
            var keys = Keys;
            if (keys == null) return;

            secondsSinceKeyRefresh += Time.unscaledDeltaTime;
            if (secondsSinceKeyRefresh < 1f) return;

            secondsSinceKeyRefresh = 0f;

            // UNSCALED time on purpose: a popup that pauses the game by zeroing timeScale
            // must not also stop the wait the player is watching count down.
            keys.Refresh();
        }

        // Symmetric with the forwarding in Resolve. Both GameState and this component
        // die with the scene today, so nothing actually leaks -- this exists so the
        // pairing is visible and stays correct if either lifetime ever changes.
        // EventBus removes by delegate equality (target + method), so the method-group
        // expressions here match the ones subscribed above.
        private void OnDestroy()
        {
            // Unsubscribed BEFORE the early return below, because keys hang off the
            // session rather than off GameState -- pairing them with the state's null
            // check would leak the subscription in exactly the case where a session
            // exists but LiveState does not.
            Keys?.KeysChanged.Unsubscribe(KeysChanged.Publish);

            var state = LiveState;
            if (state == null) return;

            state.SoftMoneyChanged.Unsubscribe(SoftMoneyChanged.Publish);
            state.GemsChanged.Unsubscribe(GemsChanged.Publish);
            state.CurrentDayIndexChanged.Unsubscribe(CurrentDayIndexChanged.Publish);
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

            var live = LiveState;
            if (live == null)
            {
                profile = new PlayerProfileStore().Load();
            }
            else
            {
                // Forward, don't re-implement: GameState's setters already publish only
                // on an actual change, so this adds no filtering of its own.
                var state = live;
                state.SoftMoneyChanged.Subscribe(SoftMoneyChanged.Publish);
                state.GemsChanged.Subscribe(GemsChanged.Publish);

                // Inside the live branch, unlike keys: the day number DOES have a
                // save-file mode (the profile field), so there is nothing to subscribe to
                // when there is no session -- the value simply cannot change there.
                state.CurrentDayIndexChanged.Subscribe(CurrentDayIndexChanged.Publish);
            }

            // Outside the live/save-file branch above, because keys hang off the SESSION
            // rather than off GameState: a scene can have a session (and therefore keys)
            // in both of that branch's cases. Null here means no session at all, which
            // since D-022 is a lost reference rather than a mode -- the log below says so.
            Keys?.KeysChanged.Subscribe(KeysChanged.Publish);

            // Says which of the two modes this instance picked, once per scene load.
            // Not noise: it is the only visible difference between "correctly reading
            // the save file on the menu" and "silently stopped following the live
            // wallet because an override was lost", which look identical on screen.
            Debug.Log(
                $"{nameof(HudWalletSource)} on '{name}' in scene '{gameObject.scene.name}': " +
                (LiveState != null
                    ? $"live GameState (session host: {sessionHost.GetType().Name})."
                    : "save file (NO session host wired -- since D-022 both scenes should have one, so this now means a lost reference rather than an expected menu)."),
                this);
        }
    }
}
