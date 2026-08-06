using ExpoTheExplorer.Core;
using ExpoTheExplorer.Data;
using NUnit.Framework;
using UnityEngine;

namespace ExpoTheExplorer.Tests.EditMode
{
    public class GameStateTests
    {
        private GameConfig config;

        [SetUp]
        public void SetUp()
        {
            config = ScriptableObject.CreateInstance<GameConfig>();
        }

        [TearDown]
        public void TearDown()
        {
            Object.DestroyImmediate(config);
        }

        [Test]
        public void NewGameState_AlwaysHasThreeTicketSlots()
        {
            var state = new GameState(config);

            Assert.AreEqual(3, GameState.TicketSlotCount);
            Assert.AreEqual(3, state.TicketSlots.Length);
        }

        [Test]
        public void NewGameState_TakesStartingResourcesFromConfig()
        {
            var state = new GameState(config);

            Assert.AreEqual(config.StartingLives, state.Lives);
            Assert.AreEqual(config.StartingLives, state.MaxLives);
            Assert.AreEqual(config.StartingSoftMoney, state.SoftMoney);
            Assert.AreEqual(config.StartingGems, state.Gems);
            Assert.AreEqual(config.StartingXp, state.Xp);
            Assert.AreEqual(config.StartingLevel, state.Level);
        }

        [Test]
        public void Xp_SetToDifferentValue_PublishesXpChangedWithNewValue()
        {
            var state = new GameState(config);

            int? published = null;
            state.XpChanged.Subscribe(xp => published = xp);

            state.Xp = 50;

            Assert.AreEqual(50, published);
        }

        [Test]
        public void Xp_SetToSameValue_DoesNotPublishXpChanged()
        {
            var state = new GameState(config);

            var publishCount = 0;
            state.XpChanged.Subscribe(_ => publishCount++);

            state.Xp = state.Xp;

            Assert.AreEqual(0, publishCount);
        }

        [Test]
        public void Level_SetToDifferentValue_PublishesLevelChangedWithNewValue()
        {
            var state = new GameState(config);

            int? published = null;
            state.LevelChanged.Subscribe(level => published = level);

            state.Level = 4;

            Assert.AreEqual(4, published);
        }

        [Test]
        public void Level_SetToSameValue_DoesNotPublishLevelChanged()
        {
            var state = new GameState(config);

            var publishCount = 0;
            state.LevelChanged.Subscribe(_ => publishCount++);

            state.Level = state.Level;

            Assert.AreEqual(0, publishCount);
        }
    }
}
