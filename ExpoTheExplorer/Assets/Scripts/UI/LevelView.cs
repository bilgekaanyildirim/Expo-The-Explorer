using System.Collections.Generic;
using ExpoTheExplorer.Bootstrap;
using ExpoTheExplorer.Core;
using TMPro;
using UnityEngine;

namespace ExpoTheExplorer.UI
{
    // Top HUD readout for GameState.Level. Reactive, not polled: binds to
    // GameState.LevelChanged (fired by the property setter on every actual
    // change) instead of re-stringifying every frame -- same pattern as
    // GemsView/SoftMoneyView/LivesView.
    public class LevelView : MonoBehaviour
    {
        [SerializeField] private GameManager gameManager;
        [SerializeField] private TMP_Text levelText;

        private GameState state;

        private void Start()
        {
            if (!ValidateReferences()) return;

            state = gameManager.State;
            state.LevelChanged.Subscribe(Refresh);
            Refresh(state.Level);
        }

        private void OnDestroy()
        {
            state?.LevelChanged.Unsubscribe(Refresh);
        }

        private void Refresh(int level)
        {
            levelText.text = $"Lv. {level}";
        }

        // Every field here is wired by hand in the Editor -- a missing one
        // should fail loudly with a clear pointer to which field, not a bare
        // NullReferenceException.
        private bool ValidateReferences()
        {
            var missing = new List<string>();
            if (gameManager == null) missing.Add(nameof(gameManager));
            if (levelText == null) missing.Add(nameof(levelText));

            if (missing.Count == 0) return true;

            Debug.LogError($"{nameof(LevelView)} on '{name}' is missing Inspector reference(s): {string.Join(", ", missing)}.", this);
            return false;
        }
    }
}
