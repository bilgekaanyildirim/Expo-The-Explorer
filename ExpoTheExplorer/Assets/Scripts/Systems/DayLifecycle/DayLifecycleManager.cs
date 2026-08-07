using System;
using ExpoTheExplorer.Core;

namespace ExpoTheExplorer.Systems.DayLifecycle
{
    public class DayLifecycleManager
    {
        private readonly GameState state;
        private readonly Func<int> getTicketsRequiredForDay;

        public DayLifecycleManager(GameState state, Func<int> getTicketsRequiredForDay)
        {
            this.state = state;
            this.getTicketsRequiredForDay = getTicketsRequiredForDay;
        }

        // == not >=: TicketsDeliveredToday only ever increments by exactly 1,
        // from exactly one call site (GameManager.OnTicketDelivered), so it can
        // never skip past the exact goal value -- this guarantees DayCompleted
        // fires exactly once per day, not once per delivery for every delivery
        // at or past the goal.
        public void RecordDelivery()
        {
            state.TicketsDeliveredToday++;
            if (state.TicketsDeliveredToday == getTicketsRequiredForDay())
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
