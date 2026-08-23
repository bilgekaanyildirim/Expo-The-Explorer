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
    // "Orders delivered" = the day's summed Order Value (the guaranteed food
    // price of everything delivered), "Tips" = the summed tip on top of that,
    // "Total" = both combined, which already equals the SoftMoney the player
    // gained today. Stars are one per life still held and are computed in
    // DayLifecycleManager.StarCount (decisions.md D-008) -- the per-Day authored
    // thresholds this class once compared Total against are gone.
    //
    // Its three buttons are the whole set of exits from a finished day: Next Day
    // plays on, Retry redoes this one for a better star count, and Go Back leaves
    // for the main screen while persisting the NEXT day, so Play there picks up
    // where this popup left off (decisions.md D-012). Same hand-built-in-Editor,
    // SetActive-toggled pattern as GameOverPopupView -- this script never
    // instantiates UI.
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
            goBackButton.onClick.AddListener(OnGoBackClicked);
        }

        private void OnDestroy()
        {
            if (state != null) state.DayCompleted.Unsubscribe(Show);

            nextDayButton.onClick.RemoveListener(OnNextDayClicked);
            retryButton.onClick.RemoveListener(OnRetryClicked);
            goBackButton.onClick.RemoveListener(OnGoBackClicked);
        }

        private void Show(int _)
        {
            var summary = gameManager.DayLifecycleManager;

            ordersDeliveredCountText.text = summary.OrdersDeliveredCount.ToString();
            ordersDeliveredValueText.text = summary.OrdersDeliveredValue.ToString();
            tipsValueText.text = summary.TipsValue.ToString();
            ordersFailedCountText.text = summary.OrdersFailedCount.ToString();
            ordersFailedValueText.text = "0"; // no failure-penalty formula in the GDD yet
            totalText.text = summary.Total.ToString();

            // Just renders the count the day's bookkeeping already worked out -- the rule
            // behind it lives in DayLifecycleManager.StarCount, where it can be tested.
            var stars = summary.StarCount;
            star1Filled.SetActive(stars >= 1);
            star2Filled.SetActive(stars >= 2);
            star3Filled.SetActive(stars >= 3);

            popupRoot.SetActive(true);
        }

        private void Hide()
        {
            popupRoot.SetActive(false);
        }

        // Asks for the next day and hides itself only if there still IS a day scene to hide
        // in. Both reasons to leave -- no next Day authored, or the next day opening a
        // Day-unlocked prop the player should be shown (decisions.md D-042) -- are decided by
        // GameManager, which owns flow. This view used to branch on the first of them; adding
        // the second here would have made a popup the place where scene routing is decided.
        private void OnNextDayClicked()
        {
            if (gameManager.TryContinueIntoNextDay()) Hide();
        }

        // Always interactable, even at 3 stars -- a player can want a redo
        // regardless of score. GameManager.RetryCompletedDay handles rolling
        // this attempt's SoftMoney gains back off before replaying.
        private void OnRetryClicked()
        {
            gameManager.RetryCompletedDay();
            Hide();
        }

        // No Hide() and no teardown of our own: the scene load destroys this whole
        // scene, popup included. GameManager owns what has to survive it (the next
        // day's index, written to the profile).
        private void OnGoBackClicked()
        {
            gameManager.ReturnToMainScreenFromCompletedDay();
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
