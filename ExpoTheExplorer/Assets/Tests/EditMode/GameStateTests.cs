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
        }
    }
}
