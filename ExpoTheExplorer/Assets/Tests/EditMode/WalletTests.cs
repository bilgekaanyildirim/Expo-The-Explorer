using ExpoTheExplorer.Core;
using ExpoTheExplorer.Data;
using ExpoTheExplorer.Systems.ProgressionSystem;
using NUnit.Framework;
using UnityEngine;

namespace ExpoTheExplorer.Tests.EditMode
{
    // Adım 1 is a behaviour-neutral move, so these lock in what the code did
    // BEFORE the Wallet existed -- if any of them changes meaning later, that is
    // Adım 2 deliberately changing the rules, not a refactor drifting.
    //
    // Balances are seeded by assigning GameState directly, which the test
    // assembly alone may do (Core/AssemblyInfo.cs) -- and Gems have no earn path
    // in the game at all, so there is nothing else to seed them with.
    public class WalletTests
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
        public void EarnSoftMoney_AddsToBalance()
        {
            var state = new GameState(gameConfig);
            var wallet = new Wallet(state);

            wallet.EarnSoftMoney(30);
            wallet.EarnSoftMoney(12);

            Assert.AreEqual(42, state.SoftMoney);
        }

        // Earning is one-directional so it can never become a spending path that
        // skips the affordability check.
        [Test]
        public void EarnSoftMoney_WithZeroOrNegative_ChangesNothing()
        {
            var state = new GameState(gameConfig) { SoftMoney = 100 };
            var wallet = new Wallet(state);

            wallet.EarnSoftMoney(0);
            wallet.EarnSoftMoney(-25);

            Assert.AreEqual(100, state.SoftMoney);
        }

        [Test]
        public void TrySpendSoftMoney_WithEnough_DeductsAndReturnsTrue()
        {
            var state = new GameState(gameConfig) { SoftMoney = 500 };
            var wallet = new Wallet(state);

            var spent = wallet.TrySpendSoftMoney(250);

            Assert.IsTrue(spent);
            Assert.AreEqual(250, state.SoftMoney);
        }

        [Test]
        public void TrySpendSoftMoney_WithoutEnough_SpendsNothingAndReturnsFalse()
        {
            var state = new GameState(gameConfig) { SoftMoney = 100 };
            var wallet = new Wallet(state);

            var spent = wallet.TrySpendSoftMoney(250);

            Assert.IsFalse(spent);
            Assert.AreEqual(100, state.SoftMoney);
        }

        // Exact-cost spend must succeed: the check is "can't afford", not
        // "must have change left over".
        [Test]
        public void TrySpendSoftMoney_WithExactBalance_Succeeds()
        {
            var state = new GameState(gameConfig) { SoftMoney = 250 };
            var wallet = new Wallet(state);

            Assert.IsTrue(wallet.TrySpendSoftMoney(250));
            Assert.AreEqual(0, state.SoftMoney);
        }

        [Test]
        public void TrySpendGems_WithEnough_DeductsAndReturnsTrue()
        {
            var state = new GameState(gameConfig) { Gems = 10 };
            var wallet = new Wallet(state);

            var spent = wallet.TrySpendGems(5);

            Assert.IsTrue(spent);
            Assert.AreEqual(5, state.Gems);
        }

        [Test]
        public void TrySpendGems_WithoutEnough_SpendsNothingAndReturnsFalse()
        {
            var state = new GameState(gameConfig) { Gems = 4 };
            var wallet = new Wallet(state);

            var spent = wallet.TrySpendGems(5);

            Assert.IsFalse(spent);
            Assert.AreEqual(4, state.Gems);
        }

        [Test]
        public void RevertToDayStart_ReturnsSoftMoneyToTheCapturedBalance()
        {
            var state = new GameState(gameConfig) { SoftMoney = 100 };
            var wallet = new Wallet(state);

            wallet.CaptureDayStart();
            wallet.EarnSoftMoney(200);
            wallet.RevertToDayStart();

            Assert.AreEqual(100, state.SoftMoney);
        }

        // The constructor captures, so a revert with no explicit CaptureDayStart
        // still has a baseline (GameManager relies on this for day 0).
        [Test]
        public void Constructor_CapturesDayStart_SoRevertWorksWithoutAnExplicitCapture()
        {
            var state = new GameState(gameConfig) { SoftMoney = 75 };
            var wallet = new Wallet(state);

            wallet.EarnSoftMoney(400);
            wallet.RevertToDayStart();

            Assert.AreEqual(75, state.SoftMoney);
        }

        // Adım 1's deliberate boundary: RetryCompletedDay never touched Gems, so
        // neither does this. Adım 2 is where both currencies get the atomic
        // day-attempt rule (earnings reverted, spending NOT refunded, clamped at
        // 0) -- if this test starts failing, that is the change arriving.
        [Test]
        public void RevertToDayStart_LeavesGemsUntouched()
        {
            var state = new GameState(gameConfig) { Gems = 10 };
            var wallet = new Wallet(state);

            wallet.TrySpendGems(4);
            wallet.RevertToDayStart();

            Assert.AreEqual(6, state.Gems);
        }

        // Spending through the Wallet must still travel via GameState's setters,
        // or the HUD views bound to these events would silently stop updating.
        [Test]
        public void WalletWrites_StillPublishGameStateChangeEvents()
        {
            var state = new GameState(gameConfig) { SoftMoney = 100, Gems = 10 };
            var wallet = new Wallet(state);

            int? publishedSoftMoney = null;
            int? publishedGems = null;
            state.SoftMoneyChanged.Subscribe(value => publishedSoftMoney = value);
            state.GemsChanged.Subscribe(value => publishedGems = value);

            wallet.EarnSoftMoney(50);
            wallet.TrySpendGems(4);

            Assert.AreEqual(150, publishedSoftMoney);
            Assert.AreEqual(6, publishedGems);
        }
    }
}
