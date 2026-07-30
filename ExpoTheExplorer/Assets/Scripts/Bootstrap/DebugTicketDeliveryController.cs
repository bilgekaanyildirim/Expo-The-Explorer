using UnityEngine;
using UnityEngine.InputSystem;

namespace ExpoTheExplorer.Bootstrap
{
    // Temporary dev-only tool: lets us exercise ticket refill + board
    // distribution before the real Tray/drag-and-drop delivery flow exists.
    // Press 1/2/3 to instantly deliver the ticket in that slot. Safe to delete
    // once Tray ships and real delivery is testable in Play mode.
    public class DebugTicketDeliveryController : MonoBehaviour
    {
        [SerializeField] private GameManager gameManager;

        private void Update()
        {
            var keyboard = Keyboard.current;
            if (keyboard == null || gameManager == null) return;

            if (keyboard.digit1Key.wasPressedThisFrame) gameManager.TicketSlotManager.DeliverTicket(0);
            if (keyboard.digit2Key.wasPressedThisFrame) gameManager.TicketSlotManager.DeliverTicket(1);
            if (keyboard.digit3Key.wasPressedThisFrame) gameManager.TicketSlotManager.DeliverTicket(2);
        }
    }
}
