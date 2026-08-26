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
            var manager = new TrayManager(state, slotIndex => delivered.Add(slotIndex), _ => state.Lives--);

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
            var manager = new TrayManager(state, _ => callOrder.Add("delivered"), _ => { });

            manager.TryAddItem(0, new BoardItem(main, ticket.Modifications), () => callOrder.Add("detached"));

            Assert.AreEqual(new List<string> { "detached", "delivered" }, callOrder);
        }

        [Test]
        public void TryAddItem_RejectedDrop_OnAcceptedCallbackNeverFires()
        {
            var state = new GameState(gameConfig);
            var main = CreateFoodItem();
            var manager = new TrayManager(state, _ => { }, _ => state.Lives--);

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
            var manager = new TrayManager(state, slotIndex => delivered.Add(slotIndex), _ => state.Lives--);

            var accepted = manager.TryAddItem(0, new BoardItem(wrongMain, ticket.Modifications));

            Assert.IsTrue(accepted);
            Assert.IsEmpty(delivered);
            Assert.AreEqual(startingLives - 1, state.Lives);
            Assert.AreEqual(0, manager.GetContents(0).Count);
            Assert.AreEqual(1, state.Board.OccupiedCellCount);

            // WHERE it lands is not part of the contract: TrayManager always holds a
            // System.Random, so BoardGrid.RequestSpawn picks uniformly among the empty
            // cells. Asserting cell (0, 0) made this test a 1-in-30 coin flip on a 6x5
            // board -- it read as a flaky test and was written off as one for a while.
            // What the scatter rule actually promises is that the item is back on the
            // board, so that is what gets asserted.
            Assert.AreSame(wrongMain, SingleItemOnBoard(state.Board).Config);
        }

        // D-099. Flinging a tray's worth of food across the board is the most visible thing
        // that can happen behind a Game Over popup, and the player is looking at the popup.
        // The life is still spent; only the scatter waits -- and the items stay in the tray
        // meanwhile, so the resume starts from the picture that was on screen.
        [Test]
        public void TryAddItem_WhenTheWrongOrderTakesTheLastLife_HoldsTheScatterAndKeepsTheTray()
        {
            var state = new GameState(gameConfig);
            var main = CreateFoodItem();
            var wrongMain = CreateFoodItem();
            var ticket = CreateTicket(new List<FoodItemConfig> { main });
            state.TicketSlots[0] = ticket;
            state.Lives = 1;

            var manager = new TrayManager(state, _ => { }, _ =>
            {
                state.Lives--;
                if (state.Lives <= 0) state.IsAwaitingContinue = true;
            });

            var accepted = manager.TryAddItem(0, new BoardItem(wrongMain, ticket.Modifications));

            Assert.IsTrue(accepted, "The item was still taken -- the batch check is what failed.");
            Assert.IsTrue(state.IsAwaitingContinue);
            Assert.AreEqual(0, state.Board.OccupiedCellCount, "Nothing may land on the board behind the popup.");
            Assert.AreEqual(1, manager.GetContents(0).Count, "The items stay where the player put them.");
        }

        [Test]
        public void ResolveDeferredScatters_OnceTheDayResumes_ScattersAndClearsTheTray()
        {
            var state = new GameState(gameConfig);
            var main = CreateFoodItem();
            var wrongMain = CreateFoodItem();
            var ticket = CreateTicket(new List<FoodItemConfig> { main });
            state.TicketSlots[0] = ticket;
            state.Lives = 1;

            var manager = new TrayManager(state, _ => { }, _ =>
            {
                state.Lives--;
                if (state.Lives <= 0) state.IsAwaitingContinue = true;
            });
            manager.TryAddItem(0, new BoardItem(wrongMain, ticket.Modifications));

            state.IsAwaitingContinue = false;
            state.Lives = state.MaxLives;

            manager.ResolveDeferredScatters();

            Assert.AreEqual(1, state.Board.OccupiedCellCount);
            Assert.AreSame(wrongMain, SingleItemOnBoard(state.Board).Config);
            Assert.AreEqual(0, manager.GetContents(0).Count, "The tray empties exactly as an undeferred scatter would.");
        }

        [Test]
        public void ResolveDeferredScatters_WithNothingDeferred_DoesNothing()
        {
            var state = new GameState(gameConfig);
            var main = CreateFoodItem();
            var ticket = CreateTicket(new List<FoodItemConfig> { main, CreateFoodItem() });
            state.TicketSlots[0] = ticket;
            var manager = new TrayManager(state, _ => { }, _ => state.Lives--);
            manager.TryAddItem(0, new BoardItem(main, ticket.Modifications));

            manager.ResolveDeferredScatters();

            Assert.AreEqual(1, manager.GetContents(0).Count, "It runs on every live frame -- it must be inert.");
            Assert.AreEqual(0, state.Board.OccupiedCellCount);
        }

        // The deferral dies with the items it was owed on: this method exists precisely
        // because the board is being wiped in the same reset, so a scatter surviving into the
        // new day would drop the old day's items onto it.
        [Test]
        public void DiscardAllForNewDay_ClearsADeferredScatter_SoTheNewDayOpensOnAnEmptyBoard()
        {
            var state = new GameState(gameConfig);
            var main = CreateFoodItem();
            var wrongMain = CreateFoodItem();
            var ticket = CreateTicket(new List<FoodItemConfig> { main });
            state.TicketSlots[0] = ticket;
            state.Lives = 1;

            var manager = new TrayManager(state, _ => { }, _ =>
            {
                state.Lives--;
                if (state.Lives <= 0) state.IsAwaitingContinue = true;
            });
            manager.TryAddItem(0, new BoardItem(wrongMain, ticket.Modifications));

            manager.DiscardAllForNewDay();
            state.IsAwaitingContinue = false;
            state.Lives = state.MaxLives;

            manager.ResolveDeferredScatters();

            Assert.AreEqual(0, state.Board.OccupiedCellCount, "The old day's items must not reach the new board.");
            Assert.AreEqual(0, manager.GetContents(0).Count);
        }

        // Fails loudly rather than returning null if the board does not hold exactly one
        // item, so a future regression surfaces here instead of as a NullReferenceException
        // inside the assertion above.
        private static BoardItem SingleItemOnBoard(BoardGrid board)
        {
            BoardItem found = null;
            for (var x = 0; x < board.Width; x++)
            {
                for (var y = 0; y < board.Height; y++)
                {
                    var item = board.ItemAt(x, y);
                    if (item == null) continue;
                    Assert.IsNull(found, "Expected exactly one item on the board.");
                    found = item;
                }
            }

            Assert.IsNotNull(found, "Expected exactly one item on the board, found none.");
            return found;
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

            var manager = new TrayManager(state, _ => { }, _ => state.Lives--);

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
            var manager = new TrayManager(state, slotIndex => delivered.Add(slotIndex), _ => state.Lives--);

            manager.TryAddItem(0, new BoardItem(main, ticket.Modifications));

            Assert.IsEmpty(delivered);
            Assert.AreEqual(1, manager.GetContents(0).Count);
        }

        [Test]
        public void TryAddItem_NoActiveTicketInSlot_RejectsItem()
        {
            var state = new GameState(gameConfig);
            var main = CreateFoodItem();
            var manager = new TrayManager(state, _ => { }, _ => state.Lives--);

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
            var manager = new TrayManager(state, _ => { }, _ => state.Lives--);

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

            var manager = new TrayManager(state, _ => { }, _ => state.Lives--);
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
            var manager = new TrayManager(state, _ => { }, _ => state.Lives--);

            Assert.DoesNotThrow(() => manager.OnTicketAssigned(0));
            Assert.AreEqual(0, state.Board.OccupiedCellCount);
        }

        [Test]
        public void DiscardAllForNewDay_ClearsAllSlotsWithoutScattering()
        {
            var state = new GameState(gameConfig);
            var main = CreateFoodItem();
            var side = CreateFoodItem(FoodCategory.Side);
            var ticketA = CreateTicket(new List<FoodItemConfig> { main, side });
            var ticketB = CreateTicket(new List<FoodItemConfig> { main, side });
            state.TicketSlots[0] = ticketA;
            state.TicketSlots[1] = ticketB;

            var manager = new TrayManager(state, _ => { }, _ => state.Lives--);
            manager.TryAddItem(0, new BoardItem(main, ticketA.Modifications));
            manager.TryAddItem(1, new BoardItem(main, ticketB.Modifications));

            manager.DiscardAllForNewDay();

            for (var i = 0; i < GameState.TicketSlotCount; i++)
            {
                Assert.AreEqual(0, manager.GetContents(i).Count);
            }
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
            var manager = new TrayManager(state, _ => { }, _ => state.Lives--);

            Assert.DoesNotThrow(() => manager.TryAddItem(0, new BoardItem(wrongMain, ticket.Modifications)));
            Assert.AreEqual(1, state.Board.PendingSpawnCount);
        }
    }
}
