using System;
using System.Collections.Generic;
using ExpoTheExplorer.Core;
using ExpoTheExplorer.Data;
using ExpoTheExplorer.Systems.TraySystem;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace ExpoTheExplorer.Tests.EditMode
{
    public class TraySystemTests
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

        private static Ticket CreateTicket(List<FoodItemConfig> requiredItems, List<Modification> modifications = null, float timeLimitSeconds = 90f)
        {
            return new Ticket("Test Customer", PatienceType.Normal, requiredItems, modifications ?? new List<Modification>(), timeLimitSeconds);
        }

        [Test]
        public void TryAddItem_CorrectItemsAndModifications_DeliversAndClearsTray()
        {
            var state = new GameState(gameConfig);
            var main = CreateFoodItem();
            var ticket = CreateTicket(new List<FoodItemConfig> { main });
            state.TicketSlots[0] = ticket;

            var delivered = new List<int>();
            var manager = new TrayManager(state, slotIndex => delivered.Add(slotIndex), () => state.Lives--);

            var accepted = manager.TryAddItem(0, new BoardItem(main, ticket.Modifications));

            Assert.IsTrue(accepted);
            Assert.AreEqual(new List<int> { 0 }, delivered);
            Assert.AreEqual(0, manager.GetContents(0).Count);
        }

        [Test]
        public void TryAddItem_OnAcceptedCallback_FiresBeforeDeliverTicketCascade()
        {
            // Regression test: onAccepted must fire before the batch check
            // (and the deliverTicket cascade it can trigger) runs — a caller
            // uses it to detach the item from the board's data model right
            // away, so a required-pool re-check that cascade triggers
            // (BoardDistributor, via GameManager) sees an accurate board
            // instead of this item still occupying its old cell.
            var state = new GameState(gameConfig);
            var main = CreateFoodItem();
            var ticket = CreateTicket(new List<FoodItemConfig> { main });
            state.TicketSlots[0] = ticket;

            var callOrder = new List<string>();
            var manager = new TrayManager(state, _ => callOrder.Add("delivered"), () => { });

            manager.TryAddItem(0, new BoardItem(main, ticket.Modifications), () => callOrder.Add("detached"));

            Assert.AreEqual(new List<string> { "detached", "delivered" }, callOrder);
        }

        [Test]
        public void TryAddItem_RejectedDrop_OnAcceptedCallbackNeverFires()
        {
            var state = new GameState(gameConfig);
            var main = CreateFoodItem();
            var manager = new TrayManager(state, _ => { }, () => state.Lives--);

            var onAcceptedCalled = false;
            var accepted = manager.TryAddItem(0, new BoardItem(main, new List<Modification>()), () => onAcceptedCalled = true);

            Assert.IsFalse(accepted);
            Assert.IsFalse(onAcceptedCalled);
        }

        [Test]
        public void TryAddItem_WrongItem_LosesLifeAndScattersToBoardAndClearsTray()
        {
            var state = new GameState(gameConfig);
            var main = CreateFoodItem();
            var wrongMain = CreateFoodItem();
            var ticket = CreateTicket(new List<FoodItemConfig> { main });
            state.TicketSlots[0] = ticket;
            var startingLives = state.Lives;

            var delivered = new List<int>();
            var manager = new TrayManager(state, slotIndex => delivered.Add(slotIndex), () => state.Lives--);

            var accepted = manager.TryAddItem(0, new BoardItem(wrongMain, ticket.Modifications));

            Assert.IsTrue(accepted);
            Assert.IsEmpty(delivered);
            Assert.AreEqual(startingLives - 1, state.Lives);
            Assert.AreEqual(0, manager.GetContents(0).Count);
            Assert.AreEqual(1, state.Board.OccupiedCellCount);
            Assert.AreSame(wrongMain, state.Board.ItemAt(0, 0)?.Config);
        }

        [Test]
        public void TryAddItem_WrongModifications_LosesLifeAndScattersToBoard()
        {
            var state = new GameState(gameConfig);
            var main = CreateFoodItem();
            var mod = CreateModification();
            var ticket = CreateTicket(new List<FoodItemConfig> { main }, new List<Modification> { new(mod, true) });
            state.TicketSlots[0] = ticket;
            var startingLives = state.Lives;

            var manager = new TrayManager(state, _ => { }, () => state.Lives--);

            manager.TryAddItem(0, new BoardItem(main, new List<Modification>()));

            Assert.AreEqual(startingLives - 1, state.Lives);
            Assert.AreEqual(1, state.Board.OccupiedCellCount);
        }

        [Test]
        public void TryAddItem_TrayNotYetFull_DoesNotDeliverOrClear()
        {
            var state = new GameState(gameConfig);
            var main = CreateFoodItem();
            var side = CreateFoodItem(FoodCategory.Side);
            var ticket = CreateTicket(new List<FoodItemConfig> { main, side });
            state.TicketSlots[0] = ticket;

            var delivered = new List<int>();
            var manager = new TrayManager(state, slotIndex => delivered.Add(slotIndex), () => state.Lives--);

            manager.TryAddItem(0, new BoardItem(main, ticket.Modifications));

            Assert.IsEmpty(delivered);
            Assert.AreEqual(1, manager.GetContents(0).Count);
        }

        [Test]
        public void TryAddItem_NoActiveTicketInSlot_RejectsItem()
        {
            var state = new GameState(gameConfig);
            var main = CreateFoodItem();
            var manager = new TrayManager(state, _ => { }, () => state.Lives--);

            var accepted = manager.TryAddItem(0, new BoardItem(main, new List<Modification>()));

            Assert.IsFalse(accepted);
        }

        [Test]
        public void TryAddItem_TicketAlreadyDelivered_RejectsItem()
        {
            var state = new GameState(gameConfig);
            var main = CreateFoodItem();
            var ticket = CreateTicket(new List<FoodItemConfig> { main });
            ticket.State = TicketState.Delivered;
            state.TicketSlots[0] = ticket;
            var manager = new TrayManager(state, _ => { }, () => state.Lives--);

            var accepted = manager.TryAddItem(0, new BoardItem(main, new List<Modification>()));

            Assert.IsFalse(accepted);
        }

        [Test]
        public void OnTicketAssigned_PartialTray_ScattersToBoardWithoutTouchingLives()
        {
            var state = new GameState(gameConfig);
            var main = CreateFoodItem();
            var side = CreateFoodItem(FoodCategory.Side);
            var ticket = CreateTicket(new List<FoodItemConfig> { main, side });
            state.TicketSlots[0] = ticket;
            var startingLives = state.Lives;

            var manager = new TrayManager(state, _ => { }, () => state.Lives--);
            manager.TryAddItem(0, new BoardItem(main, ticket.Modifications));

            manager.OnTicketAssigned(0);

            Assert.AreEqual(startingLives, state.Lives);
            Assert.AreEqual(0, manager.GetContents(0).Count);
            Assert.AreEqual(1, state.Board.OccupiedCellCount);
        }

        [Test]
        public void OnTicketAssigned_EmptyTray_DoesNothingWithoutThrowing()
        {
            var state = new GameState(gameConfig);
            var manager = new TrayManager(state, _ => { }, () => state.Lives--);

            Assert.DoesNotThrow(() => manager.OnTicketAssigned(0));
            Assert.AreEqual(0, state.Board.OccupiedCellCount);
        }

        [Test]
        public void TryAddItem_BoardFullOnScatter_RequestSpawnQueuesWithoutThrowing()
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
            var wrongMain = CreateFoodItem();
            var ticket = CreateTicket(new List<FoodItemConfig> { main });
            state.TicketSlots[0] = ticket;
            var manager = new TrayManager(state, _ => { }, () => state.Lives--);

            Assert.DoesNotThrow(() => manager.TryAddItem(0, new BoardItem(wrongMain, ticket.Modifications)));
            Assert.AreEqual(1, state.Board.PendingSpawnCount);
        }
    }
}
