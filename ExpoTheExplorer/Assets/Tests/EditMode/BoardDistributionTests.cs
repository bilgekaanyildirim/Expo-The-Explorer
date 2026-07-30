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

        private BoardDistributionConfig CreateDistributionConfig(float noiseSpawnIntervalSeconds)
        {
            var config = ScriptableObject.CreateInstance<BoardDistributionConfig>();
            spawnedAssets.Add(config);

            var serialized = new SerializedObject(config);
            serialized.FindProperty("noiseSpawnIntervalSeconds").floatValue = noiseSpawnIntervalSeconds;
            serialized.ApplyModifiedPropertiesWithoutUndo();

            return config;
        }

        private Ticket CreateTicket(List<FoodItemConfig> requiredItems, List<Modification> modifications = null, float timeLimitSeconds = 90f)
        {
            return new Ticket("Test Customer", PatienceType.Normal, requiredItems, modifications ?? new List<Modification>(), timeLimitSeconds);
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
        public void Tick_ActiveTicketNeedsMissingItem_SpawnsIt()
        {
            var state = new GameState(gameConfig);
            var main = CreateFoodItem();
            var ticket = CreateTicket(new List<FoodItemConfig> { main });
            var distributor = new BoardDistributor(state, CreateDistributionConfig(999f));

            distributor.Tick(0f, new[] { ticket }, Array.Empty<Ticket>());

            Assert.AreEqual(1, CountMatchingItemsOnBoard(state.Board, main, ticket.Modifications));
        }

        [Test]
        public void Tick_RequiredItemAlreadyOnBoard_DoesNotSpawnDuplicate()
        {
            var state = new GameState(gameConfig);
            var main = CreateFoodItem();
            var ticket = CreateTicket(new List<FoodItemConfig> { main });
            state.Board.TryPlaceItem(new BoardItem(main, ticket.Modifications), 0, 0);
            var distributor = new BoardDistributor(state, CreateDistributionConfig(999f));

            distributor.Tick(0f, new[] { ticket }, Array.Empty<Ticket>());

            Assert.AreEqual(1, CountMatchingItemsOnBoard(state.Board, main, ticket.Modifications));
        }

        [Test]
        public void Tick_TwoTicketsNeedSameExactItem_SpawnsTwoCopies()
        {
            var state = new GameState(gameConfig);
            var main = CreateFoodItem();
            var ticketA = CreateTicket(new List<FoodItemConfig> { main });
            var ticketB = CreateTicket(new List<FoodItemConfig> { main });
            var distributor = new BoardDistributor(state, CreateDistributionConfig(999f));

            distributor.Tick(0f, new[] { ticketA, ticketB }, Array.Empty<Ticket>());

            Assert.AreEqual(2, CountMatchingItemsOnBoard(state.Board, main, ticketA.Modifications));
        }

        [Test]
        public void Tick_DifferentModificationCombosOfSameFood_TrackedSeparately()
        {
            var state = new GameState(gameConfig);
            var main = CreateFoodItem();
            var mod = CreateModification();
            var ticketPlain = CreateTicket(new List<FoodItemConfig> { main });
            var ticketModified = CreateTicket(new List<FoodItemConfig> { main }, new List<Modification> { new(mod, true) });
            var distributor = new BoardDistributor(state, CreateDistributionConfig(999f));

            distributor.Tick(0f, new[] { ticketPlain, ticketModified }, Array.Empty<Ticket>());

            Assert.AreEqual(1, CountMatchingItemsOnBoard(state.Board, main, ticketPlain.Modifications));
            Assert.AreEqual(1, CountMatchingItemsOnBoard(state.Board, main, ticketModified.Modifications));
        }

        [Test]
        public void Tick_NoiseInterval_DoesNotSpawnBeforeIntervalElapses()
        {
            var state = new GameState(gameConfig);
            var main = CreateFoodItem();
            var upcoming = CreateTicket(new List<FoodItemConfig> { main });
            var distributor = new BoardDistributor(state, CreateDistributionConfig(4f));

            distributor.Tick(1f, Array.Empty<Ticket>(), new[] { upcoming });

            Assert.AreEqual(0, state.Board.OccupiedCellCount);
        }

        [Test]
        public void Tick_NoiseInterval_SpawnsItemFromUpcomingQueue_AfterIntervalElapses()
        {
            var state = new GameState(gameConfig);
            var main = CreateFoodItem();
            var upcoming = CreateTicket(new List<FoodItemConfig> { main });
            var distributor = new BoardDistributor(state, CreateDistributionConfig(2f));

            distributor.Tick(2f, Array.Empty<Ticket>(), new[] { upcoming });

            Assert.AreEqual(1, state.Board.OccupiedCellCount);
        }

        [Test]
        public void Tick_NoUpcomingTickets_SkipsNoiseSpawnWithoutThrowing()
        {
            var state = new GameState(gameConfig);
            var distributor = new BoardDistributor(state, CreateDistributionConfig(1f));

            Assert.DoesNotThrow(() => distributor.Tick(5f, Array.Empty<Ticket>(), Array.Empty<Ticket>()));
            Assert.AreEqual(0, state.Board.OccupiedCellCount);
        }

        [Test]
        public void Tick_SameUpcomingTicket_NeverLeaksMoreThanOnce_AcrossManyIntervals()
        {
            var state = new GameState(gameConfig);
            var main = CreateFoodItem();
            var upcoming = CreateTicket(new List<FoodItemConfig> { main });
            var distributor = new BoardDistributor(state, CreateDistributionConfig(1f));

            for (var i = 0; i < 10; i++)
            {
                distributor.Tick(1f, Array.Empty<Ticket>(), new[] { upcoming });
            }

            Assert.AreEqual(1, state.Board.OccupiedCellCount);
        }

        [Test]
        public void Tick_TicketLeavesUpcomingQueue_NewTicketBecomesEligibleForNoiseAgain()
        {
            var state = new GameState(gameConfig);
            var mainA = CreateFoodItem();
            var mainB = CreateFoodItem();
            var ticketA = CreateTicket(new List<FoodItemConfig> { mainA });
            var ticketB = CreateTicket(new List<FoodItemConfig> { mainB });
            var distributor = new BoardDistributor(state, CreateDistributionConfig(1f));

            distributor.Tick(1f, Array.Empty<Ticket>(), new[] { ticketA });
            distributor.Tick(1f, Array.Empty<Ticket>(), new[] { ticketB });

            Assert.AreEqual(2, state.Board.OccupiedCellCount);
        }

        [Test]
        public void Tick_BoardFull_RequestSpawnQueuesWithoutThrowing()
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
            var distributor = new BoardDistributor(state, CreateDistributionConfig(999f));

            Assert.DoesNotThrow(() => distributor.Tick(0f, new[] { ticket }, Array.Empty<Ticket>()));
            Assert.AreEqual(1, state.Board.PendingSpawnCount);
        }
    }
}
