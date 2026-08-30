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

        private void SetLivesConfig(int continueGemCost)
        {
            var serialized = new SerializedObject(livesConfig);
            serialized.FindProperty("continueGemCost").intValue = continueGemCost;
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
            SetLivesConfig(continueGemCost: 5);
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
            SetLivesConfig(continueGemCost: 5);
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

        // The two TryContinueWithSoftMoney cases that stood here are gone with the method
        // (2026-08-30 audit). They were the ONLY thing still calling it: the SoftMoney
        // continue's button was never built into the Game Over prefab, so the feature was
        // reachable from this file and nowhere else, and the user's call was that it had
        // been dropped rather than half-built. What they pinned -- spend-then-refill, and
        // refuse-without-spending when the player cannot afford it -- is still pinned by
        // the two TryContinueWithGems cases above, which exercise the same
        // RefillLivesAndResume through the surviving paid path.

        // THE ORDER INSIDE RefillLivesAndResume, pinned from the only place it can be
        // observed: a LivesChanged subscriber (decisions.md D-103). GameState.Lives
        // publishes from its setter, synchronously, so this handler runs INSIDE the
        // assignment -- it sees exactly what SettingsPopupView, LivesView and
        // HapticsBinder see on a resume.
        //
        // The end state was already asserted by the two tests above and stayed green
        // through the bug, which is the point of writing this one differently: what
        // broke was never the final values, it was the state the event carried on its
        // way there. A day cannot both have a full bar of lives and be awaiting
        // Continue, and no subscriber should ever be handed that pair.
        [Test]
        public void RefillForNewDay_ClearsIsAwaitingContinue_BeforeLivesChangedIsPublished()
        {
            var state = new GameState(gameConfig);
            state.Lives = 0;
            state.IsAwaitingContinue = true;

            var published = 0;
            var awaitingContinueSeenByHandler = true;
            void OnLivesChanged(int _)
            {
                published++;
                awaitingContinueSeenByHandler = state.IsAwaitingContinue;
            }

            state.LivesChanged.Subscribe(OnLivesChanged);
            try
            {
                new LivesManager(state, livesConfig, new Wallet(state)).RefillForNewDay();
            }
            finally
            {
                state.LivesChanged.Unsubscribe(OnLivesChanged);
            }

            Assert.AreEqual(1, published, "The refill should publish LivesChanged exactly once.");
            Assert.IsFalse(
                awaitingContinueSeenByHandler,
                "A LivesChanged subscriber must see the day already resumed. With the flag cleared "
                + "after the Lives write, SettingsPopupView disabled its open button on the very "
                + "frame Retry was meant to re-enable it, and nothing published again until the "
                + "next life was lost.");
        }

        // The paid Continue reaches the same private body, and it is the route with a
        // wallet spend in front of it -- worth its own case so a future affordability
        // change cannot quietly move the flag back behind the publish on one path only.
        [Test]
        public void TryContinueWithGems_ClearsIsAwaitingContinue_BeforeLivesChangedIsPublished()
        {
            SetLivesConfig(continueGemCost: 5);
            var state = new GameState(gameConfig);
            state.Lives = 0;
            state.IsAwaitingContinue = true;
            state.Gems = 10;

            var awaitingContinueSeenByHandler = true;
            void OnLivesChanged(int _) => awaitingContinueSeenByHandler = state.IsAwaitingContinue;

            state.LivesChanged.Subscribe(OnLivesChanged);
            bool result;
            try
            {
                result = new LivesManager(state, livesConfig, new Wallet(state)).TryContinueWithGems();
            }
            finally
            {
                state.LivesChanged.Unsubscribe(OnLivesChanged);
            }

            Assert.IsTrue(result);
            Assert.IsFalse(awaitingContinueSeenByHandler);
        }
    }
}
