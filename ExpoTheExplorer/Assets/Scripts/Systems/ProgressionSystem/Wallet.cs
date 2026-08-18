using System;
using ExpoTheExplorer.Core;

namespace ExpoTheExplorer.Systems.ProgressionSystem
{
    // The ONE writer of GameState.SoftMoney and GameState.Gems (root CLAUDE.md:
    // "every piece of data has a single writer"). Lives in ProgressionSystem
    // because the project's CLAUDE.md Section 5 assigns the currencies here, and
    // is enforced rather than documented: those two setters are `internal` and
    // Core/AssemblyInfo.cs names this assembly as a friend, so nobody else can
    // assign a balance even by accident.
    //
    // Plain C#, no MonoBehaviour dependency, so it is unit-testable without a
    // scene. Deliberately holds no balance of its own -- GameState stays the
    // single place the numbers live (and the place that publishes the change
    // events HUD views bind to). This class owns the RULES for changing them,
    // not the values.
    //
    // Adım 1 moved the four scattered writers in here verbatim; Adım 2 added the
    // per-day spend ledger and made RevertToDayStart enforce the atomic-day rule
    // (see that method). Everything a day pays out is provisional until the day
    // is completed -- what makes it permanent is Adım 4's persistence, which does
    // not exist yet: nothing is written to disk, so a session still starts at 0.
    public class Wallet
    {
        private readonly GameState state;

        // Both balances as of the moment the current day began. NOT re-captured
        // on a life-loss retry, so they survive any number of retries of the same
        // day as the true pre-day baseline.
        private int dayStartSoftMoney;
        private int dayStartGems;

        // Everything SPENT since the day began -- the half of the atomic-day rule
        // that is never given back. Like the snapshots these deliberately do NOT
        // reset on a retry: two paid Continues across two failed attempts of the
        // same day are both still spent, measured against the one baseline above.
        private int softMoneySpentThisDay;
        private int gemsSpentThisDay;

        public Wallet(GameState state)
        {
            this.state = state;
            CaptureDayStart();
        }

        // Seeds both balances from a loaded PlayerProfile (economy-plan.md Adım 4).
        // The load path has to come through here for the same reason every other
        // write does: GameState's setters are internal to this assembly, so
        // GameManager physically cannot apply a profile itself.
        //
        // Re-captures the day-start baseline afterwards, because the day the player
        // is about to play starts from the RESTORED balance -- leaving the snapshot
        // at the pre-load 0 would make the first retry of the session revert their
        // whole wallet to zero.
        //
        // Negative values are clamped to 0: the file is plain JSON on a user's
        // disk, and a hand-edited negative balance should read as broke rather
        // than as debt that every later revert would preserve.
        public void ApplyPersistedBalances(int softMoney, int gems)
        {
            state.SoftMoney = Math.Max(0, softMoney);
            state.Gems = Math.Max(0, gems);
            CaptureDayStart();
        }

        // Earning is one-directional on purpose: a negative amount is ignored
        // rather than quietly subtracting, so this can never become a second
        // spending path behind TrySpend's affordability check. Not a behaviour
        // change -- the only caller passes a delivery payout, which is never
        // negative (Order Value and tip are both non-negative).
        public void EarnSoftMoney(int amount)
        {
            if (amount <= 0) return;

            state.SoftMoney += amount;
        }

        // Returns false and spends NOTHING when the player can't afford it --
        // the check and the deduction have to sit together, which is exactly why
        // callers can no longer be trusted with the setter. A negative cost is
        // refused outright so spending cannot be used to add money; a zero cost
        // still succeeds, so a free Continue stays authorable.
        public bool TrySpendSoftMoney(int cost)
        {
            if (cost < 0 || state.SoftMoney < cost) return false;

            state.SoftMoney -= cost;
            softMoneySpentThisDay += cost;
            return true;
        }

        public bool TrySpendGems(int cost)
        {
            if (cost < 0 || state.Gems < cost) return false;

            state.Gems -= cost;
            gemsSpentThisDay += cost;
            return true;
        }

        // The one place a new day's baseline is set: snapshot both balances and
        // wipe the spend ledger. Called by GameManager on the first day and on
        // every advance -- never by a retry, which is what keeps a retried day
        // measured against the day it actually started from.
        public void CaptureDayStart()
        {
            dayStartSoftMoney = state.SoftMoney;
            dayStartGems = state.Gems;
            softMoneySpentThisDay = 0;
            gemsSpentThisDay = 0;
        }

        // "A day attempt is atomic" (economy-plan.md): a day's economic result is
        // provisional until the day is completed successfully. On a failed day
        // (GameManager.OnDayRetried) or a voluntary redo of a won one
        // (RetryCompletedDay), everything EARNED that day is taken back and
        // everything SPENT stays spent:
        //
        //     balance = max(0, dayStartBalance - spentThisDay)
        //
        // The clamp is decision D1, approved 2026-08-18. It matters when a player
        // funds a Continue with money earned that same day: the earnings vanish
        // with the revert, so the purchase is effectively paid out of the
        // day-start balance, and a big enough purchase can take all of it. They
        // end the day poorer than they began it, but never in debt. The
        // alternative -- letting only the day-start balance fund a Continue --
        // was rejected as one more rule for the player to learn.
        public void RevertToDayStart()
        {
            state.SoftMoney = Math.Max(0, dayStartSoftMoney - softMoneySpentThisDay);
            state.Gems = Math.Max(0, dayStartGems - gemsSpentThisDay);
        }
    }
}
