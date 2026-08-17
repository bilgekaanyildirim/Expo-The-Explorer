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
        private readonly TicketRuntimeSettings ticketRuntime;
        private readonly TicketFactory ticketFactory;
        private readonly Dictionary<int, Ticket> peekCache = new();
        private int cursor;

        // ticketRuntime comes from the Day being played, ticketFactory from the session --
        // the factory is only a customer-name picker here, and the name pool is not
        // per-Day balancing (decisions.md D-005).
        public DayTicketSequenceProvider(IReadOnlyList<ResolvedTicketEntry> ticketSequence, TicketRuntimeSettings ticketRuntime, TicketFactory ticketFactory)
        {
            this.ticketSequence = ticketSequence;
            this.ticketRuntime = ticketRuntime;
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

            // Reuse a peeked instance if one exists for this index (see PeekUpcoming) rather
            // than rolling a fresh one -- callers that peeked ahead (BoardDistributor's
            // leakedTickets dedup is reference-identity-based) must get the exact same Ticket
            // back when it actually arrives, not a second, differently-randomized copy.
            if (peekCache.TryGetValue(index, out var cached))
            {
                peekCache.Remove(index);
                return cached;
            }

            return TicketEntryFactory.Create(ticketSequence[index], ticketRuntime, ticketFactory, index);
        }

        // Looks ahead into the authored sequence WITHOUT advancing cursor -- unlike NextTicket,
        // calling this any number of times has no effect on what NextTicket returns next. Each
        // constructed Ticket is cached by index so a later NextTicket() call for that same index
        // returns this exact instance (see NextTicket's comment for why that matters). Returns
        // fewer than `count` once the sequence runs out, same "running out is expected, not an
        // error" stance HasNext already documents -- never throws.
        public IReadOnlyList<Ticket> PeekUpcoming(int count)
        {
            var result = new List<Ticket>(count);
            var end = Math.Min(cursor + count, ticketSequence.Count);
            for (var index = cursor; index < end; index++)
            {
                if (!peekCache.TryGetValue(index, out var ticket))
                {
                    ticket = TicketEntryFactory.Create(ticketSequence[index], ticketRuntime, ticketFactory, index);
                    peekCache[index] = ticket;
                }
                result.Add(ticket);
            }
            return result;
        }
    }
}
