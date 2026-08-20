using System.Collections.Generic;
using TMPro;
using UnityEngine;

namespace ExpoTheExplorer.UI
{
    // Top HUD readout for lives, shown as "current/max" (e.g. "3/3"). In the day
    // scene it is reactive, not polled: it binds to LivesChanged and MaxLivesChanged
    // (both fired by their property setters on every actual change -- LoseLife only
    // moves Lives, TryContinue can move both). Distinct from LivesDepleted, which
    // fires once, the instant Lives hits 0.
    //
    // Since D-013 the HUD Canvas is one prefab shared by both scenes, so this view
    // reads through HudWalletSource rather than holding a GameManager, and since
    // D-014 it does not know that two modes exist: it subscribes to the source's
    // forwarded lives event and renders. On the main screen that event never fires,
    // which is correct -- nothing there can cost a life.
    //
    // D-014 also made the main screen's number REAL. Lives are persisted now, so the
    // source reports the player's saved remaining lives there instead of the
    // DefaultStartingLives placeholder this view used to display as a constant "3/3".
    public class LivesView : MonoBehaviour
    {
        [SerializeField] private HudWalletSource walletSource;
        [SerializeField] private TMP_Text livesText;

        // Start(), not Awake() — Unity's Awake() order across different GameObjects
        // is unspecified, and GameManager.Awake (which sets State) may not have run
        // yet. Start() is always safe: every object's Awake() runs before any
        // object's Start(). HudWalletSource resolves lazily for exactly this reason,
        // so asking it anything from here is what keeps that guarantee.
        private void Start()
        {
            if (!ValidateReferences()) return;

            // One bus, fed by both of GameState's lives events inside the source --
            // this label cannot act on half of "current/max", so splitting them here
            // would buy nothing.
            walletSource.LivesChanged.Subscribe(OnLivesChanged);
            Refresh();
        }

        private void OnDestroy()
        {
            if (walletSource != null) walletSource.LivesChanged.Unsubscribe(OnLivesChanged);
        }

        // The one place a payload is deliberately dropped, and it is confined to this
        // adapter line: whichever half changed, the label re-renders both, so the new
        // value alone cannot drive it.
        private void OnLivesChanged(int _) => Refresh();

        private void Refresh()
        {
            livesText.text = $"{walletSource.Lives}/{walletSource.MaxLives}";
        }

        // Both fields are wired inside the PREFAB (walletSource points at the canvas
        // root beside it), so they are prefab data and need no per-scene override —
        // a missing one should still fail loudly with a clear pointer to which field.
        private bool ValidateReferences()
        {
            var missing = new List<string>();
            if (walletSource == null) missing.Add(nameof(walletSource));
            if (livesText == null) missing.Add(nameof(livesText));

            if (missing.Count == 0) return true;

            Debug.LogError($"{nameof(LivesView)} on '{name}' is missing Inspector reference(s): {string.Join(", ", missing)}.", this);
            return false;
        }
    }
}
