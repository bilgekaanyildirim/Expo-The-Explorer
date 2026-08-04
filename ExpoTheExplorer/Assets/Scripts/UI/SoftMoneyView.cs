using System.Collections.Generic;
using ExpoTheExplorer.Bootstrap;
using ExpoTheExplorer.Core;
using TMPro;
using UnityEngine;

namespace ExpoTheExplorer.UI
{
    // Top HUD readout for GameState.SoftMoney (GDD Section 10 — earned per
    // delivery). Reactive, not polled: binds to GameState.SoftMoneyChanged
    // (fired by the property setter on every actual change, e.g.
    // GameManager's tip payout) instead of re-stringifying every frame.
    public class SoftMoneyView : MonoBehaviour
    {
        [SerializeField] private GameManager gameManager;
        [SerializeField] private TMP_Text softMoneyText;

        private GameState state;

        // Subscribes from Start(), not Awake() -- Unity's Awake() order across
        // different GameObjects is unspecified, and GameManager.Awake (which
        // sets State) may not have run yet, throwing a NullReferenceException
        // on gameManager.State. Start() is always safe: Unity runs every
        // object's Awake() before any object's Start() in a given frame (same
        // reason BoardView only ever touches gameManager.State from Start).
        private void Start()
        {
            if (!ValidateReferences()) return;

            state = gameManager.State;
            state.SoftMoneyChanged.Subscribe(Refresh);
            Refresh(state.SoftMoney);
        }

        private void OnDestroy()
        {
            state?.SoftMoneyChanged.Unsubscribe(Refresh);
        }

        private void Refresh(int softMoney)
        {
            softMoneyText.text = softMoney.ToString();
        }

        // Every field here is wired by hand in the Editor — a missing one
        // should fail loudly with a clear pointer to which field, not a bare
        // NullReferenceException.
        private bool ValidateReferences()
        {
            var missing = new List<string>();
            if (gameManager == null) missing.Add(nameof(gameManager));
            if (softMoneyText == null) missing.Add(nameof(softMoneyText));

            if (missing.Count == 0) return true;

            Debug.LogError($"{nameof(SoftMoneyView)} on '{name}' is missing Inspector reference(s): {string.Join(", ", missing)}.", this);
            return false;
        }
    }
}
