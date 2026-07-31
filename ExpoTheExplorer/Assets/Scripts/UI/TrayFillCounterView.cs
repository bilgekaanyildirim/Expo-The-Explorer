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
        private bool isValid;

        public void Initialize(GameManager gameManager, int slotIndex)
        {
            this.gameManager = gameManager;
            this.slotIndex = slotIndex;

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
            if (isValid) Refresh();
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
