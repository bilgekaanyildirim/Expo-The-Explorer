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

        // Callers must check this before calling NextTicket() -- a finite Day's sequence
        // WILL run out mid-day (TicketSlotManager.AssignTicket relies on this, see its
        // 2026-08 bugfix notes), that's expected and not an authoring bug.
        public bool HasNext => cursor < ticketSequence.Count;

        // No fallback: ticketSequence.Count must equal the Day's ticketsRequiredForDay
        // (enforced by DayValidator, PR-6.6, before the Day is ever saved). Reaching this
        // without checking HasNext first is a caller bug, not an authoring one.
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
