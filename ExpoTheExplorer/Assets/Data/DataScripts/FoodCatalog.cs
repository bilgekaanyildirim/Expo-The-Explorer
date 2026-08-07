using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace ExpoTheExplorer.Data
{
    [CreateAssetMenu(fileName = "FoodCatalog", menuName = "ExpoTheExplorer/Data/Food Catalog")]
    public class FoodCatalog : ScriptableObject
    {
        [SerializeField] private List<FoodItemConfig> items = new();

        public IReadOnlyList<FoodItemConfig> Items => items;

        public FoodItemConfig GetById(string id) => items.FirstOrDefault(item => item.Id == id);

        public ModificationConfig GetModificationById(string id) =>
            items.SelectMany(item => item.AvailableModifications).FirstOrDefault(mod => mod.Id == id);
    }
}
