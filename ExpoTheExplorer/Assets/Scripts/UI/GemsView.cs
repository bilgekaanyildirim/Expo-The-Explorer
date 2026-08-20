using System.Collections.Generic;
using TMPro;
using UnityEngine;

namespace ExpoTheExplorer.UI
{
    // Top HUD readout for Gems (GDD Section 10 — hard currency). Reactive where
    // there is something to react to: in the day scene it binds to
    // GameState.GemsChanged (fired by the property setter on every actual change,
    // e.g. LivesManager.TryContinueWithGems spending Gems) instead of
    // re-stringifying every frame.
    //
    // Since D-013 the HUD Canvas is one prefab shared by both scenes, so this view
    // no longer holds a GameManager of its own: HudWalletSource decides where the
    // number comes from (live GameState, or the save file on the main screen).
    //
    // Since D-014 it does not know that TWO modes exist either -- it subscribes to the
    // source's forwarded event and renders. On the main screen that event never fires,
    // which is correct rather than a gap: nothing there can change a balance.
    public class GemsView : MonoBehaviour
    {
        [SerializeField] private HudWalletSource walletSource;
        [SerializeField] private TMP_Text gemsText;

        // Start(), not Awake() — Unity's Awake() order across different GameObjects
        // is unspecified, and GameManager.Awake (which sets State) may not have run
        // yet. Start() is always safe: every object's Awake() runs before any
        // object's Start(). HudWalletSource resolves lazily for exactly this reason,
        // so asking it anything from here is what keeps that guarantee.
        private void Start()
        {
            if (!ValidateReferences()) return;

            walletSource.GemsChanged.Subscribe(Refresh);
            Refresh(walletSource.Gems);
        }

        private void OnDestroy()
        {
            if (walletSource != null) walletSource.GemsChanged.Unsubscribe(Refresh);
        }

        private void Refresh(int gems)
        {
            gemsText.text = gems.ToString();
        }

        // Both fields are wired inside the PREFAB (walletSource points at the canvas
        // root beside it), so they are prefab data and need no per-scene override —
        // a missing one should still fail loudly with a clear pointer to which field.
        private bool ValidateReferences()
        {
            var missing = new List<string>();
            if (walletSource == null) missing.Add(nameof(walletSource));
            if (gemsText == null) missing.Add(nameof(gemsText));

            if (missing.Count == 0) return true;

            Debug.LogError($"{nameof(GemsView)} on '{name}' is missing Inspector reference(s): {string.Join(", ", missing)}.", this);
            return false;
        }
    }
}
