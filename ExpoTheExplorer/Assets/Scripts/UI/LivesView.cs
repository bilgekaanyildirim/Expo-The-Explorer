using System.Collections.Generic;
using ExpoTheExplorer.Bootstrap;
using ExpoTheExplorer.Core;
using TMPro;
using UnityEngine;

namespace ExpoTheExplorer.UI
{
    // Top HUD readout for GameState.Lives, shown as "current/max" (e.g. "3/3").
    // Reactive, not polled: binds to LivesChanged and MaxLivesChanged (both
    // fired by their property setters on every actual change -- LoseLife only
    // moves Lives, TryContinue can move both) instead of re-stringifying every
    // frame. Distinct from LivesDepleted, which only fires once, the instant
    // Lives hits 0.
    public class LivesView : MonoBehaviour
    {
        [SerializeField] private GameManager gameManager;
        [SerializeField] private TMP_Text livesText;

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
            state.LivesChanged.Subscribe(Refresh);
            state.MaxLivesChanged.Subscribe(Refresh);
            Refresh(state.Lives);
        }

        private void OnDestroy()
        {
            if (state == null) return;
            state.LivesChanged.Unsubscribe(Refresh);
            state.MaxLivesChanged.Unsubscribe(Refresh);
        }

        // Payload is ignored -- whichever half changed, the label always
        // needs both current values to re-render "current/max".
        private void Refresh(int _)
        {
            livesText.text = $"{state.Lives}/{state.MaxLives}";
        }

        // Every field here is wired by hand in the Editor — a missing one
        // should fail loudly with a clear pointer to which field, not a bare
        // NullReferenceException.
        private bool ValidateReferences()
        {
            var missing = new List<string>();
            if (gameManager == null) missing.Add(nameof(gameManager));
            if (livesText == null) missing.Add(nameof(livesText));

            if (missing.Count == 0) return true;

            Debug.LogError($"{nameof(LivesView)} on '{name}' is missing Inspector reference(s): {string.Join(", ", missing)}.", this);
            return false;
        }
    }
}
