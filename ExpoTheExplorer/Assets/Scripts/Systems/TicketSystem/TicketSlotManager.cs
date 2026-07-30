using System;
using System.Collections.Generic;
using ExpoTheExplorer.Core;

namespace ExpoTheExplorer.Systems.TicketSystem
{
    // Owns the "3 fixed slots, refilled into the same index" invariant (GDD
    // Section 3) plus per-ticket countdown/timeout (GDD Section 7). Deliberately
    // has no reference to BoardGrid — timeout never scatters a tray, only a
    // wrong delivery does (that's the future Tray system's job).
    //
    // Slots are filled from a pre-generated lookahead queue rather than calling
    // nextTicketProvider on demand — this is what lets BoardDistributor's noise
    // pool "leak" items from tickets the player hasn't seen yet (GDD Section 4).
    // Queued-but-not-yet-active tickets are never touched by Tick (it only walks
    // TicketSlots), so their timers stay frozen until they're dequeued into a slot.
    public class TicketSlotManager
    {
        private readonly GameState state;
        private readonly Func<Ticket> nextTicketProvider;
        private readonly int lookaheadCount;
        private readonly List<Ticket> upcomingTickets = new();

        public IReadOnlyList<Ticket> UpcomingTickets => upcomingTickets;

        public TicketSlotManager(GameState state, Func<Ticket> nextTicketProvider, int lookaheadCount = 10)
        {
            this.state = state;
            this.nextTicketProvider = nextTicketProvider;
            this.lookaheadCount = Math.Max(GameState.TicketSlotCount, lookaheadCount);
        }

        public void FillEmptySlots()
        {
            for (var i = 0; i < GameState.TicketSlotCount; i++)
            {
                if (state.TicketSlots[i] == null)
                {
                    state.TicketSlots[i] = DequeueNextTicket();
                }
            }
        }

        public void DeliverTicket(int slotIndex)
        {
            var ticket = state.TicketSlots[slotIndex];
            if (ticket == null) return;

            ticket.State = TicketState.Delivered;
            state.TicketDelivered.Publish(ticket);
            state.TicketSlots[slotIndex] = DequeueNextTicket();
        }

        // Life-agnostic on purpose: GDD only ties life loss to a failed tray
        // check or a timeout (handled in Tick). This stays reusable for any
        // other non-punitive cancellation reason.
        public void CancelTicket(int slotIndex)
        {
            var ticket = state.TicketSlots[slotIndex];
            if (ticket == null) return;

            ticket.State = TicketState.Cancelled;
            state.TicketCancelled.Publish(ticket);
            state.TicketSlots[slotIndex] = DequeueNextTicket();
        }

        private void EnsureQueueFilled()
        {
            while (upcomingTickets.Count < lookaheadCount)
            {
                upcomingTickets.Add(nextTicketProvider());
            }
        }

        private Ticket DequeueNextTicket()
        {
            EnsureQueueFilled();
            var next = upcomingTickets[0];
            upcomingTickets.RemoveAt(0);
            EnsureQueueFilled();
            return next;
        }

        public void Tick(float deltaSeconds)
        {
            for (var i = 0; i < GameState.TicketSlotCount; i++)
            {
                var ticket = state.TicketSlots[i];
                if (ticket == null || ticket.State != TicketState.Active) continue;

                ticket.RemainingSeconds = Math.Max(0f, ticket.RemainingSeconds - deltaSeconds);
                if (ticket.RemainingSeconds <= 0f)
                {
                    state.Lives--;
                    CancelTicket(i);
                }
            }
        }
    }
}
