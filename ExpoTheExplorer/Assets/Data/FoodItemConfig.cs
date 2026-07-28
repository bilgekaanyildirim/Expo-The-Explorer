using System.Collections.Generic;
using UnityEngine;

namespace ExpoTheExplorer.Data
{
    public enum FoodCategory
    {
        Main,
        Side,
        Drink
    }

    [CreateAssetMenu(fileName = "FoodItemConfig", menuName = "ExpoTheExplorer/Data/Food Item Config")]
    public class FoodItemConfig : ScriptableObject
    {
        [SerializeField] private string id;
        [SerializeField] private string displayName;
        [SerializeField] private Sprite sprite;
        [SerializeField] private FoodCategory category;

        [Tooltip("Modifications that can appear on a ticket for this item. Only meaningful for Main category items — leave empty for Side/Drink (GDD Section 3.2).")]
        [SerializeField] private List<ModificationConfig> availableModifications = new();

        public string Id => id;
        public string DisplayName => displayName;
        public Sprite Sprite => sprite;
        public FoodCategory Category => category;
        public IReadOnlyList<ModificationConfig> AvailableModifications => availableModifications;
    }
}
