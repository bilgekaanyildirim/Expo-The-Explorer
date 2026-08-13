using ExpoTheExplorer.Core;
using ExpoTheExplorer.Data;
using ExpoTheExplorer.Systems.TicketSystem;

namespace ExpoTheExplorer.Systems.DaySystem
{
    public static class TicketEntryFactory
    {
        public static Ticket Create(ResolvedTicketEntry entry, TicketGenerationConfig config, TicketFactory ticketFactory, long arrivalSequence)
        {
            var customerName = string.IsNullOrEmpty(entry.CustomerNameOverride)
                ? ticketFactory.PickRandomCustomerName()
                : entry.CustomerNameOverride;

            var timeLimitSeconds = entry.TimeLimitSecondsOverride > 0f
                ? entry.TimeLimitSecondsOverride
                : config.TimeLimitSecondsFor(entry.PatienceType);

            return new Ticket(customerName, entry.PatienceType, entry.RequiredItems, entry.Modifications, timeLimitSeconds, arrivalSequence);
        }
    }
}
