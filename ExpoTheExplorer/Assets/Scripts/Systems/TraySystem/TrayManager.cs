using System;
using System.Collections.Generic;
using ExpoTheExplorer.Core;
using UnityEngine;

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
        private readonly System.Random random;
        private readonly TraySlot[] slots;

        public TrayManager(GameState state, Action<int> deliverTicket, System.Random random = null)
        {
            this.state = state;
            this.deliverTicket = deliverTicket;
            this.random = random ?? new System.Random();

            slots = new TraySlot[GameState.TicketSlotCount];
            for (var i = 0; i < slots.Length; i++)
            {
                slots[i] = new TraySlot();
            }
        }

        public IReadOnlyList<BoardItem> GetContents(int slotIndex) => slots[slotIndex].Items;

        // Called when a player drags an item back out of a tray (e.g. to
        // return it to the board) — must stop counting it toward that slot's
        // fill the instant it's picked up, not just once it lands elsewhere.
        public bool RemoveItem(int slotIndex, BoardItem item) => slots[slotIndex].Remove(item);

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
                    Debug.Log($"[Tray] Slot {slotIndex}: successfully delivered ticket for {ticket.CustomerName}.");

                    // Cleared BEFORE deliverTicket, not after — deliverTicket
                    // cascades synchronously into AssignTicket ->
                    // TicketAssigned -> OnTicketAssigned below, which reads
                    // this exact slot to decide whether anything needs
                    // scattering back to the board. Clearing first makes
                    // that check correctly see an empty slot; clearing after
                    // (the previous order) let that cascade see the
                    // just-delivered items still sitting here and wrongly
                    // scatter them onto the board as if they were orphaned.
                    slot.Clear();
                    deliverTicket(slotIndex);
                }
                else
                {
                    Debug.Log($"[Tray] Slot {slotIndex}: wrong order for {ticket.CustomerName} — losing a life and scattering items back to the board.");
                    state.Lives--;
                    ScatterBackToBoard(slotIndex, slot);
                    slot.Clear();
                }
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

            ScatterBackToBoard(slotIndex, slot);
            slot.Clear();
        }

        // Brackets its own RequestSpawn calls with TraySlotScatterBegin/End
        // (slotIndex) — narrower than TicketDelivered/TicketAssigned on
        // purpose: a successful delivery cascades into AssignTicket ->
        // TicketAssigned synchronously too (the next ticket's required
        // items spawning), and a view reacting to those broader events
        // would otherwise wrongly tag that unrelated spawn as part of a
        // scatter. This method is only ever reached for a wrong order or a
        // timeout, never a delivery, so it can't cross paths with that.
        private void ScatterBackToBoard(int slotIndex, TraySlot slot)
        {
            state.TraySlotScatterBegin.Publish(slotIndex);
            foreach (var item in slot.Items)
            {
                state.Board.RequestSpawn(item, random);
            }
            state.TraySlotScatterEnd.Publish(slotIndex);
        }
    }
}
