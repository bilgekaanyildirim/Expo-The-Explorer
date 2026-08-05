using System.Collections.Generic;
using ExpoTheExplorer.Bootstrap;
using ExpoTheExplorer.Core;
using ExpoTheExplorer.Data;
using UnityEngine;

namespace ExpoTheExplorer.UI
{
    // Composition root for the 3 fixed ticket card slots — instantiates
    // cardPrefab (built + wired by hand in the Editor) into cardsParent (also
    // built by hand: Canvas > panel with a HorizontalLayoutGroup). This script
    // owns no Canvas/layout of its own — all of that lives in the Editor-built
    // hierarchy, not in code.
    public class TicketCardsView : MonoBehaviour
    {
        [SerializeField] private GameManager gameManager;
        [SerializeField] private TicketCardView cardPrefab;
        [SerializeField] private Transform cardsParent;
        [Tooltip("Shared tuning for board/tray/ticket-card animation durations.")]
        [SerializeField] private BoardAnimationConfig animConfig;

        [Tooltip("Ticket background art per patience type (GDD Section 8): red=impatient, green=patient. Normal uses the yellow background here in place of the GDD's neutral/cream suggestion.")]
        [SerializeField] private Sprite normalTicketSprite;
        [SerializeField] private Sprite impatientTicketSprite;
        [SerializeField] private Sprite patientTicketSprite;

        [Tooltip("Modification row box tint per patience type — matches the card border above.")]
        [SerializeField] private Color normalModificationBoxColor = new Color(1f, 0.7921569f, 0.5254902f);
        [SerializeField] private Color impatientModificationBoxColor = new Color(1f, 0.6039216f, 0.6039216f);
        [SerializeField] private Color patientModificationBoxColor = new Color(0.6980392f, 0.9137255f, 0.6980392f);

        [Tooltip("Shared icons for a modification row's direction — same two sprites for every ingredient.")]
        [SerializeField] private Sprite additionSprite;
        [SerializeField] private Sprite removalSprite;

        public Sprite TicketSpriteFor(PatienceType patienceType) => patienceType switch
        {
            PatienceType.Impatient => impatientTicketSprite,
            PatienceType.Patient => patientTicketSprite,
            _ => normalTicketSprite,
        };

        public Color ModificationBoxColorFor(PatienceType patienceType) => patienceType switch
        {
            PatienceType.Impatient => impatientModificationBoxColor,
            PatienceType.Patient => patientModificationBoxColor,
            _ => normalModificationBoxColor,
        };

        public Sprite DirectionSpriteFor(bool isAddition) => isAddition ? additionSprite : removalSprite;

        private readonly List<TicketCardView> cards = new();

        // Looked up by WorldTrayView (via its own TicketCardsView reference)
        // once this object's Awake has run — cards are Instantiate'd here at
        // runtime, so unlike this component itself they can't be wired by
        // hand in the Editor ahead of time.
        public TicketCardView GetCard(int slotIndex) => cards[slotIndex];

        private void Awake()
        {
            if (!ValidateReferences()) return;

            ClearExistingCards();
            cards.Clear();

            for (var i = 0; i < GameState.TicketSlotCount; i++)
            {
                var card = Instantiate(cardPrefab, cardsParent);
                card.Initialize(gameManager, i, this, animConfig);
                cards.Add(card);

                var fillCounter = card.GetComponentInChildren<TrayFillCounterView>(true);
                if (fillCounter != null) fillCounter.Initialize(gameManager, i);
            }
        }

        // Every field here is wired by hand in the Editor — a missing one
        // should fail loudly with a clear pointer to which field, not a bare
        // NullReferenceException/ArgumentException deep in Unity's own code.
        private bool ValidateReferences()
        {
            var missing = new List<string>();
            if (gameManager == null) missing.Add(nameof(gameManager));
            if (cardPrefab == null) missing.Add(nameof(cardPrefab));
            if (cardsParent == null) missing.Add(nameof(cardsParent));
            if (animConfig == null) missing.Add(nameof(animConfig));

            if (missing.Count == 0) return true;

            Debug.LogError($"{nameof(TicketCardsView)} on '{name}' is missing Inspector reference(s): {string.Join(", ", missing)}.", this);
            return false;
        }

        // Defensive: if a leftover card instance (e.g. the one used to build the
        // prefab) is still sitting under cardsParent, remove it first so we
        // always end up with exactly GameState.TicketSlotCount cards.
        private void ClearExistingCards()
        {
            for (var i = cardsParent.childCount - 1; i >= 0; i--)
            {
                Destroy(cardsParent.GetChild(i).gameObject);
            }
        }
    }
}
