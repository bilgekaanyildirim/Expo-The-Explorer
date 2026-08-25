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

        // The three ApplyPersistedLives cases that stood here are gone with the method
        // (decisions.md D-064). They pinned the CLAMP that guarded a saved life count
        // against a corrupt or hand-edited file; there is no saved count any more, so
        // there is no clamp to guard and nothing here to test. What replaces them is
        // the case directly below: a brand-new GameState opens at a full bar, which is
        // now the ONLY thing that decides how many lives a day starts with.
        [Test]
        public void NewState_OpensAtAFullBar_WithNothingSeedingIt()
        {
            var state = new GameState(gameConfig);

            Assert.AreEqual(GameState.DefaultStartingLives, state.Lives);
            Assert.AreEqual(GameState.DefaultStartingLives, state.MaxLives);
            Assert.AreEqual(state.MaxLives, state.Lives, "a day must open with every heart filled");
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

        // Renamed with the method (D-064): the free refill now serves the successful
        // advance to the next day as well as the three retry/abandon paths, so it is
        // named for the refill rather than for a retry. The behaviour it pins is
        // unchanged -- full bar, flag cleared, and not a coin touched.
        [Test]
        public void RefillForNewDay_RefillsLivesToMaxLives_ClearsIsAwaitingContinue_NoCurrencyCheck()
        {
            var state = new GameState(gameConfig);
            state.MaxLives = 3;
            state.Lives = 0;
            state.IsAwaitingContinue = true;
            state.SoftMoney = 0;
            state.Gems = 0;
            var manager = new LivesManager(state, livesConfig, new Wallet(state));

            manager.RefillForNewDay();

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
