using ExpoTheExplorer.Core;
using ExpoTheExplorer.Data;
using ExpoTheExplorer.Systems.LivesSystem;
using ExpoTheExplorer.Systems.ProgressionSystem;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace ExpoTheExplorer.Tests.EditMode
{
    public class LivesSystemTests
    {
        private GameConfig gameConfig;
        private LivesConfig livesConfig;

        [SetUp]
        public void SetUp()
        {
            gameConfig = ScriptableObject.CreateInstance<GameConfig>();
            livesConfig = ScriptableObject.CreateInstance<LivesConfig>();
        }

        [TearDown]
        public void TearDown()
        {
            Object.DestroyImmediate(gameConfig);
            Object.DestroyImmediate(livesConfig);
        }

        private void SetLivesConfig(int continueGemCost, int continueSoftMoneyCost)
        {
            var serialized = new SerializedObject(livesConfig);
            serialized.FindProperty("continueGemCost").intValue = continueGemCost;
            serialized.FindProperty("continueSoftMoneyCost").intValue = continueSoftMoneyCost;
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        // ApplyPersistedLives (decisions.md D-014). Lives are saved now, and they come
        // back through LivesManager because it is their single writer -- these cases
        // pin the CLAMP, which is what stands between a corrupt or hand-edited file and
        // a player who cannot act.
        [Test]
        public void ApplyPersistedLives_WithinRange_RestoresTheSavedCount()
        {
            var state = new GameState(gameConfig);
            var manager = new LivesManager(state, livesConfig, new Wallet(state));

            manager.ApplyPersistedLives(1);

            Assert.AreEqual(1, state.Lives);
        }

        // A saved 0 is a player dead on arrival with no way to act, so it is refused
        // rather than restored. PlayerProfileStore already upgrades pre-v3 files to full
        // lives, so a 0 arriving here means a corrupt or hand-edited file.
        [Test]
        public void ApplyPersistedLives_Zero_ClampsToOne()
        {
            var state = new GameState(gameConfig);
            var manager = new LivesManager(state, livesConfig, new Wallet(state));

            manager.ApplyPersistedLives(0);

            Assert.AreEqual(1, state.Lives);
        }

        // Above MaxLives would draw a life bar fuller than the game admits exists.
        [Test]
        public void ApplyPersistedLives_AboveMaxLives_ClampsToMaxLives()
        {
            var state = new GameState(gameConfig);
            var manager = new LivesManager(state, livesConfig, new Wallet(state));

            manager.ApplyPersistedLives(state.MaxLives + 5);

            Assert.AreEqual(state.MaxLives, state.Lives);
        }

        [Test]
        public void LoseLife_DecrementsLives()
        {
            var state = new GameState(gameConfig);
            var startingLives = state.Lives;
            var manager = new LivesManager(state, livesConfig, new Wallet(state));

            manager.LoseLife();

            Assert.AreEqual(startingLives - 1, state.Lives);
        }

        [Test]
        public void LoseLife_WhenLivesReachesZero_SetsIsAwaitingContinue_AndPublishesLivesDepleted()
        {
            var state = new GameState(gameConfig);
            state.Lives = 1;
            state.Gems = 7;
            var manager = new LivesManager(state, livesConfig, new Wallet(state));

            int? publishedGems = null;
            state.LivesDepleted.Subscribe(gems => publishedGems = gems);

            manager.LoseLife();

            Assert.AreEqual(0, state.Lives);
            Assert.IsTrue(state.IsAwaitingContinue);
            Assert.AreEqual(7, publishedGems);
        }

        [Test]
        public void LoseLife_WhenAlreadyAtZero_DoesNotGoNegative_AndDoesNotPublishAgain()
        {
            var state = new GameState(gameConfig);
            state.Lives = 1;
            var manager = new LivesManager(state, livesConfig, new Wallet(state));
            manager.LoseLife();

            var publishCount = 0;
            state.LivesDepleted.Subscribe(_ => publishCount++);

            manager.LoseLife();
            manager.LoseLife();

            Assert.AreEqual(0, state.Lives);
            Assert.AreEqual(0, publishCount);
        }

        [Test]
        public void LoseLife_AboveOne_DoesNotSetIsAwaitingContinue_OrPublish()
        {
            var state = new GameState(gameConfig);
            state.Lives = 3;
            var manager = new LivesManager(state, livesConfig, new Wallet(state));

            var published = false;
            state.LivesDepleted.Subscribe(_ => published = true);

            manager.LoseLife();

            Assert.AreEqual(2, state.Lives);
            Assert.IsFalse(state.IsAwaitingContinue);
            Assert.IsFalse(published);
        }

        [Test]
        public void RetryDay_RefillsLivesToMaxLives_ClearsIsAwaitingContinue_NoCurrencyCheck()
        {
            var state = new GameState(gameConfig);
            state.MaxLives = 3;
            state.Lives = 0;
            state.IsAwaitingContinue = true;
            state.SoftMoney = 0;
            state.Gems = 0;
            var manager = new LivesManager(state, livesConfig, new Wallet(state));

            manager.RetryDay();

            Assert.AreEqual(state.MaxLives, state.Lives);
            Assert.IsFalse(state.IsAwaitingContinue);
            Assert.AreEqual(0, state.SoftMoney);
            Assert.AreEqual(0, state.Gems);
        }

        [Test]
        public void TryContinueWithGems_WithEnoughGems_SpendsGemsAndRefillsLivesToMaxLives_AndClearsIsAwaitingContinue()
        {
            SetLivesConfig(continueGemCost: 5, continueSoftMoneyCost: 250);
            var state = new GameState(gameConfig);
            state.MaxLives = 3;
            state.Lives = 0;
            state.IsAwaitingContinue = true;
            state.Gems = 10;
            var manager = new LivesManager(state, livesConfig, new Wallet(state));

            var result = manager.TryContinueWithGems();

            Assert.IsTrue(result);
            Assert.AreEqual(5, state.Gems);
            Assert.AreEqual(state.MaxLives, state.Lives);
            Assert.IsFalse(state.IsAwaitingContinue);
        }

        [Test]
        public void TryContinueWithGems_WithoutEnoughGems_ReturnsFalse_AndSpendsNothing_AndLeavesLivesAtZero()
        {
            SetLivesConfig(continueGemCost: 5, continueSoftMoneyCost: 250);
            var state = new GameState(gameConfig);
            state.Lives = 0;
            state.IsAwaitingContinue = true;
            state.Gems = 4;
            var manager = new LivesManager(state, livesConfig, new Wallet(state));

            var result = manager.TryContinueWithGems();

            Assert.IsFalse(result);
            Assert.AreEqual(4, state.Gems);
            Assert.AreEqual(0, state.Lives);
            Assert.IsTrue(state.IsAwaitingContinue);
        }

        [Test]
        public void TryContinueWithSoftMoney_WithEnoughSoftMoney_SpendsSoftMoneyAndRefillsLivesToMaxLives_AndClearsIsAwaitingContinue()
        {
            SetLivesConfig(continueGemCost: 5, continueSoftMoneyCost: 250);
            var state = new GameState(gameConfig);
            state.MaxLives = 3;
            state.Lives = 0;
            state.IsAwaitingContinue = true;
            state.SoftMoney = 500;
            var manager = new LivesManager(state, livesConfig, new Wallet(state));

            var result = manager.TryContinueWithSoftMoney();

            Assert.IsTrue(result);
            Assert.AreEqual(250, state.SoftMoney);
            Assert.AreEqual(state.MaxLives, state.Lives);
            Assert.IsFalse(state.IsAwaitingContinue);
        }

        [Test]
        public void TryContinueWithSoftMoney_WithoutEnoughSoftMoney_ReturnsFalse_AndSpendsNothing_AndLeavesLivesAtZero()
        {
            SetLivesConfig(continueGemCost: 5, continueSoftMoneyCost: 250);
            var state = new GameState(gameConfig);
            state.Lives = 0;
            state.IsAwaitingContinue = true;
            state.SoftMoney = 100;
            var manager = new LivesManager(state, livesConfig, new Wallet(state));

            var result = manager.TryContinueWithSoftMoney();

            Assert.IsFalse(result);
            Assert.AreEqual(100, state.SoftMoney);
            Assert.AreEqual(0, state.Lives);
            Assert.IsTrue(state.IsAwaitingContinue);
        }
    }
}
