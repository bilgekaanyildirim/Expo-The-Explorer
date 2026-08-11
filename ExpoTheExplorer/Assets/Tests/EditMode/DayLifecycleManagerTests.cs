using ExpoTheExplorer.Core;
using ExpoTheExplorer.Data;
using ExpoTheExplorer.Systems.DayLifecycle;
using ExpoTheExplorer.Systems.EconomySystem;
using NUnit.Framework;
using UnityEngine;

namespace ExpoTheExplorer.Tests.EditMode
{
    public class DayLifecycleManagerTests
    {
        private static readonly DeliveryTipResult SampleTip =
            new DeliveryTipResult(baseTip: 10f, speedTier: SpeedTier.Standard, speedMultiplier: 1f, patienceDecayCoefficient: 1f);

        private GameConfig gameConfig;

        [SetUp]
        public void SetUp()
        {
            gameConfig = ScriptableObject.CreateInstance<GameConfig>();
        }

        [TearDown]
        public void TearDown()
        {
            Object.DestroyImmediate(gameConfig);
        }

        [Test]
        public void RecordDelivery_IncrementsCounter()
        {
            var state = new GameState(gameConfig);
            var manager = new DayLifecycleManager(state);

            manager.RecordDelivery(SampleTip);
            manager.RecordDelivery(SampleTip);

            Assert.AreEqual(2, state.TicketsDeliveredToday);
        }

        // Day completion is now TicketSlotManager's job (sequence exhaustion + all
        // slots empty, see its AssignTicket) -- a delivery-count goal here would
        // never fire once a single ticket was lost to a timeout instead of being
        // delivered (bug: fixed 2026-08).
        [Test]
        public void RecordDelivery_NeverPublishesDayCompleted()
        {
            var state = new GameState(gameConfig);
            var manager = new DayLifecycleManager(state);

            var published = false;
            state.DayCompleted.Subscribe(_ => published = true);

            manager.RecordDelivery(SampleTip);
            manager.RecordDelivery(SampleTip);
            manager.RecordDelivery(SampleTip);

            Assert.IsFalse(published);
        }

        [Test]
        public void ResetForNewDay_ResetsCounterToZero()
        {
            var state = new GameState(gameConfig);
            var manager = new DayLifecycleManager(state);
            manager.RecordDelivery(SampleTip);
            manager.RecordDelivery(SampleTip);

            manager.ResetForNewDay();

            Assert.AreEqual(0, state.TicketsDeliveredToday);
            Assert.AreEqual(0, manager.Total);
            Assert.AreEqual(0, manager.OrdersFailedCount);
        }

        [Test]
        public void RecordDelivery_SplitsBaseTipAndBonusIntoSeparateTotals()
        {
            var state = new GameState(gameConfig);
            var manager = new DayLifecycleManager(state);
            var tip = new DeliveryTipResult(baseTip: 20f, speedTier: SpeedTier.Lightning, speedMultiplier: 1.5f, patienceDecayCoefficient: 1f);

            manager.RecordDelivery(tip); // TotalTip = 30 -> bonus = 10

            Assert.AreEqual(20, manager.OrdersDeliveredValue);
            Assert.AreEqual(10, manager.TipsValue);
            Assert.AreEqual(30, manager.Total);
        }

        [Test]
        public void RecordFailure_IncrementsOrdersFailedCount()
        {
            var state = new GameState(gameConfig);
            var manager = new DayLifecycleManager(state);

            manager.RecordFailure();
            manager.RecordFailure();

            Assert.AreEqual(2, manager.OrdersFailedCount);
        }
    }
}
