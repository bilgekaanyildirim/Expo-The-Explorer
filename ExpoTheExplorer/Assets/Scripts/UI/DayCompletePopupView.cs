using System.Collections.Generic;
using DG.Tweening;
using ExpoTheExplorer.Bootstrap;
using ExpoTheExplorer.Core;
using ExpoTheExplorer.Data;
using ExpoTheExplorer.Systems.DayLifecycle;
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
    // gained today. Stars are computed in DayLifecycleManager.StarCount -- the per-Day
    // authored thresholds this class once compared Total against are gone.
    //
    // Since D-060 the rating is a SCORE, not a count of surviving lives: the seconds the
    // player handed back across the day, as a fraction of the day's whole ticket clock,
    // minus what their mistakes cost. So the receipt shows that arithmetic too (see
    // WriteStarScoreBreakdown) -- a star lost to a slow day and a star lost to a wrong
    // order are different information, and a bare star count cannot tell them apart.
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

        // The visual half of the same three numbers (decisions.md D-061): the score as a
        // bar the three stars stand on. Optional for the same reason the rows above are --
        // a scene that has not built it yet keeps the old fixed-interval star seating.
        //
        // This view only hands it the two numbers; the FLIGHT drives it, because the bar's
        // fill is what paces the seating and the flight owns that sequence.
        [Tooltip("Optional. The score bar the three stars stand on. Leave it empty and the stars seat on their old fixed rhythm instead.")]
        [SerializeField] private StarScoreBarView starScoreBar;
        [SerializeField] private Button nextDayButton;
        [SerializeField] private Button retryButton;
        [SerializeField] private Button goBackButton;

        [Tooltip("Performs the payout: seats the stars, then flies gems and coins to the HUD counters. It is what actually credits the wallet (D-057), so this popup no longer just reports the day's earnings — it hands them over.")]
        [SerializeField] private DayRewardFlightView rewardFlight;

        // OPTIONAL, and deliberately outside ValidateReferences, for the reason spelled
        // out at rewardFlight below: a slot left empty must never be able to suppress
        // this popup, because the popup is the only way out of a finished day. Unwired,
        // it appears the instant the day completes, exactly as it did before D-101.
        [Tooltip("Optional. Read for the delay this popup waits before appearing, so the last delivery's own lift and fade is seen first. Leave it empty and the popup appears instantly.")]
        [SerializeField] private BoardAnimationConfig animConfig;

        // OPTIONAL, and not in ValidateReferences at all -- not even reported, unlike
        // rewardFlight. The flight is the payout wearing a costume, so an author who leaves it
        // empty has lost something; this is decoration on a receipt that reads perfectly well
        // without it, and an error in the console for a missing party trick trains people to
        // ignore the console.
        [Tooltip("Optional. Assets/Prefabs/UI/CelebrationConfetti.prefab — two cannons fired the moment the receipt appears. Built by ExpoTheExplorer > Celebration > Build Confetti.")]
        [SerializeField] private ConfettiView confettiPrefab;

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

        // THE POPUP IS DELAYED, THE DAY IS NOT (D-101). TicketSlotManager sets
        // IsDayComplete before it publishes this, so for the whole wait no ticket is
        // assigned, the clock counts nothing down, and every slot is empty — which means
        // TrayManager refuses every drop too. What IS still running is the delivery that
        // just finished the day: the item settling into its slot (D-100), the tray
        // growing, lifting and fading, the ticket card sliding away, the tray growing
        // back in. This popup used to land on top of all of it.
        //
        // The payout starts when the popup does, one wait later than before. That moves
        // nothing that matters: the money was never credited during the day and every
        // exit commits the purse regardless (D-057).
        //
        // SetLink to this GameObject so a scene torn down mid-wait cannot raise it.
        private void Show(int _)
        {
            if (animConfig == null || animConfig.DayCompletePopupDelay <= 0f)
            {
                ShowNow();
                return;
            }

            DOVirtual.DelayedCall(animConfig.DayCompletePopupDelay, ShowNow).SetLink(gameObject);
        }

        // The receipt is read when the popup appears rather than when the day completed.
        // Nothing can change those figures during the wait — the day is over and the
        // clock is stopped — but reading them late is the honest order and costs nothing.
        private void ShowNow()
        {
            var summary = gameManager.DayLifecycleManager;

            ordersDeliveredCountText.text = summary.OrdersDeliveredCount.ToString();
            ordersDeliveredValueText.text = summary.OrdersDeliveredValue.ToString();
            tipsValueText.text = summary.TipsValue.ToString();
            totalText.text = summary.Total.ToString();

            // Before the popup goes live, so the markers are already where this day's
            // thresholds put them and nothing is seen sliding into place. Anchors resolve on
            // activation, so an inactive hierarchy is the right time to write them -- unlike
            // the flight's own measurements, which need the layout to exist first.
            if (starScoreBar != null)
            {
                starScoreBar.Prepare(summary.ScoreProgressToMaxStars, summary.TwoStarProgressPosition);
            }

            popupRoot.SetActive(true);

            // The receipt fades up rather than snapping in. It arrives after a deliberate
            // wait (DayCompletePopupDelay above) with the last delivery still settling
            // behind it, and a hard cut there reads as an interruption of the thing the
            // wait exists to let you watch.
            if (animConfig != null) PopupFade.In(popupRoot, animConfig.PopupFadeInDuration);

            // UNDER THIS POPUP AND OVER EVERYTHING ELSE, which is the user's requirement and
            // the reason this is BurstBelow rather than BurstOnTop: the receipt has to stay
            // readable, and the rest of the day scene is what the paper is celebrating over.
            // BurstBelow does that by sibling index, so it survives the popup being moved.
            //
            // AFTER SetActive, not before: it reads popupRoot's place in the canvas, and an
            // inactive branch is skipped by the parent lookup that finds the canvas.
            //
            // Fired and forgotten. Nothing below waits on it, and every exit from this popup
            // destroys the scene or the burst with it -- so tapping straight through the
            // payout cannot leave paper hanging in the air.
            ConfettiView.BurstBelow(confettiPrefab, popupRoot.transform as RectTransform);

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

            var earned = EarnedStarObjects(summary.StarCount);

            // No flight wired is a wiring MISTAKE, not a mode -- but it must not cost the
            // player the whole popup. D-057's own rule is that the money is safe without
            // this object (every exit commits the purse through GameManager), so the
            // fallback is to show the score plainly and let the exits pay. Before this, a
            // single empty slot made ValidateReferences fail, which returned out of Start,
            // which meant the popup never subscribed to DayCompleted at all -- so finishing
            // a day showed NOTHING, and the one thing on screen that could have explained
            // it was the popup that failed to appear.
            if (rewardFlight == null)
            {
                foreach (var star in earned) star.SetActive(true);
                if (starScoreBar != null) starScoreBar.ShowTargetWithoutAnimating();
                return;
            }

            rewardFlight.Play(earned, totalText.rectTransform);
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

            // rewardFlight is deliberately NOT in this list any more. It used to be, and
            // that turned one empty slot into "finishing a day shows nothing at all": this
            // method returning false returns out of Start, and Start is where the popup
            // subscribes to DayCompleted. Everything left in the list above is something the
            // popup cannot render a receipt without; the flight is a performance, and Show
            // already falls back to seating the stars plainly when it is missing. It is
            // still reported, just not fatally.
            if (rewardFlight == null)
            {
                Debug.LogError(
                    $"{nameof(DayCompletePopupView)} on '{name}': {nameof(rewardFlight)} is not wired, so the day's " +
                    "reward will not animate. The receipt still shows and the exits still pay out in full.", this);
            }

            if (missing.Count == 0) return true;

            Debug.LogError($"{nameof(DayCompletePopupView)} on '{name}' is missing Inspector reference(s): {string.Join(", ", missing)}.", this);
            return false;
        }
    }
}
