using ExpoTheExplorer.Bootstrap;
using TMPro;
using UnityEngine;

namespace ExpoTheExplorer.UI
{
    // Purely a "x/y" readout under TicketCard.prefab — the tray's actual item
    // visuals live in the world-space WorldTrayView instead (GDD Section 5),
    // this just polls TrayManager for the count. Same poll+diff philosophy
    // TicketCardView already uses for its timer.
    public class TrayFillCounterView : MonoBehaviour
    {
        [SerializeField] private TMP_Text fillCounterText;

        private GameManager gameManager;
        private int slotIndex;
        private TicketCardView ticketCardView;
        private bool isValid;

        // The last pair actually written to the label, so Update can tell a frame that
        // changed the readout from the ~59 that did not. Start at -1 rather than 0: a tray
        // legitimately reads "0/0" between tickets, and a cache seeded with the real first
        // value would skip the write that puts it on screen.
        private int lastContentCount = -1;
        private int lastRequiredCount = -1;
        private bool lastEnabled;

        public void Initialize(GameManager gameManager, int slotIndex, TicketCardView ticketCardView)
        {
            this.gameManager = gameManager;
            this.slotIndex = slotIndex;
            this.ticketCardView = ticketCardView;

            isValid = fillCounterText != null;
            if (!isValid)
            {
                Debug.LogError($"{nameof(TrayFillCounterView)} on '{name}' is missing its {nameof(fillCounterText)} reference.", this);
                return;
            }

            // Seeded from the label's own state rather than left at the field's default:
            // the cache has to start out TRUE, or a first frame that wants the counter
            // hidden would compare false-to-false and skip the write that hides it.
            lastEnabled = fillCounterText.enabled;

            Refresh();
        }

        // Still a poll, deliberately: the tray's count is moved by a synchronous cascade
        // (TrayManager.TryAddItem -> deliverTicket -> TicketSlotManager.AssignTicket) and by
        // a timeout scatter, and an event-driven label would have to be re-subscribed at
        // every one of those seams. What changed is that polling no longer WRITES: the two
        // reads below are a list count and an array index, and everything past them is
        // skipped on the ~59 frames out of 60 where neither number moved.
        //
        // That guard is the whole point. This used to interpolate a fresh "x/y" string and
        // assign TMP_Text.text every frame -- three trays at 60 fps is 180 allocations and
        // 180 layout/vertex dirty marks a second, and it was the only frame-frequency
        // allocation left in the project (every other .text write is event-driven or
        // throttled to 1 s). TMP does not diff the assignment for us; a label only stays
        // clean if nothing hands it a new string.
        private void Update()
        {
            if (!isValid) return;

            // Hidden for the duration of the tray/ticket-card delivery and
            // scatter animations (TicketCardView.IsAnimating) — those already
            // rewrite the tray contents and ticket mid-flight, so the "x/y"
            // readout would otherwise flicker through stale/mid-transition
            // values instead of landing cleanly on the settled count.
            var shouldShow = !ticketCardView.IsAnimating;
            if (shouldShow != lastEnabled)
            {
                lastEnabled = shouldShow;
                fillCounterText.enabled = shouldShow;
            }

            Refresh();
        }

        private void Refresh()
        {
            var contents = gameManager.TrayManager.GetContents(slotIndex);
            var ticket = gameManager.State.TicketSlots[slotIndex];
            var required = ticket?.RequiredItems.Count ?? 0;

            if (contents.Count == lastContentCount && required == lastRequiredCount) return;

            lastContentCount = contents.Count;
            lastRequiredCount = required;
            fillCounterText.text = $"{contents.Count}/{required}";
        }
    }
}
