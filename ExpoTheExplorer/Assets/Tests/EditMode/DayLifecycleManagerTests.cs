using ExpoTheExplorer.Core;
using ExpoTheExplorer.Data;
using ExpoTheExplorer.Systems.DayLifecycle;
using NUnit.Framework;
using UnityEngine;

namespace ExpoTheExplorer.Tests.EditMode
{
    public class DayLifecycleManagerTests
    {
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

            manager.RecordDelivery();
            manager.RecordDelivery();

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

            manager.RecordDelivery();
            manager.RecordDelivery();
            manager.RecordDelivery();

            Assert.IsFalse(published);
        }

        [Test]
        public void ResetForNewDay_ResetsCounterToZero()
        {
            var state = new GameState(gameConfig);
            var manager = new DayLifecycleManager(state);
            manager.RecordDelivery();
            manager.RecordDelivery();

            manager.ResetForNewDay();

            Assert.AreEqual(0, state.TicketsDeliveredToday);
        }
    }
}
