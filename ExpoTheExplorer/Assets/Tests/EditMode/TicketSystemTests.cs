using System;
using System.Collections.Generic;
using System.Linq;
using ExpoTheExplorer.Core;
using ExpoTheExplorer.Data;
using ExpoTheExplorer.Systems.TicketSystem;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace ExpoTheExplorer.Tests.EditMode
{
    public class TicketSystemTests
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

        private Ticket CreateSimpleTicket(float timeLimitSeconds = 10f)
        {
            return new Ticket(
                "Test Customer",
                PatienceType.Normal,
                new List<FoodItemConfig>(),
                new List<Modification>(),
                timeLimitSeconds);
        }

        private TicketSlotManager CreateManager(GameState state, Func<Ticket> provider = null)
        {
            return new TicketSlotManager(state, provider ?? (() => CreateSimpleTicket()));
        }

        private FoodItemConfig CreateFoodItem(FoodCategory category, List<ModificationConfig> availableModifications = null)
        {
            var foodConfig = ScriptableObject.CreateInstance<FoodItemConfig>();
            spawnedAssets.Add(foodConfig);

            var serialized = new SerializedObject(foodConfig);
            serialized.FindProperty("category").enumValueIndex = (int)category;

            if (availableModifications is { Count: > 0 })
            {
                var modsProperty = serialized.FindProperty("availableModifications");
                modsProperty.ClearArray();
                for (var i = 0; i < availableModifications.Count; i++)
                {
                    modsProperty.InsertArrayElementAtIndex(i);
                    modsProperty.GetArrayElementAtIndex(i).objectReferenceValue = availableModifications[i];
                }
            }

            serialized.ApplyModifiedPropertiesWithoutUndo();
            return foodConfig;
        }

        private ModificationConfig CreateModification()
        {
            var modConfig = ScriptableObject.CreateInstance<ModificationConfig>();
            spawnedAssets.Add(modConfig);
            return modConfig;
        }

        [Test]
        public void FillEmptySlots_OnFreshGameState_FillsAllThreeSlots()
        {
            var state = new GameState(gameConfig);
            var manager = CreateManager(state);

            manager.FillEmptySlots();

            Assert.IsTrue(state.TicketSlots.All(t => t != null));
        }

        [Test]
        public void DeliverTicket_MarksDeliveredAndRefillsSameSlotWithNewTicket()
        {
            var state = new GameState(gameConfig);
            var manager = CreateManager(state);
            manager.FillEmptySlots();
            var original = state.TicketSlots[0];

            manager.DeliverTicket(0);

            Assert.AreEqual(TicketState.Delivered, original.State);
            Assert.IsNotNull(state.TicketSlots[0]);
            Assert.AreNotSame(original, state.TicketSlots[0]);
        }

        [Test]
        public void DeliverTicket_PublishesTicketDeliveredEvent_WithTheDeliveredTicket()
        {
            var state = new GameState(gameConfig);
            var manager = CreateManager(state);
            manager.FillEmptySlots();
            var original = state.TicketSlots[0];

            Ticket published = null;
            state.TicketDelivered.Subscribe(t => published = t);

            manager.DeliverTicket(0);

            Assert.AreSame(original, published);
        }

        [Test]
        public void Tick_WhenTicketTimesOut_DecrementsLivesAndCancelsAndRefillsSameSlot()
        {
            var state = new GameState(gameConfig);
            var manager = CreateManager(state);
            var ticket = CreateSimpleTicket(1f);
            state.TicketSlots[0] = ticket;
            var initialLives = state.Lives;

            manager.Tick(2f);

            Assert.AreEqual(initialLives - 1, state.Lives);
            Assert.AreEqual(TicketState.Cancelled, ticket.State);
            Assert.IsNotNull(state.TicketSlots[0]);
            Assert.AreNotSame(ticket, state.TicketSlots[0]);
        }

        [Test]
        public void Tick_BeforeTimeExpires_OnlyDecrementsRemainingSeconds_DoesNotCancelOrTouchLives()
        {
            var state = new GameState(gameConfig);
            var manager = CreateManager(state);
            var ticket = CreateSimpleTicket(10f);
            state.TicketSlots[0] = ticket;
            var initialLives = state.Lives;

            manager.Tick(1f);

            Assert.AreEqual(9f, ticket.RemainingSeconds, 0.0001f);
            Assert.AreEqual(TicketState.Active, ticket.State);
            Assert.AreEqual(initialLives, state.Lives);
        }

        [Test]
        public void CancelTicket_PublishesTicketCancelledEvent_AndDoesNotTouchLives()
        {
            var state = new GameState(gameConfig);
            var manager = CreateManager(state);
            var ticket = CreateSimpleTicket();
            state.TicketSlots[0] = ticket;
            var initialLives = state.Lives;

            Ticket published = null;
            state.TicketCancelled.Subscribe(t => published = t);

            manager.CancelTicket(0);

            Assert.AreSame(ticket, published);
            Assert.AreEqual(initialLives, state.Lives);
        }

        [Test]
        public void TicketSlots_StayAtThreeConcurrentActiveTickets_AcrossRepeatedDeliverCancelCycles()
        {
            var state = new GameState(gameConfig);
            var manager = CreateManager(state);
            manager.FillEmptySlots();

            for (var i = 0; i < 10; i++)
            {
                var slotIndex = i % GameState.TicketSlotCount;
                if (i % 2 == 0) manager.DeliverTicket(slotIndex);
                else manager.CancelTicket(slotIndex);

                Assert.AreEqual(GameState.TicketSlotCount, state.TicketSlots.Count(t => t != null));
            }
        }

        [Test]
        public void TicketFactory_Create_OnlyUsesItemsFromSuppliedPool_AndProducesPlausibleItemCount()
        {
            var modA = CreateModification();
            var modB = CreateModification();
            var main = CreateFoodItem(FoodCategory.Main, new List<ModificationConfig> { modA, modB });
            var side = CreateFoodItem(FoodCategory.Side);
            var drink = CreateFoodItem(FoodCategory.Drink);
            var pool = new List<FoodItemConfig> { main, side, drink };

            var genConfig = ScriptableObject.CreateInstance<TicketGenerationConfig>();
            spawnedAssets.Add(genConfig);

            var factory = new TicketFactory(genConfig, new System.Random(12345));
            var ticket = factory.Create(pool, "Test Customer", PatienceType.Normal);

            Assert.That(ticket.RequiredItems.Count, Is.InRange(1, 3));
            Assert.IsTrue(ticket.RequiredItems.All(item => pool.Contains(item)));
            Assert.AreEqual(genConfig.NormalTimeLimitSeconds, ticket.TimeLimitSeconds);
            Assert.IsTrue(ticket.Modifications.All(m => main.AvailableModifications.Contains(m.Config)));
        }

        [Test]
        public void TicketFactory_PickRandomCustomerName_ReturnsNameFromConfigPool()
        {
            var genConfig = ScriptableObject.CreateInstance<TicketGenerationConfig>();
            spawnedAssets.Add(genConfig);

            var factory = new TicketFactory(genConfig, new System.Random(12345));
            var name = factory.PickRandomCustomerName();

            Assert.IsTrue(genConfig.CustomerNames.Contains(name));
        }
    }
}
