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
        //
        // The override-vs-patience rule itself moved to ResolvedTicketEntry (D-060): the
        // day's star budget has to add up the same limits this line hands the player, so
        // both sides ask the entry rather than each doing the arithmetic.
        public static Ticket Create(ResolvedTicketEntry entry, TicketRuntimeSettings ticketRuntime, TicketFactory ticketFactory, long arrivalSequence)
        {
            var customerName = string.IsNullOrEmpty(entry.CustomerNameOverride)
                ? ticketFactory.PickRandomCustomerName()
                : entry.CustomerNameOverride;

            // The face is drawn HERE, beside the name, because this is the path that
            // builds the ticket the player is handed — and because the factory's random
            // is unseeded on this path, unlike the seeded one Day generation uses
            // (decisions.md D-139, and the note on PickRandomCustomerPortrait itself).
            //
            // No CustomerPortraitOverride to match CustomerNameOverride above: a Day
            // authors names and nothing has asked it to author faces, and an override
            // nobody sets would be a second authority for this field waiting to drift
            // from the draw.
            var customerPortrait = ticketFactory.PickRandomCustomerPortrait();

            var timeLimitSeconds = entry.TimeLimitSecondsWith(ticketRuntime);

            return new Ticket(customerName, entry.PatienceType, entry.RequiredItems, entry.Modifications, timeLimitSeconds, arrivalSequence, customerPortrait);
        }
    }
}
