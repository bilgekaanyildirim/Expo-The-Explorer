using ExpoTheExplorer.Data;
using ExpoTheExplorer.Session;
using UnityEngine;

namespace ExpoTheExplorer.UI
{
    // The main screen's composition root: it builds the same GameSession the day scene
    // does, and nothing else. That is the whole point -- the menu now has a GameState and
    // a Wallet, which is what a shop needs in order to spend money at all, and the shared
    // HUD can follow it live instead of reading the save file once (decisions.md D-022).
    //
    // Deliberately NOT a GameManager. It builds the session half only; the day runtime
    // (ticket slots, tray, board distribution, the per-frame tick) stays out, because a
    // running day behind a menu would time tickets out and cost lives -- the concrete
    // reason moving GameManager here was rejected in D-021.
    //
    // It is a SessionHost so HudWalletSource can hold a reference to "this scene's
    // session provider" without knowing which of the two it is.
    public class MainScreenRoot : SessionHost
    {
        // The same three assets GameManager takes. A MainScreenSessionSetup menu step used
        // to fill them, on the reasoning that "find the one asset of this type" is a search
        // an author should not have to repeat; it was deleted with the other one-shot
        // builders on 2026-08-30, so they are dragged in by hand now. They were always
        // serialized fields rather than a lookup -- which is what makes the manual route
        // work at all, and what let a project with two of something be corrected here.
        [SerializeField] private GameConfig gameConfig;
        [SerializeField] private LivesConfig livesConfig;

        // Required, and it joins the guard below rather than getting one of its own
        // (decisions.md D-065): the main screen needs a session for the same reason the
        // day scene does, and a KeyManager cannot be built without this.
        [SerializeField] private KeyConfig keyConfig;

        // Deliberately NOT part of the guard below, unlike the three above it: this screen
        // must open even when powerups are unwired. Missing it costs the powerup shop
        // (powerup-plan Adım 3) and nothing else, whereas a missing GameConfig/LivesConfig/
        // KeyConfig means there is no session at all and the HUD silently falls back to
        // reading the save file. Loud but not fatal — the same failing-open trade the day
        // scene makes with the identical field.
        //
        // The main screen needs this for a reason the day scene does not: it is the only
        // place charges are SOLD. It registers no effects, so it can never spend one.
        [Tooltip("Powerup stock knobs. Needed so the main screen can show and sell powerup charges. Create via Create > ExpoTheExplorer > Data > Powerup Config.")]
        [SerializeField] private PowerupConfig powerupConfig;

        [Tooltip("Only used to parse the Day catalog, which this screen needs so it can tell which Day the player is on. Nothing on the menu reads a food item.")]
        [SerializeField] private FoodCatalog foodCatalog;

        private GameSession session;

        public override GameSession Session => session;

        // Awake, not Start: every reader is a view's Start (HudWalletSource resolves
        // lazily from there), and Unity runs all Awakes before any Start. This is the same
        // ordering GameManager relies on, so both scenes behave identically.
        private void Awake()
        {
            // 60 rather than the platform default, which on mobile is 30. The whole game is
            // a finger dragging an item across a board, and a drag is the one interaction
            // where the frame rate IS the feel -- at 30 the item visibly lags the finger.
            // Set in both scene roots rather than once: the value survives a scene load, so
            // in a real build MainScreen (build index 0) would be enough, but the day scene
            // is opened directly from the Editor constantly and would otherwise run at half
            // the shipped rate while it is being tuned. Assigning it twice costs nothing.
            //
            // A literal rather than a GameConfig field on purpose: the no-magic-numbers
            // invariant governs CONTENT data -- numbers a designer tunes per Day -- and a
            // frame-rate target is a platform setting that no Day can disagree about.
            Application.targetFrameRate = 60;

            if (gameConfig == null || livesConfig == null || keyConfig == null)
            {
                // Loud, because the failure is otherwise quiet: with no session the HUD
                // silently falls back to reading the save file, which looks correct until
                // the first purchase fails to update the coin count.
                Debug.LogError(
                    $"{nameof(MainScreenRoot)} on '{name}' is missing a config reference " +
                    $"(gameConfig: {gameConfig != null}, livesConfig: {livesConfig != null}, " +
                    $"keyConfig: {keyConfig != null}). " +
                    "Run ExpoTheExplorer > Meta > Wire MainScreen Session.",
                    this);
                return;
            }

            if (powerupConfig == null)
            {
                Debug.LogError(
                    $"{nameof(MainScreenRoot)} on '{name}' has no {nameof(PowerupConfig)} wired, so this screen " +
                    "cannot show or sell powerup charges. Everything else works. " +
                    "Run ExpoTheExplorer > Meta > Wire MainScreen Session.",
                    this);
            }

            session = new GameSession(gameConfig, livesConfig, keyConfig, foodCatalog, powerupConfig);
        }
    }
}
