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
    // delegates for delivery and life loss instead of TicketSlotManager/
    // LivesManager references, so this only depends on Core, not
    // Systems.TicketSystem or Systems.LivesSystem (same pattern as
    // BoardDistributor).
    public class TrayManager
    {
        private readonly GameState state;
        private readonly Action<int> deliverTicket;
        private readonly Action loseLife;
        private readonly System.Random random;
        private readonly TraySlot[] slots;

        // Takes a loseLife delegate (LivesManager.LoseLife in practice), same
        // rationale as deliverTicket: keeps this decoupled from a concrete
        // Systems.LivesSystem reference while still centralizing life loss in
        // one place shared with TicketSlotManager's timeout case (CLAUDE.md
        // Section 5 — Lives System).
        public TrayManager(GameState state, Action<int> deliverTicket, Action loseLife, System.Random random = null)
        {
            this.state = state;
            this.deliverTicket = deliverTicket;
            this.loseLife = loseLife;
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
        //
        // onAccepted fires the instant acceptance is confirmed, before the
        // batch check below — the caller (BoardItemDragHandler, via
        // WorldTrayView) uses it to detach this item from the board's data
        // model right then. If that detach instead waited until after this
        // call returns (as it used to), a delivery on THIS exact call cascades
        // synchronously into BoardDistributor's required-pool re-check
        // (deliverTicket -> AssignTicket -> OnOrderPlaced) while the item is
        // still sitting in its old board cell, so a food/modification combo
        // shared with another active ticket (e.g. a plain Side/Drink with no
        // modifications) reads as "already present" and never gets
        // replenished — leaving that other ticket permanently short once this
        // item is actually removed a moment later.
        public bool TryAddItem(int slotIndex, BoardItem item, Action onAccepted = null)
        {
            var ticket = state.TicketSlots[slotIndex];
            if (ticket == null || ticket.State != TicketState.Active) return false;

            var slot = slots[slotIndex];
            if (slot.IsFull(ticket)) return false;

            onAccepted?.Invoke();
            slot.Add(item);

            if (slot.IsFull(ticket))
            {
                if (slot.Matches(ticket))
                {
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
                    loseLife();
                    ScatterBackToBoard(slotIndex, slot);
                    slot.Clear();
                }
            }

            return true;
        }

        // Full day-reset primitive (GameManager.RetryDay). Clears every slot's
        // tray WITHOUT scattering the contents onto the board -- the board is
        // already being wiped by BoardGrid.Clear() in the same reset, so
        // scattering here would just reintroduce items onto what's supposed
        // to end up as an empty board. Must run before
        // TicketSlotManager.ResetSlotsForNewDay(), so that reset's
        // TicketAssigned cascade into OnTicketAssigned below finds already-
        // empty trays and no-ops instead of scattering.
        public void DiscardAllForNewDay()
        {
            foreach (var slot in slots)
            {
                slot.Clear();
            }
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
