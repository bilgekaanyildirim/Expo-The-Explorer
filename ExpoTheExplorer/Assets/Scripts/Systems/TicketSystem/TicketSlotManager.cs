using System;
using ExpoTheExplorer.Core;

namespace ExpoTheExplorer.Systems.TicketSystem
{
    // Owns the "3 fixed slots, refilled into the same index" invariant (GDD
    // Section 3) plus per-ticket countdown/timeout (GDD Section 7). Deliberately
    // has no reference to BoardGrid — timeout never scatters a tray, only a
    // wrong delivery does (that's the future Tray system's job).
    //
    // Pulls exactly one ticket from nextTicketProvider() per slot assignment --
    // used to pre-buffer a lookahead queue instead (bug: fixed 2026-08), so
    // BoardDistributor's noise pool could "leak" items from tickets the player
    // hadn't seen yet (GDD Section 4). That buffer became vestigial once the Day
    // system (PR-6) removed BoardDistributor from the runtime path entirely --
    // board content is now pre-authored (DayBoardTimelinePlayer), nothing reads
    // "upcoming" tickets anymore. Worse, the buffer actively broke the Day system:
    // it made nextTicketProvider() get called lookaheadCount+N times over a day
    // needing only N tickets, so a fixed-length authored ticketSequence (length
    // == N, the locked invariant) always ran out immediately. Removed rather than
    // resized -- nothing needs a lookahead beyond "the ticket about to fill this
    // slot" anymore.
    public class TicketSlotManager
    {
        private readonly GameState state;
        private readonly Func<Ticket> nextTicketProvider;
        private readonly Action loseLife;

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
        public TicketSlotManager(GameState state, Func<Ticket> nextTicketProvider, Action loseLife)
        {
            this.state = state;
            this.nextTicketProvider = nextTicketProvider;
            this.loseLife = loseLife;
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

            var ticket = nextTicketProvider();
            state.TicketSlots[slotIndex] = ticket;
            state.TicketAssigned.Publish((slotIndex, ticket));
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
