using ExpoTheExplorer.Core;
using ExpoTheExplorer.Data;

namespace ExpoTheExplorer.Systems.DayLifecycle
{
    public class DayLifecycleManager
    {
        private readonly GameState state;
        private readonly GameConfig config;

        public DayLifecycleManager(GameState state, GameConfig config)
        {
            this.state = state;
            this.config = config;
        }

        // == not >=: TicketsDeliveredToday only ever increments by exactly 1,
        // from exactly one call site (GameManager.OnTicketDelivered), so it can
        // never skip past the exact goal value -- this guarantees DayCompleted
        // fires exactly once per day, not once per delivery for every delivery
        // at or past the goal.
        public void RecordDelivery()
        {
            state.TicketsDeliveredToday++;
            if (state.TicketsDeliveredToday == config.TicketsRequiredPerDay)
            {
                state.DayCompleted.Publish(state.TicketsDeliveredToday);
            }
        }

        public void ResetForNewDay()
        {
            state.TicketsDeliveredToday = 0;
        }
    }
}
