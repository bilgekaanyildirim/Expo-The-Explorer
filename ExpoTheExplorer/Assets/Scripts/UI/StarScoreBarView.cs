using DG.Tweening;
using UnityEngine;
using UnityEngine.UI;

namespace ExpoTheExplorer.UI
{
    // The Day Complete star score as a fill bar (decisions.md D-061), and the CLOCK the star
    // row runs on: DayRewardFlightView asks it "has the fill reached star 2 yet?" instead of
    // counting fixed intervals, which is what makes "my speed earned that star" legible
    // rather than two animations that merely happen at the same time.
    //
    // It does NOT move the stars. D-061 also had it position them, standing each one on its
    // own threshold along the bar; the user removed that (D-062) -- a star's position is the
    // scene's business now, and nothing in the game writes one. What survived is the half
    // that carries the meaning without moving anything: the fill's travel, and the moment
    // each star is due.
    //
    // The bar is scaled so FULL means three stars, not 100% of the score -- see
    // DayLifecycleManager.ScoreProgressToMaxStars. Star one is due at the very start, because
    // it is the reward for FINISHING the day rather than for any speed.
    //
    // Holds no art decisions. Background and fill are whatever sprites the scene gives it;
    // the only requirement is that the fill Image is set to Filled / Horizontal / Origin
    // Left, since fillAmount is the single value this class animates.
    public class StarScoreBarView : MonoBehaviour
    {
        [Tooltip("The Image that fills. Image Type must be Filled, Fill Method Horizontal, Fill Origin Left — fillAmount is the only thing this class animates.")]
        [SerializeField] private Image fill;

        [Header("Animation")]
        [Tooltip("How long the fill takes to travel from empty to the day's score. The stars pop as it passes them, so this number sets the whole rhythm of the star row.")]
        [SerializeField, Min(0f)] private float fillDuration = 0.84f;

        [Tooltip("Ease of the fill. Linear keeps 'the bar reached my star' honest; an ease-out makes a high score feel like it is straining toward the last star.")]
        [SerializeField] private Ease fillEase = Ease.OutQuad;

        private float targetFill;
        private float twoStarPosition = 1f;
        private Tween fillTween;

        // Whether this day's travel has actually started. It exists for one ordering case:
        // the popup calls Prepare and THEN starts the flight, whose first act is to finish
        // any previous performance -- which would otherwise snap this bar to the new target
        // for one frame before the fill it just reset began. A bar that has not begun has
        // nothing to snap to.
        private bool fillStarted;

        // 1 rather than 0 when there is no fill Image: this value gates the flight's wait,
        // and an unwired bar must let the stars through immediately rather than stall a
        // sequence that is holding the day's payout.
        public float CurrentFill => fill == null ? 1f : fill.fillAmount;

        public bool IsFillFinished => fillTween == null || !fillTween.IsActive();

        // How far along the bar star `index` becomes due, 0 at the start and 1 at full.
        // Since D-062 this is a moment in the fill's travel and nothing else -- it used to be
        // a place on screen as well. Out-of-range reads as full, so a caller that somehow
        // asks for a fourth star waits for the fill to finish instead of being handed a free
        // pass at 0.
        public float PositionOf(int starIndex)
        {
            if (starIndex <= 0) return 0f;
            if (starIndex == 1) return twoStarPosition;
            return 1f;
        }

        // Called once per day completion, BEFORE anything animates: takes this day's figures
        // and empties the fill. Kills a live tween first, because a player who redoes a
        // completed day gets here with the previous performance still running.
        public void Prepare(float scoreProgress, float twoStarDuePosition)
        {
            KillFill();

            fillStarted = false;
            twoStarPosition = Mathf.Clamp01(twoStarDuePosition);
            targetFill = Mathf.Clamp01(scoreProgress);

            if (fill != null) fill.fillAmount = 0f;
        }

        // Starts the travel. Separate from Prepare because the flight owns WHEN the
        // performance begins, while the popup owns what the numbers are.
        public void BeginFill()
        {
            if (fill == null) return;

            KillFill();
            fillStarted = true;

            if (fillDuration <= 0f)
            {
                fill.fillAmount = targetFill;
                return;
            }

            fill.fillAmount = 0f;
            fillTween = fill.DOFillAmount(targetFill, fillDuration).SetEase(fillEase);
        }

        // The score is shown in full, now. Called when the player taps through the payout,
        // so they still see what they scored.
        public void SnapToTarget()
        {
            KillFill();

            if (!fillStarted || fill == null) return;
            fill.fillAmount = targetFill;
        }

        // The score, shown without ever travelling. Separate from SnapToTarget because it
        // deliberately ignores the "has it started" guard: this is the path where the
        // performance cannot happen at all (a broken flight reference), and the player must
        // still see their score -- where SnapToTarget is the path where a performance is
        // being CUT SHORT, and one that never began has nothing to cut.
        public void ShowTargetWithoutAnimating()
        {
            KillFill();

            if (fill == null) return;
            fillStarted = true;
            fill.fillAmount = targetFill;
        }

        private void OnDestroy()
        {
            KillFill();
        }

        private void KillFill()
        {
            if (fillTween == null) return;

            if (fillTween.IsActive()) fillTween.Kill();
            fillTween = null;
        }
    }
}
