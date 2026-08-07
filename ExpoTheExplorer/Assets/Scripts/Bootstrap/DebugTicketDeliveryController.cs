using UnityEngine;
using UnityEngine.InputSystem;

namespace ExpoTheExplorer.Bootstrap
{
    // Temporary dev-only tool: lets us exercise ticket refill + board
    // distribution before the real Tray/drag-and-drop delivery flow exists,
    // and Day transitions before the PR-9 popup exists to trigger them.
    // Press 1/2/3 to instantly deliver the ticket in that slot, N to advance
    // to the next Day, R to retry the current Day. Safe to delete once Tray
    // and the Day-Complete/Game-Over popups ship and both are testable in
    // Play mode through the real UI.
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
            if (keyboard.nKey.wasPressedThisFrame) gameManager.AdvanceToNextDay();
            if (keyboard.rKey.wasPressedThisFrame) gameManager.RetryDay();
        }
    }
}
