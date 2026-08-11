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

        public Sprite NormalTicketSprite => normalTicketSprite;
        public Sprite ImpatientTicketSprite => impatientTicketSprite;
        public Sprite PatientTicketSprite => patientTicketSprite;

        public Color NormalModificationBoxColor => normalModificationBoxColor;
        public Color ImpatientModificationBoxColor => impatientModificationBoxColor;
        public Color PatientModificationBoxColor => patientModificationBoxColor;

        public Sprite AdditionSprite => additionSprite;
        public Sprite RemovalSprite => removalSprite;
    }
}
