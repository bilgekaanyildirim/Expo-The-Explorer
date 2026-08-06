using System.Collections.Generic;
using ExpoTheExplorer.Bootstrap;
using ExpoTheExplorer.Core;
using UnityEngine;
using UnityEngine.UI;

namespace ExpoTheExplorer.UI
{
    // First fillAmount-based bar in this project -- Image (Type: Filled), not
    // Slider, since this is read-only display, not interactive input.
    public class XpBarView : MonoBehaviour
    {
        [SerializeField] private GameManager gameManager;
        [SerializeField] private Image fillImage;

        private GameState state;

        private void Start()
        {
            if (!ValidateReferences()) return;

            state = gameManager.State;
            // Both XP gains AND level-ups change the ratio (leveling up
            // changes the denominator -- the next level's threshold), so
            // both events need to trigger a refresh.
            state.XpChanged.Subscribe(OnXpChanged);
            state.LevelChanged.Subscribe(OnLevelChanged);
            Refresh();
        }

        private void OnDestroy()
        {
            state?.XpChanged.Unsubscribe(OnXpChanged);
            state?.LevelChanged.Unsubscribe(OnLevelChanged);
        }

        private void OnXpChanged(int _) => Refresh();
        private void OnLevelChanged(int _) => Refresh();

        private void Refresh()
        {
            fillImage.fillAmount = gameManager.LevelManager.GetXpProgressRatio();
        }

        private bool ValidateReferences()
        {
            var missing = new List<string>();
            if (gameManager == null) missing.Add(nameof(gameManager));
            if (fillImage == null) missing.Add(nameof(fillImage));

            if (missing.Count == 0) return true;

            Debug.LogError($"{nameof(XpBarView)} on '{name}' is missing Inspector reference(s): {string.Join(", ", missing)}.", this);
            return false;
        }
    }
}
