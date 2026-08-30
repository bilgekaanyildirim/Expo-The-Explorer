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

        // --- the Day Start board must serve one of the tickets on screen ---------------
        //
        // The rule this suite is protecting: a Day that authors an opening board opens with
        // exactly that board (GameManager suppresses the opening distribution), so the board
        // itself has to be able to serve at least one of the first TicketSlotCount tickets.
        // Every case below keeps ticketsRequiredForDay equal to the sequence length and
        // passes a null food selection, so each test fails on its own rule and no other.

        [Test]
        public void Validate_DayStartBoardServesTheFirstTicket_NoError()
        {
            var day = DayWithBoard(
                new List<ResolvedTicketEntry> { CreateEntry(), CreateEntry(), CreateEntry() },
                DayStartEntry(main));

            var result = DayValidator.Validate(day, null);

            CollectionAssert.IsEmpty(result.Errors);
        }

        [Test]
        public void Validate_DayStartBoardServesNoTicket_ReportsError()
        {
            var somethingElse = CreateFoodItem("somethingelse");
            var day = DayWithBoard(
                new List<ResolvedTicketEntry> { CreateEntry(), CreateEntry(), CreateEntry() },
                DayStartEntry(somethingElse));

            var result = DayValidator.Validate(day, null);

            Assert.IsFalse(result.IsValid, "A board that serves nothing on screen is an opening with no move in it.");
            Assert.IsTrue(HasErrorContaining(result, "none of the first 3 ticket(s) can be completed"));
        }

        // The boundary the whole rule turns on: only the tickets the player can SEE at open
        // count. A board stocked for the fourth ticket leaves the opening screen unplayable
        // just as surely as an empty one.
        [Test]
        public void Validate_DayStartBoardServesOnlyATicketPastTheThirdSlot_ReportsError()
        {
            var fourthTicketFood = CreateFoodItem("fourth");
            var ticketSequence = new List<ResolvedTicketEntry>
            {
                CreateEntry(), CreateEntry(), CreateEntry(),
                new(fourthTicketFood, null, null, new List<Modification>(), PatienceType.Normal, null, 0f),
            };

            var result = DayValidator.Validate(DayWithBoard(ticketSequence, DayStartEntry(fourthTicketFood)), null);

            Assert.IsFalse(result.IsValid);
            Assert.IsTrue(HasErrorContaining(result, "none of the first 3 ticket(s) can be completed"));
        }

        // A Day shorter than the slot row is judged on the tickets it actually has, and the
        // message says so rather than claiming three.
        [Test]
        public void Validate_FewerTicketsThanSlots_JudgesOnlyTheTicketsThatExist()
        {
            var somethingElse = CreateFoodItem("somethingelse");
            var day = DayWithBoard(new List<ResolvedTicketEntry> { CreateEntry() }, DayStartEntry(somethingElse));

            var result = DayValidator.Validate(day, null);

            Assert.IsFalse(result.IsValid);
            Assert.IsTrue(HasErrorContaining(result, "none of the first 1 ticket(s) can be completed"));
        }

        // The rule fires on an authored opening board and on nothing else: with no board,
        // BoardDistributor opens the Day exactly as it always has, and there is no authored
        // arrangement for this gate to hold to a standard.
        [Test]
        public void Validate_NoDayStartBoard_SkipsTheRuleEntirely()
        {
            var day = DayWithBoard(new List<ResolvedTicketEntry> { CreateEntry(), CreateEntry(), CreateEntry() });

            CollectionAssert.IsEmpty(DayValidator.Validate(day, null).Errors);
        }

        // Later timeline entries are not the opening board -- they play on a step that has
        // not happened yet, so they can neither suppress the distribution nor satisfy this.
        [Test]
        public void Validate_BoardEntriesOnlyOnLaterSteps_SkipsTheRuleEntirely()
        {
            var day = DayWithBoard(
                new List<ResolvedTicketEntry> { CreateEntry(), CreateEntry(), CreateEntry() },
                new ResolvedBoardSpawnEntry(0, main, new List<Modification>(), true, 0, 0));

            CollectionAssert.IsEmpty(DayValidator.Validate(day, null).Errors);
        }

        // Judged as a delivery is judged: every required item, in the required number.
        [Test]
        public void Validate_DayStartBoardMissesTheTicketsSideItem_ReportsError()
        {
            var side = CreateFoodItem("side", FoodCategory.Side);
            var ticket = new ResolvedTicketEntry(main, side, null, new List<Modification>(), PatienceType.Normal, null, 0f);

            var result = DayValidator.Validate(DayWithBoard(new List<ResolvedTicketEntry> { ticket }, DayStartEntry(main)), null);

            Assert.IsFalse(result.IsValid, "Main alone does not complete a ticket that also wants a side.");
        }

        [Test]
        public void Validate_DayStartBoardHasBothOfTheTicketsItems_NoError()
        {
            var side = CreateFoodItem("side", FoodCategory.Side);
            var ticket = new ResolvedTicketEntry(main, side, null, new List<Modification>(), PatienceType.Normal, null, 0f);
            var day = DayWithBoard(new List<ResolvedTicketEntry> { ticket }, DayStartEntry(main), DayStartEntry(side));

            CollectionAssert.IsEmpty(DayValidator.Validate(day, null).Errors);
        }

        // A ticket wanting two colas is not served by one -- the board is compared as a
        // multiset, the same way TraySlot compares a delivered tray.
        [Test]
        public void Validate_DayStartBoardHasOneCopyOfAnItemTheTicketWantsTwice_ReportsError()
        {
            var drink = CreateFoodItem("drink", FoodCategory.Drink);
            // Side and drink slots holding the same food: one ticket, two of that item.
            var ticket = new ResolvedTicketEntry(main, drink, drink, new List<Modification>(), PatienceType.Normal, null, 0f);
            var day = DayWithBoard(new List<ResolvedTicketEntry> { ticket }, DayStartEntry(main), DayStartEntry(drink));

            Assert.IsFalse(DayValidator.Validate(day, null).IsValid);
        }

        // Modifications are part of the identity, exactly as they are at the tray check: a
        // plain burger does not serve a ticket that ordered extra cheese, and an item
        // carrying a modification nobody asked for does not serve a plain one.
        [Test]
        public void Validate_DayStartBoardItemLacksTheTicketsModification_ReportsError()
        {
            var extraCheese = new Modification(CreateModificationConfig(), true);
            var ticket = new ResolvedTicketEntry(main, null, null, new List<Modification> { extraCheese }, PatienceType.Normal, null, 0f);

            var result = DayValidator.Validate(DayWithBoard(new List<ResolvedTicketEntry> { ticket }, DayStartEntry(main)), null);

            Assert.IsFalse(result.IsValid);
        }

        [Test]
        public void Validate_DayStartBoardItemCarriesTheTicketsModification_NoError()
        {
            var config = CreateModificationConfig();
            var ticket = new ResolvedTicketEntry(
                main, null, null, new List<Modification> { new(config, true) }, PatienceType.Normal, null, 0f);
            var day = DayWithBoard(
                new List<ResolvedTicketEntry> { ticket },
                DayStartEntry(main, new List<Modification> { new(config, true) }));

            CollectionAssert.IsEmpty(DayValidator.Validate(day, null).Errors);
        }

        // An entry without Use Exact Cell lands wherever the board has room, but it IS on
        // the board -- which is all this rule asks. (The tutorial rule above asks about a
        // cell and does require the flag; the two questions are different.)
        [Test]
        public void Validate_DayStartBoardEntryWithoutAnExactCell_StillCounts()
        {
            var day = DayWithBoard(
                new List<ResolvedTicketEntry> { CreateEntry() },
                new ResolvedBoardSpawnEntry(-1, main, new List<Modification>(), false, 0, 0));

            CollectionAssert.IsEmpty(DayValidator.Validate(day, null).Errors);
        }

        private static ResolvedBoardSpawnEntry DayStartEntry(FoodItemConfig item, List<Modification> modifications = null)
        {
            return new ResolvedBoardSpawnEntry(-1, item, modifications ?? new List<Modification>(), true, 0, 0);
        }

        // ticketsRequiredForDay tracks the sequence length so the count rule stays quiet and
        // each board test fails on the board rule alone.
        private static DayDefinition DayWithBoard(
            List<ResolvedTicketEntry> ticketSequence,
            params ResolvedBoardSpawnEntry[] boardTimeline)
        {
            return new DayDefinition(0, ticketSequence.Count, ticketSequence, new List<ResolvedBoardSpawnEntry>(boardTimeline));
        }

        // --- settings blocks (day-config-plan step 6) ---------------------------------

        // The one range error that can actually fire: TicketRuntimeSettings clamps with
        // Max(0f, x), so a 0 survives into the validator. The other range rules that used to
        // sit beside this one were removed -- their constructors clamp the value away before
        // the validator ever sees it, so they could never fail.
        [Test]
        public void Validate_ZeroTimeLimit_IsAnError()
        {
            var day = DayWithSettings(ticketRuntime: new TicketRuntimeSettings(45f, 0f, 150f, 10));

            var result = DayValidator.Validate(day, null);

            Assert.IsFalse(result.IsValid);
            Assert.IsTrue(HasErrorContaining(result, "time limit must be greater than 0"));
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



        [Test]
        public void Validate_WellFormedSettings_ProducesNoWarnings()
        {
            var day = DayWithSettings();

            var result = DayValidator.Validate(day, null);

            Assert.IsTrue(result.IsValid);
            CollectionAssert.IsEmpty(result.Warnings);
        }

        // Authoring-side callers build a DayDefinition with no settings blocks at all (the
        // ctor leaves them null); the rules must skip rather than throw.
        [Test]
        public void Validate_NoSettingsBlocks_DoesNotThrowOrComplainAboutThem()
        {
            var day = new DayDefinition(0, 0, new List<ResolvedTicketEntry>(), new List<ResolvedBoardSpawnEntry>());

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
            BoardDistributionSettings board = null)
        {
            return new DayDefinition(
                0, 0, new List<ResolvedTicketEntry>(), new List<ResolvedBoardSpawnEntry>(),
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

        // The category is not decoration here: TicketRequirements attaches a ticket's
        // modifications to the Main dish and to nothing else, so a side authored as a Main
        // would be compared against a different key than the one the game would build.
        private FoodItemConfig CreateFoodItem(string id, FoodCategory category = FoodCategory.Main)
        {
            var item = ScriptableObject.CreateInstance<FoodItemConfig>();
            spawned.Add(item);

            var serialized = new SerializedObject(item);
            serialized.FindProperty("id").stringValue = id;
            serialized.FindProperty("category").enumValueIndex = (int)category;
            serialized.ApplyModifiedPropertiesWithoutUndo();

            return item;
        }

        // No fields set on purpose: RequiredItemKey compares modifications by config
        // REFERENCE plus direction, so a bare instance is a complete identity for a test.
        private ModificationConfig CreateModificationConfig()
        {
            var config = ScriptableObject.CreateInstance<ModificationConfig>();
            spawned.Add(config);
            return config;
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
