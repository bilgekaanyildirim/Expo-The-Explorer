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
        private int totalBaseTipToday;
        private int totalBonusTipToday;
        private int ordersFailedToday;

        public DayLifecycleManager(GameState state)
        {
            this.state = state;
        }

        public int OrdersDeliveredCount => state.TicketsDeliveredToday;
        public int OrdersDeliveredValue => totalBaseTipToday;
        public int TipsValue => totalBonusTipToday;
        public int OrdersFailedCount => ordersFailedToday;
        public int Total => totalBaseTipToday + totalBonusTipToday;

        // Rounds once per delivery, the same way GameManager rounds before
        // adding to SoftMoney -- keeps OrdersDeliveredValue + TipsValue always
        // summing to exactly the SoftMoney the player actually received today,
        // instead of drifting from float rounding done twice.
        public void RecordDelivery(DeliveryTipResult tipResult)
        {
            var totalRounded = Mathf.RoundToInt(tipResult.TotalTip);
            var baseRounded = Mathf.RoundToInt(tipResult.BaseTip);
            totalBaseTipToday += baseRounded;
            totalBonusTipToday += totalRounded - baseRounded;
            state.TicketsDeliveredToday++;
        }

        public void RecordFailure()
        {
            ordersFailedToday++;
        }

        public void ResetForNewDay()
        {
            state.TicketsDeliveredToday = 0;
            totalBaseTipToday = 0;
            totalBonusTipToday = 0;
            ordersFailedToday = 0;
        }
    }
}
