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

        [Header("Wrong Order")]
        [Tooltip("Duration (seconds) the tray and its contents shake before scattering back to the board on a wrong order.")]
        [SerializeField] private float scatterShakeDuration = 0.5f;
        [Tooltip("Shake strength (world units) for the wrong-order pre-scatter shake.")]
        [SerializeField] private float scatterShakeStrength = 0.3f;
        [Tooltip("Number of full left-right oscillations during the pre-scatter shake.")]
        [SerializeField] private float scatterShakeFrequency = 8f;
        [Tooltip("How much more (multiplier on ScatterShakeStrength) the tray's contents shake compared to the tray itself.")]
        [SerializeField] private float scatterShakeItemMultiplier = 1.2f;

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
        [Tooltip("Seconds for ONE full on-off cycle of the danger flash a ticket card runs once its remaining time drops into EconomyConfig's Critical Ratio (the instant the timer bar turns red). The card is lit for the first half and rested for the second — a square wave, so this is the only speed knob. WHICH two colours it flashes between lives on TicketCardVisualsConfig. 0 turns the flash off and leaves the danger image showing solid instead — deliberately not a fast strobe.")]
        [SerializeField, Min(0f)] private float ticketDangerBlinkPeriod = 0.6f;

        [Header("Life Lost")]
        [Tooltip("How far the broken heart rises over the tray that cost a life, in RectTransform units — the hearts are canvas Images, so this is the same unit as the ticket card's own 150-unit exit lift, NOT world units.")]
        [SerializeField] private float lifeLostHeartRiseDistance = 120f;
        [Tooltip("Duration (seconds) of that rise-and-fade. Floored in code at a tenth of a second, so a 0 here is a very quick heart rather than an invisible one.")]
        [SerializeField, Min(0f)] private float lifeLostHeartDuration = 1f;

        [Header("Tutorial Spotlight")]
        [Tooltip("How dark the scene goes behind the tutorial's one lit hotdog and one lit tray. 0 = no dimming at all, 1 = solid black. The lit pair and the target tray's ticket card are unaffected -- this is the opacity of the black sheet everything ELSE sits behind.")]
        [SerializeField, Range(0f, 1f)] private float tutorialDimOpacity = 0.72f;
        [Tooltip("Opacity of the ghost hotdog that loops from the lit item to the lit tray. It is a copy of the real item, so 1 would make it indistinguishable from the one the player is meant to grab.")]
        [SerializeField, Range(0f, 1f)] private float tutorialGhostOpacity = 0.55f;
        [Tooltip("Seconds the ghost takes to travel from the item to the tray, once per loop.")]
        [SerializeField, Min(0.05f)] private float tutorialGhostTravelDuration = 0.9f;
        [Tooltip("Seconds of stillness between one ghost arriving and the next one setting off. 0 makes it a continuous stream rather than a repeated gesture.")]
        [SerializeField, Min(0f)] private float tutorialGhostLoopPause = 0.45f;

        [Tooltip("Where a tutorial step's message sits, as a fraction of screen height from the bottom (0 = bottom edge, 1 = top). Tunable rather than fixed because it has to miss the board, the trays and the ticket cards, and only the scene knows where those are.")]
        [SerializeField, Range(0f, 1f)] private float tutorialMessageScreenHeight = 0.28f;

        [Tooltip("Font size of a tutorial step's message, in the same units as the ticket card's own text (it borrows that card's font so the two match).")]
        [SerializeField, Min(1f)] private float tutorialMessageFontSize = 36f;

        [Tooltip("Seconds the arrows and the message take to fade in when a step begins. They appear AFTER the dim so the eye lands on the lit pair first rather than on text.")]
        [SerializeField, Min(0f)] private float tutorialHintFadeDuration = 0.35f;

        public float SnapBackDuration => snapBackDuration;
        public float TraySettleDuration => traySettleDuration;
        public float PopInDuration => popInDuration;
        public float PopInJumpPower => popInJumpPower;
        public float SlotClearDuration => slotClearDuration;
        public float ScatterShakeDuration => scatterShakeDuration;
        public float ScatterShakeStrength => scatterShakeStrength;
        public float ScatterShakeFrequency => scatterShakeFrequency;
        public float ScatterShakeItemMultiplier => scatterShakeItemMultiplier;
        public float DeliveryGrowScale => deliveryGrowScale;
        public float DeliveryGrowDuration => deliveryGrowDuration;
        public float DeliveryLiftDistance => deliveryLiftDistance;
        public float DeliveryFadeDuration => deliveryFadeDuration;
        public float DeliveryReentryDuration => deliveryReentryDuration;
        public float TicketExitLiftDistance => ticketExitLiftDistance;
        public float TicketExitDuration => ticketExitDuration;
        public float TicketEntryDropDistance => ticketEntryDropDistance;
        public float TicketEntryDuration => ticketEntryDuration;
        public float TicketDangerBlinkPeriod => ticketDangerBlinkPeriod;
        public float LifeLostHeartRiseDistance => lifeLostHeartRiseDistance;
        public float LifeLostHeartDuration => lifeLostHeartDuration;
        public float TutorialDimOpacity => tutorialDimOpacity;
        public float TutorialGhostOpacity => tutorialGhostOpacity;
        public float TutorialGhostTravelDuration => tutorialGhostTravelDuration;
        public float TutorialGhostLoopPause => tutorialGhostLoopPause;
        public float TutorialMessageScreenHeight => tutorialMessageScreenHeight;
        public float TutorialMessageFontSize => tutorialMessageFontSize;
        public float TutorialHintFadeDuration => tutorialHintFadeDuration;
    }
}
