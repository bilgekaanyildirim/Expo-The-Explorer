using ExpoTheExplorer.Core;

namespace ExpoTheExplorer.Systems.DayLifecycle
{
    // Delivery bookkeeping only -- "Day complete" itself is decided by
    // TicketSlotManager (bug: fixed 2026-08), not by a delivery-count goal
    // here. A timed-out ticket never increments this counter, so a goal-based
    // check would never fire once even one ticket was lost to a timeout.
    public class DayLifecycleManager
    {
        private readonly GameState state;

        public DayLifecycleManager(GameState state)
        {
            this.state = state;
        }

        public void RecordDelivery()
        {
            state.TicketsDeliveredToday++;
        }

        public void ResetForNewDay()
        {
            state.TicketsDeliveredToday = 0;
        }
    }
}
