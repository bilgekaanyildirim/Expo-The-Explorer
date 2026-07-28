using System.Collections.Generic;
using UnityEngine;

namespace ExpoTheExplorer.Data
{
    [CreateAssetMenu(fileName = "FoodCatalog", menuName = "ExpoTheExplorer/Data/Food Catalog")]
    public class FoodCatalog : ScriptableObject
    {
        [SerializeField] private List<FoodItemConfig> items = new();

        public IReadOnlyList<FoodItemConfig> Items => items;
    }
}
