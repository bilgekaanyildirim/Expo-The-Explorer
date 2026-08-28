using System.Collections.Generic;
using TMPro;
using UnityEngine;

namespace ExpoTheExplorer.UI
{
    // The HUD's day badge (decisions.md D-130): which authored Day the player is on,
    // shown on both screens because the badge is content of the shared HUD prefab.
    //
    // It reads through HudWalletSource rather than holding a session reference of its own,
    // for the reason KeysView does and LivesView does not (D-064/D-065): a prefab asset
    // cannot store a scene reference at all, and HudWalletSource's sessionHost field is
    // already the one per-scene override that solves it. A second such override on this
    // view would be a second thing an "Apply All" could quietly drop.
    //
    // ONLY THE NUMBER is written here. The word beside it ("Day", "Gun") is a label in the
    // prefab, the same split SettingsPopupView makes -- writing it here would put authored
    // text in code, which the root CLAUDE.md forbids outright.
    public class DayNumberView : MonoBehaviour
    {
        [SerializeField] private HudWalletSource walletSource;
        [SerializeField] private TMP_Text dayText;

        // Start(), not Awake(): the session is built in its host's Awake and Unity does not
        // order Awake across GameObjects. HudWalletSource resolves lazily for exactly that
        // reason, so asking it anything from here is what keeps the guarantee -- the rule
        // every other HUD view in this project follows.
        private void Start()
        {
            if (!ValidateReferences()) return;

            // One subscription and one render, not a per-frame re-stringify. The value
            // changes once per day and GameState's setter publishes only on an actual
            // change, so there is nothing for an Update to catch that this misses.
            //
            // On the main screen the event never fires, which is correct rather than a
            // gap: the day cannot advance while the player is standing on the menu, and
            // the single render at Start already shows the day Play would open.
            walletSource.CurrentDayIndexChanged.Subscribe(Refresh);
            Refresh(walletSource.CurrentDayIndex);
        }

        private void OnDestroy()
        {
            if (walletSource != null) walletSource.CurrentDayIndexChanged.Unsubscribe(Refresh);
        }

        // +1 because CurrentDayIndex is a catalog position and the player counts from one
        // -- the same conversion MainScreenView makes for its "Continue Day X" caption and
        // SettingsPopupView for its pause-menu readout. The conversion lives in the views
        // on purpose: the index stays one number everywhere it is stored or compared.
        private void Refresh(int currentDayIndex)
        {
            dayText.text = (currentDayIndex + 1).ToString();
        }

        // Both fields are wired inside the PREFAB (walletSource points at the canvas root
        // above it, dayText at the label below it), so they are prefab data and need no
        // per-scene override -- a missing one should still fail loudly with a pointer to
        // which field, not leave a badge showing whatever the prefab was authored with.
        private bool ValidateReferences()
        {
            var missing = new List<string>();
            if (walletSource == null) missing.Add(nameof(walletSource));
            if (dayText == null) missing.Add(nameof(dayText));

            if (missing.Count == 0) return true;

            Debug.LogError($"{nameof(DayNumberView)} on '{name}' is missing Inspector reference(s): {string.Join(", ", missing)}.", this);
            return false;
        }
    }
}
