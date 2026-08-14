using System;
using System.Collections.Generic;
using ExpoTheExplorer.Core;
using ExpoTheExplorer.Data;
using ExpoTheExplorer.Systems.BoardDistribution;
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

        private FoodItemConfig CreateFoodItem(FoodCategory category = FoodCategory.Main)
        {
            var foodConfig = ScriptableObject.CreateInstance<FoodItemConfig>();
            spawnedAssets.Add(foodConfig);

            var serialized = new SerializedObject(foodConfig);
            serialized.FindProperty("category").enumValueIndex = (int)category;
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
        private BoardDistributionConfig CreateDistributionConfig(
            float noiseLeakCountLambda, int guaranteedTicketCount = 1, int leakDepth = 10, int maxLeakCount = 10,
            GuaranteedTicketCountMode guaranteedTicketCountMode = GuaranteedTicketCountMode.Manual,
            float guaranteedTicketCountLambda = 0f, float earlyTicketWeightDecay = 0f, float urgentTimeThresholdSeconds = 0f)
        {
            var config = ScriptableObject.CreateInstance<BoardDistributionConfig>();
            spawnedAssets.Add(config);

            var serialized = new SerializedObject(config);
            serialized.FindProperty("noiseLeakCountLambda").floatValue = noiseLeakCountLambda;
            serialized.FindProperty("guaranteedTicketCountMode").enumValueIndex = (int)guaranteedTicketCountMode;
            serialized.FindProperty("guaranteedTicketCount").intValue = guaranteedTicketCount;
            serialized.FindProperty("guaranteedTicketCountLambda").floatValue = guaranteedTicketCountLambda;
            serialized.FindProperty("earlyTicketWeightDecay").floatValue = earlyTicketWeightDecay;
            serialized.FindProperty("urgentTimeThresholdSeconds").floatValue = urgentTimeThresholdSeconds;
            serialized.FindProperty("leakDepth").intValue = leakDepth;
            serialized.FindProperty("maxLeakCount").intValue = maxLeakCount;
            serialized.ApplyModifiedPropertiesWithoutUndo();

            return config;
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
    }
}
