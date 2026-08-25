using UnityEngine;

namespace ExpoTheExplorer.Data
{
    // Duplicates the visual constants TicketCardsView owns as private scene fields
    // (Assets/Scripts/UI/TicketCardsView.cs) so Editor-only tooling (Day Editor's
    // ticket card preview) can read them without any scene/prefab dependency --
    // TicketCardsView itself is left untouched. Assign the same sprites/colors
    // already set there. Deliberately holds no PatienceType-aware lookup logic
    // (ExpoTheExplorer.Data has zero assembly references, and PatienceType lives in
    // ExpoTheExplorer.Core) -- callers switch on PatienceType themselves.
    [CreateAssetMenu(fileName = "TicketCardVisualsConfig", menuName = "ExpoTheExplorer/Data/Ticket Card Visuals Config")]
    public class TicketCardVisualsConfig : ScriptableObject
    {
        [SerializeField] private Sprite normalTicketSprite;
        [SerializeField] private Sprite impatientTicketSprite;
        [SerializeField] private Sprite patientTicketSprite;

        [SerializeField] private Color normalModificationBoxColor = new(1f, 0.7921569f, 0.5254902f);
        [SerializeField] private Color impatientModificationBoxColor = new(1f, 0.6039216f, 0.6039216f);
        [SerializeField] private Color patientModificationBoxColor = new(0.6980392f, 0.9137255f, 0.6980392f);

        [SerializeField] private Sprite additionSprite;
        [SerializeField] private Sprite removalSprite;

        [Header("Timer Bar")]
        [Tooltip("How many seconds one divided section of the timer bar stands for. The bar itself always spans the " +
                 "ticket's whole time limit, so this only decides where the divider ticks land -- a ticket whose limit " +
                 "isn't a whole multiple of this ends with one shorter final section rather than shifting every tick.")]
        [SerializeField, Min(1f)] private float timerSegmentSeconds = 5f;

        // WHERE the bar changes colour is not authored here: the two thresholds
        // moved to EconomyConfig (WarningRatio/CriticalRatio) because they also
        // decide the tip tier, and one number has to drive both or the colour the
        // player sees can drift away from the money they get. Only the colours
        // themselves are a visual choice, so only they stayed.
        [Tooltip("Bar colour while the ticket still has more than EconomyConfig's Warning Ratio of its time left.")]
        [SerializeField] private Color timerFillColor = new(0.4941176f, 0.8156863f, 0.4941176f);
        [Tooltip("Bar colour once remaining time drops to EconomyConfig's Warning Ratio — the same instant the tip drops to Tip Rate Warning.")]
        [SerializeField] private Color timerWarningColor = new(1f, 0.7686275f, 0.4196078f);
        [Tooltip("Bar colour once remaining time drops to EconomyConfig's Critical Ratio — the same instant the tip drops to Tip Rate Critical.")]
        [SerializeField] private Color timerCriticalColor = new(1f, 0.4823529f, 0.4823529f);

        [Header("Danger Flash")]
        // Colour, not transparency. Fading the card's alpha took the customer name,
        // the dish photo and the timer bar down with the paper, so the card was at
        // its least readable exactly when it mattered most; tinting only the paper
        // leaves everything printed on it fully opaque. The flash is a SQUARE wave
        // between these two — half the period on each — because a smooth ramp
        // between them reads as fading, which is the thing this replaced.
        [Tooltip("The loud tint the ticket's paper takes for the lit half of the danger flash. Full saturation is the point — this is the alarm.")]
        [SerializeField] private Color dangerFlashColor = new(1f, 0.1490196f, 0.1490196f);
        [Tooltip("The tint it takes for the other half. Left WHITE, the paper simply returns to its own artwork and the card reads as flashing red; put a second loud colour here and it ALTERNATES between the two instead (red/yellow). This is the one knob for how garish the flash is.")]
        [SerializeField] private Color dangerFlashRestColor = Color.white;

        public Sprite NormalTicketSprite => normalTicketSprite;
        public Sprite ImpatientTicketSprite => impatientTicketSprite;
        public Sprite PatientTicketSprite => patientTicketSprite;

        public Color NormalModificationBoxColor => normalModificationBoxColor;
        public Color ImpatientModificationBoxColor => impatientModificationBoxColor;
        public Color PatientModificationBoxColor => patientModificationBoxColor;

        public Sprite AdditionSprite => additionSprite;
        public Sprite RemovalSprite => removalSprite;

        public float TimerSegmentSeconds => timerSegmentSeconds;
        public Color TimerFillColor => timerFillColor;
        public Color TimerWarningColor => timerWarningColor;
        public Color TimerCriticalColor => timerCriticalColor;

        public Color DangerFlashColor => dangerFlashColor;
        public Color DangerFlashRestColor => dangerFlashRestColor;
    }
}
