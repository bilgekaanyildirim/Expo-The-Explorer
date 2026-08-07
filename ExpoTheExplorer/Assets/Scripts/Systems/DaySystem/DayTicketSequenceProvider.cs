using System;
using System.Collections.Generic;
using ExpoTheExplorer.Core;
using ExpoTheExplorer.Data;
using ExpoTheExplorer.Systems.TicketSystem;

namespace ExpoTheExplorer.Systems.DaySystem
{
    public class DayTicketSequenceProvider
    {
        private readonly IReadOnlyList<ResolvedTicketEntry> ticketSequence;
        private readonly TicketGenerationConfig config;
        private readonly TicketFactory ticketFactory;
        private int cursor;

        public DayTicketSequenceProvider(IReadOnlyList<ResolvedTicketEntry> ticketSequence, TicketGenerationConfig config, TicketFactory ticketFactory)
        {
            this.ticketSequence = ticketSequence;
            this.config = config;
            this.ticketFactory = ticketFactory;
        }

        // No fallback: ticketSequence.Count must equal the Day's ticketsRequiredForDay
        // (enforced by DayValidator, PR-6.6, before the Day is ever saved) -- running
        // past the end means that guarantee was violated, an authoring bug.
        public Ticket NextTicket()
        {
            var index = cursor;
            if (index >= ticketSequence.Count)
            {
                throw new InvalidOperationException(
                    $"Day's authored ticketSequence only has {ticketSequence.Count} entries but ticket #{index + 1} was requested.");
            }
            cursor++;
            return TicketEntryFactory.Create(ticketSequence[index], config, ticketFactory, index);
        }
    }
}
