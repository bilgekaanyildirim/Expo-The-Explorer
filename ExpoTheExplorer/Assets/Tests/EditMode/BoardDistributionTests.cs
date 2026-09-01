using System;
using System.Collections.Generic;
using ExpoTheExplorer.Core;
using ExpoTheExplorer.Data;
using ExpoTheExplorer.Systems.BoardDistribution;
using ExpoTheExplorer.Systems.DaySystem;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace ExpoTheExplorer.Tests.EditMode
{
    public class BoardDistributionTests
    {
        // Mirrors TicketSystemTests.ExtremeLambda — large enough that
        // TruncatedPoisson.Sample deterministically returns n (all available
        // candidates), used wherever the old chance=1f meant "always leaks".
        private const float ExtremeLambda = 1_000_000f;

        private GameConfig gameConfig;
        private readonly List<UnityEngine.Object> spawnedAssets = new();

        [SetUp]
        public void SetUp()
        {
            gameConfig = ScriptableObject.CreateInstance<GameConfig>();
        }

        [TearDown]
        public void TearDown()
        {
            UnityEngine.Object.DestroyImmediate(gameConfig);
            foreach (var asset in spawnedAssets)
            {
                UnityEngine.Object.DestroyImmediate(asset);
            }
            spawnedAssets.Clear();
        }

        private FoodItemConfig CreateFoodItem(FoodCategory category = FoodCategory.Main, string id = null)
        {
            var foodConfig = ScriptableObject.CreateInstance<FoodItemConfig>();
            spawnedAssets.Add(foodConfig);

            var serialized = new SerializedObject(foodConfig);
            serialized.FindProperty("category").enumValueIndex = (int)category;
            if (id != null) serialized.FindProperty("id").stringValue = id;
            serialized.ApplyModifiedPropertiesWithoutUndo();

            return foodConfig;
        }

        private ModificationConfig CreateModification()
        {
            var modConfig = ScriptableObject.CreateInstance<ModificationConfig>();
            spawnedAssets.Add(modConfig);
            return modConfig;
        }

        // earlyTicketWeightDecay defaults to 0 (not the production default) so
        // every pre-existing test in this file -- written when selection was
        // deterministic "earliest N" -- keeps that exact behavior unchanged:
        // decay 0 gives rank >= 1 candidates exactly zero weight, so the
        // arrival-weighted lottery degenerates back to "always pick earliest"
        // (Math.Pow(0, 0) == 1, Math.Pow(0, r > 0) == 0). Same isolation
        // pattern as this file's own ExtremeLambda constant for noise-leak
        // count. urgentTimeThresholdSeconds defaults to 0 so no test ticket
        // (all use timeLimitSeconds significantly above 0) is ever
        // accidentally treated as urgent unless a test opts in explicitly.
        // Builds the plain settings BoardDistributor now takes (D-004) instead of a
        // ScriptableObject asset -- no CreateInstance, no SerializedObject reflection over
        // private field names, and nothing for TearDown to destroy. Every call site keeps
        // its named arguments unchanged; only what this returns moved.
        private static BoardDistributionSettings CreateDistributionConfig(
            float noiseLeakCountLambda, int guaranteedTicketCount = 1, int leakDepth = 10, int maxLeakCount = 10,
            GuaranteedTicketCountMode guaranteedTicketCountMode = GuaranteedTicketCountMode.Manual,
            float guaranteedTicketCountLambda = 0f, float earlyTicketWeightDecay = 0f, float urgentTimeThresholdSeconds = 0f)
        {
            return new BoardDistributionSettings(
                noiseLeakCountLambda,
                guaranteedTicketCountMode,
                guaranteedTicketCount,
                guaranteedTicketCountLambda,
                earlyTicketWeightDecay,
                urgentTimeThresholdSeconds,
                leakDepth,
                maxLeakCount);
        }

        private Ticket CreateTicket(List<FoodItemConfig> requiredItems, List<Modification> modifications = null, float timeLimitSeconds = 90f, long arrivalSequence = 0)
        {
            return new Ticket("Test Customer", PatienceType.Normal, requiredItems, modifications ?? new List<Modification>(), timeLimitSeconds, arrivalSequence);
        }

        private static int CountMatchingItemsOnBoard(BoardGrid board, FoodItemConfig food, IReadOnlyList<Modification> modifications)
        {
            var key = new RequiredItemKey(food, modifications);
            var count = 0;
            for (var x = 0; x < board.Width; x++)
            {
                for (var y = 0; y < board.Height; y++)
                {
                    var item = board.ItemAt(x, y);
                    if (item != null && new RequiredItemKey(item.Config, item.Modifications).Equals(key)) count++;
                }
            }

            return count;
        }

        // Closes the loop this whole change exists for (D-004): a number typed into a Day
        // file has to end up changing what BoardDistributor does. Everything between --
        // JSON text, DayCatalogParser, DayDefinition, BoardDistributionSettings -- is
        // exercised for real here; only GameManager's MonoBehaviour wiring is out of reach
        // of an EditMode test.
        [Test]
        public void GuaranteedTicketCountAuthoredInDayJson_ChangesHowManyTicketsGetCovered()
        {
            var burger = CreateFoodItem(FoodCategory.Main, id: "burger");
            var fries = CreateFoodItem(FoodCategory.Main, id: "fries");
            var cola = CreateFoodItem(FoodCategory.Main, id: "cola");
            var catalog = CreateCatalog(burger);

            var tickets = new[]
            {
                CreateTicket(new List<FoodItemConfig> { burger }, arrivalSequence: 0),
                CreateTicket(new List<FoodItemConfig> { fries }, arrivalSequence: 1),
                CreateTicket(new List<FoodItemConfig> { cola }, arrivalSequence: 2),
            };

            var coveredWithBudgetOne = CoveredTicketCount(ParseDayBoardDistribution(catalog, guaranteedTicketCount: 1), tickets);
            var coveredWithBudgetThree = CoveredTicketCount(ParseDayBoardDistribution(catalog, guaranteedTicketCount: 3), tickets);

            Assert.AreEqual(1, coveredWithBudgetOne, "A Day authored with guaranteedTicketCount 1 should cover exactly one ticket.");
            Assert.AreEqual(3, coveredWithBudgetThree, "A Day authored with guaranteedTicketCount 3 should cover all three.");
        }

        // Runs the real parser over real JSON text rather than hand-building settings --
        // a settings object built in-test would prove nothing about the Day file path.
        // earlyTicketWeightDecay/urgentTimeThresholdSeconds/noiseLeakCountLambda are all 0
        // so the only variable is the budget: deterministic earliest-first, no urgency
        // override, no noise items to confuse the count.
        private static BoardDistributionSettings ParseDayBoardDistribution(FoodCatalog catalog, int guaranteedTicketCount)
        {
            var dayJson = new DayJson
            {
                runtime = new DayRuntimeJson
                {
                    dayIndex = 0,
                    ticketsRequiredForDay = 1,
                    boardDistribution = new BoardDistributionJson
                    {
                        noiseLeakCountLambda = 0f,
                        guaranteedTicketCountMode = "Manual",
                        guaranteedTicketCount = guaranteedTicketCount,
                        guaranteedTicketCountLambda = 0f,
                        earlyTicketWeightDecay = 0f,
                        urgentTimeThresholdSeconds = 0f,
                        leakDepth = 10,
                        maxLeakCount = 10,
                    },
                    // Required since D-005: a Day with no ticketRuntime block is dropped by
                    // the parser. Irrelevant to what this test measures -- it only needs the
                    // Day to load so the boardDistribution block can be read back off it.
                    ticketRuntime = new TicketRuntimeJson
                    {
                        impatientTimeLimitSeconds = 45f,
                        normalTimeLimitSeconds = 90f,
                        patientTimeLimitSeconds = 150f,
                        upcomingQueueSize = 10,
                    },
                    ticketSequence = new[] { new TicketEntryJson { mainItemId = "burger", patienceType = "Normal" } },
                },
            };

            var parsed = DayCatalogParser.ParseAll(
                new[] { new DayJsonFile("test", JsonUtility.ToJson(dayJson)) }, catalog);

            Assert.AreEqual(1, parsed.Count, "Day fixture failed to parse -- the test is broken, not the code.");
            return parsed[0].BoardDistribution;
        }

        // A fresh board per call: the two budgets have to be measured independently, and
        // BoardDistributor never un-spawns, so reusing one board would let the first run's
        // items count toward the second.
        private int CoveredTicketCount(BoardDistributionSettings settings, IReadOnlyList<Ticket> tickets)
        {
            var state = new GameState(gameConfig);
            new BoardDistributor(state, settings).OnOrderPlaced(tickets, Array.Empty<Ticket>());

            var covered = 0;
            foreach (var ticket in tickets)
            {
                if (CountMatchingItemsOnBoard(state.Board, ticket.RequiredItems[0], ticket.Modifications) > 0) covered++;
            }

            return covered;
        }

        private FoodCatalog CreateCatalog(params FoodItemConfig[] items)
        {
            var catalog = ScriptableObject.CreateInstance<FoodCatalog>();
            spawnedAssets.Add(catalog);

            var serialized = new SerializedObject(catalog);
            var property = serialized.FindProperty("items");
            property.arraySize = items.Length;
            for (var i = 0; i < items.Length; i++)
            {
                property.GetArrayElementAtIndex(i).objectReferenceValue = items[i];
            }
            serialized.ApplyModifiedPropertiesWithoutUndo();

            return catalog;
        }

        // Pins which fields BoardDistributionSettings clamps, because that ctor replaced
        // BoardDistributionConfig's getters and has to match them field for field. Copying
        // the asset's OnValidate rules instead put a Clamp(0, 3) on GuaranteedTicketCountLambda,
        // which silently turned the ExtremeLambda test below into a coin flip -- the sort of
        // regression that reads as "flaky test" rather than "wrong code".
        [Test]
        public void Settings_ClampOnlyTheFieldsTheConfigGettersClamped()
        {
            var settings = new BoardDistributionSettings(
                noiseLeakCountLambda: ExtremeLambda,
                guaranteedTicketCountMode: GuaranteedTicketCountMode.Poisson,
                guaranteedTicketCount: 0,
                guaranteedTicketCountLambda: ExtremeLambda,
                earlyTicketWeightDecay: 5f,
                urgentTimeThresholdSeconds: -3f,
                leakDepth: 99,
                maxLeakCount: 0);

            // Poisson rates pass through untouched -- TruncatedPoisson bounds the outcome.
            Assert.AreEqual(ExtremeLambda, settings.NoiseLeakCountLambda);
            Assert.AreEqual(ExtremeLambda, settings.GuaranteedTicketCountLambda);

            // Everything the getters clamped still clamps, with the same bounds.
            Assert.AreEqual(1, settings.GuaranteedTicketCount, "0 would mean no ticket is completable (GDD Section 4).");
            Assert.AreEqual(1f, settings.EarlyTicketWeightDecay);
            Assert.AreEqual(0f, settings.UrgentTimeThresholdSeconds);
            Assert.AreEqual(10, settings.LeakDepth);
            Assert.AreEqual(1, settings.MaxLeakCount);
        }

        [Test]
        public void OnOrderPlaced_ActiveTicketNeedsMissingItem_SpawnsIt()
        {
            var state = new GameState(gameConfig);
            var main = CreateFoodItem();
            var ticket = CreateTicket(new List<FoodItemConfig> { main });
            var distributor = new BoardDistributor(state, CreateDistributionConfig(0f));

            distributor.OnOrderPlaced(new[] { ticket }, Array.Empty<Ticket>());

            Assert.AreEqual(1, CountMatchingItemsOnBoard(state.Board, main, ticket.Modifications));
        }

        [Test]
        public void OnOrderPlaced_SerializedGuaranteedCountIsZero_StillGuaranteesOneTicket()
        {
            // Regression test: a BoardDistributionConfig asset that predates the
            // guaranteedTicketCount field can deserialize it at the raw CLR default
            // (0) instead of running the declared `= 1` initializer, which used to
            // silently disable the required-pool guarantee entirely (no ticket ever
            // completable). GuaranteedTicketCount must clamp this back up to 1.
            var state = new GameState(gameConfig);
            var main = CreateFoodItem();
            var ticket = CreateTicket(new List<FoodItemConfig> { main });
            var distributor = new BoardDistributor(state, CreateDistributionConfig(0f, guaranteedTicketCount: 0));

            distributor.OnOrderPlaced(new[] { ticket }, Array.Empty<Ticket>());

            Assert.AreEqual(1, CountMatchingItemsOnBoard(state.Board, main, ticket.Modifications));
        }

        [Test]
        public void OnOrderPlaced_RequiredItemAlreadyOnBoard_DoesNotSpawnDuplicate()
        {
            var state = new GameState(gameConfig);
            var main = CreateFoodItem();
            var ticket = CreateTicket(new List<FoodItemConfig> { main });
            state.Board.TryPlaceItem(new BoardItem(main, ticket.Modifications), 0, 0);
            var distributor = new BoardDistributor(state, CreateDistributionConfig(0f));

            distributor.OnOrderPlaced(new[] { ticket }, Array.Empty<Ticket>());

            Assert.AreEqual(1, CountMatchingItemsOnBoard(state.Board, main, ticket.Modifications));
        }

        // Backs the trayContentsForSlot delegate BoardDistributor takes (D-152) with a
        // plain per-slot array, indexed the way TrayManager indexes its own trays. Left
        // null for a slot with nothing in it, which also exercises the reader's null
        // branch — TrayManager's own snapshot helper hands out null the same way.
        private static IReadOnlyList<BoardItem>[] EmptyTrays()
        {
            return new IReadOnlyList<BoardItem>[GameState.TicketSlotCount];
        }

        [Test]
        public void OnOrderPlaced_RequiredItemAlreadyInThatTicketsTray_DoesNotSpawnDuplicate()
        {
            // The reason D-152 exists. The player dragged the burger into the tray, so it
            // is no longer on the board; counting only the board reads the requirement as
            // unmet and spawns a second burger on top of the one already banked.
            var state = new GameState(gameConfig);
            var main = CreateFoodItem();
            var ticket = CreateTicket(new List<FoodItemConfig> { main });
            state.TicketSlots[0] = ticket;

            var trays = EmptyTrays();
            trays[0] = new List<BoardItem> { new(main, ticket.Modifications) };
            var distributor = new BoardDistributor(
                state, CreateDistributionConfig(0f), trayContentsForSlot: slot => trays[slot]);

            distributor.OnOrderPlaced(new[] { ticket }, Array.Empty<Ticket>());

            Assert.AreEqual(0, CountMatchingItemsOnBoard(state.Board, main, ticket.Modifications));
        }

        [Test]
        public void OnOrderPlaced_RequiredItemSitsInAnotherTicketsTray_StillSpawnsForThisTicket()
        {
            // The half that must NOT change, and the reason the credit is per-ticket
            // rather than one board+all-trays pool: A's banked burger is committed to A
            // and unavailable to B, so B still needs one spawned. Exactly one appears --
            // A is covered by its own tray, B by the board. A global pool would spawn
            // zero and leave B unable to ever complete (the road back to D-044).
            var state = new GameState(gameConfig);
            var main = CreateFoodItem();
            var ticketA = CreateTicket(new List<FoodItemConfig> { main }, arrivalSequence: 0);
            var ticketB = CreateTicket(new List<FoodItemConfig> { main }, arrivalSequence: 1);
            state.TicketSlots[0] = ticketA;
            state.TicketSlots[1] = ticketB;

            var trays = EmptyTrays();
            trays[0] = new List<BoardItem> { new(main, ticketA.Modifications) };
            var distributor = new BoardDistributor(
                state, CreateDistributionConfig(0f, guaranteedTicketCount: 2),
                trayContentsForSlot: slot => trays[slot]);

            distributor.OnOrderPlaced(new[] { ticketA, ticketB }, Array.Empty<Ticket>());

            Assert.AreEqual(1, CountMatchingItemsOnBoard(state.Board, main, ticketA.Modifications));
        }

        [Test]
        public void OnOrderPlaced_TicketNeedsTwoOfACombo_TrayCoversOnlyOneOfThem()
        {
            // The tray credit is consumed per requirement, not tested once: one banked
            // burger cancels one of the two, never both.
            var state = new GameState(gameConfig);
            var main = CreateFoodItem();
            var ticket = CreateTicket(new List<FoodItemConfig> { main, main });
            state.TicketSlots[0] = ticket;

            var trays = EmptyTrays();
            trays[0] = new List<BoardItem> { new(main, ticket.Modifications) };
            var distributor = new BoardDistributor(
                state, CreateDistributionConfig(0f), trayContentsForSlot: slot => trays[slot]);

            distributor.OnOrderPlaced(new[] { ticket }, Array.Empty<Ticket>());

            Assert.AreEqual(1, CountMatchingItemsOnBoard(state.Board, main, ticket.Modifications));
        }

        [Test]
        public void OnOrderPlaced_TrayHoldsAWrongItem_CreditsNothingAndTheRequirementStillSpawns()
        {
            // A player can drop anything into a tray, right or wrong. A wrong item keys
            // to no requirement, so it must not silently absorb one.
            var state = new GameState(gameConfig);
            var main = CreateFoodItem(id: "burger");
            var somethingElse = CreateFoodItem(id: "hotdog");
            var ticket = CreateTicket(new List<FoodItemConfig> { main });
            state.TicketSlots[0] = ticket;

            var trays = EmptyTrays();
            trays[0] = new List<BoardItem> { new(somethingElse, Array.Empty<Modification>()) };
            var distributor = new BoardDistributor(
                state, CreateDistributionConfig(0f), trayContentsForSlot: slot => trays[slot]);

            distributor.OnOrderPlaced(new[] { ticket }, Array.Empty<Ticket>());

            Assert.AreEqual(1, CountMatchingItemsOnBoard(state.Board, main, ticket.Modifications));
        }

        [Test]
        public void OnOrderPlaced_TrayHoldsThePlainVersionOfAModifiedRequirement_StillSpawns()
        {
            // The tray is counted through RequiredItemKey, the same key the board is
            // counted through, so a plain burger in the tray does not satisfy a ticket
            // that ordered it with extra cheese.
            var state = new GameState(gameConfig);
            var main = CreateFoodItem();
            var mod = CreateModification();
            var ticket = CreateTicket(new List<FoodItemConfig> { main }, new List<Modification> { new(mod, true) });
            state.TicketSlots[0] = ticket;

            var trays = EmptyTrays();
            trays[0] = new List<BoardItem> { new(main, Array.Empty<Modification>()) };
            var distributor = new BoardDistributor(
                state, CreateDistributionConfig(0f), trayContentsForSlot: slot => trays[slot]);

            distributor.OnOrderPlaced(new[] { ticket }, Array.Empty<Ticket>());

            Assert.AreEqual(1, CountMatchingItemsOnBoard(state.Board, main, ticket.Modifications));
        }

        [Test]
        public void OnOrderPlaced_GuaranteedTicketIsStillQueued_TrayCreditNeverAppliesToIt()
        {
            // A queued ticket occupies no slot, so it has no tray -- whatever happens to
            // sit in slot 0's tray must not be read as ITS banked item. The budget of 2
            // covers the active ticket first and then reaches the queued one (D-044).
            var state = new GameState(gameConfig);
            var main = CreateFoodItem(id: "burger");
            var queuedFood = CreateFoodItem(id: "hotdog");
            var active = CreateTicket(new List<FoodItemConfig> { main }, arrivalSequence: 0);
            var queued = CreateTicket(new List<FoodItemConfig> { queuedFood }, arrivalSequence: 1);
            state.TicketSlots[0] = active;

            var trays = EmptyTrays();
            trays[0] = new List<BoardItem> { new(queuedFood, Array.Empty<Modification>()) };
            var distributor = new BoardDistributor(
                state, CreateDistributionConfig(0f, guaranteedTicketCount: 2),
                trayContentsForSlot: slot => trays[slot]);

            distributor.OnOrderPlaced(new[] { active }, new[] { queued });

            Assert.AreEqual(1, CountMatchingItemsOnBoard(state.Board, main, active.Modifications),
                "The active ticket's burger is not in its tray, so it still spawns.");
            Assert.AreEqual(1, CountMatchingItemsOnBoard(state.Board, queuedFood, Array.Empty<Modification>()),
                "The queued ticket has no tray; slot 0's hotdog belongs to nobody's requirement but the board's.");
        }

        [Test]
        public void OnOrderPlaced_DefaultGuaranteedCount_OnlyGuaranteesEarliestArrivedTicket()
        {
            var state = new GameState(gameConfig);
            var main = CreateFoodItem();
            var ticketA = CreateTicket(new List<FoodItemConfig> { main }, arrivalSequence: 0);
            var ticketB = CreateTicket(new List<FoodItemConfig> { main }, arrivalSequence: 1);
            var distributor = new BoardDistributor(state, CreateDistributionConfig(0f, guaranteedTicketCount: 1));

            distributor.OnOrderPlaced(new[] { ticketA, ticketB }, Array.Empty<Ticket>());

            Assert.AreEqual(1, CountMatchingItemsOnBoard(state.Board, main, ticketA.Modifications));
        }

        [Test]
        public void OnOrderPlaced_GuaranteedCountTwo_GuaranteesTwoEarliestArrivedTickets()
        {
            var state = new GameState(gameConfig);
            var main = CreateFoodItem();
            var ticketA = CreateTicket(new List<FoodItemConfig> { main }, arrivalSequence: 0);
            var ticketB = CreateTicket(new List<FoodItemConfig> { main }, arrivalSequence: 1);
            var distributor = new BoardDistributor(state, CreateDistributionConfig(0f, guaranteedTicketCount: 2));

            distributor.OnOrderPlaced(new[] { ticketA, ticketB }, Array.Empty<Ticket>());

            Assert.AreEqual(2, CountMatchingItemsOnBoard(state.Board, main, ticketA.Modifications));
        }

        [Test]
        public void OnOrderPlaced_SlotIndexDoesNotMatchArrivalOrder_EarliestArrivedTicketStillPicked()
        {
            var state = new GameState(gameConfig);
            var mainInSlot0 = CreateFoodItem();
            var mainInSlot1 = CreateFoodItem();
            var laterArrivedInSlot0 = CreateTicket(new List<FoodItemConfig> { mainInSlot0 }, arrivalSequence: 5);
            var earlierArrivedInSlot1 = CreateTicket(new List<FoodItemConfig> { mainInSlot1 }, arrivalSequence: 2);
            var distributor = new BoardDistributor(state, CreateDistributionConfig(0f, guaranteedTicketCount: 1));

            distributor.OnOrderPlaced(new[] { laterArrivedInSlot0, earlierArrivedInSlot1 }, Array.Empty<Ticket>());

            Assert.AreEqual(0, CountMatchingItemsOnBoard(state.Board, mainInSlot0, laterArrivedInSlot0.Modifications));
            Assert.AreEqual(1, CountMatchingItemsOnBoard(state.Board, mainInSlot1, earlierArrivedInSlot1.Modifications));
        }

        [Test]
        public void OnOrderPlaced_GuaranteedCountExceedsActiveTickets_ReachesIntoUpcomingQueue()
        {
            var state = new GameState(gameConfig);
            var mainActive1 = CreateFoodItem();
            var mainActive2 = CreateFoodItem();
            var mainUpcoming = CreateFoodItem();
            var active1 = CreateTicket(new List<FoodItemConfig> { mainActive1 }, arrivalSequence: 0);
            var active2 = CreateTicket(new List<FoodItemConfig> { mainActive2 }, arrivalSequence: 1);
            var nextUpcoming = CreateTicket(new List<FoodItemConfig> { mainUpcoming }, arrivalSequence: 2);
            var laterUpcoming = CreateTicket(new List<FoodItemConfig> { CreateFoodItem() }, arrivalSequence: 3);
            // GuaranteedTicketCount is clamped to 3 (GDD's 3-slot invariant), so
            // budget: 3 with only 2 active tickets present is the maximal way to
            // still force one pick out of the upcoming queue.
            var distributor = new BoardDistributor(state, CreateDistributionConfig(0f, guaranteedTicketCount: 3));

            distributor.OnOrderPlaced(new[] { active1, active2 }, new[] { nextUpcoming, laterUpcoming });

            Assert.AreEqual(1, CountMatchingItemsOnBoard(state.Board, mainUpcoming, nextUpcoming.Modifications));
        }

        [Test]
        public void OnOrderPlaced_DifferentModificationCombosOfSameFood_TrackedSeparately()
        {
            var state = new GameState(gameConfig);
            var main = CreateFoodItem();
            var mod = CreateModification();
            var ticketPlain = CreateTicket(new List<FoodItemConfig> { main }, arrivalSequence: 0);
            var ticketModified = CreateTicket(new List<FoodItemConfig> { main }, new List<Modification> { new(mod, true) }, arrivalSequence: 1);
            var distributor = new BoardDistributor(state, CreateDistributionConfig(0f, guaranteedTicketCount: 2));

            distributor.OnOrderPlaced(new[] { ticketPlain, ticketModified }, Array.Empty<Ticket>());

            Assert.AreEqual(1, CountMatchingItemsOnBoard(state.Board, main, ticketPlain.Modifications));
            Assert.AreEqual(1, CountMatchingItemsOnBoard(state.Board, main, ticketModified.Modifications));
        }

        [Test]
        public void OnOrderPlaced_LambdaZero_NeverLeaks()
        {
            var state = new GameState(gameConfig);
            var main = CreateFoodItem();
            var upcoming = CreateTicket(new List<FoodItemConfig> { main }, arrivalSequence: 100);
            // An unrelated, earlier-arrived active ticket satisfies the (now-mandatory,
            // minimum 1) required-pool guarantee with a distinct food, so its spawn
            // can't be confused with a noise leak of `main` — this test only cares
            // whether `main` (the noise-source food) shows up. arrivalSequence: 100 on
            // the noise ticket makes sure it's never the one required-pool picks.
            var activeTicket = CreateTicket(new List<FoodItemConfig> { CreateFoodItem() }, arrivalSequence: 0);
            var distributor = new BoardDistributor(state, CreateDistributionConfig(0f));

            distributor.OnOrderPlaced(new[] { activeTicket }, new[] { upcoming });

            Assert.AreEqual(0, CountMatchingItemsOnBoard(state.Board, main, upcoming.Modifications));
        }

        [Test]
        public void OnOrderPlaced_ExtremeLambda_LeaksFromUpcomingQueue()
        {
            var state = new GameState(gameConfig);
            var main = CreateFoodItem();
            var upcoming = CreateTicket(new List<FoodItemConfig> { main }, arrivalSequence: 100);
            // See comment in OnOrderPlaced_LambdaZero_NeverLeaks above.
            var activeTicket = CreateTicket(new List<FoodItemConfig> { CreateFoodItem() }, arrivalSequence: 0);
            var distributor = new BoardDistributor(state, CreateDistributionConfig(ExtremeLambda));

            distributor.OnOrderPlaced(new[] { activeTicket }, new[] { upcoming });

            Assert.AreEqual(1, CountMatchingItemsOnBoard(state.Board, main, upcoming.Modifications));
        }

        [Test]
        public void OnOrderPlaced_NoUpcomingTickets_SkipsNoiseSpawnWithoutThrowing()
        {
            var state = new GameState(gameConfig);
            var distributor = new BoardDistributor(state, CreateDistributionConfig(ExtremeLambda));

            Assert.DoesNotThrow(() => distributor.OnOrderPlaced(Array.Empty<Ticket>(), Array.Empty<Ticket>()));
            Assert.AreEqual(0, state.Board.OccupiedCellCount);
        }

        [Test]
        public void OnOrderPlaced_SameUpcomingTicket_NeverLeaksMoreThanOnce_AcrossManyOrders()
        {
            var state = new GameState(gameConfig);
            var main = CreateFoodItem();
            var upcoming = CreateTicket(new List<FoodItemConfig> { main }, arrivalSequence: 100);
            // See comment in OnOrderPlaced_LambdaZero_NeverLeaks above.
            var activeTicket = CreateTicket(new List<FoodItemConfig> { CreateFoodItem() }, arrivalSequence: 0);
            var distributor = new BoardDistributor(state, CreateDistributionConfig(ExtremeLambda));

            for (var i = 0; i < 10; i++)
            {
                distributor.OnOrderPlaced(new[] { activeTicket }, new[] { upcoming });
            }

            Assert.AreEqual(1, CountMatchingItemsOnBoard(state.Board, main, upcoming.Modifications));
        }

        [Test]
        public void OnOrderPlaced_TicketLeavesUpcomingQueue_NewTicketBecomesEligibleForNoiseAgain()
        {
            var state = new GameState(gameConfig);
            var mainA = CreateFoodItem();
            var mainB = CreateFoodItem();
            var ticketA = CreateTicket(new List<FoodItemConfig> { mainA }, arrivalSequence: 100);
            var ticketB = CreateTicket(new List<FoodItemConfig> { mainB }, arrivalSequence: 101);
            // See comment in OnOrderPlaced_LambdaZero_NeverLeaks above.
            var activeTicket = CreateTicket(new List<FoodItemConfig> { CreateFoodItem() }, arrivalSequence: 0);
            var distributor = new BoardDistributor(state, CreateDistributionConfig(ExtremeLambda));

            distributor.OnOrderPlaced(new[] { activeTicket }, new[] { ticketA });
            distributor.OnOrderPlaced(new[] { activeTicket }, new[] { ticketB });

            Assert.AreEqual(1, CountMatchingItemsOnBoard(state.Board, mainA, ticketA.Modifications));
            Assert.AreEqual(1, CountMatchingItemsOnBoard(state.Board, mainB, ticketB.Modifications));
        }

        [Test]
        public void OnOrderPlaced_ExtremeLambda_MultipleUpcomingTickets_LeaksFromEveryCandidateInOneCall()
        {
            var state = new GameState(gameConfig);
            var mainA = CreateFoodItem();
            var mainB = CreateFoodItem();
            var mainC = CreateFoodItem();
            var ticketA = CreateTicket(new List<FoodItemConfig> { mainA }, arrivalSequence: 100);
            var ticketB = CreateTicket(new List<FoodItemConfig> { mainB }, arrivalSequence: 101);
            var ticketC = CreateTicket(new List<FoodItemConfig> { mainC }, arrivalSequence: 102);
            // See comment in OnOrderPlaced_LambdaZero_NeverLeaks above.
            var activeTicket = CreateTicket(new List<FoodItemConfig> { CreateFoodItem() }, arrivalSequence: 0);
            var distributor = new BoardDistributor(state, CreateDistributionConfig(ExtremeLambda));

            distributor.OnOrderPlaced(new[] { activeTicket }, new[] { ticketA, ticketB, ticketC });

            Assert.AreEqual(1, CountMatchingItemsOnBoard(state.Board, mainA, ticketA.Modifications));
            Assert.AreEqual(1, CountMatchingItemsOnBoard(state.Board, mainB, ticketB.Modifications));
            Assert.AreEqual(1, CountMatchingItemsOnBoard(state.Board, mainC, ticketC.Modifications));
        }

        [Test]
        public void OnOrderPlaced_LeakDepth_LimitsCandidatesToNearestUpcomingTickets()
        {
            var state = new GameState(gameConfig);
            var mainNear0 = CreateFoodItem();
            var mainNear1 = CreateFoodItem();
            var mainFar0 = CreateFoodItem();
            var mainFar1 = CreateFoodItem();
            // upcomingTickets[0] is the front of the FIFO queue (arrives soonest);
            // leakDepth: 2 should only ever consider indices 0 and 1 as candidates.
            var ticketNear0 = CreateTicket(new List<FoodItemConfig> { mainNear0 }, arrivalSequence: 100);
            var ticketNear1 = CreateTicket(new List<FoodItemConfig> { mainNear1 }, arrivalSequence: 101);
            var ticketFar0 = CreateTicket(new List<FoodItemConfig> { mainFar0 }, arrivalSequence: 102);
            var ticketFar1 = CreateTicket(new List<FoodItemConfig> { mainFar1 }, arrivalSequence: 103);
            // See comment in OnOrderPlaced_LambdaZero_NeverLeaks above.
            var activeTicket = CreateTicket(new List<FoodItemConfig> { CreateFoodItem() }, arrivalSequence: 0);
            var distributor = new BoardDistributor(state, CreateDistributionConfig(ExtremeLambda, leakDepth: 2));

            for (var i = 0; i < 10; i++)
            {
                distributor.OnOrderPlaced(
                    new[] { activeTicket },
                    new[] { ticketNear0, ticketNear1, ticketFar0, ticketFar1 });
            }

            Assert.AreEqual(1, CountMatchingItemsOnBoard(state.Board, mainNear0, ticketNear0.Modifications));
            Assert.AreEqual(1, CountMatchingItemsOnBoard(state.Board, mainNear1, ticketNear1.Modifications));
            Assert.AreEqual(0, CountMatchingItemsOnBoard(state.Board, mainFar0, ticketFar0.Modifications));
            Assert.AreEqual(0, CountMatchingItemsOnBoard(state.Board, mainFar1, ticketFar1.Modifications));
        }

        [Test]
        public void OnOrderPlaced_SerializedLeakDepthIsZero_StillLeaksFromNearestTicket()
        {
            // Regression test: a BoardDistributionConfig asset that predates the
            // leakDepth field can deserialize it at the raw CLR default (0) instead
            // of running the declared `= 10` initializer, which would silently
            // disable noise leaking entirely (Take(0) is always empty). LeakDepth
            // must clamp this back up to 1 — same rationale as GuaranteedTicketCount.
            var state = new GameState(gameConfig);
            var main = CreateFoodItem();
            var upcoming = CreateTicket(new List<FoodItemConfig> { main }, arrivalSequence: 100);
            // See comment in OnOrderPlaced_LambdaZero_NeverLeaks above.
            var activeTicket = CreateTicket(new List<FoodItemConfig> { CreateFoodItem() }, arrivalSequence: 0);
            var distributor = new BoardDistributor(state, CreateDistributionConfig(ExtremeLambda, leakDepth: 0));

            distributor.OnOrderPlaced(new[] { activeTicket }, new[] { upcoming });

            Assert.AreEqual(1, CountMatchingItemsOnBoard(state.Board, main, upcoming.Modifications));
        }

        [Test]
        public void OnOrderPlaced_MaxLeakCountBelowCandidateCount_CapsLeakCountIndependentlyOfLeakDepth()
        {
            var state = new GameState(gameConfig);
            var upcomingTickets = new List<Ticket>();
            var mains = new List<FoodItemConfig>();
            for (var i = 0; i < 5; i++)
            {
                var main = CreateFoodItem();
                mains.Add(main);
                upcomingTickets.Add(CreateTicket(new List<FoodItemConfig> { main }, arrivalSequence: 100 + i));
            }
            // See comment in OnOrderPlaced_LambdaZero_NeverLeaks above.
            var activeTicket = CreateTicket(new List<FoodItemConfig> { CreateFoodItem() }, arrivalSequence: 0);
            // leakDepth (default 10) makes all 5 tickets candidates, but
            // maxLeakCount: 2 must still cap the leak count to 2 regardless —
            // proving MaxLeakCount works independently of LeakDepth/candidate
            // availability, not as a side effect of the candidate window size.
            var distributor = new BoardDistributor(state, CreateDistributionConfig(ExtremeLambda, maxLeakCount: 2));

            distributor.OnOrderPlaced(new[] { activeTicket }, upcomingTickets);

            var leakedCount = 0;
            for (var i = 0; i < mains.Count; i++)
            {
                leakedCount += CountMatchingItemsOnBoard(state.Board, mains[i], upcomingTickets[i].Modifications);
            }

            Assert.AreEqual(2, leakedCount);
        }

        [Test]
        public void OnOrderPlaced_SerializedMaxLeakCountIsZero_StillLeaksAtLeastOne()
        {
            // Regression test: a BoardDistributionConfig asset that predates the
            // maxLeakCount field can deserialize it at the raw CLR default (0)
            // instead of running the declared `= 10` initializer, which would
            // silently disable noise leaking entirely (Sample(0, ...) is always
            // 0). MaxLeakCount must clamp this back up to 1 — same rationale as
            // GuaranteedTicketCount/LeakDepth.
            var state = new GameState(gameConfig);
            var main = CreateFoodItem();
            var upcoming = CreateTicket(new List<FoodItemConfig> { main }, arrivalSequence: 100);
            // See comment in OnOrderPlaced_LambdaZero_NeverLeaks above.
            var activeTicket = CreateTicket(new List<FoodItemConfig> { CreateFoodItem() }, arrivalSequence: 0);
            var distributor = new BoardDistributor(state, CreateDistributionConfig(ExtremeLambda, maxLeakCount: 0));

            distributor.OnOrderPlaced(new[] { activeTicket }, new[] { upcoming });

            Assert.AreEqual(1, CountMatchingItemsOnBoard(state.Board, main, upcoming.Modifications));
        }

        [Test]
        public void OnOrderPlaced_ModerateLambda_SometimesLeaksMoreThanOneItemPerCall()
        {
            var moderateLambda = 3f;
            var upcomingCount = 5;
            var trialsWithMultipleLeaks = 0;

            for (var trial = 0; trial < 40; trial++)
            {
                var state = new GameState(gameConfig);
                var upcomingTickets = new List<Ticket>();
                for (var i = 0; i < upcomingCount; i++)
                {
                    upcomingTickets.Add(CreateTicket(new List<FoodItemConfig> { CreateFoodItem() }, arrivalSequence: 100 + i));
                }

                // guaranteedTicketCount defaults to 1, so one required-pool item
                // always spawns from the earliest-arrived upcoming ticket before
                // the leak step runs — baseline occupied count is exactly 1. Two
                // or more leaked cells beyond that (total > 2) is the signal that
                // a single OnOrderPlaced call leaked more than one item.
                var distributor = new BoardDistributor(state, CreateDistributionConfig(moderateLambda));
                distributor.OnOrderPlaced(Array.Empty<Ticket>(), upcomingTickets);

                if (state.Board.OccupiedCellCount > 2) trialsWithMultipleLeaks++;
            }

            Assert.Greater(trialsWithMultipleLeaks, 0,
                "Expected at least one trial to leak more than one item from a single OnOrderPlaced call.");
        }

        [Test]
        public void OnOrderPlaced_BoardFull_RequestSpawnQueuesWithoutThrowing()
        {
            var state = new GameState(gameConfig);
            for (var y = 0; y < state.Board.Height; y++)
            {
                for (var x = 0; x < state.Board.Width; x++)
                {
                    state.Board.TryPlaceItem(new BoardItem(CreateFoodItem(), new List<Modification>()), x, y);
                }
            }

            var main = CreateFoodItem();
            var ticket = CreateTicket(new List<FoodItemConfig> { main });
            var distributor = new BoardDistributor(state, CreateDistributionConfig(0f));

            Assert.DoesNotThrow(() => distributor.OnOrderPlaced(new[] { ticket }, Array.Empty<Ticket>()));
            Assert.AreEqual(1, state.Board.PendingSpawnCount);
        }

        [Test]
        public void OnOrderPlaced_UrgentActiveTicket_GuaranteedEvenWhenNotEarliest()
        {
            var state = new GameState(gameConfig);
            var mainEarly = CreateFoodItem();
            var mainUrgent = CreateFoodItem();
            // Budget is 1 and decay is 0 (deterministic earliest-first), so
            // WITHOUT the urgency override the required pool would only ever
            // guarantee `early` (arrivalSequence: 0). `urgent` arrived later but
            // has fallen under the threshold -- it must still get its item.
            var early = CreateTicket(new List<FoodItemConfig> { mainEarly }, timeLimitSeconds: 90f, arrivalSequence: 0);
            var urgent = CreateTicket(new List<FoodItemConfig> { mainUrgent }, timeLimitSeconds: 90f, arrivalSequence: 1);
            urgent.RemainingSeconds = 5f;
            var distributor = new BoardDistributor(state, CreateDistributionConfig(0f, guaranteedTicketCount: 1, urgentTimeThresholdSeconds: 10f));

            distributor.OnOrderPlaced(new[] { early, urgent }, Array.Empty<Ticket>());

            Assert.AreEqual(1, CountMatchingItemsOnBoard(state.Board, mainUrgent, urgent.Modifications));
        }

        [Test]
        public void OnOrderPlaced_UrgentTicket_ConsumesFromBudget_NonUrgentNotAlsoGuaranteed()
        {
            var state = new GameState(gameConfig);
            var mainEarly = CreateFoodItem();
            var mainUrgent = CreateFoodItem();
            var early = CreateTicket(new List<FoodItemConfig> { mainEarly }, timeLimitSeconds: 90f, arrivalSequence: 0);
            var urgent = CreateTicket(new List<FoodItemConfig> { mainUrgent }, timeLimitSeconds: 90f, arrivalSequence: 1);
            urgent.RemainingSeconds = 5f;
            // Budget: 1. The single urgent ticket consumes the whole budget, so
            // the earliest non-urgent ticket must NOT also get a required item
            // ("bütçeden düşsün" -- urgency consumes from, not adds to, budget).
            var distributor = new BoardDistributor(state, CreateDistributionConfig(0f, guaranteedTicketCount: 1, urgentTimeThresholdSeconds: 10f));

            distributor.OnOrderPlaced(new[] { early, urgent }, Array.Empty<Ticket>());

            Assert.AreEqual(0, CountMatchingItemsOnBoard(state.Board, mainEarly, early.Modifications));
        }

        [Test]
        public void OnOrderPlaced_UrgentCountExceedsBudget_AllUrgentTicketsStillGuaranteed()
        {
            var state = new GameState(gameConfig);
            var mainUrgent1 = CreateFoodItem();
            var mainUrgent2 = CreateFoodItem();
            var urgent1 = CreateTicket(new List<FoodItemConfig> { mainUrgent1 }, timeLimitSeconds: 90f, arrivalSequence: 0);
            var urgent2 = CreateTicket(new List<FoodItemConfig> { mainUrgent2 }, timeLimitSeconds: 90f, arrivalSequence: 1);
            urgent1.RemainingSeconds = 5f;
            urgent2.RemainingSeconds = 5f;
            // Budget: 1, but 2 urgent tickets -- urgency always wins, even past
            // budget ("her ikisini de garantile").
            var distributor = new BoardDistributor(state, CreateDistributionConfig(0f, guaranteedTicketCount: 1, urgentTimeThresholdSeconds: 10f));

            distributor.OnOrderPlaced(new[] { urgent1, urgent2 }, Array.Empty<Ticket>());

            Assert.AreEqual(1, CountMatchingItemsOnBoard(state.Board, mainUrgent1, urgent1.Modifications));
            Assert.AreEqual(1, CountMatchingItemsOnBoard(state.Board, mainUrgent2, urgent2.Modifications));
        }

        [Test]
        public void OnOrderPlaced_PoissonMode_BudgetAlwaysWithinOneToTicketSlotCount()
        {
            var state = new GameState(gameConfig);
            var mains = new List<FoodItemConfig>();
            var tickets = new List<Ticket>();
            for (var i = 0; i < GameState.TicketSlotCount; i++)
            {
                var main = CreateFoodItem();
                mains.Add(main);
                tickets.Add(CreateTicket(new List<FoodItemConfig> { main }, arrivalSequence: i));
            }
            // Lambda 0 truncated+shifted should always sample the minimum (1).
            var distributor = new BoardDistributor(state, CreateDistributionConfig(
                0f, guaranteedTicketCountMode: GuaranteedTicketCountMode.Poisson, guaranteedTicketCountLambda: 0f));

            distributor.OnOrderPlaced(tickets, Array.Empty<Ticket>());

            var guaranteedCount = 0;
            for (var i = 0; i < mains.Count; i++)
            {
                guaranteedCount += CountMatchingItemsOnBoard(state.Board, mains[i], tickets[i].Modifications);
            }

            Assert.AreEqual(1, guaranteedCount);
        }

        [Test]
        public void OnOrderPlaced_PoissonMode_ExtremeLambda_GuaranteesEveryActiveTicket()
        {
            var state = new GameState(gameConfig);
            var mains = new List<FoodItemConfig>();
            var tickets = new List<Ticket>();
            for (var i = 0; i < GameState.TicketSlotCount; i++)
            {
                var main = CreateFoodItem();
                mains.Add(main);
                tickets.Add(CreateTicket(new List<FoodItemConfig> { main }, arrivalSequence: i));
            }
            var distributor = new BoardDistributor(state, CreateDistributionConfig(
                0f, guaranteedTicketCountMode: GuaranteedTicketCountMode.Poisson, guaranteedTicketCountLambda: ExtremeLambda));

            distributor.OnOrderPlaced(tickets, Array.Empty<Ticket>());

            for (var i = 0; i < mains.Count; i++)
            {
                Assert.AreEqual(1, CountMatchingItemsOnBoard(state.Board, mains[i], tickets[i].Modifications));
            }
        }

        [Test]
        public void OnOrderPlaced_EarlyTicketWeightDecayOfOne_EventuallySelectsEveryCandidate()
        {
            // Decay 1.0 = uniform weighting (arrival order ignored). Across many
            // independent trials with budget 1 among 2 equally-weighted
            // candidates, both must get picked at least once -- if decay were
            // still behaving like the deterministic default (0), ticketB would
            // never be picked. Same weak, non-flaky-by-construction style as
            // this file's own OnOrderPlaced_ModerateLambda_SometimesLeaksMoreThanOneItemPerCall.
            var mainA = CreateFoodItem();
            var mainB = CreateFoodItem();
            var aPicked = false;
            var bPicked = false;

            for (var trial = 0; trial < 60 && !(aPicked && bPicked); trial++)
            {
                var state = new GameState(gameConfig);
                var ticketA = CreateTicket(new List<FoodItemConfig> { mainA }, arrivalSequence: 0);
                var ticketB = CreateTicket(new List<FoodItemConfig> { mainB }, arrivalSequence: 1);
                var distributor = new BoardDistributor(state, CreateDistributionConfig(0f, guaranteedTicketCount: 1, earlyTicketWeightDecay: 1f));

                distributor.OnOrderPlaced(new[] { ticketA, ticketB }, Array.Empty<Ticket>());

                if (CountMatchingItemsOnBoard(state.Board, mainA, ticketA.Modifications) == 1) aPicked = true;
                if (CountMatchingItemsOnBoard(state.Board, mainB, ticketB.Modifications) == 1) bPicked = true;
            }

            Assert.IsTrue(aPicked && bPicked, "Expected uniform weighting (decay 1.0) to eventually select both candidates across independent trials.");
        }

        [Test]
        public void OnOrderPlaced_SameActiveTicketsAcrossManyRounds_OnlyOneTicketsFoodEverGuaranteed()
        {
            // Regression test for a real playtest bug: without sticky
            // guaranteed-ticket state, re-rolling the arrival-weighted lottery on
            // every OnOrderPlaced call -- even when the active-ticket set hasn't
            // changed between calls -- could pick a DIFFERENT ticket each round
            // and spawn its (distinctly-modified) required item too, since
            // nothing here ever un-spawns an already-placed item. Over enough
            // rounds this silently drifted "at least one ticket guaranteed"
            // toward "eventually every ticket guaranteed". decay: 1f (uniform)
            // makes each round's draw genuinely random, so this must hold even
            // though B and C individually have a real chance to win any one round.
            var state = new GameState(gameConfig);
            var mainA = CreateFoodItem();
            var mainB = CreateFoodItem();
            var mainC = CreateFoodItem();
            var ticketA = CreateTicket(new List<FoodItemConfig> { mainA }, arrivalSequence: 0);
            var ticketB = CreateTicket(new List<FoodItemConfig> { mainB }, arrivalSequence: 1);
            var ticketC = CreateTicket(new List<FoodItemConfig> { mainC }, arrivalSequence: 2);
            var distributor = new BoardDistributor(state, CreateDistributionConfig(0f, guaranteedTicketCount: 1, earlyTicketWeightDecay: 1f));

            for (var round = 0; round < 30; round++)
            {
                distributor.OnOrderPlaced(new[] { ticketA, ticketB, ticketC }, Array.Empty<Ticket>());
            }

            var guaranteedFoodCount =
                CountMatchingItemsOnBoard(state.Board, mainA, ticketA.Modifications) +
                CountMatchingItemsOnBoard(state.Board, mainB, ticketB.Modifications) +
                CountMatchingItemsOnBoard(state.Board, mainC, ticketC.Modifications);

            Assert.AreEqual(1, guaranteedFoodCount);
        }

        [Test]
        public void OnOrderPlaced_GuaranteedTicketDeliveredAndReplaced_FreedSlotFillsFromRemainingPool()
        {
            var state = new GameState(gameConfig);
            var mainA = CreateFoodItem();
            var mainB = CreateFoodItem();
            var mainC = CreateFoodItem();
            var ticketA = CreateTicket(new List<FoodItemConfig> { mainA }, arrivalSequence: 0);
            var ticketB = CreateTicket(new List<FoodItemConfig> { mainB }, arrivalSequence: 1);
            var ticketC = CreateTicket(new List<FoodItemConfig> { mainC }, arrivalSequence: 2);
            var distributor = new BoardDistributor(state, CreateDistributionConfig(0f, guaranteedTicketCount: 1));

            // Round 1: decay 0 deterministically guarantees the earliest (A).
            distributor.OnOrderPlaced(new[] { ticketA, ticketB, ticketC }, Array.Empty<Ticket>());
            Assert.AreEqual(1, CountMatchingItemsOnBoard(state.Board, mainA, ticketA.Modifications));

            // A is delivered and leaves the pool entirely -- its guaranteed slot
            // must free up and get filled by the next-earliest remaining ticket (B).
            distributor.OnOrderPlaced(new[] { ticketB, ticketC }, Array.Empty<Ticket>());

            Assert.AreEqual(1, CountMatchingItemsOnBoard(state.Board, mainB, ticketB.Modifications));
            Assert.AreEqual(0, CountMatchingItemsOnBoard(state.Board, mainC, ticketC.Modifications));
        }

        [Test]
        public void OnOrderPlaced_QueuedTicketsCouldWinLottery_ActiveTicketIsAlwaysGuaranteed()
        {
            // Regression test for the playtest bug in decisions.md D-044: the budget was
            // drawn from one combined active+upcoming pool, so with the shipped balancing
            // (budget 1, a 10-deep lookahead) the only guaranteed slot regularly went to
            // a ticket the player could not see, and NO active ticket had its items on
            // the board -- board full of food, no legal move, ticket expires for a life.
            //
            // decay 1f (uniform) is what makes this a real test: every candidate has an
            // equal chance, so under the old combined pool the single active ticket would
            // win only ~1 round in 4 and this loop would fail almost immediately. Under
            // the active-first rule it is guaranteed every round, so the assertion inside
            // the loop is deterministic -- non-flaky by construction, not by trial count.
            var mainActive = CreateFoodItem();

            for (var trial = 0; trial < 40; trial++)
            {
                var state = new GameState(gameConfig);
                var active = CreateTicket(new List<FoodItemConfig> { mainActive }, arrivalSequence: 0);
                var upcoming = new[]
                {
                    CreateTicket(new List<FoodItemConfig> { CreateFoodItem() }, arrivalSequence: 1),
                    CreateTicket(new List<FoodItemConfig> { CreateFoodItem() }, arrivalSequence: 2),
                    CreateTicket(new List<FoodItemConfig> { CreateFoodItem() }, arrivalSequence: 3),
                };
                var distributor = new BoardDistributor(state, CreateDistributionConfig(
                    0f, guaranteedTicketCount: 1, earlyTicketWeightDecay: 1f));

                distributor.OnOrderPlaced(new[] { active }, upcoming);

                Assert.AreEqual(
                    1,
                    CountMatchingItemsOnBoard(state.Board, mainActive, active.Modifications),
                    $"trial {trial}: the active ticket must be guaranteed before any queued one.");
            }
        }

        [Test]
        public void OnOrderPlaced_QueuedTicketAlreadyHoldsTheBudget_NewActiveTicketStillGetsCovered()
        {
            // The sticky half of the same bug. Selection persists across calls, and a
            // queued ticket does not leave the pool when a slot fills -- so a queued
            // ticket that won the budget in an earlier round used to keep holding it
            // round after round while active tickets starved. Round 1 has no active
            // tickets at all, which is the one legitimate way the budget lands on a
            // queued ticket; round 2 must still cover the newly active one.
            var state = new GameState(gameConfig);
            var mainQueued = CreateFoodItem();
            var mainActive = CreateFoodItem();
            var queued = CreateTicket(new List<FoodItemConfig> { mainQueued }, arrivalSequence: 0);
            var active = CreateTicket(new List<FoodItemConfig> { mainActive }, arrivalSequence: 1);
            var distributor = new BoardDistributor(state, CreateDistributionConfig(0f, guaranteedTicketCount: 1));

            distributor.OnOrderPlaced(Array.Empty<Ticket>(), new[] { queued });
            Assert.AreEqual(1, CountMatchingItemsOnBoard(state.Board, mainQueued, queued.Modifications));

            distributor.OnOrderPlaced(new[] { active }, new[] { queued });

            Assert.AreEqual(1, CountMatchingItemsOnBoard(state.Board, mainActive, active.Modifications));
            // The queued ticket keeps what was already spawned for it -- nothing here
            // ever un-spawns an item, it just stops consuming the active budget.
            Assert.AreEqual(1, CountMatchingItemsOnBoard(state.Board, mainQueued, queued.Modifications));
        }
    }
}
