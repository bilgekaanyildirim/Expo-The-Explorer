using System.Collections.Generic;
using ExpoTheExplorer.Bootstrap;
using ExpoTheExplorer.Core;
using ExpoTheExplorer.Systems.PowerupSystem;
using ExpoTheExplorer.Systems.TraySystem;
using UnityEngine;

namespace ExpoTheExplorer.UI
{
    // The DOING half of GDD 5.2 #1 -- Auto-Collect. What to pick is decided in
    // PowerupEffects.PlanAutoCollect and PowerupEffects.UnwantedTrayItems (pure, and
    // therefore testable); this class only carries the plan out.
    //
    // THE WHOLE PRESS IS DECIDED BEFORE THE FIRST MOVE (D-110). This used to ask for one
    // next-move at a time against the live board, which let the later slots collect items
    // that the earlier slots' own deliveries had just spawned. Everything here now walks a
    // closed list and re-verifies each step against the board rather than re-deciding it.
    //
    // IT MOVES ITEMS THE WAY A FINGER DOES, through WorldTrayView.TryAcceptDrop with the
    // board item's own BoardItemDragHandler. That is not a stylistic choice: a tray's
    // contents are drawn entirely by the dragged GameObject being reparented into a slot,
    // and nothing rebuilds them from the data model. A version of this that wrote straight
    // into TrayManager would be perfectly correct in state and show an empty tray.
    //
    // Reusing that path also means every rule already attached to a drop comes for free --
    // detaching from the board before the batch check, the delivery lift and fade, the
    // ticket card's exit animation, the tray's re-entrance. None of it is re-implemented
    // here, and none of it can drift from what a manual drop does.
    //
    // It lives in the UI assembly because it must, and that is the reason the decision was
    // pulled out: Assembly-CSharp is a predefined assembly and no asmdef -- including the
    // test assembly -- can reference it, so nothing in this file can be covered by an
    // EditMode test. Everything with a rule in it was moved where a test can reach it.
    public class AutoCollectRunner : MonoBehaviour
    {
        [SerializeField] private GameManager gameManager;

        [Tooltip("Needed so a board cell can hand over the same drag handler a finger would have grabbed.")]
        [SerializeField] private BoardView boardView;

        [Tooltip("One tray per ticket slot, in SLOT ORDER (slot 0 first). Index is the slot index — a swapped pair puts items in the wrong tray.")]
        [SerializeField] private WorldTrayView[] trays;

        private bool isValid;

        private void Awake()
        {
            isValid = ValidateReferences();
        }

        // Returns whether anything actually happened. That bool is GDD 5.2's "no wasted
        // press" rule reaching PowerupManager: a board with none of the items the active
        // tickets still need, and no stray tray item to send home, costs the player nothing.
        // A press that ONLY sent items home does count -- the board visibly changes and a
        // tray that was guaranteed to cost a life is un-jammed (D-110, the user's call).
        public bool Run()
        {
            if (!isValid) return false;

            var state = gameManager.State;
            var trayManager = gameManager.TrayManager;
            if (state == null || trayManager == null) return false;

            // 1 -- SEND HOME WHAT THE TICKET NEVER ASKED FOR. A tray holding a mis-drop can
            // never resolve to a delivery: the junk occupies space the order needs, so the
            // batch check is guaranteed to fire on a wrong tray and cost a life. Returning
            // it first is what makes such a slot completable again, and the item rejoins the
            // board so another ticket can have it. Runs BEFORE the snapshot below, which is
            // why a returned item is part of this press's budget rather than a phantom.
            var returnedAny = ReturnUnwantedTrayItems(state, trayManager);

            // 2 -- THE TICKETS IN HAND, captured by INSTANCE before anything moves. This is
            // what keeps one press from becoming a whole day: filling a tray delivers it,
            // which synchronously assigns a NEW ticket into the same slot, whose required
            // items then spawn onto the board. GDD 5.2 says this powerup places the items
            // the ACTIVE tickets need, not "until the queue is empty". Reference equality is
            // the check, not slot emptiness, because the replacement ticket occupies the
            // very same slot.
            var captured = new Ticket[GameState.TicketSlotCount];
            for (var i = 0; i < GameState.TicketSlotCount; i++)
            {
                captured[i] = state.TicketSlots[i];
            }

            // 3 -- THE PLAN, closed before the first move. Everything the press will do is
            // decided here, against the board and trays as they stand right now: completable
            // tickets first, then partial fills on what is left (PowerupEffects
            // .PlanAutoCollect). Items that spawn during the run cannot enter it, and
            // neither can tickets that arrive during it.
            var trayContents = new IReadOnlyList<BoardItem>[GameState.TicketSlotCount];
            for (var i = 0; i < GameState.TicketSlotCount; i++)
            {
                trayContents[i] = trayManager.GetContents(i);
            }

            var plan = PowerupEffects.PlanAutoCollect(state, trayContents);
            var movedAny = false;

            // 4 -- EXECUTE, in plan order, verifying every step against the live board.
            foreach (var move in plan)
            {
                var slot = move.SlotIndex;

                // The slot's ticket resolved mid-run (its own last move delivered it, or a
                // cascade cancelled it). Its remaining moves belong to a ticket that is no
                // longer there, so they are dropped rather than aimed at the new one.
                if (!ReferenceEquals(state.TicketSlots[slot], captured[slot])) continue;

                // The cell no longer holds the item the plan reserved. A delivery backfills
                // the cells it empties, so this is a real possibility rather than paranoia --
                // and taking whatever landed there instead is exactly the bug this rewrite
                // exists to remove.
                if (!ReferenceEquals(state.Board.ItemAt(move.X, move.Y), move.Item)) continue;

                var tray = TrayFor(slot);
                if (tray == null) continue;

                if (!boardView.TryGetDragHandler(move.X, move.Y, out var handler)) continue;

                // The pickup half of the gesture, which no finger is here to perform
                // (D-113). Without it the item reaches the tray with its resting scale
                // never recorded, and the drop that COMPLETES an order sets it to that
                // unwritten value -- scale zero, invisible for the whole delivery. It also
                // lands any fly-in still in progress, so the board's tween and the tray's
                // settle are never writing this transform at the same time.
                handler.SettleForAutoCollect();

                // The call OnDrop makes, differing in one thing only: the item flies in
                // slower (D-112), because one press sends several off at once and a
                // finger's flight time reads as all of them scattering at the same instant.
                // A refusal is not an error -- the tray can legitimately say no (it is
                // mid-delivery, or the tutorial has it locked) -- so that move is skipped.
                if (!tray.TryAcceptAutoCollectDrop(handler)) continue;

                movedAny = true;
            }

            return movedAny || returnedAny;
        }

        // Every tray's unwanted leftovers, back onto the board. The DECIDING is
        // PowerupEffects.UnwantedTrayItems (pure, and therefore testable); this only asks
        // each tray to carry it out, because the tray is the only object that can -- a tray
        // item's GameObject is a child of one of its slots, and nothing else can take it
        // out of there without leaving the model and the view disagreeing.
        private bool ReturnUnwantedTrayItems(GameState state, TrayManager trayManager)
        {
            var returnedAny = false;

            for (var slot = 0; slot < GameState.TicketSlotCount; slot++)
            {
                var tray = TrayFor(slot);
                if (tray == null) continue;

                var unwanted = PowerupEffects.UnwantedTrayItems(state, slot, trayManager.GetContents(slot));
                if (unwanted.Count == 0) continue;

                if (tray.ReturnItemsToBoard(unwanted) > 0) returnedAny = true;
            }

            return returnedAny;
        }

        // Index IS the slot index. Guarded rather than trusted because the array is dragged
        // in by hand and a short one is a silent half-working powerup otherwise.
        private WorldTrayView TrayFor(int slotIndex) =>
            trays != null && slotIndex < trays.Length ? trays[slotIndex] : null;

        private bool ValidateReferences()
        {
            var missing = new List<string>();
            if (gameManager == null) missing.Add(nameof(gameManager));
            if (boardView == null) missing.Add(nameof(boardView));
            if (trays == null || trays.Length < GameState.TicketSlotCount)
            {
                missing.Add($"{nameof(trays)} (needs {GameState.TicketSlotCount}, in slot order)");
            }

            if (missing.Count == 0) return true;

            Debug.LogError(
                $"{nameof(AutoCollectRunner)} on '{name}' is missing Inspector reference(s): " +
                $"{string.Join(", ", missing)}. Auto-Collect will do nothing — and because it reports that " +
                "honestly, it will not cost the player a charge either.",
                this);
            return false;
        }
    }
}
