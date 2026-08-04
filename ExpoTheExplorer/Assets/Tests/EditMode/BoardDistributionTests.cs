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

        private BoardDistributionConfig CreateDistributionConfig(float noiseLeakChance, int guaranteedTicketCount = 1)
        {
            var config = ScriptableObject.CreateInstance<BoardDistributionConfig>();
            spawnedAssets.Add(config);

            var serialized = new SerializedObject(config);
            serialized.FindProperty("noiseLeakChance").floatValue = noiseLeakChance;
            serialized.FindProperty("guaranteedTicketCount").intValue = guaranteedTicketCount;
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
            var mainActive3 = CreateFoodItem();
            var mainUpcoming = CreateFoodItem();
            var active1 = CreateTicket(new List<FoodItemConfig> { mainActive1 }, arrivalSequence: 0);
            var active2 = CreateTicket(new List<FoodItemConfig> { mainActive2 }, arrivalSequence: 1);
            var active3 = CreateTicket(new List<FoodItemConfig> { mainActive3 }, arrivalSequence: 2);
            var nextUpcoming = CreateTicket(new List<FoodItemConfig> { mainUpcoming }, arrivalSequence: 3);
            var laterUpcoming = CreateTicket(new List<FoodItemConfig> { CreateFoodItem() }, arrivalSequence: 4);
            var distributor = new BoardDistributor(state, CreateDistributionConfig(0f, guaranteedTicketCount: 4));

            distributor.OnOrderPlaced(new[] { active1, active2, active3 }, new[] { nextUpcoming, laterUpcoming });

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
        public void OnOrderPlaced_NoiseLeakChanceZero_NeverLeaks()
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
        public void OnOrderPlaced_NoiseLeakChanceOne_LeaksFromUpcomingQueue()
        {
            var state = new GameState(gameConfig);
            var main = CreateFoodItem();
            var upcoming = CreateTicket(new List<FoodItemConfig> { main }, arrivalSequence: 100);
            // See comment in OnOrderPlaced_NoiseLeakChanceZero_NeverLeaks above.
            var activeTicket = CreateTicket(new List<FoodItemConfig> { CreateFoodItem() }, arrivalSequence: 0);
            var distributor = new BoardDistributor(state, CreateDistributionConfig(1f));

            distributor.OnOrderPlaced(new[] { activeTicket }, new[] { upcoming });

            Assert.AreEqual(1, CountMatchingItemsOnBoard(state.Board, main, upcoming.Modifications));
        }

        [Test]
        public void OnOrderPlaced_NoUpcomingTickets_SkipsNoiseSpawnWithoutThrowing()
        {
            var state = new GameState(gameConfig);
            var distributor = new BoardDistributor(state, CreateDistributionConfig(1f));

            Assert.DoesNotThrow(() => distributor.OnOrderPlaced(Array.Empty<Ticket>(), Array.Empty<Ticket>()));
            Assert.AreEqual(0, state.Board.OccupiedCellCount);
        }

        [Test]
        public void OnOrderPlaced_SameUpcomingTicket_NeverLeaksMoreThanOnce_AcrossManyOrders()
        {
            var state = new GameState(gameConfig);
            var main = CreateFoodItem();
            var upcoming = CreateTicket(new List<FoodItemConfig> { main }, arrivalSequence: 100);
            // See comment in OnOrderPlaced_NoiseLeakChanceZero_NeverLeaks above.
            var activeTicket = CreateTicket(new List<FoodItemConfig> { CreateFoodItem() }, arrivalSequence: 0);
            var distributor = new BoardDistributor(state, CreateDistributionConfig(1f));

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
            // See comment in OnOrderPlaced_NoiseLeakChanceZero_NeverLeaks above.
            var activeTicket = CreateTicket(new List<FoodItemConfig> { CreateFoodItem() }, arrivalSequence: 0);
            var distributor = new BoardDistributor(state, CreateDistributionConfig(1f));

            distributor.OnOrderPlaced(new[] { activeTicket }, new[] { ticketA });
            distributor.OnOrderPlaced(new[] { activeTicket }, new[] { ticketB });

            Assert.AreEqual(1, CountMatchingItemsOnBoard(state.Board, mainA, ticketA.Modifications));
            Assert.AreEqual(1, CountMatchingItemsOnBoard(state.Board, mainB, ticketB.Modifications));
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
    }
}
