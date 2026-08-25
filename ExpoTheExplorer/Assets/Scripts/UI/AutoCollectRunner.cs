using System.Collections.Generic;
using ExpoTheExplorer.Bootstrap;
using ExpoTheExplorer.Core;
using ExpoTheExplorer.Systems.PowerupSystem;
using UnityEngine;

namespace ExpoTheExplorer.UI
{
    // The DOING half of GDD 5.2 #1 -- Auto-Collect. What to pick is decided in
    // PowerupEffects.TryFindAutoCollectItem (pure, and therefore testable); this class only
    // carries out the moves.
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
        // A ticket's required list maxes out at three items today, so any slot finishes in
        // three moves. Eight is slack rather than a design number: it exists so that a bug
        // in the search -- one that kept returning a cell the tray then refused -- cannot
        // hang the editor in an infinite loop. If this limit is ever REACHED, something is
        // wrong upstream, which is why it says so in the console.
        private const int MaxMovesPerSlot = 8;

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

        // Returns whether anything was actually moved. That bool is GDD 5.2's "no wasted
        // press" rule reaching PowerupManager: a board with none of the items the active
        // tickets still need costs the player nothing.
        public bool Run()
        {
            if (!isValid) return false;

            var state = gameManager.State;
            var trayManager = gameManager.TrayManager;
            if (state == null || trayManager == null) return false;

            // THE TICKETS IN HAND, captured by INSTANCE before anything moves. This is what
            // keeps one press from becoming a whole day: filling a tray delivers it, which
            // synchronously assigns a NEW ticket into the same slot, whose required items
            // then spawn onto the board -- and a loop that kept going would collect those
            // too, deliver again, and chain until the Day's sequence ran out. GDD 5.2 says
            // this powerup places the items the ACTIVE tickets need, not "until the queue
            // is empty". Reference equality is the check, not slot emptiness, because the
            // replacement ticket occupies the very same slot.
            var captured = new Ticket[GameState.TicketSlotCount];
            for (var i = 0; i < GameState.TicketSlotCount; i++)
            {
                captured[i] = state.TicketSlots[i];
            }

            var movedAny = false;

            for (var slot = 0; slot < GameState.TicketSlotCount; slot++)
            {
                if (captured[slot] == null) continue;

                var tray = TrayFor(slot);
                if (tray == null) continue;

                for (var move = 0; move < MaxMovesPerSlot; move++)
                {
                    // Re-asked every iteration rather than walked from a list built once.
                    // A single accepted drop can deliver the ticket, assign the next one and
                    // respawn the board inside this very call, so a frozen plan would be
                    // describing a board that no longer exists.
                    if (!ReferenceEquals(state.TicketSlots[slot], captured[slot])) break;

                    if (!PowerupEffects.TryFindAutoCollectItem(
                            state, slot, trayManager.GetContents(slot), out var x, out var y))
                    {
                        break;
                    }

                    if (!boardView.TryGetDragHandler(x, y, out var handler)) break;

                    // The same call OnDrop makes. A refusal here is not an error -- the tray
                    // can legitimately say no (its ticket resolved mid-cascade) -- so the
                    // slot simply stops.
                    if (!tray.TryAcceptDrop(handler)) break;

                    movedAny = true;

                    if (move == MaxMovesPerSlot - 1)
                    {
                        Debug.LogWarning(
                            $"{nameof(AutoCollectRunner)}: slot {slot} hit the {MaxMovesPerSlot}-move safety limit. " +
                            "A ticket needs at most three items, so this means the search kept offering an item the " +
                            "tray would not resolve — worth looking at.",
                            this);
                    }
                }
            }

            return movedAny;
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
