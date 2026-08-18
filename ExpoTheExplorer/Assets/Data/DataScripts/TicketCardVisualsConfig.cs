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
    }
}
