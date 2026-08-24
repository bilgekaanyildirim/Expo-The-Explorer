using System;

namespace ExpoTheExplorer.Systems.ProgressionSystem
{
    // What a COMPLETED day owes the player, and how much of that debt has been handed
    // over so far. It exists because the payout stopped being instantaneous: a day's
    // coins and gems are no longer credited the moment they are earned, they are held
    // here and released piece by piece as the Day Complete popup's reward flight lands
    // each icon on its HUD counter.
    //
    // It is NOT a second authority on the player's money, which is the reason it is safe
    // to exist at all: it holds a DEBT that has never been in the wallet, `Wallet` stays
    // the compiler-enforced single writer of both balances, and every Take here is paired
    // with exactly one Earn there by the caller that owns both (GameManager).
    //
    // Plain C# with no Unity dependency, so the arithmetic that decides how much each
    // flying coin is worth is unit-testable without a scene -- which matters more than
    // usual here, because the alternative (working it out inside a coroutine) would put
    // real money in the one place this project cannot run a test.
    public class DayRewardPurse
    {
        private int softMoneyRemaining;
        private int gemsRemaining;

        // Negative debts clamp to zero rather than throwing: the two inputs are a day's
        // receipt total and a star count times an authored rate, and an authored rate is
        // reachable by a user with an Inspector. Owing nothing is the right reading of a
        // nonsense number, and it keeps the flight sequence a no-op instead of a crash.
        public DayRewardPurse(int softMoney, int gems)
        {
            softMoneyRemaining = Math.Max(0, softMoney);
            gemsRemaining = Math.Max(0, gems);
        }

        public int SoftMoneyRemaining => softMoneyRemaining;
        public int GemsRemaining => gemsRemaining;
        public bool IsEmpty => softMoneyRemaining == 0 && gemsRemaining == 0;

        // Every Take returns what was ACTUALLY taken, never what was asked for. That is
        // what makes the animation safe to get wrong: a flight that claims more than the
        // day earned (a rounding slip, a skipped step, a double callback) drains the
        // purse and stops, it cannot mint money the day did not make.
        public int TakeSoftMoney(int amount) => Take(ref softMoneyRemaining, amount);

        public int TakeGems(int amount) => Take(ref gemsRemaining, amount);

        public int TakeAllSoftMoney() => Take(ref softMoneyRemaining, softMoneyRemaining);

        public int TakeAllGems() => Take(ref gemsRemaining, gemsRemaining);

        private static int Take(ref int pool, int amount)
        {
            if (amount <= 0) return 0;

            var taken = Math.Min(pool, amount);
            pool -= taken;
            return taken;
        }

        // The `index`-th of `count` integer shares of `total`, computed so the shares sum
        // back to EXACTLY total with no drift and no leftover -- ten coins splitting 137
        // pay 13,14,14,13,14,14,13,14,14,14 rather than ten times 13 with a 7-coin
        // remainder nobody ever receives.
        //
        // Done by differencing two running totals instead of dividing and patching the
        // last share, because the last-share patch is the version that silently breaks
        // when the sequence is skipped halfway. Widened to long before multiplying: a
        // day's total times a coin index overflows int surprisingly early once a config
        // is tuned upward, and an overflow here would be a negative payout.
        public static int ShareOf(int total, int index, int count)
        {
            if (total <= 0 || count <= 0 || index < 0 || index >= count) return 0;

            var upToThis = (long)total * (index + 1) / count;
            var upToPrevious = (long)total * index / count;
            return (int)(upToThis - upToPrevious);
        }
    }
}
