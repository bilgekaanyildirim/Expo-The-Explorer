using ExpoTheExplorer.Core;
using ExpoTheExplorer.Systems.EconomySystem;
using UnityEngine;

namespace ExpoTheExplorer.Systems.DayLifecycle
{
    // Delivery bookkeeping only -- "Day complete" itself is decided by
    // TicketSlotManager (bug: fixed 2026-08), not by a delivery-count goal
    // here. A timed-out ticket never increments this counter, so a goal-based
    // check would never fire once even one ticket was lost to a timeout.
    public class DayLifecycleManager
    {
        private readonly GameState state;
        private int totalOrderValueToday;
        private int totalTipToday;
        private int ordersFailedToday;

        public DayLifecycleManager(GameState state)
        {
            this.state = state;
        }

        public int OrdersDeliveredCount => state.TicketsDeliveredToday;
        public int OrdersDeliveredValue => totalOrderValueToday;
        public int TipsValue => totalTipToday;
        public int OrdersFailedCount => ordersFailedToday;
        public int Total => totalOrderValueToday + totalTipToday;

        public const int MaxStars = 3;

        // One star lost per life lost: a flawless day is 3 stars, one slip is 2, two is 1
        // (decisions.md D-008). Replaces the old comparison of Total against per-Day authored
        // thresholds -- stars now measure accuracy alone, and nothing has to be balanced by
        // hand for each Day.
        //
        // Lives start at 3, so finishing a day with 3 or more failures is only reachable by
        // paying Gems to continue: that refills lives but deliberately does not reset this
        // counter, since it is still the same attempt. Those runs floor at 0 stars -- the day
        // is completed and paid out, it just earns nothing to show for it.
        public int StarCount => Mathf.Max(0, MaxStars - ordersFailedToday);

        // Rounds the delivery ONCE, the same way GameManager rounds before adding
        // to SoftMoney -- keeps OrdersDeliveredValue + TipsValue always summing to
        // exactly the SoftMoney the player actually received today, instead of
        // drifting from float rounding done twice.
        //
        // Order Value needs no rounding at all now (food prices are ints), so the
        // whole remainder lands in the tip line -- which is also why that line can
        // no longer go negative the way the old TotalTip - BaseTip did whenever a
        // patience coefficient below 1 shrank the total under its own base.
        public void RecordDelivery(DeliveryPayoutResult payout)
        {
            var totalRounded = Mathf.RoundToInt(payout.Total);
            totalOrderValueToday += payout.OrderValue;
            totalTipToday += totalRounded - payout.OrderValue;
            state.TicketsDeliveredToday++;
        }

        public void RecordFailure()
        {
            ordersFailedToday++;
        }

        public void ResetForNewDay()
        {
            state.TicketsDeliveredToday = 0;
            totalOrderValueToday = 0;
            totalTipToday = 0;
            ordersFailedToday = 0;
        }
    }
}
