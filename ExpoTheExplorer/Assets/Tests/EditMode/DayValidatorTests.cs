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
        private TicketGenerationConfig ticketConfig;

        [SetUp]
        public void SetUp()
        {
            main = CreateFoodItem("main");
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
            var day = new DayDefinition(0, ticketsRequiredForDay: 5, ticketSequence, new List<ResolvedBoardSpawnEntry>());

            var result = DayValidator.Validate(day, new List<FoodItemConfig> { main });

            Assert.IsFalse(result.IsValid);
            Assert.IsTrue(HasErrorContaining(result, "ticketsRequiredForDay is 5"));
        }

        [Test]
        public void Validate_TicketCountMatches_NoErrors()
        {
            var ticketSequence = new List<ResolvedTicketEntry> { CreateEntry(), CreateEntry(), CreateEntry() };
            var day = new DayDefinition(0, ticketsRequiredForDay: 3, ticketSequence, new List<ResolvedBoardSpawnEntry>());

            var result = DayValidator.Validate(day, new List<FoodItemConfig> { main });

            CollectionAssert.IsEmpty(result.Errors);
            Assert.IsTrue(result.IsValid);
        }

        [Test]
        public void Validate_TicketUsesFoodOutsideTheSelection_ReportsErrorPerTicket()
        {
            var unselected = CreateFoodItem("unselected");
            var ticketSequence = new List<ResolvedTicketEntry>
            {
                CreateEntry(),
                new(unselected, null, null, new List<Modification>(), PatienceType.Normal, null, 0f),
            };
            var day = new DayDefinition(0, ticketsRequiredForDay: 2, ticketSequence, new List<ResolvedBoardSpawnEntry>());

            var result = DayValidator.Validate(day, new List<FoodItemConfig> { main });

            Assert.IsFalse(result.IsValid);
            Assert.AreEqual(1, result.Errors.Count, "Only the second ticket is outside the selection.");
            Assert.IsTrue(HasErrorContaining(result, "Ticket 1: 'unselected'"));
        }

        [Test]
        public void Validate_NullSelection_SkipsTheFoodCheck()
        {
            var unselected = CreateFoodItem("unselected");
            var ticketSequence = new List<ResolvedTicketEntry>
            {
                new(unselected, null, null, new List<Modification>(), PatienceType.Normal, null, 0f),
            };
            var day = new DayDefinition(0, ticketsRequiredForDay: 1, ticketSequence, new List<ResolvedBoardSpawnEntry>());

            // null means "no catalog to resolve the selection against", not "nothing allowed".
            CollectionAssert.IsEmpty(DayValidator.Validate(day, null).Errors);
        }

        [Test]
        public void Validate_GeneratedDayIsAlwaysValid()
        {
            var catalog = ScriptableObject.CreateInstance<FoodCatalog>();
            spawned.Add(catalog);
            SetItemsList(catalog, main);

            // A Day's food selection is absolute (an unset one is an empty Day), so this
            // has to name the foods that exist before it can generate anything at all.
            var editorMeta = new DayEditorMetaJson { allowedFoodItemIds = new[] { main.Id } };
            var ticketSequence = DayContentGenerator.Generate(catalog, ticketConfig, editorMeta, ticketsRequiredForDay: 10, seed: 11);

            var dayJson = new DayJson
            {
                runtime = new DayRuntimeJson
                {
                    dayIndex = 0,
                    ticketsRequiredForDay = 10,
                    boardDistribution = ValidBoardDistribution(),
                    ticketSequence = ticketSequence,
                    boardTimeline = System.Array.Empty<BoardSpawnEntryJson>(),
                },
            };
            var json = JsonUtility.ToJson(dayJson);
            var parsed = DayCatalogParser.ParseAll(new[] { new DayJsonFile("test", json) }, catalog);
            var day = parsed[0];

            var validation = DayValidator.Validate(day, catalog.Items);

            CollectionAssert.IsEmpty(validation.Errors);
        }

        // DayCatalogParser drops a Day whose runtime.boardDistribution block is missing, so
        // any test that round-trips through it has to author one. Values are irrelevant here
        // -- DayValidator does not look at them -- they just have to be structurally valid.
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
