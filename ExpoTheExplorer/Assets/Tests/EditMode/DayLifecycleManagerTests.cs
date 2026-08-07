using ExpoTheExplorer.Core;
using ExpoTheExplorer.Data;
using ExpoTheExplorer.Systems.DayLifecycle;
using NUnit.Framework;
using UnityEditor;
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

        private void SetTicketsRequiredPerDay(int count)
        {
            var serialized = new SerializedObject(gameConfig);
            serialized.FindProperty("ticketsRequiredPerDay").intValue = count;
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        [Test]
        public void RecordDelivery_BelowGoal_IncrementsCounter_DoesNotPublishDayCompleted()
        {
            SetTicketsRequiredPerDay(3);
            var state = new GameState(gameConfig);
            var manager = new DayLifecycleManager(state, () => gameConfig.TicketsRequiredPerDay);

            var published = false;
            state.DayCompleted.Subscribe(_ => published = true);

            manager.RecordDelivery();
            manager.RecordDelivery();

            Assert.AreEqual(2, state.TicketsDeliveredToday);
            Assert.IsFalse(published);
        }

        [Test]
        public void RecordDelivery_ReachesGoalExactly_PublishesDayCompletedWithCount()
        {
            SetTicketsRequiredPerDay(3);
            var state = new GameState(gameConfig);
            var manager = new DayLifecycleManager(state, () => gameConfig.TicketsRequiredPerDay);

            int? published = null;
            state.DayCompleted.Subscribe(count => published = count);

            manager.RecordDelivery();
            manager.RecordDelivery();
            manager.RecordDelivery();

            Assert.AreEqual(3, published);
        }

        [Test]
        public void RecordDelivery_PastGoal_DoesNotPublishDayCompletedAgain()
        {
            SetTicketsRequiredPerDay(1);
            var state = new GameState(gameConfig);
            var manager = new DayLifecycleManager(state, () => gameConfig.TicketsRequiredPerDay);

            var publishCount = 0;
            state.DayCompleted.Subscribe(_ => publishCount++);

            manager.RecordDelivery();
            manager.RecordDelivery();
            manager.RecordDelivery();

            Assert.AreEqual(1, publishCount);
        }

        [Test]
        public void RecordDelivery_UsesInjectedDelegate_IndependentOfGameConfig()
        {
            // gameConfig defaults ticketsRequiredPerDay to 10 -- deliberately left
            // untouched here to prove the delegate, not GameConfig, drives the goal.
            var state = new GameState(gameConfig);
            var manager = new DayLifecycleManager(state, () => 2);

            int? published = null;
            state.DayCompleted.Subscribe(count => published = count);

            manager.RecordDelivery();
            Assert.IsNull(published);
            manager.RecordDelivery();

            Assert.AreEqual(2, published);
        }

        [Test]
        public void ResetForNewDay_ResetsCounterToZero()
        {
            var state = new GameState(gameConfig);
            var manager = new DayLifecycleManager(state, () => gameConfig.TicketsRequiredPerDay);
            manager.RecordDelivery();
            manager.RecordDelivery();

            manager.ResetForNewDay();

            Assert.AreEqual(0, state.TicketsDeliveredToday);
        }
    }
}
