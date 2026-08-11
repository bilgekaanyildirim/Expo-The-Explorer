using System.Collections.Generic;
using ExpoTheExplorer.Bootstrap;
using ExpoTheExplorer.Core;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace ExpoTheExplorer.UI
{
    // Shown once GameState.DayCompleted fires (day's ticket goal reached).
    // Breaks the day's earnings down the way the mockup's receipt does:
    // "Orders delivered" = the day's summed BaseTip (the guaranteed per-item
    // value), "Tips" = the summed speed/patience multiplier bonus on top of
    // that, "Total" = both combined, which already equals the SoftMoney the
    // player gained today. Stars compare Total against the current Day's
    // authored thresholds. Go Back has nowhere to navigate yet (single-scene
    // project) and stays non-interactable, mirroring GameOverPopupView's
    // disabled Main Menu button. Same hand-built-in-Editor, SetActive-toggled
    // pattern as GameOverPopupView -- this script never instantiates UI.
    public class DayCompletePopupView : MonoBehaviour
    {
        [SerializeField] private GameManager gameManager;
        [SerializeField] private GameObject popupRoot;
        [SerializeField] private TMP_Text ordersDeliveredCountText;
        [SerializeField] private TMP_Text ordersDeliveredValueText;
        [SerializeField] private TMP_Text tipsValueText;
        [SerializeField] private TMP_Text ordersFailedCountText;
        [SerializeField] private TMP_Text ordersFailedValueText;
        [SerializeField] private TMP_Text totalText;
        [SerializeField] private GameObject star1Filled;
        [SerializeField] private GameObject star2Filled;
        [SerializeField] private GameObject star3Filled;
        [SerializeField] private Button nextDayButton;
        [SerializeField] private Button retryButton;
        [SerializeField] private Button goBackButton;

        private GameState state;

        private void Start()
        {
            if (!ValidateReferences()) return;

            popupRoot.SetActive(false);

            state = gameManager.State;
            state.DayCompleted.Subscribe(Show);

            nextDayButton.onClick.AddListener(OnNextDayClicked);
            retryButton.onClick.AddListener(OnRetryClicked);

            // TODO: wire once a menu/day-select scene exists.
            goBackButton.interactable = false;
        }

        private void OnDestroy()
        {
            if (state != null) state.DayCompleted.Unsubscribe(Show);

            nextDayButton.onClick.RemoveListener(OnNextDayClicked);
            retryButton.onClick.RemoveListener(OnRetryClicked);
        }

        private void Show(int _)
        {
            var summary = gameManager.DayLifecycleManager;
            var thresholds = gameManager.CurrentDayStarThresholds;

            ordersDeliveredCountText.text = summary.OrdersDeliveredCount.ToString();
            ordersDeliveredValueText.text = summary.OrdersDeliveredValue.ToString();
            tipsValueText.text = summary.TipsValue.ToString();
            ordersFailedCountText.text = summary.OrdersFailedCount.ToString();
            ordersFailedValueText.text = "0"; // no failure-penalty formula in the GDD yet
            totalText.text = summary.Total.ToString();

            star1Filled.SetActive(summary.Total >= thresholds.Star1);
            star2Filled.SetActive(summary.Total >= thresholds.Star2);
            star3Filled.SetActive(summary.Total >= thresholds.Star3);

            popupRoot.SetActive(true);
        }

        private void Hide()
        {
            popupRoot.SetActive(false);
        }

        private void OnNextDayClicked()
        {
            gameManager.AdvanceToNextDay();
            Hide();
        }

        // Always interactable, even at 3 stars -- a player can want a redo
        // regardless of score. GameManager.RetryCompletedDay handles rolling
        // this attempt's SoftMoney/Xp/Level gains back off before replaying.
        private void OnRetryClicked()
        {
            gameManager.RetryCompletedDay();
            Hide();
        }

        // Every field here is wired by hand in the Editor -- a missing one
        // should fail loudly with a clear pointer to which field, not a bare
        // NullReferenceException.
        private bool ValidateReferences()
        {
            var missing = new List<string>();
            if (gameManager == null) missing.Add(nameof(gameManager));
            if (popupRoot == null) missing.Add(nameof(popupRoot));
            if (ordersDeliveredCountText == null) missing.Add(nameof(ordersDeliveredCountText));
            if (ordersDeliveredValueText == null) missing.Add(nameof(ordersDeliveredValueText));
            if (tipsValueText == null) missing.Add(nameof(tipsValueText));
            if (ordersFailedCountText == null) missing.Add(nameof(ordersFailedCountText));
            if (ordersFailedValueText == null) missing.Add(nameof(ordersFailedValueText));
            if (totalText == null) missing.Add(nameof(totalText));
            if (star1Filled == null) missing.Add(nameof(star1Filled));
            if (star2Filled == null) missing.Add(nameof(star2Filled));
            if (star3Filled == null) missing.Add(nameof(star3Filled));
            if (nextDayButton == null) missing.Add(nameof(nextDayButton));
            if (retryButton == null) missing.Add(nameof(retryButton));
            if (goBackButton == null) missing.Add(nameof(goBackButton));

            if (missing.Count == 0) return true;

            Debug.LogError($"{nameof(DayCompletePopupView)} on '{name}' is missing Inspector reference(s): {string.Join(", ", missing)}.", this);
            return false;
        }
    }
}
