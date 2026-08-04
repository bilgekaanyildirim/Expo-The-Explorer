using System.Collections.Generic;
using ExpoTheExplorer.Bootstrap;
using TMPro;
using UnityEngine;

namespace ExpoTheExplorer.UI
{
    // Top HUD readout for GameState.Lives (GDD Section 3/6 — Lives System).
    // Polls every frame and always rewrites the text, same poll-without-diffing
    // idiom as SoftMoneyView: Lives has no dedicated EventBus (LivesDepleted
    // only fires once, the instant it hits 0 — not on every decrement), and
    // restringifying one int every frame is cheap enough that adding a
    // change-notification API for it isn't worth the extra moving part.
    public class LivesView : MonoBehaviour
    {
        [SerializeField] private GameManager gameManager;
        [SerializeField] private TMP_Text livesText;

        private bool isValid;

        // Deliberately does NOT call Refresh() here -- Unity's Awake() order
        // across different GameObjects is unspecified, and GameManager.Awake
        // (which sets State) may not have run yet, throwing a
        // NullReferenceException on gameManager.State. Update() is always
        // safe: Unity runs every object's Awake() before any object's
        // Update() in a given frame, so GameManager.State is guaranteed set
        // by then (same reason SoftMoneyView only ever touches
        // gameManager.State from Update, never from Awake/Initialize).
        private void Awake()
        {
            isValid = ValidateReferences();
        }

        private void Update()
        {
            if (isValid) Refresh();
        }

        private void Refresh()
        {
            livesText.text = gameManager.State.Lives.ToString();
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
