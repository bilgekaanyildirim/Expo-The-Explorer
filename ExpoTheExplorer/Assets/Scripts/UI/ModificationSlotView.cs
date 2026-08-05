using UnityEngine;
using UnityEngine.UI;

namespace ExpoTheExplorer.UI
{
    // One modification row — an ingredient icon (e.g. Cheese) paired with a
    // direction icon (Addition/Removal). Cloned per-modification by
    // TicketCardView; this component just binds the two Image references
    // wired up in the Editor.
    public class ModificationSlotView : MonoBehaviour
    {
        [SerializeField] private Image ingredientImage;
        [SerializeField] private Image directionImage;
        [Tooltip("This row's own box background — tinted per-ticket to match the card's patience-type color.")]
        [SerializeField] private Image background;

        public void SetModification(Sprite ingredientSprite, Sprite directionSprite, Color boxColor)
        {
            ingredientImage.sprite = ingredientSprite;
            directionImage.sprite = directionSprite;
            background.color = boxColor;
        }
    }
}
