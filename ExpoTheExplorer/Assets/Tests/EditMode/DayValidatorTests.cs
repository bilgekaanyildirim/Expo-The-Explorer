using System.Collections.Generic;
using ExpoTheExplorer.Core;
using ExpoTheExplorer.Data;
using ExpoTheExplorer.Systems.DaySystem;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace ExpoTheExplorer.Tests.EditMode
{
    public class DayValidatorTests
    {
        private readonly List<UnityEngine.Object> spawned = new();

        private FoodItemConfig main;
        private GameConfig gameConfig;
        private TicketGenerationConfig ticketConfig;

        [SetUp]
        public void SetUp()
        {
            main = CreateFoodItem("main");
            gameConfig = CreateGameConfig(6, 5);
            ticketConfig = CreateTicketGenerationConfig(upcomingQueueSize: 3);
        }

        [TearDown]
        public void TearDown()
        {
            foreach (var o in spawned) UnityEngine.Object.DestroyImmediate(o);
            spawned.Clear();
        }

        [Test]
        public void Validate_TicketCountMismatch_ReportsError()
        {
            var ticketSequence = new List<ResolvedTicketEntry> { CreateEntry(), CreateEntry(), CreateEntry() };
            var boardTimeline = CreateBoardTimelineCoveringEachStep(3);
            var day = new DayDefinition(0, ticketsRequiredForDay: 5, ticketSequence, boardTimeline, retryVariant: null);

            var result = DayValidator.Validate(day);

            Assert.IsFalse(result.IsValid);
            Assert.IsTrue(HasErrorContaining(result, "ticketsRequiredForDay is 5"));
        }

        [Test]
        public void Validate_UnplayableStep_NoCompletableTicket_ReportsError()
        {
            var ticketSequence = new List<ResolvedTicketEntry> { CreateEntry(), CreateEntry(), CreateEntry() };
            var day = new DayDefinition(0, ticketsRequiredForDay: 3, ticketSequence, new List<ResolvedBoardSpawnEntry>(), retryVariant: null);

            var result = DayValidator.Validate(day);

            Assert.IsFalse(result.IsValid);
            Assert.IsTrue(HasErrorContaining(result, "Step 0:"));
        }

        [Test]
        public void Validate_PlayableDay_NoErrors()
        {
            var ticketSequence = new List<ResolvedTicketEntry> { CreateEntry(), CreateEntry(), CreateEntry() };
            var boardTimeline = CreateBoardTimelineCoveringEachStep(3);
            var day = new DayDefinition(0, ticketsRequiredForDay: 3, ticketSequence, boardTimeline, retryVariant: null);

            var result = DayValidator.Validate(day);

            CollectionAssert.IsEmpty(result.Errors);
            Assert.IsTrue(result.IsValid);
        }

        [Test]
        public void Validate_GeneratedDayIsAlwaysValid()
        {
            var catalog = ScriptableObject.CreateInstance<FoodCatalog>();
            spawned.Add(catalog);
            SetItemsList(catalog, main);

            var boardConfig = ScriptableObject.CreateInstance<BoardDistributionConfig>();
            spawned.Add(boardConfig);

            var result = DayContentGenerator.Generate(catalog, gameConfig, ticketConfig, boardConfig, null, ticketsRequiredForDay: 10, seed: 11);

            var dayJson = new DayJson
            {
                runtime = new DayRuntimeJson
                {
                    dayIndex = 0,
                    ticketsRequiredForDay = 10,
                    ticketSequence = result.TicketSequence,
                    boardTimeline = result.BoardTimeline,
                    hasRetryVariant = false,
                    retryVariant = null,
                },
            };
            var json = JsonUtility.ToJson(dayJson);
            var parsed = DayCatalogParser.ParseAll(new[] { new DayJsonFile("test", json) }, catalog);
            var day = parsed[0];

            var validation = DayValidator.Validate(day);

            CollectionAssert.IsEmpty(validation.Errors);
        }

        [Test]
        public void Validate_RetryVariantAlsoInvalid_ReportsPrefixedError()
        {
            var validSequence = new List<ResolvedTicketEntry> { CreateEntry(), CreateEntry(), CreateEntry() };
            var validTimeline = CreateBoardTimelineCoveringEachStep(3);

            var brokenVariant = new DayDefinition(0, ticketsRequiredForDay: 3, validSequence, new List<ResolvedBoardSpawnEntry>(), retryVariant: null);
            var day = new DayDefinition(0, ticketsRequiredForDay: 3, validSequence, validTimeline, retryVariant: brokenVariant);

            var result = DayValidator.Validate(day);

            Assert.IsFalse(result.IsValid);
            Assert.IsTrue(HasErrorContaining(result, "RetryVariant: Step 0:"));
        }

        // Regression test for the field bug: 5 tickets all need a shared, plain (no
        // modification) side item, but only 3 units of it are ever spawned across the whole
        // Day -- mirrors the exact ratio found in the user's real day_00.json (9 demanders,
        // 6 supply units), scaled down. The old "board never shrinks" playability model
        // missed this entirely since it never accounted for cumulative demand on a fungible
        // RequiredItemKey; DaySolvabilityChecker must now report it.
        [Test]
        public void Validate_FungibleItemUndersupply_ReportsShortfallForLaterTickets()
        {
            var side = CreateFoodItem("side");
            var ticketSequence = new List<ResolvedTicketEntry>();
            for (var i = 0; i < 5; i++)
            {
                ticketSequence.Add(new ResolvedTicketEntry(main, side, null, new List<Modification>(), PatienceType.Normal, null, 0f));
            }

            var boardTimeline = CreateBoardTimelineCoveringEachStep(5);
            boardTimeline.Add(new ResolvedBoardSpawnEntry(0, side, new List<Modification>(), useExactCell: true, x: 0, y: 0));
            boardTimeline.Add(new ResolvedBoardSpawnEntry(1, side, new List<Modification>(), useExactCell: true, x: 0, y: 0));
            boardTimeline.Add(new ResolvedBoardSpawnEntry(2, side, new List<Modification>(), useExactCell: true, x: 0, y: 0));

            var day = new DayDefinition(0, ticketsRequiredForDay: 5, ticketSequence, boardTimeline, retryVariant: null);

            var result = DayValidator.Validate(day);

            Assert.IsFalse(result.IsValid);
            Assert.IsTrue(HasErrorContaining(result, "Step 3:"));
            Assert.IsTrue(HasErrorContaining(result, "Step 4:"));
            Assert.IsTrue(HasErrorContaining(result, "'side'"));
        }

        [Test]
        public void Validate_MultipleIndependentErrors_ReportsAll()
        {
            var ticketSequence = new List<ResolvedTicketEntry> { CreateEntry(), CreateEntry(), CreateEntry() };
            var day = new DayDefinition(0, ticketsRequiredForDay: 5, ticketSequence, new List<ResolvedBoardSpawnEntry>(), retryVariant: null);

            var result = DayValidator.Validate(day);

            Assert.IsTrue(HasErrorContaining(result, "ticketsRequiredForDay is 5"));
            Assert.IsTrue(HasErrorContaining(result, "Step 0:"));
        }

        private static bool HasErrorContaining(DayValidationResult result, string substring)
        {
            foreach (var error in result.Errors)
            {
                if (error.Contains(substring)) return true;
            }
            return false;
        }

        private ResolvedTicketEntry CreateEntry()
        {
            return new ResolvedTicketEntry(main, null, null, new List<Modification>(), PatienceType.Normal, null, 0f);
        }

        // Every CreateEntry() ticket requires exactly the shared `main` item with no
        // modifications, so a board timeline that spawns one `main` per step (at a fresh
        // cell each time) satisfies completability at every step.
        private List<ResolvedBoardSpawnEntry> CreateBoardTimelineCoveringEachStep(int stepCount)
        {
            var entries = new List<ResolvedBoardSpawnEntry>();
            for (var step = 0; step < stepCount; step++)
            {
                entries.Add(new ResolvedBoardSpawnEntry(step, main, new List<Modification>(), useExactCell: true, x: step % 6, y: step / 6));
            }
            return entries;
        }

        private FoodItemConfig CreateFoodItem(string id)
        {
            var item = ScriptableObject.CreateInstance<FoodItemConfig>();
            spawned.Add(item);

            var serialized = new SerializedObject(item);
            serialized.FindProperty("id").stringValue = id;
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

        private TicketGenerationConfig CreateTicketGenerationConfig(int upcomingQueueSize)
        {
            var config = ScriptableObject.CreateInstance<TicketGenerationConfig>();
            spawned.Add(config);

            var serialized = new SerializedObject(config);
            serialized.FindProperty("upcomingQueueSize").intValue = upcomingQueueSize;
            serialized.ApplyModifiedPropertiesWithoutUndo();

            return config;
        }
    }
}
