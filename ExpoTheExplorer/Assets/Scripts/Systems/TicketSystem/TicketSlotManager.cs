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
        private readonly Action<int> loseLife;

        // Which slots timed out on the last life and are still waiting to be cancelled
        // (D-099). Sized like the slot array and indexed the same way, so a deferral cannot
        // point at a slot that does not exist. Not a queue and not a list of "pending
        // commands": the only postponed action is this class's own CancelTicket, and the only
        // thing worth remembering about it is which slot it is owed on.
        private readonly bool[] deferredTimeouts = new bool[GameState.TicketSlotCount];

        // Set once the Day's authored ticket sequence is exhausted AND every
        // slot it fed has resolved (delivered or cancelled) -- see AssignTicket.
        // Deliberately NOT keyed to a delivery-count goal (bug: fixed 2026-08):
        // nextTicketProvider() is asked for one ticket per slot fill (3 at day
        // start, one more per resolution after that), so the sequence always
        // runs out a few resolutions before a "deliver N times" goal could ever
        // be reached, and a timed-out ticket never counts as delivered at all --
        // keying completion to deliveries made the day literally uncompletable.
        // Once true, no further ticket ever gets assigned into a slot and Tick
        // stops counting down the ones still active (there are none, by
        // construction) -- cleared again by ResetSlotsForNewDay, the existing
        // retry entry point.
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
        //
        // It carries the SLOT the life was lost on. That is not this class
        // learning what kind of failure it caused — decisions.md D-060 keeps the
        // cause split up in GameManager's two named entry points and that is
        // unchanged — it is the index this class already loops over, handed on so
        // a view can point at the ticket that ran out instead of guessing. The
        // tray cannot work it out for itself: its own poll only notices a timeout
        // that had items to scatter, so an untouched ticket expiring left no trace
        // anywhere (decisions.md D-078).
        public TicketSlotManager(GameState state, Func<Ticket> nextTicketProvider, Action<int> loseLife)
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
                // A deferred cancellation is owed on a ticket this loop is about to discard,
                // so it dies with it (D-099). Carrying one across a reset would cancel a slot
                // of the NEW day on its first live frame -- the day would open having already
                // thrown a ticket away.
                deferredTimeouts[i] = false;
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
        // rather than polling every frame. nextTicketProvider() returns null
        // once the Day's sequence is exhausted (GameManager.CreateNextTicket) --
        // the slot just stays empty (UI already renders that, TicketCardView.
        // RebuildContent). The Day is only truly over once that's true for
        // every slot at once: whichever resolution (delivery or cancellation)
        // empties the LAST still-active slot is what fires DayCompleted here.
        private void AssignTicket(int slotIndex)
        {
            if (IsDayComplete) return;

            var ticket = nextTicketProvider();
            state.TicketSlots[slotIndex] = ticket;
            state.TicketAssigned.Publish((slotIndex, ticket));

            if (ticket == null && AllSlotsEmpty())
            {
                IsDayComplete = true;
                state.DayCompleted.Publish(state.TicketsDeliveredToday);
            }
        }

        private bool AllSlotsEmpty()
        {
            for (var i = 0; i < GameState.TicketSlotCount; i++)
            {
                if (state.TicketSlots[i] != null) return false;
            }
            return true;
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
                    loseLife(i);

                    // The order is load-bearing and unchanged: the life goes first, so the
                    // heart breaks on the tray that lost it (D-078) and the popup is already
                    // up by the time we get here. What changed is what follows -- see below.
                    if (state.IsAwaitingContinue)
                    {
                        // That life was the LAST one, and the popup went up inside the call
                        // above. Cancelling now would replace this ticket and cascade a whole
                        // board round -- new items popping in, an orphaned tray scattering --
                        // behind a popup the player is reading (D-099). So the ticket is left
                        // standing at 0 seconds and the cancellation waits for Continue.
                        //
                        // Marked per SLOT rather than remembered as "the one that expired":
                        // two tickets can run out in the same frame, and deferring only the
                        // first would leave the second sitting at 0 to expire again on the
                        // very next live tick, spending a life from the freshly refilled bar.
                        deferredTimeouts[i] = true;
                        continue;
                    }

                    CancelTicket(i);
                }
            }
        }

        // Runs the cancellations the hold above postponed. Called from GameManager.Update on
        // the first live frame after the day resumes -- driven by reading IsAwaitingContinue
        // rather than by an event LivesManager would publish, because the hold is lifted by
        // four different callers (two paid Continues, the retry, and the debug refill) and a
        // publisher any one of them forgets leaves a slot empty for the rest of the day, with
        // the day unable to complete. The flag cannot be forgotten: it is the same one the
        // gate already reads.
        //
        // Safe to call on any frame: with nothing deferred it is three bool reads.
        public void ResolveDeferredTimeouts()
        {
            if (IsDayComplete) return;

            for (var i = 0; i < GameState.TicketSlotCount; i++)
            {
                if (!deferredTimeouts[i]) continue;

                // Cleared BEFORE the cancel, not after: CancelTicket publishes
                // TicketCancelled and assigns a replacement, both synchronously, and that
                // cascade reaches GameManager -> BoardDistributor -> back here. Clearing
                // afterwards would let a re-entrant pass see a flag for work already in
                // progress and cancel the fresh ticket that just arrived.
                deferredTimeouts[i] = false;
                CancelTicket(i);
            }
        }
    }
}
