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
            var result = DayContentGenerator.Generate(catalog, ticketConfig, SelectEveryFood(catalog), ticketsRequiredForDay: 5, seed: 1);

            Assert.AreEqual(5, result.Length);
        }

        [Test]
        public void Generate_SameSeed_ProducesIdenticalOutput()
        {
            var first = DayContentGenerator.Generate(catalog, ticketConfig, SelectEveryFood(catalog), ticketsRequiredForDay: 5, seed: 42);
            var second = DayContentGenerator.Generate(catalog, ticketConfig, SelectEveryFood(catalog), ticketsRequiredForDay: 5, seed: 42);

            Assert.AreEqual(ToComparableJson(first), ToComparableJson(second));
        }

        [Test]
        public void Generate_WithDayTicketGenerationSettings_ForcesInclusionExtremes()
        {
            var editorMeta = SelectEveryFood(catalog);
            editorMeta.ticketGeneration = new TicketGenerationJson
            {
                sideInclusionChance = 0f,
                drinkInclusionChance = 1f,
                modificationCountLambda = 0f,
                modificationAdditionChance = 0.5f,
            };

            var result = DayContentGenerator.Generate(catalog, ticketConfig, editorMeta, ticketsRequiredForDay: 5, seed: 3);

            foreach (var entry in result)
            {
                Assert.IsTrue(string.IsNullOrEmpty(entry.sideItemId), "Side inclusion chance was overridden to 0 -- no ticket should have a side item.");
                Assert.IsFalse(string.IsNullOrEmpty(entry.drinkItemId), "Drink inclusion chance was overridden to 1 -- every ticket should have a drink item.");
            }
        }

        [Test]
        public void Generate_WithFoodSelection_OnlyRollsSelectedItems()
        {
            var allowedMain = CreateFoodItem("main-allowed", FoodCategory.Main);
            var excludedMain = CreateFoodItem("main-excluded", FoodCategory.Main);
            var allowedSide = CreateFoodItem("side-allowed", FoodCategory.Side);
            var excludedSide = CreateFoodItem("side-excluded", FoodCategory.Side);

            var selectiveCatalog = ScriptableObject.CreateInstance<FoodCatalog>();
            spawned.Add(selectiveCatalog);
            SetItemsList(selectiveCatalog, allowedMain, excludedMain, allowedSide, excludedSide);

            var editorMeta = new DayEditorMetaJson
            {
                allowedFoodItemIds = new[] { allowedMain.Id, allowedSide.Id },
                // Forcing a side onto every ticket is what makes the side assertion mean
                // something -- at the default inclusion chance it could pass vacuously by
                // never rolling a side at all.
                ticketGeneration = new TicketGenerationJson
                {
                    sideInclusionChance = 1f,
                    drinkInclusionChance = 0f,
                    modificationCountLambda = 0f,
                    modificationAdditionChance = 0.5f,
                },
            };

            var result = DayContentGenerator.Generate(selectiveCatalog, ticketConfig, editorMeta, ticketsRequiredForDay: 20, seed: 4);

            Assert.AreEqual(20, result.Length);
            foreach (var entry in result)
            {
                Assert.AreEqual(allowedMain.Id, entry.mainItemId, "An excluded Main dish was rolled.");
                Assert.AreEqual(allowedSide.Id, entry.sideItemId, "An excluded Side item was rolled.");
            }
        }

        // The selection IS the Day's food set, so "nothing selected" must mean an empty
        // Day, not a silent fallback to the whole catalog -- that fallback is exactly the
        // behaviour the master on/off toggle used to provide and that was removed.
        [Test]
        public void ResolveFoodPool_NothingSelected_ReturnsEmptyPool()
        {
            CollectionAssert.IsEmpty(DayContentGenerator.ResolveFoodPool(catalog, null));
            CollectionAssert.IsEmpty(DayContentGenerator.ResolveFoodPool(catalog, new DayEditorMetaJson()));
            CollectionAssert.IsEmpty(DayContentGenerator.ResolveFoodPool(
                catalog, new DayEditorMetaJson { allowedFoodItemIds = Array.Empty<string>() }));
        }

        [Test]
        public void ResolveFoodPool_KeepsSelectedIdsAndDropsUnknownOnes()
        {
            var editorMeta = new DayEditorMetaJson
            {
                // "gone" stands in for an id left behind by a FoodItemConfig that was
                // renamed or deleted after the Day was authored: it must drop out of the
                // pool, not resolve to a null entry TicketFactory would trip over.
                allowedFoodItemIds = new[] { "side", "gone" },
            };

            var pool = DayContentGenerator.ResolveFoodPool(catalog, editorMeta);

            Assert.AreEqual(1, pool.Count);
            Assert.AreEqual("side", pool[0].Id);
        }

        [Test]
        public void ResolveFoodPool_EverythingSelected_ReturnsCatalogOrder()
        {
            var pool = DayContentGenerator.ResolveFoodPool(catalog, SelectEveryFood(catalog));

            CollectionAssert.AreEqual(catalog.Items, pool);
        }

        [Test]
        public void Generate_ThenPlayback_TicketContentMatchesGeneratedSequence()
        {
            var result = DayContentGenerator.Generate(catalog, ticketConfig, SelectEveryFood(catalog), ticketsRequiredForDay: 8, seed: 7);

            var dayJson = new DayJson
            {
                runtime = new DayRuntimeJson
                {
                    dayIndex = 0,
                    ticketsRequiredForDay = 8,
                    boardDistribution = ValidBoardDistribution(),
                    ticketRuntime = ValidTicketRuntime(),
                    ticketSequence = result,
                    boardTimeline = Array.Empty<BoardSpawnEntryJson>(),
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

        // DayCatalogParser drops a Day whose runtime.boardDistribution block is missing. This
        // suite only round-trips through the parser to prove the generated ticketSequence
        // survives it, so the values just have to be structurally valid.
        private static BoardDistributionJson ValidBoardDistribution()
        {
            return new BoardDistributionJson
            {
                guaranteedTicketCountMode = "Manual",
                guaranteedTicketCount = 1,
                leakDepth = 10,
                maxLeakCount = 10,
            };
        }

        // DayCatalogParser drops a Day whose runtime.ticketRuntime block is missing or has
        // a non-positive time limit. Values are the designed defaults; this suite does not
        // assert on them, it just needs the Day to be loadable.
        private static TicketRuntimeJson ValidTicketRuntime()
        {
            return new TicketRuntimeJson
            {
                impatientTimeLimitSeconds = 45f,
                normalTimeLimitSeconds = 90f,
                patientTimeLimitSeconds = 150f,
                upcomingQueueSize = 10,
            };
        }

        private static string ToComparableJson(TicketEntryJson[] result)
        {
            return JsonUtility.ToJson(new ComparableResult { ticketSequence = result });
        }

        // A Day's food selection is absolute -- an unset one means an empty Day -- so a
        // test that cares about some other aspect of generation still has to say which
        // foods exist. This is the "everything in the catalog" shorthand for those.
        private static DayEditorMetaJson SelectEveryFood(FoodCatalog target)
        {
            var ids = new string[target.Items.Count];
            for (var i = 0; i < ids.Length; i++) ids[i] = target.Items[i].Id;
            return new DayEditorMetaJson { allowedFoodItemIds = ids };
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
