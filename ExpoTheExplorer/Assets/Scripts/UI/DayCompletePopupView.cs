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
    // Since D-057 the receipt is not just a report: nothing is credited during a day, so
    // this popup is where the day actually PAYS. The figures above are written straight
    // out, but the stars and the money are handed to DayRewardFlightView, which seats the
    // stars one at a time and flies gems and coins to the HUD counters -- and each icon
    // landing is the moment its share is credited. Hence CompleteRewardFlight on every
    // exit: tapping through the payout has to be a skip, never a forfeit.
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
        [SerializeField] private TMP_Text totalText;
        [SerializeField] private GameObject star1Filled;
        [SerializeField] private GameObject star2Filled;
        [SerializeField] private GameObject star3Filled;
        [SerializeField] private Button nextDayButton;
        [SerializeField] private Button retryButton;
        [SerializeField] private Button goBackButton;

        [Tooltip("Performs the payout: seats the stars, then flies gems and coins to the HUD counters. It is what actually credits the wallet (D-057), so this popup no longer just reports the day's earnings — it hands them over.")]
        [SerializeField] private DayRewardFlightView rewardFlight;

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
            totalText.text = summary.Total.ToString();

            popupRoot.SetActive(true);

            // The stars are no longer switched on here. Since D-057 they are SEATED, one
            // at a time, by the reward flight -- and each one that lands releases a gem
            // toward the HUD, which is the moment that gem is actually credited. So this
            // hands over the earned ones and the popup's job ends: the count behind it is
            // still DayLifecycleManager.StarCount, where the rule can be tested.
            //
            // Activated AFTER popupRoot, or the flight would measure positions on a
            // hierarchy that is still inactive and put every icon at the origin.
            star1Filled.SetActive(false);
            star2Filled.SetActive(false);
            star3Filled.SetActive(false);

            rewardFlight.Play(EarnedStarObjects(summary.StarCount), totalText.rectTransform);
        }

        // The earned stars in seating order, and nothing else -- the flight seats exactly
        // what it is given, so an unearned star is absent from the list rather than being
        // passed with a flag telling the animator to skip it.
        private IReadOnlyList<GameObject> EarnedStarObjects(int stars)
        {
            var earned = new List<GameObject>();
            if (stars >= 1) earned.Add(star1Filled);
            if (stars >= 2) earned.Add(star2Filled);
            if (stars >= 3) earned.Add(star3Filled);

            return earned;
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
            CompleteRewardFlight();
            if (gameManager.TryContinueIntoNextDay()) Hide();
        }

        // Always interactable, even at 3 stars -- a player can want a redo
        // regardless of score. GameManager.RetryCompletedDay handles rolling
        // this attempt's SoftMoney gains back off before replaying.
        private void OnRetryClicked()
        {
            CompleteRewardFlight();
            gameManager.RetryCompletedDay();
            Hide();
        }

        // No Hide() and no teardown of our own: the scene load destroys this whole
        // scene, popup included. GameManager owns what has to survive it (the next
        // day's index, written to the profile).
        private void OnGoBackClicked()
        {
            CompleteRewardFlight();
            gameManager.ReturnToMainScreenFromCompletedDay();
        }

        // Every exit calls this FIRST, which is what makes tapping through the payout a
        // skip rather than a forfeit: the flight stops, its icons go, and everything it
        // had not handed over yet is credited in one lump. Safe to call when the sequence
        // has already finished or never started -- it is a no-op unless something is
        // actually in flight.
        //
        // It is not the only thing standing between the player and a lost reward, just
        // the tidiest: GameManager commits on the exits themselves too, so even a popup
        // whose rewardFlight was never wired pays out in full.
        private void CompleteRewardFlight()
        {
            if (rewardFlight != null) rewardFlight.CompleteImmediately();
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
            if (totalText == null) missing.Add(nameof(totalText));
            if (star1Filled == null) missing.Add(nameof(star1Filled));
            if (star2Filled == null) missing.Add(nameof(star2Filled));
            if (star3Filled == null) missing.Add(nameof(star3Filled));
            if (nextDayButton == null) missing.Add(nameof(nextDayButton));
            if (retryButton == null) missing.Add(nameof(retryButton));
            if (goBackButton == null) missing.Add(nameof(goBackButton));
            if (rewardFlight == null) missing.Add(nameof(rewardFlight));

            if (missing.Count == 0) return true;

            Debug.LogError($"{nameof(DayCompletePopupView)} on '{name}' is missing Inspector reference(s): {string.Join(", ", missing)}.", this);
            return false;
        }
    }
}
