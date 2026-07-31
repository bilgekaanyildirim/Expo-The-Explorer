using UnityEngine;

namespace ExpoTheExplorer.Data
{
    [CreateAssetMenu(fileName = "BoardAnimationConfig", menuName = "ExpoTheExplorer/Data/Board Animation Config")]
    public class BoardAnimationConfig : ScriptableObject
    {
        [Header("Drag End")]
        [Tooltip("Duration (seconds) of the ease-back tween when an item is dropped somewhere invalid.")]
        [SerializeField] private float snapBackDuration = 0.25f;
        [Tooltip("Duration (seconds) of the settle tween when an item is dropped into a tray slot.")]
        [SerializeField] private float traySettleDuration = 0.2f;

        [Header("Board")]
        [Tooltip("Duration (seconds) of the pop-in scale tween when an item newly appears on the board.")]
        [SerializeField] private float popInDuration = 0.2f;
        [Tooltip("Arc height of the fly-in from BoardView's Starting Point, as a multiple of one cell size (0 = straight line, no arc).")]
        [SerializeField] private float popInJumpPower = 0.5f;

        [Header("Tray")]
        [Tooltip("Duration (seconds) of the scale-down tween before a resolved tray slot's items are destroyed.")]
        [SerializeField] private float slotClearDuration = 0.15f;

        [Header("Delivery Success")]
        [Tooltip("Scale multiplier the tray (and the just-delivered item) grows to on a successful delivery.")]
        [SerializeField] private float deliveryGrowScale = 1.15f;
        [Tooltip("Duration (seconds) of that grow-up.")]
        [SerializeField] private float deliveryGrowDuration = 0.2f;
        [Tooltip("World-space distance the tray and its contents lift upward while fading out, after the grow.")]
        [SerializeField] private float deliveryLiftDistance = 1.5f;
        [Tooltip("Duration (seconds) of the lift-and-fade-out.")]
        [SerializeField] private float deliveryFadeDuration = 0.35f;
        [Tooltip("Duration (seconds) of the next tray growing back in from below (DeliveryLiftDistance below rest position) once the previous one finishes lifting off.")]
        [SerializeField] private float deliveryReentryDuration = 0.25f;

        [Header("Ticket Card")]
        [Tooltip("How far the ticket card slides (RectTransform units) while fading out, once its tray starts lifting off after a successful delivery.")]
        [SerializeField] private float ticketExitLiftDistance = 150f;
        [Tooltip("Duration (seconds) of the ticket card's exit slide-and-fade.")]
        [SerializeField] private float ticketExitDuration = 0.3f;
        [Tooltip("How far above rest position (RectTransform units) the next ticket's card starts before dropping in.")]
        [SerializeField] private float ticketEntryDropDistance = 150f;
        [Tooltip("Duration (seconds) of the next ticket card's drop-in-and-fade.")]
        [SerializeField] private float ticketEntryDuration = 0.3f;

        public float SnapBackDuration => snapBackDuration;
        public float TraySettleDuration => traySettleDuration;
        public float PopInDuration => popInDuration;
        public float PopInJumpPower => popInJumpPower;
        public float SlotClearDuration => slotClearDuration;
        public float DeliveryGrowScale => deliveryGrowScale;
        public float DeliveryGrowDuration => deliveryGrowDuration;
        public float DeliveryLiftDistance => deliveryLiftDistance;
        public float DeliveryFadeDuration => deliveryFadeDuration;
        public float DeliveryReentryDuration => deliveryReentryDuration;
        public float TicketExitLiftDistance => ticketExitLiftDistance;
        public float TicketExitDuration => ticketExitDuration;
        public float TicketEntryDropDistance => ticketEntryDropDistance;
        public float TicketEntryDuration => ticketEntryDuration;
    }
}
