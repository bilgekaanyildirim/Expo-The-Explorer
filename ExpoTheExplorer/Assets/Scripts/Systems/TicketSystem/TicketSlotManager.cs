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
        private readonly Action loseLife;
        private readonly int lookaheadCount;
        private readonly List<Ticket> upcomingTickets = new();

        public IReadOnlyList<Ticket> UpcomingTickets => upcomingTickets;

        // Set once the day's delivery goal is hit (GameManager.OnDayCompleted)
        // so no further ticket ever gets assigned into a slot and Tick stops
        // counting down the ones still active -- cleared again by
        // ResetSlotsForNewDay, the existing retry entry point.
        public bool IsDayComplete { get; private set; }

        public void PauseForDayComplete()
        {
            IsDayComplete = true;
        }

        // Takes a loseLife delegate (LivesManager.LoseLife in practice) rather
        // than mutating GameState.Lives directly — keeps life-loss centralized
        // in one place shared with TrayManager's wrong-delivery case, instead
        // of two systems separately decrementing the same field (GDD Section
        // 3/6, CLAUDE.md Section 5 — Lives System).
        public TicketSlotManager(GameState state, Func<Ticket> nextTicketProvider, Action loseLife, int lookaheadCount = 10)
        {
            this.state = state;
            this.nextTicketProvider = nextTicketProvider;
            this.loseLife = loseLife;
            this.lookaheadCount = Math.Max(GameState.TicketSlotCount, lookaheadCount);
        }

        public void FillEmptySlots()
        {
            for (var i = 0; i < GameState.TicketSlotCount; i++)
            {
                if (state.TicketSlots[i] == null)
                {
                    AssignTicket(i);
                }
            }
        }

        public void DeliverTicket(int slotIndex)
        {
            var ticket = state.TicketSlots[slotIndex];
            if (ticket == null) return;

            ticket.State = TicketState.Delivered;
            state.TicketDelivered.Publish((slotIndex, ticket));
            AssignTicket(slotIndex);
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
            AssignTicket(slotIndex);
        }

        // Free day-reset primitive (GameManager.RetryDay) -- deliberately NOT
        // built on CancelTicket, which would publish 3 spurious TicketCancelled
        // events for tickets that weren't really cancelled, just discarded.
        // Nulls every slot FIRST, then assigns all three in a second pass --
        // NOT combined into one loop iteration, because AssignTicket's
        // TicketAssigned publish cascades synchronously into
        // GameManager.OnTicketAssigned -> BoardDistributor.OnOrderPlaced,
        // which reads the WHOLE TicketSlots array on every call. A combined
        // single-pass loop would let that call see a mix of already-reset and
        // still-stale Active tickets in not-yet-processed slots, and
        // BoardDistributor would spawn required-pool items for tickets that
        // are about to be discarded anyway.
        public void ResetSlotsForNewDay()
        {
            IsDayComplete = false;

            for (var i = 0; i < GameState.TicketSlotCount; i++)
            {
                state.TicketSlots[i] = null;
            }

            for (var i = 0; i < GameState.TicketSlotCount; i++)
            {
                AssignTicket(i);
            }
        }

        // Publishes TicketAssigned with the slot index + NEW ticket — this is
        // what lets BoardDistributor spawn that order's required items and
        // TrayManager clear that slot's tray, right when the order arrives,
        // rather than polling every frame.
        private void AssignTicket(int slotIndex)
        {
            if (IsDayComplete) return;

            var ticket = DequeueNextTicket();
            state.TicketSlots[slotIndex] = ticket;
            state.TicketAssigned.Publish((slotIndex, ticket));
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
            if (IsDayComplete) return;

            for (var i = 0; i < GameState.TicketSlotCount; i++)
            {
                var ticket = state.TicketSlots[i];
                if (ticket == null || ticket.State != TicketState.Active) continue;

                ticket.RemainingSeconds = Math.Max(0f, ticket.RemainingSeconds - deltaSeconds);
                if (ticket.RemainingSeconds <= 0f)
                {
                    loseLife();
                    CancelTicket(i);
                }
            }
        }
    }
}
