using ExpoTheExplorer.Core;
using ExpoTheExplorer.Data;
using ExpoTheExplorer.Systems.ProgressionSystem;
using NUnit.Framework;
using UnityEngine;

namespace ExpoTheExplorer.Tests.EditMode
{
    // Two layers here. The spend/earn cases came from Adım 1, which was a
    // behaviour-neutral move and locked in what the scattered writers already did.
    // The revert cases are Adım 2's atomic-day rule and DO change behaviour on
    // purpose: earnings taken back, spending never refunded, clamped at 0 (D1).
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

        // Gems reach the revert through the spend ledger now (Adım 2) rather than
        // being skipped entirely (Adım 1). The EXPECTED NUMBER is identical either
        // way -- Gems have no earn path in the game, so "untouched" and "spending
        // is not refunded" cannot be told apart by the balance alone. Only the
        // reason changed, which is worth saying out loud so nobody reads the
        // unchanged 6 as proof that nothing happened here.
        [Test]
        public void RevertToDayStart_DoesNotRefundGemsSpentThisDay()
        {
            var state = new GameState(gameConfig) { Gems = 10 };
            var wallet = new Wallet(state);

            wallet.TrySpendGems(4);
            wallet.RevertToDayStart();

            Assert.AreEqual(6, state.Gems);
        }

        // Sorun C: this is the farm that used to exist. Losing a day kept the
        // money it had earned, so failing on purpose paid.
        [Test]
        public void RevertToDayStart_TakesBackWhatTheDayEarned()
        {
            var state = new GameState(gameConfig) { SoftMoney = 1000 };
            var wallet = new Wallet(state);
            wallet.CaptureDayStart();

            wallet.EarnSoftMoney(300);
            wallet.RevertToDayStart();

            Assert.AreEqual(1000, state.SoftMoney);
        }

        // Sorun D: the other half of the rule. A paid Continue is gone for good,
        // even though the attempt it bought is being thrown away.
        [Test]
        public void RevertToDayStart_DoesNotRefundSoftMoneySpentThisDay()
        {
            var state = new GameState(gameConfig) { SoftMoney = 1000 };
            var wallet = new Wallet(state);
            wallet.CaptureDayStart();

            wallet.EarnSoftMoney(300);
            Assert.IsTrue(wallet.TrySpendSoftMoney(250));
            wallet.RevertToDayStart();

            Assert.AreEqual(750, state.SoftMoney);
        }

        // Decision D1. The player started the day with 100, earned 200, then spent
        // 250 on a Continue. The earnings vanish with the revert, so the purchase
        // comes out of the day-start balance and takes all of it -- but never
        // leaves them in debt.
        [Test]
        public void RevertToDayStart_WhenContinueWasFundedByTheDaysEarnings_ClampsAtZero()
        {
            var state = new GameState(gameConfig) { SoftMoney = 100 };
            var wallet = new Wallet(state);
            wallet.CaptureDayStart();

            wallet.EarnSoftMoney(200);
            Assert.IsTrue(wallet.TrySpendSoftMoney(250));
            wallet.RevertToDayStart();

            Assert.AreEqual(0, state.SoftMoney);
        }

        // The ledger survives a retry on purpose: both Continues stay spent, and
        // both are measured against the one day-start baseline rather than against
        // whatever the previous revert left behind.
        [Test]
        public void RevertToDayStart_AcrossTwoRetriesOfTheSameDay_KeepsBothContinuesSpent()
        {
            var state = new GameState(gameConfig) { SoftMoney = 1000 };
            var wallet = new Wallet(state);
            wallet.CaptureDayStart();

            Assert.IsTrue(wallet.TrySpendSoftMoney(250));
            wallet.RevertToDayStart();
            Assert.AreEqual(750, state.SoftMoney);

            Assert.IsTrue(wallet.TrySpendSoftMoney(250));
            wallet.RevertToDayStart();

            Assert.AreEqual(500, state.SoftMoney);
        }

        // ...but a real day change does clear it, or yesterday's spending would
        // keep being subtracted from every day that follows.
        [Test]
        public void CaptureDayStart_ClearsTheSpendLedger()
        {
            var state = new GameState(gameConfig) { SoftMoney = 1000 };
            var wallet = new Wallet(state);
            wallet.CaptureDayStart();

            Assert.IsTrue(wallet.TrySpendSoftMoney(250));
            wallet.RevertToDayStart();
            Assert.AreEqual(750, state.SoftMoney);

            wallet.CaptureDayStart(); // next day begins
            wallet.EarnSoftMoney(100);
            wallet.RevertToDayStart();

            Assert.AreEqual(750, state.SoftMoney);
        }

        // A day where nothing was earned and nothing was spent must come out
        // exactly where it went in -- the clamp must not quietly zero a balance
        // just because a revert happened.
        [Test]
        public void RevertToDayStart_WithNoActivity_LeavesBothBalancesAlone()
        {
            var state = new GameState(gameConfig) { SoftMoney = 400, Gems = 7 };
            var wallet = new Wallet(state);
            wallet.CaptureDayStart();

            wallet.RevertToDayStart();

            Assert.AreEqual(400, state.SoftMoney);
            Assert.AreEqual(7, state.Gems);
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
