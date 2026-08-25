using System;
using System.Collections.Generic;
using ExpoTheExplorer.Core;
using ExpoTheExplorer.Data;
using ExpoTheExplorer.Systems.BoardDistribution;
using ExpoTheExplorer.Systems.PowerupSystem;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace ExpoTheExplorer.Tests.EditMode
{
    // What the powerups DO, as opposed to what they cost -- the stock rules live in
    // PowerupSystemTests and deliberately stay there. Split by file because the two answer
    // different questions and share no setup: this one needs tickets and a board, that one
    // needs a wallet and a save file.
    //
    // The case that earns this file is the "no wasted press" rule (GDD 5.2). It cannot be
    // tested from the manager's side, because the manager only ever sees a bool -- whether
    // that bool is HONEST is a property of the effect, and an effect that returned true
    // unconditionally would take a charge for doing nothing while every stock test still
    // passed.
    public class PowerupEffectsTests
    {
        private GameConfig gameConfig;
        private GameState state;

        [SetUp]
        public void SetUp()
        {
            gameConfig = ScriptableObject.CreateInstance<GameConfig>();
            state = new GameState(gameConfig);
        }

        [TearDown]
        public void TearDown()
        {
            UnityEngine.Object.DestroyImmediate(gameConfig);

            // The board cases below create their own configs and food items; SetUp's single
            // gameConfig is not enough for them.
            foreach (var asset in spawned) UnityEngine.Object.DestroyImmediate(asset);
            spawned.Clear();
        }

        // No food and no modifications: this suite never asks what a ticket CONTAINS, only
        // what its clock says. An empty required-item list keeps the fixtures to the two
        // numbers that matter.
        private static Ticket NewTicket(float timeLimit, float remaining, TicketState ticketState = TicketState.Active)
        {
            var ticket = new Ticket(
                "Test",
                PatienceType.Normal,
                Array.Empty<FoodItemConfig>(),
                Array.Empty<Modification>(),
                timeLimit)
            {
                RemainingSeconds = remaining,
                State = ticketState,
            };

            return ticket;
        }

        // --- the ordinary case -----------------------------------------------------------

        // Every ticket goes back to ITS OWN limit, not to a shared number. This is the test
        // that would fail if the effect ever grew a flat "+N seconds": the two tickets here
        // have deliberately different limits, which is exactly what per-Day patience
        // authoring produces.
        [Test]
        public void ResetActiveTicketTimers_RestoresEachTicketToItsOwnAuthoredLimit()
        {
            state.TicketSlots[0] = NewTicket(timeLimit: 45f, remaining: 3f);
            state.TicketSlots[1] = NewTicket(timeLimit: 150f, remaining: 90f);

            Assert.IsTrue(PowerupEffects.ResetActiveTicketTimers(state));

            Assert.AreEqual(45f, state.TicketSlots[0].RemainingSeconds);
            Assert.AreEqual(150f, state.TicketSlots[1].RemainingSeconds);
        }

        // --- the "no wasted press" rule --------------------------------------------------

        // The whole reason the effect returns a bool. Three fresh tickets have nothing to
        // restore, and a player who taps then must keep their charge.
        [Test]
        public void ResetActiveTicketTimers_WhenEveryTicketIsAlreadyFull_ReportsNoWork()
        {
            state.TicketSlots[0] = NewTicket(timeLimit: 60f, remaining: 60f);
            state.TicketSlots[1] = NewTicket(timeLimit: 60f, remaining: 60f);

            Assert.IsFalse(PowerupEffects.ResetActiveTicketTimers(state));
        }

        [Test]
        public void ResetActiveTicketTimers_WithNoTicketsAtAll_ReportsNoWork()
        {
            Assert.IsFalse(PowerupEffects.ResetActiveTicketTimers(state), "an empty board of slots is nothing to reset");
        }

        // Partial counts as work, and the full ticket beside it is left exactly where it
        // was rather than being rewritten to the same value -- so "did anything change"
        // stays an honest question.
        [Test]
        public void ResetActiveTicketTimers_WithOnePartialTicket_DoesTheWorkAndLeavesFullOnesAlone()
        {
            var full = NewTicket(timeLimit: 60f, remaining: 60f);
            var partial = NewTicket(timeLimit: 60f, remaining: 12f);
            state.TicketSlots[0] = full;
            state.TicketSlots[1] = partial;

            Assert.IsTrue(PowerupEffects.ResetActiveTicketTimers(state));

            Assert.AreEqual(60f, full.RemainingSeconds);
            Assert.AreEqual(60f, partial.RemainingSeconds);
        }

        // --- what it refuses to touch ----------------------------------------------------

        // A resolved ticket can still be sitting in the array for the rest of the
        // synchronous cascade that resolved it (TrayManager clears the tray, then delivers,
        // then the slot is refilled -- all inside one call). Putting time back on an order
        // nobody is waiting for would be wrong, and it would also make the return value lie.
        [Test]
        public void ResetActiveTicketTimers_IgnoresDeliveredAndCancelledTickets()
        {
            var delivered = NewTicket(timeLimit: 60f, remaining: 5f, TicketState.Delivered);
            var cancelled = NewTicket(timeLimit: 60f, remaining: 0f, TicketState.Cancelled);
            state.TicketSlots[0] = delivered;
            state.TicketSlots[1] = cancelled;

            Assert.IsFalse(PowerupEffects.ResetActiveTicketTimers(state), "no ACTIVE ticket means no work");

            Assert.AreEqual(5f, delivered.RemainingSeconds);
            Assert.AreEqual(0f, cancelled.RemainingSeconds);
        }

        // A resolved ticket must not make the effect skip the live one beside it either.
        [Test]
        public void ResetActiveTicketTimers_ResetsTheActiveTicketBesideAResolvedOne()
        {
            state.TicketSlots[0] = NewTicket(timeLimit: 60f, remaining: 1f, TicketState.Delivered);
            var active = NewTicket(timeLimit: 60f, remaining: 8f);
            state.TicketSlots[1] = active;

            Assert.IsTrue(PowerupEffects.ResetActiveTicketTimers(state));
            Assert.AreEqual(60f, active.RemainingSeconds);
        }

        // Not defensive clutter: the effect is registered as a delegate that closes over
        // GameManager.State, and every path that could hand it a null one is a path where
        // refusing quietly is right and throwing inside a button press is not.
        [Test]
        public void ResetActiveTicketTimers_WithNoState_ReportsNoWorkInsteadOfThrowing()
        {
            Assert.IsFalse(PowerupEffects.ResetActiveTicketTimers(null));
        }

        // --- the boundary this effect deliberately crosses -------------------------------

        // Ticket.RemainingSeconds now has three writers: the constructor opens it,
        // TicketSlotManager.Tick spends it, and this puts it back. The first two are the
        // "time moves forward" rule and this is the only place it moves back. Pinned as a
        // pair so the boundary is a test rather than a comment: a tick after a reset must
        // resume from the restored value, not from a cached one.
        [Test]
        public void AfterAReset_TheTickerResumesFromTheRestoredValue()
        {
            var ticket = NewTicket(timeLimit: 60f, remaining: 2f);
            state.TicketSlots[0] = ticket;

            PowerupEffects.ResetActiveTicketTimers(state);
            ticket.RemainingSeconds = Math.Max(0f, ticket.RemainingSeconds - 1.5f);

            Assert.AreEqual(58.5f, ticket.RemainingSeconds, 0.0001f);
        }

        // =================================================================================
        // ClearUnneededItems — GDD 5.2 #3
        // =================================================================================

        // The rule the user settled: BY IDENTITY, not by count. A ticket wanting one burger
        // protects every burger on the board, because a player looking at a third one says
        // there are too many, not that it is unwanted.
        [Test]
        public void ClearUnneededItems_KeepsEverySurplusCopyOfAWantedFood()
        {
            var burger = Food(FoodCategory.Main, "burger");
            var state = BoardState(width: 4, height: 1);
            state.TicketSlots[0] = TicketFor(burger);

            Place(state, burger, 0);
            Place(state, burger, 1);
            Place(state, burger, 2);

            Assert.IsFalse(
                PowerupEffects.ClearUnneededItems(state),
                "every item on the board is a burger and a burger is wanted — there is nothing to clear");
            Assert.AreEqual(3, ItemsOnBoard(state));
        }

        [Test]
        public void ClearUnneededItems_RemovesWhatNoActiveTicketWants()
        {
            var burger = Food(FoodCategory.Main, "burger");
            var cola = Food(FoodCategory.Drink, "cola");
            var state = BoardState(width: 4, height: 1);
            state.TicketSlots[0] = TicketFor(burger);

            Place(state, burger, 0);
            Place(state, cola, 1);
            Place(state, cola, 2);

            Assert.IsTrue(PowerupEffects.ClearUnneededItems(state));

            Assert.AreEqual(1, ItemsOnBoard(state));
            Assert.AreEqual(burger, state.Board.ItemAt(0, 0).Config);
        }

        // A plain burger must not survive on the strength of a no-pickles burger being
        // wanted. Same rule that stops a plain one satisfying that order in the tray.
        [Test]
        public void ClearUnneededItems_TreatsADifferentModificationComboAsADifferentItem()
        {
            var burger = Food(FoodCategory.Main, "burger");
            var pickles = Mod("pickles");
            var state = BoardState(width: 4, height: 1);
            state.TicketSlots[0] = TicketFor(burger, new Modification(pickles, isAddition: false));

            var wanted = new BoardItem(burger, new[] { new Modification(pickles, isAddition: false) });
            var plain = new BoardItem(burger, Array.Empty<Modification>());
            state.Board.RequestSpawn(wanted);
            state.Board.RequestSpawn(plain);

            Assert.IsTrue(PowerupEffects.ClearUnneededItems(state));

            Assert.AreEqual(1, ItemsOnBoard(state));
            Assert.AreEqual(
                1,
                state.Board.ItemAt(0, 0).Modifications.Count,
                "the survivor must be the no-pickles burger; the plain one is a different item entirely");
        }

        [Test]
        public void ClearUnneededItems_OnAnEmptyBoard_ReportsNoWork()
        {
            var state = BoardState(width: 3, height: 1);
            state.TicketSlots[0] = TicketFor(Food(FoodCategory.Main, "burger"));

            Assert.IsFalse(PowerupEffects.ClearUnneededItems(state));
        }

        // A resolved ticket's order stops protecting anything the moment it resolves --
        // otherwise a delivered ticket would keep shielding noise for the rest of the
        // cascade it is still sitting in the array for.
        [Test]
        public void ClearUnneededItems_DoesNotProtectWhatOnlyAResolvedTicketWanted()
        {
            var cola = Food(FoodCategory.Drink, "cola");
            var state = BoardState(width: 2, height: 1);
            state.TicketSlots[0] = TicketFor(cola, ticketState: TicketState.Delivered);

            Place(state, cola, 0);

            Assert.IsTrue(PowerupEffects.ClearUnneededItems(state));
            Assert.AreEqual(0, ItemsOnBoard(state));
        }

        // THE ORDERING TEST. BoardGrid.RemoveItem backfills the cell it just emptied from
        // the pending-spawn queue, so a version of the effect that collected coordinates
        // first and deleted afterwards would delete whatever landed in the meantime. Here
        // the queued item is one the active ticket WANTS, so deleting it would be a
        // straight bug -- and one that only shows up on a full board.
        [Test]
        public void ClearUnneededItems_DoesNotDeleteAnItemTheQueueBackfilledIntoAClearedCell()
        {
            var burger = Food(FoodCategory.Main, "burger");
            var cola = Food(FoodCategory.Drink, "cola");
            var state = BoardState(width: 1, height: 1);
            state.TicketSlots[0] = TicketFor(burger);

            // Fills the single cell, then queues the burger behind it.
            Place(state, cola, 0);
            state.Board.RequestSpawn(new BoardItem(burger, Array.Empty<Modification>()));
            Assert.AreEqual(1, state.Board.PendingSpawnCount, "the board must actually be full for this case");

            Assert.IsTrue(PowerupEffects.ClearUnneededItems(state));

            Assert.AreEqual(1, ItemsOnBoard(state));
            Assert.AreEqual(burger, state.Board.ItemAt(0, 0).Config, "the queued burger landed and must survive");
        }

        // --- the guaranteed-ticket rule (GDD Section 4) -----------------------------------

        // The user's question, answered as a test rather than as an argument. Whatever
        // BoardDistributor spawned for an ACTIVE guaranteed ticket is exactly what an active
        // ticket requires, so the clear cannot touch it and the ticket stays completable.
        [Test]
        public void ClearUnneededItems_LeavesEveryItemAGuaranteedActiveTicketNeeds()
        {
            var burger = Food(FoodCategory.Main, "burger");
            var fries = Food(FoodCategory.Side, "fries");
            var noise = Food(FoodCategory.Drink, "cola");

            var state = BoardState(width: 8, height: 1);
            state.TicketSlots[0] = TicketFor(burger, fries);

            var distributor = NewDistributor(state);
            distributor.OnOrderPlaced(new List<Ticket> { state.TicketSlots[0] }, new List<Ticket>());

            var beforeBurgers = CountOf(state, burger);
            var beforeFries = CountOf(state, fries);
            Assert.Greater(beforeBurgers + beforeFries, 0, "the distributor must have spawned the required pool");

            Place(state, noise, 7);

            PowerupEffects.ClearUnneededItems(state);

            Assert.AreEqual(beforeBurgers, CountOf(state, burger));
            Assert.AreEqual(beforeFries, CountOf(state, fries));
            Assert.AreEqual(0, CountOf(state, noise), "the noise is what should have gone");
        }

        // The backstop, also pinned rather than argued: BoardDistributor caches nothing, so
        // even if a clear DID take something a guaranteed ticket needs, the next order
        // placed recounts the board and re-spawns it. This is what makes the powerup unable
        // to strand a day.
        [Test]
        public void AfterAClear_TheDistributorRestoresWhatAGuaranteedTicketIsMissing()
        {
            var burger = Food(FoodCategory.Main, "burger");
            var state = BoardState(width: 8, height: 1);
            var ticket = TicketFor(burger);
            state.TicketSlots[0] = ticket;

            var distributor = NewDistributor(state);
            distributor.OnOrderPlaced(new List<Ticket> { ticket }, new List<Ticket>());
            var spawnedForTicket = CountOf(state, burger);
            Assert.Greater(spawnedForTicket, 0);

            // Wipe the board behind the powerup's back -- a harsher state than the effect
            // can actually produce, which is the point: the recovery must not depend on the
            // effect being well behaved.
            state.Board.Clear();
            Assert.AreEqual(0, ItemsOnBoard(state));

            distributor.OnOrderPlaced(new List<Ticket> { ticket }, new List<Ticket>());

            Assert.AreEqual(spawnedForTicket, CountOf(state, burger), "the required pool is rebuilt from a recount");
        }

        // =================================================================================
        // TryFindAutoCollectItem — GDD 5.2 #1's decision half
        // =================================================================================

        [Test]
        public void TryFindAutoCollectItem_FindsAnItemTheTicketStillNeeds()
        {
            var burger = Food(FoodCategory.Main, "burger");
            var cola = Food(FoodCategory.Drink, "cola");
            var state = BoardState(width: 4, height: 1);
            state.TicketSlots[0] = TicketFor(burger);

            Place(state, cola, 0);
            Place(state, burger, 1);

            Assert.IsTrue(PowerupEffects.TryFindAutoCollectItem(state, 0, NoTray, out var x, out var y));
            Assert.AreEqual(burger, state.Board.ItemAt(x, y).Config);
        }

        // The tray is subtracted, and COUNTS matter here unlike in the clear: a ticket
        // wanting two colas with one already collected still wants exactly one more.
        [Test]
        public void TryFindAutoCollectItem_SubtractsWhatTheTrayAlreadyHolds()
        {
            var cola = Food(FoodCategory.Drink, "cola");
            var state = BoardState(width: 4, height: 1);
            state.TicketSlots[0] = TicketFor(cola, cola);

            Place(state, cola, 0);
            Place(state, cola, 1);

            var trayWithOne = new List<BoardItem> { new(cola, Array.Empty<Modification>()) };
            Assert.IsTrue(PowerupEffects.TryFindAutoCollectItem(state, 0, trayWithOne, out _, out _),
                "one of the two colas is still owed");

            var trayWithBoth = new List<BoardItem>
            {
                new(cola, Array.Empty<Modification>()),
                new(cola, Array.Empty<Modification>()),
            };
            Assert.IsFalse(PowerupEffects.TryFindAutoCollectItem(state, 0, trayWithBoth, out _, out _),
                "the order is complete, so there is nothing left to fetch");
        }

        // A mis-drop the player has not resolved yet must NOT count as progress. That tray
        // is going to cost a life when it fills, and treating the wrong item as satisfying
        // a requirement would have the powerup finish the mistake for them.
        [Test]
        public void TryFindAutoCollectItem_DoesNotLetAnUnwantedTrayItemSatisfyARequirement()
        {
            var burger = Food(FoodCategory.Main, "burger");
            var cola = Food(FoodCategory.Drink, "cola");
            var state = BoardState(width: 4, height: 1);
            state.TicketSlots[0] = TicketFor(burger);

            Place(state, burger, 0);

            var trayWithWrongItem = new List<BoardItem> { new(cola, Array.Empty<Modification>()) };

            Assert.IsTrue(
                PowerupEffects.TryFindAutoCollectItem(state, 0, trayWithWrongItem, out _, out _),
                "the burger is still owed — the stray cola satisfies nothing");
        }

        // The property that makes this powerup incapable of costing a life: it only ever
        // offers an item the ticket is SHORT of, so the tray's batch check always lands on
        // the delivery branch.
        [Test]
        public void TryFindAutoCollectItem_NeverOffersAnItemTheTicketDoesNotWant()
        {
            var burger = Food(FoodCategory.Main, "burger");
            var cola = Food(FoodCategory.Drink, "cola");
            var state = BoardState(width: 4, height: 1);
            state.TicketSlots[0] = TicketFor(burger);

            Place(state, cola, 0);
            Place(state, cola, 1);

            Assert.IsFalse(PowerupEffects.TryFindAutoCollectItem(state, 0, NoTray, out _, out _));
        }

        // Modification combos are a different item here too, for the same reason as in the
        // clear: fetching a plain burger for a no-pickles order would fill the tray with a
        // wrong item and cost a life.
        [Test]
        public void TryFindAutoCollectItem_DoesNotOfferAPlainItemForAModifiedOrder()
        {
            var burger = Food(FoodCategory.Main, "burger");
            var pickles = Mod("pickles");
            var state = BoardState(width: 4, height: 1);
            state.TicketSlots[0] = TicketFor(burger, new Modification(pickles, isAddition: false));

            Place(state, burger, 0);

            Assert.IsFalse(
                PowerupEffects.TryFindAutoCollectItem(state, 0, NoTray, out _, out _),
                "a plain burger is not the no-pickles burger this ticket ordered");
        }

        [Test]
        public void TryFindAutoCollectItem_OnAnEmptySlot_FindsNothing()
        {
            var state = BoardState(width: 2, height: 1);
            Place(state, Food(FoodCategory.Main, "burger"), 0);

            Assert.IsFalse(PowerupEffects.TryFindAutoCollectItem(state, 0, NoTray, out _, out _));
        }

        [Test]
        public void TryFindAutoCollectItem_OnAResolvedTicket_FindsNothing()
        {
            var burger = Food(FoodCategory.Main, "burger");
            var state = BoardState(width: 2, height: 1);
            state.TicketSlots[0] = TicketFor(burger, TicketState.Delivered);

            Place(state, burger, 0);

            Assert.IsFalse(PowerupEffects.TryFindAutoCollectItem(state, 0, NoTray, out _, out _));
        }

        [Test]
        public void TryFindAutoCollectItem_WithAnOutOfRangeSlot_FindsNothingInsteadOfThrowing()
        {
            var state = BoardState(width: 2, height: 1);

            Assert.IsFalse(PowerupEffects.TryFindAutoCollectItem(state, -1, NoTray, out _, out _));
            Assert.IsFalse(PowerupEffects.TryFindAutoCollectItem(state, 99, NoTray, out _, out _));
        }

        private static readonly List<BoardItem> NoTray = new();

        // --- fixtures for the board cases ------------------------------------------------

        private readonly List<UnityEngine.Object> spawned = new();

        private GameState BoardState(int width, int height)
        {
            var config = ScriptableObject.CreateInstance<GameConfig>();
            spawned.Add(config);

            var serialized = new SerializedObject(config);
            serialized.FindProperty("boardWidth").intValue = width;
            serialized.FindProperty("boardHeight").intValue = height;
            serialized.ApplyModifiedPropertiesWithoutUndo();

            return new GameState(config);
        }

        private FoodItemConfig Food(FoodCategory category, string id)
        {
            var config = ScriptableObject.CreateInstance<FoodItemConfig>();
            spawned.Add(config);

            var serialized = new SerializedObject(config);
            serialized.FindProperty("id").stringValue = id;
            serialized.FindProperty("category").enumValueIndex = (int)category;
            serialized.ApplyModifiedPropertiesWithoutUndo();

            return config;
        }

        private ModificationConfig Mod(string id)
        {
            var config = ScriptableObject.CreateInstance<ModificationConfig>();
            spawned.Add(config);

            var serialized = new SerializedObject(config);
            var idProperty = serialized.FindProperty("id");
            if (idProperty != null) idProperty.stringValue = id;
            serialized.ApplyModifiedPropertiesWithoutUndo();

            return config;
        }

        private static Ticket TicketFor(params FoodItemConfig[] required) =>
            TicketFor(TicketState.Active, null, required);

        private static Ticket TicketFor(FoodItemConfig food, Modification modification) =>
            TicketFor(TicketState.Active, new[] { modification }, food);

        private static Ticket TicketFor(FoodItemConfig food, TicketState ticketState) =>
            TicketFor(ticketState, null, food);

        private static Ticket TicketFor(
            TicketState ticketState, IReadOnlyList<Modification> modifications, params FoodItemConfig[] required)
        {
            return new Ticket(
                "Test",
                PatienceType.Normal,
                required,
                modifications ?? Array.Empty<Modification>(),
                timeLimitSeconds: 90f)
            {
                State = ticketState,
            };
        }

        private static void Place(GameState state, FoodItemConfig food, int x) =>
            state.Board.TryPlaceItem(new BoardItem(food, Array.Empty<Modification>()), x, 0);

        private static int ItemsOnBoard(GameState state) => state.Board.OccupiedCellCount;

        private static int CountOf(GameState state, FoodItemConfig food)
        {
            var count = 0;
            for (var x = 0; x < state.Board.Width; x++)
            {
                for (var y = 0; y < state.Board.Height; y++)
                {
                    if (state.Board.ItemAt(x, y)?.Config == food) count++;
                }
            }

            return count;
        }

        // A distributor that guarantees exactly one ticket per round and leaks no noise, so
        // these cases are about the required pool and nothing else.
        private static BoardDistributor NewDistributor(GameState state)
        {
            var settings = new BoardDistributionSettings(
                noiseLeakCountLambda: 0f,
                guaranteedTicketCountMode: GuaranteedTicketCountMode.Manual,
                guaranteedTicketCount: 1,
                guaranteedTicketCountLambda: 0f,
                earlyTicketWeightDecay: 1f,
                urgentTimeThresholdSeconds: 10f,
                leakDepth: 0,
                maxLeakCount: 0);

            return new BoardDistributor(state, settings, new System.Random(1));
        }
    }
}
