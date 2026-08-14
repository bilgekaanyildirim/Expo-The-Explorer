using System;
using System.Collections.Generic;
using ExpoTheExplorer.Core;
using ExpoTheExplorer.Data;
using ExpoTheExplorer.Systems.DaySystem;
using ExpoTheExplorer.Systems.TicketSystem;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace ExpoTheExplorer.Tests.EditMode
{
    public class DayContentGeneratorTests
    {
        private readonly List<UnityEngine.Object> spawned = new();

        private FoodCatalog catalog;
        private TicketGenerationConfig ticketConfig;

        [SetUp]
        public void SetUp()
        {
            var main = CreateFoodItem("main", FoodCategory.Main);
            var side = CreateFoodItem("side", FoodCategory.Side);
            var drink = CreateFoodItem("drink", FoodCategory.Drink);

            catalog = ScriptableObject.CreateInstance<FoodCatalog>();
            spawned.Add(catalog);
            SetItemsList(catalog, main, side, drink);

            ticketConfig = CreateTicketGenerationConfig(withNamesDatabase: true);
        }

        [TearDown]
        public void TearDown()
        {
            foreach (var o in spawned) UnityEngine.Object.DestroyImmediate(o);
            spawned.Clear();
        }

        [Test]
        public void Generate_ProducesExactlyTicketsRequiredForDayEntries()
        {
            var result = DayContentGenerator.Generate(catalog, ticketConfig, null, ticketsRequiredForDay: 5, seed: 1);

            Assert.AreEqual(5, result.Length);
        }

        [Test]
        public void Generate_SameSeed_ProducesIdenticalOutput()
        {
            var first = DayContentGenerator.Generate(catalog, ticketConfig, null, ticketsRequiredForDay: 5, seed: 42);
            var second = DayContentGenerator.Generate(catalog, ticketConfig, null, ticketsRequiredForDay: 5, seed: 42);

            Assert.AreEqual(ToComparableJson(first), ToComparableJson(second));
        }

        [Test]
        public void Generate_WithTicketGenerationOverride_ForcesInclusionExtremes()
        {
            var editorMeta = new DayEditorMetaJson
            {
                hasTicketGenerationOverride = true,
                sideInclusionChanceOverride = 0f,
                drinkInclusionChanceOverride = 1f,
                modificationCountLambdaOverride = 0f,
            };

            var result = DayContentGenerator.Generate(catalog, ticketConfig, editorMeta, ticketsRequiredForDay: 5, seed: 3);

            foreach (var entry in result)
            {
                Assert.IsTrue(string.IsNullOrEmpty(entry.sideItemId), "Side inclusion chance was overridden to 0 -- no ticket should have a side item.");
                Assert.IsFalse(string.IsNullOrEmpty(entry.drinkItemId), "Drink inclusion chance was overridden to 1 -- every ticket should have a drink item.");
            }
        }

        [Test]
        public void Generate_ThenPlayback_TicketContentMatchesGeneratedSequence()
        {
            var result = DayContentGenerator.Generate(catalog, ticketConfig, null, ticketsRequiredForDay: 8, seed: 7);

            var dayJson = new DayJson
            {
                runtime = new DayRuntimeJson
                {
                    dayIndex = 0,
                    ticketsRequiredForDay = 8,
                    ticketSequence = result,
                    boardTimeline = Array.Empty<BoardSpawnEntryJson>(),
                    hasRetryVariant = false,
                    retryVariant = null,
                },
            };
            var json = JsonUtility.ToJson(dayJson);
            var parsed = DayCatalogParser.ParseAll(new[] { new DayJsonFile("test", json) }, catalog);
            var dayDefinition = parsed[0];

            Assert.AreEqual(result.Length, dayDefinition.TicketSequence.Count);
            for (var i = 0; i < result.Length; i++)
            {
                var expected = result[i];
                var resolved = dayDefinition.TicketSequence[i];

                Assert.AreEqual(expected.mainItemId, resolved.MainItem.Id);
                Assert.AreEqual(string.IsNullOrEmpty(expected.sideItemId) ? null : expected.sideItemId, resolved.SideItem?.Id);
                Assert.AreEqual(string.IsNullOrEmpty(expected.drinkItemId) ? null : expected.drinkItemId, resolved.DrinkItem?.Id);
                Assert.AreEqual(expected.patienceType, resolved.PatienceType.ToString());
            }
        }

        [Serializable]
        private class ComparableResult
        {
            public TicketEntryJson[] ticketSequence;
        }

        private static string ToComparableJson(TicketEntryJson[] result)
        {
            return JsonUtility.ToJson(new ComparableResult { ticketSequence = result });
        }

        private FoodItemConfig CreateFoodItem(string id, FoodCategory category)
        {
            var item = ScriptableObject.CreateInstance<FoodItemConfig>();
            spawned.Add(item);

            var serialized = new SerializedObject(item);
            serialized.FindProperty("id").stringValue = id;
            serialized.FindProperty("category").enumValueIndex = (int)category;
            serialized.ApplyModifiedPropertiesWithoutUndo();

            return item;
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

        private TicketGenerationConfig CreateTicketGenerationConfig(bool withNamesDatabase)
        {
            var config = ScriptableObject.CreateInstance<TicketGenerationConfig>();
            spawned.Add(config);

            if (withNamesDatabase)
            {
                var namesDatabase = new TextAsset(
                    "[{\"id\":1,\"name\":\"Alice\",\"gender\":\"f\"},{\"id\":2,\"name\":\"Bob\",\"gender\":\"m\"}]");
                spawned.Add(namesDatabase);

                var serialized = new SerializedObject(config);
                serialized.FindProperty("namesDatabase").objectReferenceValue = namesDatabase;
                serialized.ApplyModifiedPropertiesWithoutUndo();
            }

            return config;
        }
    }
}
