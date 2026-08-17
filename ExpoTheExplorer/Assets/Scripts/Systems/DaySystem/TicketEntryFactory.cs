using ExpoTheExplorer.Core;
using ExpoTheExplorer.Data;
using ExpoTheExplorer.Systems.TicketSystem;

namespace ExpoTheExplorer.Systems.DaySystem
{
    public static class TicketEntryFactory
    {
        // ticketRuntime is the PLAYED Day's own block (decisions.md D-005), so two Days can
        // give the same patience type different time limits. A per-ticket
        // TimeLimitSecondsOverride still wins over both.
        public static Ticket Create(ResolvedTicketEntry entry, TicketRuntimeSettings ticketRuntime, TicketFactory ticketFactory, long arrivalSequence)
        {
            var customerName = string.IsNullOrEmpty(entry.CustomerNameOverride)
                ? ticketFactory.PickRandomCustomerName()
                : entry.CustomerNameOverride;

            var timeLimitSeconds = entry.TimeLimitSecondsOverride > 0f
                ? entry.TimeLimitSecondsOverride
                : ticketRuntime.TimeLimitSecondsFor(entry.PatienceType);

            return new Ticket(customerName, entry.PatienceType, entry.RequiredItems, entry.Modifications, timeLimitSeconds, arrivalSequence);
        }
    }
}
