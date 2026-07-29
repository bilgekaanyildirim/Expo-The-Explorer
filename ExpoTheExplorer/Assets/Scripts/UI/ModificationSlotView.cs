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

        public void SetModification(Sprite ingredientSprite, Sprite directionSprite)
        {
            ingredientImage.sprite = ingredientSprite;
            directionImage.sprite = directionSprite;
        }
    }
}
