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
        public void NewGameState_StartsAtDefaultLivesAndAnEmptyWallet()
        {
            var state = new GameState(config);

            Assert.AreEqual(GameState.DefaultStartingLives, state.Lives);
            Assert.AreEqual(GameState.DefaultStartingLives, state.MaxLives);
            Assert.AreEqual(0, state.SoftMoney);
            Assert.AreEqual(0, state.Gems);
        }

        // Guards the GDD Section 6 locked rule (3 lives) against a silent edit
        // to the constant now that it is no longer designer-visible in GameConfig.
        [Test]
        public void DefaultStartingLives_IsThree()
        {
            Assert.AreEqual(3, GameState.DefaultStartingLives);
        }

        // These two used to sit on Xp/Level. They are kept, pointed at SoftMoney,
        // because what they actually guard is the publish-only-on-change setter
        // pattern every reactive GameState field shares -- deleting them with the
        // XP system would have taken that guard with it.
        [Test]
        public void SoftMoney_SetToDifferentValue_PublishesSoftMoneyChangedWithNewValue()
        {
            var state = new GameState(config);

            int? published = null;
            state.SoftMoneyChanged.Subscribe(softMoney => published = softMoney);

            state.SoftMoney = 50;

            Assert.AreEqual(50, published);
        }

        [Test]
        public void SoftMoney_SetToSameValue_DoesNotPublishSoftMoneyChanged()
        {
            var state = new GameState(config);

            var publishCount = 0;
            state.SoftMoneyChanged.Subscribe(_ => publishCount++);

            state.SoftMoney = state.SoftMoney;

            Assert.AreEqual(0, publishCount);
        }
    }
}
