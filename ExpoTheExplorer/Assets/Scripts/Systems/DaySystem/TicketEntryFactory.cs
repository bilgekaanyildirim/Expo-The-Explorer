using System.Collections.Generic;
using ExpoTheExplorer.Core;
using ExpoTheExplorer.Data;
using ExpoTheExplorer.Systems.TicketSystem;

namespace ExpoTheExplorer.Systems.DaySystem
{
    public static class TicketEntryFactory
    {
        public static Ticket Create(ResolvedTicketEntry entry, TicketGenerationConfig config, TicketFactory ticketFactory, long arrivalSequence)
        {
            var requiredItems = new List<FoodItemConfig> { entry.MainItem };
            if (entry.SideItem != null) requiredItems.Add(entry.SideItem);
            if (entry.DrinkItem != null) requiredItems.Add(entry.DrinkItem);

            var customerName = string.IsNullOrEmpty(entry.CustomerNameOverride)
                ? ticketFactory.PickRandomCustomerName()
                : entry.CustomerNameOverride;

            var timeLimitSeconds = entry.TimeLimitSecondsOverride > 0f
                ? entry.TimeLimitSecondsOverride
                : config.TimeLimitSecondsFor(entry.PatienceType);

            return new Ticket(customerName, entry.PatienceType, requiredItems, entry.Modifications, timeLimitSeconds, arrivalSequence);
        }
    }
}
