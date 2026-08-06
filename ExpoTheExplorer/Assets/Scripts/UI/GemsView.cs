using System.Collections.Generic;
using ExpoTheExplorer.Bootstrap;
using ExpoTheExplorer.Core;
using TMPro;
using UnityEngine;

namespace ExpoTheExplorer.UI
{
    // Top HUD readout for GameState.Gems (GDD Section 10 — hard currency).
    // Reactive, not polled: binds to GameState.GemsChanged (fired by the
    // property setter on every actual change, e.g. LivesManager.TryContinueWithGems
    // spending Gems) instead of re-stringifying every frame.
    public class GemsView : MonoBehaviour
    {
        [SerializeField] private GameManager gameManager;
        [SerializeField] private TMP_Text gemsText;

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
            state.GemsChanged.Subscribe(Refresh);
            Refresh(state.Gems);
        }

        private void OnDestroy()
        {
            state?.GemsChanged.Unsubscribe(Refresh);
        }

        private void Refresh(int gems)
        {
            gemsText.text = gems.ToString();
        }

        // Every field here is wired by hand in the Editor — a missing one
        // should fail loudly with a clear pointer to which field, not a bare
        // NullReferenceException.
        private bool ValidateReferences()
        {
            var missing = new List<string>();
            if (gameManager == null) missing.Add(nameof(gameManager));
            if (gemsText == null) missing.Add(nameof(gemsText));

            if (missing.Count == 0) return true;

            Debug.LogError($"{nameof(GemsView)} on '{name}' is missing Inspector reference(s): {string.Join(", ", missing)}.", this);
            return false;
        }
    }
}
