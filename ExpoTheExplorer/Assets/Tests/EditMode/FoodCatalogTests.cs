using ExpoTheExplorer.Data;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace ExpoTheExplorer.Tests.EditMode
{
    public class FoodCatalogTests
    {
        private FoodCatalog catalog;
        private FoodItemConfig burger;
        private ModificationConfig noPickles;

        [SetUp]
        public void SetUp()
        {
            burger = ScriptableObject.CreateInstance<FoodItemConfig>();
            SetPrivateField(burger, "id", "burger");

            noPickles = ScriptableObject.CreateInstance<ModificationConfig>();
            SetPrivateField(noPickles, "id", "no_pickles");
            SetModificationsList(burger, noPickles);

            catalog = ScriptableObject.CreateInstance<FoodCatalog>();
            SetItemsList(catalog, burger);
        }

        [TearDown]
        public void TearDown()
        {
            Object.DestroyImmediate(burger);
            Object.DestroyImmediate(noPickles);
            Object.DestroyImmediate(catalog);
        }

        [Test]
        public void GetById_ExistingId_ReturnsItem()
        {
            Assert.AreSame(burger, catalog.GetById("burger"));
        }

        [Test]
        public void GetById_UnknownId_ReturnsNull()
        {
            Assert.IsNull(catalog.GetById("does_not_exist"));
        }

        [Test]
        public void GetModificationById_ExistingId_ReturnsConfig()
        {
            Assert.AreSame(noPickles, catalog.GetModificationById("no_pickles"));
        }

        [Test]
        public void GetModificationById_UnknownId_ReturnsNull()
        {
            Assert.IsNull(catalog.GetModificationById("does_not_exist"));
        }

        private static void SetPrivateField(Object target, string fieldName, string value)
        {
            var serialized = new SerializedObject(target);
            serialized.FindProperty(fieldName).stringValue = value;
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        private static void SetItemsList(FoodCatalog target, params FoodItemConfig[] values)
        {
            var serialized = new SerializedObject(target);
            var property = serialized.FindProperty("items");
            property.arraySize = values.Length;
            for (var i = 0; i < values.Length; i++)
            {
                property.GetArrayElementAtIndex(i).objectReferenceValue = values[i];
            }
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        private static void SetModificationsList(FoodItemConfig target, params ModificationConfig[] values)
        {
            var serialized = new SerializedObject(target);
            var property = serialized.FindProperty("availableModifications");
            property.arraySize = values.Length;
            for (var i = 0; i < values.Length; i++)
            {
                property.GetArrayElementAtIndex(i).objectReferenceValue = values[i];
            }
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }
    }
}
