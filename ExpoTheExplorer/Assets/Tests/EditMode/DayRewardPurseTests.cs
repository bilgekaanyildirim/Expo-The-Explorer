using ExpoTheExplorer.Systems.ProgressionSystem;
using NUnit.Framework;

namespace ExpoTheExplorer.Tests.EditMode
{
    // The arithmetic behind D-057's deferred payout. It is tested here rather than
    // trusted in place because the alternative home for it was a coroutine inside a
    // MonoBehaviour -- the one part of this project an EditMode test cannot reach -- and
    // the thing being computed is how much real money each flying coin is worth.
    //
    // Two properties matter more than any individual case: a Take can never hand out more
    // than the day earned, and the shares of a split always sum back to exactly the total.
    // The first stops a miscounted animation minting money; the second stops it losing
    // coins to rounding.
    public class DayRewardPurseTests
    {
        [Test]
        public void Constructor_ExposesWhatTheDayOwes()
        {
            var purse = new DayRewardPurse(320, 3);

            Assert.AreEqual(320, purse.SoftMoneyRemaining);
            Assert.AreEqual(3, purse.GemsRemaining);
            Assert.IsFalse(purse.IsEmpty);
        }

        // An authored rate is reachable from an Inspector, so a nonsense debt is a real
        // possibility. Owing nothing is the right reading of it.
        [Test]
        public void Constructor_ClampsNegativeDebtsToZero()
        {
            var purse = new DayRewardPurse(-50, -2);

            Assert.AreEqual(0, purse.SoftMoneyRemaining);
            Assert.AreEqual(0, purse.GemsRemaining);
            Assert.IsTrue(purse.IsEmpty);
        }

        [Test]
        public void TakeSoftMoney_DeductsAndReturnsWhatWasTaken()
        {
            var purse = new DayRewardPurse(100, 0);

            Assert.AreEqual(30, purse.TakeSoftMoney(30));
            Assert.AreEqual(70, purse.SoftMoneyRemaining);
        }

        // The property that makes the animation safe to get wrong: a flight claiming more
        // than the day earned drains the purse and stops.
        [Test]
        public void TakeSoftMoney_MoreThanIsOwed_TakesOnlyWhatIsLeft()
        {
            var purse = new DayRewardPurse(40, 0);

            Assert.AreEqual(40, purse.TakeSoftMoney(500));
            Assert.AreEqual(0, purse.SoftMoneyRemaining);
            Assert.AreEqual(0, purse.TakeSoftMoney(500), "An empty purse keeps paying nothing.");
        }

        [Test]
        public void TakeSoftMoney_WithZeroOrNegative_TakesNothing()
        {
            var purse = new DayRewardPurse(100, 0);

            Assert.AreEqual(0, purse.TakeSoftMoney(0));
            Assert.AreEqual(0, purse.TakeSoftMoney(-25));
            Assert.AreEqual(100, purse.SoftMoneyRemaining);
        }

        [Test]
        public void TakeGems_BehavesLikeTheOtherCurrency()
        {
            var purse = new DayRewardPurse(0, 3);

            Assert.AreEqual(1, purse.TakeGems(1));
            Assert.AreEqual(2, purse.TakeGems(9));
            Assert.AreEqual(0, purse.GemsRemaining);
        }

        // The two currencies are separate pools: draining one must not touch the other,
        // or a coin-heavy day would silently swallow its own gems.
        [Test]
        public void TakingOneCurrency_LeavesTheOtherAlone()
        {
            var purse = new DayRewardPurse(100, 3);

            purse.TakeAllSoftMoney();

            Assert.AreEqual(0, purse.SoftMoneyRemaining);
            Assert.AreEqual(3, purse.GemsRemaining);
            Assert.IsFalse(purse.IsEmpty);
        }

        // What every exit from a finished day runs: whatever the flight did not hand over
        // is paid in one lump.
        [Test]
        public void TakeAll_EmptiesThePurseAndReportsTheRemainder()
        {
            var purse = new DayRewardPurse(137, 3);
            purse.TakeSoftMoney(37);
            purse.TakeGems(1);

            Assert.AreEqual(100, purse.TakeAllSoftMoney());
            Assert.AreEqual(2, purse.TakeAllGems());
            Assert.IsTrue(purse.IsEmpty);
        }

        [Test]
        public void TakeAll_OnAnAlreadyEmptyPurse_ReportsNothingCredited()
        {
            var purse = new DayRewardPurse(0, 0);

            Assert.AreEqual(0, purse.TakeAllSoftMoney());
            Assert.AreEqual(0, purse.TakeAllGems());
        }

        // The headline property of the split: ten coins carrying 137 between them deliver
        // 137, not 130 with a remainder nobody receives.
        [Test]
        [TestCase(137, 10)]
        [TestCase(1000, 10)]
        [TestCase(7, 3)]
        [TestCase(1, 1)]
        [TestCase(99999, 7)]
        public void ShareOf_SharesAlwaysSumToTheTotal(int total, int count)
        {
            var sum = 0;
            for (var i = 0; i < count; i++)
            {
                sum += DayRewardPurse.ShareOf(total, i, count);
            }

            Assert.AreEqual(total, sum);
        }

        [Test]
        public void ShareOf_SplitsAsEvenlyAsIntegersAllow()
        {
            // 7 across 3 is 2,2,3 -- the remainder lands at the end rather than being lost.
            Assert.AreEqual(2, DayRewardPurse.ShareOf(7, 0, 3));
            Assert.AreEqual(2, DayRewardPurse.ShareOf(7, 1, 3));
            Assert.AreEqual(3, DayRewardPurse.ShareOf(7, 2, 3));
        }

        [Test]
        public void ShareOf_WithNothingToSplitOrNonsenseArguments_IsZero()
        {
            Assert.AreEqual(0, DayRewardPurse.ShareOf(0, 0, 5));
            Assert.AreEqual(0, DayRewardPurse.ShareOf(-100, 0, 5));
            Assert.AreEqual(0, DayRewardPurse.ShareOf(100, 0, 0));
            Assert.AreEqual(0, DayRewardPurse.ShareOf(100, -1, 5));
            Assert.AreEqual(0, DayRewardPurse.ShareOf(100, 5, 5));
        }

        // The reason the multiplication is widened to long: an int overflow here would not
        // throw, it would hand the player a NEGATIVE payout.
        [Test]
        public void ShareOf_WithALargeTotal_DoesNotOverflowIntoANegativeShare()
        {
            const int total = int.MaxValue - 1;
            const int count = 10;

            var sum = 0L;
            for (var i = 0; i < count; i++)
            {
                var share = DayRewardPurse.ShareOf(total, i, count);
                Assert.GreaterOrEqual(share, 0, "No share may be negative.");
                sum += share;
            }

            Assert.AreEqual(total, sum);
        }

        // The whole handover, end to end: three stars pay their gems one at a time, ten
        // coins pay the receipt, and the purse is empty with nothing left over.
        [Test]
        public void AFullHandover_PaysExactlyTheDebtAndEmptiesThePurse()
        {
            const int owedSoftMoney = 483;
            const int owedGems = 3;
            var purse = new DayRewardPurse(owedSoftMoney, owedGems);

            var paidGems = 0;
            for (var i = 0; i < 3; i++)
            {
                paidGems += purse.TakeGems(DayRewardPurse.ShareOf(owedGems, i, 3));
            }

            var paidSoftMoney = 0;
            for (var j = 0; j < 10; j++)
            {
                paidSoftMoney += purse.TakeSoftMoney(DayRewardPurse.ShareOf(owedSoftMoney, j, 10));
            }

            Assert.AreEqual(owedGems, paidGems);
            Assert.AreEqual(owedSoftMoney, paidSoftMoney);
            Assert.IsTrue(purse.IsEmpty);
        }
    }
}
