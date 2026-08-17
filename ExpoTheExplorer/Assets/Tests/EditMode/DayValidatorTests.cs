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
                    ticketRuntime = ValidTicketRuntime(),
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

        private static bool HasErrorContaining(DayValidationResult result, string substring)
        {
            foreach (var error in result.Errors)
            {
                if (error.Contains(substring)) return true;
            }
            return false;
        }

        // --- settings blocks (day-config-plan step 6) ---------------------------------

        // Errors here mirror DayCatalogParser's rejection rules, so the editor cannot save a
        // Day the runtime would then refuse to load.
        [Test]
        public void Validate_ZeroTimeLimit_IsAnError()
        {
            var day = DayWithSettings(ticketRuntime: new TicketRuntimeSettings(45f, 0f, 150f, 10));

            var result = DayValidator.Validate(day, null);

            Assert.IsFalse(result.IsValid);
            Assert.IsTrue(HasErrorContaining(result, "time limit must be greater than 0"));
        }

        [Test]
        public void Validate_ZeroQueueSize_IsAnError()
        {
            var day = DayWithSettings(ticketRuntime: new TicketRuntimeSettings(45f, 90f, 150f, 0));

            var result = DayValidator.Validate(day, null);

            Assert.IsFalse(result.IsValid);
            Assert.IsTrue(HasErrorContaining(result, "Upcoming Queue Size"));
        }

        // Warnings must never close the Save button -- that is the whole reason they are a
        // separate channel.
        [Test]
        public void Validate_TimeLimitsOutOfGddOrder_WarnsButStaysValid()
        {
            var day = DayWithSettings(ticketRuntime: new TicketRuntimeSettings(150f, 90f, 45f, 10));

            var result = DayValidator.Validate(day, null);

            Assert.IsTrue(result.IsValid, "Out-of-order limits still play; this is a design warning, not a fault.");
            Assert.IsTrue(HasWarningContaining(result, "Impatient < Normal < Patient"));
        }

        [Test]
        public void Validate_LeakDepthPastQueueSize_WarnsButStaysValid()
        {
            var day = DayWithSettings(
                ticketRuntime: new TicketRuntimeSettings(45f, 90f, 150f, 3),
                board: CreateBoardSettings(leakDepth: 10));

            var result = DayValidator.Validate(day, null);

            Assert.IsTrue(result.IsValid);
            Assert.IsTrue(HasWarningContaining(result, "reaches past Upcoming Queue Size"));
        }

        // The state both shipped Days are in: every attempt scores 3 stars.
        [Test]
        public void Validate_AllStarThresholdsZero_WarnsButStaysValid()
        {
            var result = DayValidator.Validate(DayWithSettings(), null);

            Assert.IsTrue(result.IsValid);
            Assert.IsTrue(HasWarningContaining(result, "every attempt earns 3 stars"));
        }

        [Test]
        public void Validate_StarThresholdsNotAscending_WarnsButStaysValid()
        {
            var day = DayWithSettings(star1: 300, star2: 200, star3: 100);

            var result = DayValidator.Validate(day, null);

            Assert.IsTrue(result.IsValid);
            Assert.IsTrue(HasWarningContaining(result, "expected 1 < 2 < 3 stars"));
        }

        [Test]
        public void Validate_WellFormedSettings_ProducesNoWarnings()
        {
            var day = DayWithSettings(star1: 100, star2: 200, star3: 300);

            var result = DayValidator.Validate(day, null);

            Assert.IsTrue(result.IsValid);
            CollectionAssert.IsEmpty(result.Warnings);
        }

        // Authoring-side callers build a DayDefinition with no settings blocks at all (the
        // ctor leaves them null); the rules must skip rather than throw.
        [Test]
        public void Validate_NoSettingsBlocks_DoesNotThrowOrComplainAboutThem()
        {
            var day = new DayDefinition(0, 0, new List<ResolvedTicketEntry>(), new List<ResolvedBoardSpawnEntry>(),
                star1Threshold: 100, star2Threshold: 200, star3Threshold: 300);

            DayValidationResult result = null;
            Assert.DoesNotThrow(() => result = DayValidator.Validate(day, null));
            Assert.IsTrue(result.IsValid);
            CollectionAssert.IsEmpty(result.Warnings);
        }

        private static BoardDistributionSettings CreateBoardSettings(int leakDepth = 10) => new(
            noiseLeakCountLambda: 0.5f,
            guaranteedTicketCountMode: GuaranteedTicketCountMode.Manual,
            guaranteedTicketCount: 1,
            guaranteedTicketCountLambda: 1f,
            earlyTicketWeightDecay: 0.5f,
            urgentTimeThresholdSeconds: 10f,
            leakDepth: leakDepth,
            maxLeakCount: 10);

        // Empty ticket sequence with ticketsRequiredForDay 0 keeps the pre-existing count
        // rule quiet, so each test above asserts on its own rule alone.
        private static DayDefinition DayWithSettings(
            TicketRuntimeSettings ticketRuntime = null,
            BoardDistributionSettings board = null,
            int star1 = 0, int star2 = 0, int star3 = 0)
        {
            return new DayDefinition(
                0, 0, new List<ResolvedTicketEntry>(), new List<ResolvedBoardSpawnEntry>(),
                star1, star2, star3,
                board ?? CreateBoardSettings(),
                ticketRuntime ?? new TicketRuntimeSettings(45f, 90f, 150f, 10));
        }

        private static bool HasWarningContaining(DayValidationResult result, string substring)
        {
            foreach (var warning in result.Warnings)
            {
                if (warning.Contains(substring)) return true;
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
