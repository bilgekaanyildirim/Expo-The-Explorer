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
        private GameConfig gameConfig;
        private TicketGenerationConfig ticketConfig;
        private BoardDistributionConfig boardConfig;

        [SetUp]
        public void SetUp()
        {
            var main = CreateFoodItem("main", FoodCategory.Main);
            var side = CreateFoodItem("side", FoodCategory.Side);
            var drink = CreateFoodItem("drink", FoodCategory.Drink);

            catalog = ScriptableObject.CreateInstance<FoodCatalog>();
            spawned.Add(catalog);
            SetItemsList(catalog, main, side, drink);

            gameConfig = CreateGameConfig(6, 5);
            ticketConfig = CreateTicketGenerationConfig(withNamesDatabase: true);
            boardConfig = ScriptableObject.CreateInstance<BoardDistributionConfig>();
            spawned.Add(boardConfig);
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
            var result = DayContentGenerator.Generate(catalog, gameConfig, ticketConfig, boardConfig, null, ticketsRequiredForDay: 5, seed: 1);

            Assert.AreEqual(5, result.TicketSequence.Length);
        }

        [Test]
        public void Generate_SameSeed_ProducesIdenticalOutput()
        {
            var first = DayContentGenerator.Generate(catalog, gameConfig, ticketConfig, boardConfig, null, ticketsRequiredForDay: 5, seed: 42);
            var second = DayContentGenerator.Generate(catalog, gameConfig, ticketConfig, boardConfig, null, ticketsRequiredForDay: 5, seed: 42);

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

            var result = DayContentGenerator.Generate(catalog, gameConfig, ticketConfig, boardConfig, editorMeta, ticketsRequiredForDay: 5, seed: 3);

            foreach (var entry in result.TicketSequence)
            {
                Assert.IsTrue(string.IsNullOrEmpty(entry.sideItemId), "Side inclusion chance was overridden to 0 -- no ticket should have a side item.");
                Assert.IsFalse(string.IsNullOrEmpty(entry.drinkItemId), "Drink inclusion chance was overridden to 1 -- every ticket should have a drink item.");
            }
        }

        [Test]
        public void Generate_LongDayOnSmallBoard_DeliverySimulationPreventsCapacityStarvation()
        {
            var smallGameConfig = CreateGameConfig(5, 4); // 20 cells -- would starve fast under a no-removal model
            var smallCatalog = ScriptableObject.CreateInstance<FoodCatalog>();
            spawned.Add(smallCatalog);
            SetItemsList(smallCatalog,
                CreateFoodItem("main-a", FoodCategory.Main),
                CreateFoodItem("main-b", FoodCategory.Main),
                CreateFoodItem("main-c", FoodCategory.Main));

            var result = DayContentGenerator.Generate(smallCatalog, smallGameConfig, ticketConfig, boardConfig, null, ticketsRequiredForDay: 30, seed: 5);

            Assert.AreEqual(30, result.TicketSequence.Length);

            // EnsureSolvable's repair pass places patches via a simple additive-only replay
            // (see DayContentGenerator's own comments) that has no concept of items being
            // removed by real delivery -- on a board this small relative to a 30-ticket Day,
            // nearly every cell has been "touched" by the time a late shortfall needs
            // patching, so the replay can see the board as full even though it wouldn't be
            // under real, removal-aware play. This is a known, accepted limitation (see the
            // roadmap's Bugfix note and DayContentGenerator.EnsureSolvable), not a crash or a
            // wrong ticket count -- so only that specific fallback message is tolerated here.
            foreach (var warning in result.Warnings)
            {
                Assert.IsTrue(warning.Contains("board is full at that point"), $"Unexpected warning: {warning}");
            }
        }

        // Regression test for the field bug: a rich (Main+Side+Drink) catalog with every
        // ticket forced to include a side and drink -- both fully fungible (no modifications,
        // one item each in the catalog) -- across a Day long enough that the main generation
        // loop's per-step guarantee (which only tops up the CURRENTLY active tickets) would,
        // pre-fix, leave later same-key demanders without their own supply. The repair pass in
        // DayContentGenerator.GenerateCore must close every such shortfall.
        [Test]
        public void Generate_ManyTicketsNeedingSameSideItem_NeverUndersuppliesFungibleItems()
        {
            var largeGameConfig = CreateGameConfig(10, 10); // plenty of room for repair-pass patches
            var editorMeta = new DayEditorMetaJson
            {
                hasTicketGenerationOverride = true,
                sideInclusionChanceOverride = 1f,
                drinkInclusionChanceOverride = 1f,
                modificationCountLambdaOverride = 0f,
            };

            var result = DayContentGenerator.Generate(catalog, largeGameConfig, ticketConfig, boardConfig, editorMeta, ticketsRequiredForDay: 25, seed: 9);

            var dayJson = new DayJson
            {
                runtime = new DayRuntimeJson
                {
                    dayIndex = 0,
                    ticketsRequiredForDay = 25,
                    ticketSequence = result.TicketSequence,
                    boardTimeline = result.BoardTimeline,
                    hasRetryVariant = false,
                    retryVariant = null,
                },
            };
            var json = JsonUtility.ToJson(dayJson);
            var parsed = DayCatalogParser.ParseAll(new[] { new DayJsonFile("test", json) }, catalog);
            var day = parsed[0];

            var shortfalls = DaySolvabilityChecker.FindShortfalls(day.TicketSequence, day.BoardTimeline);

            CollectionAssert.IsEmpty(shortfalls);
            CollectionAssert.IsEmpty(result.Warnings);
        }

        [Test]
        public void Generate_ThenSerializeParseAndPlayback_BoardMatchesFrozenCoordinates()
        {
            var (result, _, playbackBoard) = GenerateAndPlayback(ticketsRequiredForDay: 8, seed: 7);

            Assert.AreEqual(result.BoardTimeline.Length, playbackBoard.OccupiedCellCount);
            foreach (var entry in result.BoardTimeline)
            {
                var item = playbackBoard.ItemAt(entry.x, entry.y);
                Assert.IsNotNull(item, $"Expected an item at ({entry.x},{entry.y}) from step {entry.triggerStepIndex}.");
                Assert.AreEqual(entry.itemId, item.Config.Id);
            }
        }

        [Test]
        public void Generate_ThenPlayback_TicketContentMatchesGeneratedSequence()
        {
            var (result, dayDefinition, _) = GenerateAndPlayback(ticketsRequiredForDay: 8, seed: 7);

            Assert.AreEqual(result.TicketSequence.Length, dayDefinition.TicketSequence.Count);
            for (var i = 0; i < result.TicketSequence.Length; i++)
            {
                var expected = result.TicketSequence[i];
                var resolved = dayDefinition.TicketSequence[i];

                Assert.AreEqual(expected.mainItemId, resolved.MainItem.Id);
                Assert.AreEqual(string.IsNullOrEmpty(expected.sideItemId) ? null : expected.sideItemId, resolved.SideItem?.Id);
                Assert.AreEqual(string.IsNullOrEmpty(expected.drinkItemId) ? null : expected.drinkItemId, resolved.DrinkItem?.Id);
                Assert.AreEqual(expected.patienceType, resolved.PatienceType.ToString());
            }
        }

        private (DayContentGenerationResult Result, DayDefinition DayDefinition, BoardGrid PlaybackBoard) GenerateAndPlayback(int ticketsRequiredForDay, int seed)
        {
            var result = DayContentGenerator.Generate(catalog, gameConfig, ticketConfig, boardConfig, null, ticketsRequiredForDay, seed);

            var dayJson = new DayJson
            {
                runtime = new DayRuntimeJson
                {
                    dayIndex = 0,
                    ticketsRequiredForDay = ticketsRequiredForDay,
                    ticketSequence = result.TicketSequence,
                    boardTimeline = result.BoardTimeline,
                    hasRetryVariant = false,
                    retryVariant = null,
                },
            };
            var json = JsonUtility.ToJson(dayJson);
            var parsed = DayCatalogParser.ParseAll(new[] { new DayJsonFile("test", json) }, catalog);
            var dayDefinition = parsed[0];

            var playbackState = new GameState(gameConfig);
            var playbackTicketFactory = new TicketFactory(ticketConfig, new System.Random(1));
            var sequenceProvider = new DayTicketSequenceProvider(dayDefinition.TicketSequence, ticketConfig, playbackTicketFactory);

            for (var i = 0; i < ticketsRequiredForDay; i++)
            {
                var ticket = sequenceProvider.NextTicket();
                DayBoardTimelinePlayer.ApplyForStep(playbackState.Board, dayDefinition.BoardTimeline, ticket.ArrivalSequence);
            }

            return (result, dayDefinition, playbackState.Board);
        }

        [Serializable]
        private class ComparableResult
        {
            public TicketEntryJson[] ticketSequence;
            public BoardSpawnEntryJson[] boardTimeline;
        }

        private static string ToComparableJson(DayContentGenerationResult result)
        {
            return JsonUtility.ToJson(new ComparableResult { ticketSequence = result.TicketSequence, boardTimeline = result.BoardTimeline });
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

        private GameConfig CreateGameConfig(int boardWidth, int boardHeight)
        {
            var config = ScriptableObject.CreateInstance<GameConfig>();
            spawned.Add(config);

            var serialized = new SerializedObject(config);
            serialized.FindProperty("boardWidth").intValue = boardWidth;
            serialized.FindProperty("boardHeight").intValue = boardHeight;
            serialized.ApplyModifiedPropertiesWithoutUndo();

            return config;
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
