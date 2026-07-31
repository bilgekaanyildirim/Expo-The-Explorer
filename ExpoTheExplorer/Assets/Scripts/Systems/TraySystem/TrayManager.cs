using System;
using System.Collections.Generic;
using ExpoTheExplorer.Core;

namespace ExpoTheExplorer.Systems.TraySystem
{
    // Owns all 3 ticket slots' TraySlot state and runs the batch validation
    // rule (GDD Section 3/5): correct items AND correct modifications -> auto
    // deliver; anything wrong -> lose a life, scatter the tray back onto the
    // board. Plain C#, no MonoBehaviour dependency (CLAUDE.md Section 5) — takes
    // a delegate for delivery instead of a TicketSlotManager reference, so this
    // only depends on Core, not Systems.TicketSystem (same pattern as
    // BoardDistributor).
    public class TrayManager
    {
        private readonly GameState state;
        private readonly Action<int> deliverTicket;
        private readonly Random random;
        private readonly TraySlot[] slots;

        public TrayManager(GameState state, Action<int> deliverTicket, Random random = null)
        {
            this.state = state;
            this.deliverTicket = deliverTicket;
            this.random = random ?? new Random();

            slots = new TraySlot[GameState.TicketSlotCount];
            for (var i = 0; i < slots.Length; i++)
            {
                slots[i] = new TraySlot();
            }
        }

        public IReadOnlyList<BoardItem> GetContents(int slotIndex) => slots[slotIndex].Items;

        // Attempts to add one item to a slot's tray. Returns false (item not
        // accepted) if that slot has no active ticket or its tray is already
        // full — the drag handler is expected to treat a false result the same
        // as an invalid drop (snap back to the board). Reaching the required
        // count triggers the batch check immediately and always clears the tray
        // afterward, win or lose.
        public bool TryAddItem(int slotIndex, BoardItem item)
        {
            var ticket = state.TicketSlots[slotIndex];
            if (ticket == null || ticket.State != TicketState.Active) return false;

            var slot = slots[slotIndex];
            if (slot.IsFull(ticket)) return false;

            slot.Add(item);

            if (slot.IsFull(ticket))
            {
                if (slot.Matches(ticket))
                {
                    deliverTicket(slotIndex);
                }
                else
                {
                    state.Lives--;
                    ScatterBackToBoard(slot);
                }

                slot.Clear();
            }

            return true;
        }

        // Called when a slot's ticket changes for any reason (delivered,
        // cancelled/timed out). Only actually does something for the timeout
        // case — a delivered tray is already emptied by TryAddItem above, so
        // this is a harmless no-op then. Deliberately does NOT touch Lives:
        // a timeout already deducts one in TicketSlotManager.Tick, and this is
        // just returning now-orphaned items to the board, not a second penalty.
        public void OnTicketAssigned(int slotIndex)
        {
            var slot = slots[slotIndex];
            if (slot.Items.Count == 0) return;

            ScatterBackToBoard(slot);
            slot.Clear();
        }

        private void ScatterBackToBoard(TraySlot slot)
        {
            foreach (var item in slot.Items)
            {
                state.Board.RequestSpawn(item, random);
            }
        }
    }
}
