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

            Refresh();
        }

        private void Update()
        {
            if (!isValid) return;

            // Hidden for the duration of the tray/ticket-card delivery and
            // scatter animations (TicketCardView.IsAnimating) — those already
            // rewrite the tray contents and ticket mid-flight, so the "x/y"
            // readout would otherwise flicker through stale/mid-transition
            // values instead of landing cleanly on the settled count.
            fillCounterText.enabled = !ticketCardView.IsAnimating;
            Refresh();
        }

        private void Refresh()
        {
            var contents = gameManager.TrayManager.GetContents(slotIndex);
            var ticket = gameManager.State.TicketSlots[slotIndex];
            var required = ticket?.RequiredItems.Count ?? 0;
            fillCounterText.text = $"{contents.Count}/{required}";
        }
    }
}
