using ExpoTheExplorer.Core;
using ExpoTheExplorer.Data;
using ExpoTheExplorer.Systems.LivesSystem;
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

        private void SetLivesConfig(int continueGemCost, int continueRefillAmount)
        {
            var serialized = new SerializedObject(livesConfig);
            serialized.FindProperty("continueGemCost").intValue = continueGemCost;
            serialized.FindProperty("continueRefillAmount").intValue = continueRefillAmount;
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        [Test]
        public void LoseLife_DecrementsLives()
        {
            var state = new GameState(gameConfig);
            var startingLives = state.Lives;
            var manager = new LivesManager(state, livesConfig);

            manager.LoseLife();

            Assert.AreEqual(startingLives - 1, state.Lives);
        }

        [Test]
        public void LoseLife_WhenLivesReachesZero_SetsIsAwaitingContinue_AndPublishesLivesDepleted()
        {
            var state = new GameState(gameConfig);
            state.Lives = 1;
            state.Gems = 7;
            var manager = new LivesManager(state, livesConfig);

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
            var manager = new LivesManager(state, livesConfig);
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
            var manager = new LivesManager(state, livesConfig);

            var published = false;
            state.LivesDepleted.Subscribe(_ => published = true);

            manager.LoseLife();

            Assert.AreEqual(2, state.Lives);
            Assert.IsFalse(state.IsAwaitingContinue);
            Assert.IsFalse(published);
        }

        [Test]
        public void TryContinue_WithEnoughGems_SpendsGemsAndRefillsLives_AndClearsIsAwaitingContinue()
        {
            SetLivesConfig(continueGemCost: 5, continueRefillAmount: 3);
            var state = new GameState(gameConfig);
            state.Lives = 0;
            state.IsAwaitingContinue = true;
            state.Gems = 10;
            var manager = new LivesManager(state, livesConfig);

            var result = manager.TryContinue();

            Assert.IsTrue(result);
            Assert.AreEqual(5, state.Gems);
            Assert.AreEqual(3, state.Lives);
            Assert.IsFalse(state.IsAwaitingContinue);
        }

        [Test]
        public void TryContinue_WithoutEnoughGems_ReturnsFalse_AndSpendsNothing_AndLeavesLivesAtZero()
        {
            SetLivesConfig(continueGemCost: 5, continueRefillAmount: 3);
            var state = new GameState(gameConfig);
            state.Lives = 0;
            state.IsAwaitingContinue = true;
            state.Gems = 4;
            var manager = new LivesManager(state, livesConfig);

            var result = manager.TryContinue();

            Assert.IsFalse(result);
            Assert.AreEqual(4, state.Gems);
            Assert.AreEqual(0, state.Lives);
            Assert.IsTrue(state.IsAwaitingContinue);
        }
    }
}
