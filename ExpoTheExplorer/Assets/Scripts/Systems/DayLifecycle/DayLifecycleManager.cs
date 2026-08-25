using ExpoTheExplorer.Core;
using ExpoTheExplorer.Data;
using ExpoTheExplorer.Systems.EconomySystem;
using UnityEngine;

namespace ExpoTheExplorer.Systems.DayLifecycle
{
    // Why an order was lost. The two causes exist as separate values for one reason:
    // they are not worth the same to the star score (decisions.md D-060). A timeout
    // has already forfeited its ticket's entire share of the day's clock by the time
    // it is recorded; a wrong delivery costs a shake and a scatter and almost no time
    // at all, so without a heavier explicit penalty it would be the cheaper way to
    // fail. The receipt's "Orders failed" line still adds them back together.
    public enum DayFailureCause
    {
        WrongDelivery,
        Timeout
    }

    // Delivery bookkeeping only -- "Day complete" itself is decided by
    // TicketSlotManager (bug: fixed 2026-08), not by a delivery-count goal
    // here. A timed-out ticket never increments this counter, so a goal-based
    // check would never fire once even one ticket was lost to a timeout.
    //
    // Since D-060 it also owns the STAR RULE, which is why it now takes a config: the
    // rule is code (so it can be tested here) and its four numbers are content (so they
    // sit on StarScoreConfig, per the root CLAUDE.md invariant).
    public class DayLifecycleManager
    {
        private readonly GameState state;
        private readonly StarScoreConfig config;

        private int totalOrderValueToday;
        private int totalTipToday;
        private int wrongDeliveriesToday;
        private int timeoutsToday;

        // The two halves of the time score. Both are seconds of TICKET clock, never wall
        // clock -- three slots run at once, so the two are not the same currency and must
        // never be compared (a day whose tickets total 390s is over in well under half
        // that). Measuring in ticket seconds is what makes the score immune to how many
        // slots happen to be busy, and it needs no per-Day calibration.
        private float savedSecondsToday;
        private float totalTicketSecondsToday;

        // A null config is a wiring bug, not a mode: StarCount reads 0 and GameManager
        // says so out loud in Awake. Deliberately no fallback to the old "3 minus
        // failures" rule -- a second star rule living in code is exactly what D-060
        // removed, and a quiet fallback would hide the unwired slot for a whole day.
        public DayLifecycleManager(GameState state, StarScoreConfig config)
        {
            this.state = state;
            this.config = config;
        }

        public int OrdersDeliveredCount => state.TicketsDeliveredToday;
        public int OrdersDeliveredValue => totalOrderValueToday;
        public int TipsValue => totalTipToday;
        public int OrdersFailedCount => wrongDeliveriesToday + timeoutsToday;
        public int WrongDeliveryCount => wrongDeliveriesToday;
        public int TimeoutCount => timeoutsToday;
        public int Total => totalOrderValueToday + totalTipToday;

        public float SavedSeconds => savedSecondsToday;
        public float TotalTicketSeconds => totalTicketSecondsToday;

        public const int MaxStars = 3;

        // The fraction of the day's whole ticket clock the player handed back by
        // delivering early -- 0.45 means "across the day, 45% of the patience the
        // customers granted went unused".
        //
        // A timed-out ticket contributes nothing to the numerator while its limit stays in
        // the denominator, so it costs its own full share here before any penalty is
        // applied. That is the mechanism, not a side effect: it is why the timeout penalty
        // below is the smaller of the two.
        //
        // A day with no budget (no Day loaded, or an unauthored one) reads 0 rather than
        // dividing by zero -- "scored nothing", which with the completion floor in
        // StarCount still pays the single star the day earned by being finished.
        public float TimeEfficiency =>
            totalTicketSecondsToday <= 0f ? 0f : Mathf.Clamp01(savedSecondsToday / totalTicketSecondsToday);

        // What today's mistakes cost, in the same 0..1 currency as TimeEfficiency. Not
        // clamped on its own: it is meant to be able to exceed the whole time score, which
        // is how a sloppy fast run lands at the completion floor instead of two stars.
        public float FailurePenalty =>
            config == null
                ? 0f
                : wrongDeliveriesToday * config.WrongDeliveryPenalty + timeoutsToday * config.TimeoutPenalty;

        // The single number the star thresholds are compared against (decisions.md D-060):
        // speed earned, minus mistakes made. Clamped to 0..1 so the thresholds are always
        // read on the scale they are authored on.
        public float StarScore => Mathf.Clamp01(TimeEfficiency - FailurePenalty);

        // Where the score sits on a bar whose FULL end is three stars rather than 1.0
        // (decisions.md D-061). Scaled that way because a bar that keeps filling after the
        // last star has been earned communicates nothing -- "full" has to mean "maxed".
        //
        // Derived here rather than in the view for the same reason StarCount is: the
        // thresholds are this class's business, and a view holding its own copy of
        // ThreeStarScore is a view that starts lying the first time the asset is retuned.
        public float ScoreProgressToMaxStars
        {
            get
            {
                if (config == null) return 0f;
                if (config.ThreeStarScore <= 0f) return 1f;
                return Mathf.Clamp01(StarScore / config.ThreeStarScore);
            }
        }

        // Where the two-star notch sits on that same bar, 0 at its left edge and 1 at its
        // right. The one-star notch is not here on purpose: it is the COMPLETION floor, not
        // a score threshold, so it belongs at the bar's left edge by definition and there is
        // nothing to compute. A ThreeStarScore of 0 collapses this to the left edge too --
        // consistent with every score already being worth three stars in that case.
        public float TwoStarProgressPosition
        {
            get
            {
                if (config == null || config.ThreeStarScore <= 0f) return 0f;
                return Mathf.Clamp01(config.TwoStarScore / config.ThreeStarScore);
            }
        }

        // Replaces "one star per life lost" (D-008): the score decides the top two bands,
        // and finishing the day at all is worth the first star. Speed now matters, which is
        // the whole point of the change -- a fast player can absorb one mistake, where the
        // old rule made every mistake cost exactly one star no matter how the day was
        // played.
        //
        // Two rules survive from D-008 unchanged. Lives start at 3, so finishing a day with
        // 3 or more failures is only reachable by paying to continue -- that refills lives
        // but deliberately does not reset this counter, since it is still the same attempt,
        // and those runs still finish with nothing to show. And the ceiling is still 3.
        //
        // ThreeStarScore is tested FIRST, so authoring it below TwoStarScore makes the
        // two-star band unreachable rather than throwing -- the same literal reading
        // EconomyCalculator gives its tier ratios, and for the same reason.
        public int StarCount
        {
            get
            {
                if (config == null) return 0;
                if (OrdersFailedCount >= GameState.DefaultStartingLives) return 0;

                var score = StarScore;
                if (score >= config.ThreeStarScore) return MaxStars;
                if (score >= config.TwoStarScore) return 2;
                return 1;
            }
        }

        // Rounds the delivery ONCE, the same way GameManager rounds before adding
        // to SoftMoney -- keeps OrdersDeliveredValue + TipsValue always summing to
        // exactly the SoftMoney the player actually received today, instead of
        // drifting from float rounding done twice.
        //
        // Order Value needs no rounding at all now (food prices are ints), so the
        // whole remainder lands in the tip line -- which is also why that line can
        // no longer go negative the way the old TotalTip - BaseTip did whenever a
        // patience coefficient below 1 shrank the total under its own base.
        //
        // The seconds are NOT rounded: they are a score input, not money, and rounding
        // ten deliveries' worth of them to whole seconds would move the star band on a
        // borderline day for no reason.
        public void RecordDelivery(DeliveryPayoutResult payout)
        {
            var totalRounded = Mathf.RoundToInt(payout.Total);
            totalOrderValueToday += payout.OrderValue;
            totalTipToday += totalRounded - payout.OrderValue;
            savedSecondsToday += payout.RemainingSeconds;
            state.TicketsDeliveredToday++;
        }

        public void RecordFailure(DayFailureCause cause)
        {
            if (cause == DayFailureCause.Timeout) timeoutsToday++;
            else wrongDeliveriesToday++;
        }

        // Takes the day's ticket budget rather than reading it, because the Day the player
        // is on is GameManager's business (it owns the day index and the catalog) and this
        // class deliberately knows nothing about DaySystem. Every reset site passes the
        // CURRENT day's total, which is what makes advancing to a longer day score against
        // the longer day.
        public void ResetForNewDay(float totalTicketSecondsForDay)
        {
            state.TicketsDeliveredToday = 0;
            totalOrderValueToday = 0;
            totalTipToday = 0;
            wrongDeliveriesToday = 0;
            timeoutsToday = 0;
            savedSecondsToday = 0f;
            totalTicketSecondsToday = totalTicketSecondsForDay > 0f ? totalTicketSecondsForDay : 0f;
        }
    }
}
