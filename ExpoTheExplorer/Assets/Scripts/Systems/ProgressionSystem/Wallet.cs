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
    // Adım 1 is a pure move: every method below is the code that used to sit in
    // GameManager and LivesManager, verbatim. The day-attempt spend ledger and
    // the max(0, ...) clamp arrive in Adım 2; until then RevertToDayStart does
    // exactly what RetryCompletedDay did before it.
    public class Wallet
    {
        private readonly GameState state;

        // Snapshot of SoftMoney as of the moment the current day began. NOT
        // re-captured on a life-loss retry, so it survives any number of retries
        // of the same day as the true pre-day baseline (the behaviour
        // GameManager.dayStartSoftMoney had before this class existed).
        private int dayStartSoftMoney;

        public Wallet(GameState state)
        {
            this.state = state;
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
        // callers can no longer be trusted with the setter.
        public bool TrySpendSoftMoney(int cost)
        {
            if (state.SoftMoney < cost) return false;

            state.SoftMoney -= cost;
            return true;
        }

        public bool TrySpendGems(int cost)
        {
            if (state.Gems < cost) return false;

            state.Gems -= cost;
            return true;
        }

        public void CaptureDayStart()
        {
            dayStartSoftMoney = state.SoftMoney;
        }

        // Rolls SoftMoney back to the day-start snapshot: the voluntary "redo a
        // day I already won for a better star score" path, so replaying cannot
        // stack income on top of what the day already paid out.
        //
        // Gems are deliberately untouched, because RetryCompletedDay never
        // touched them either. Taking Gems back here would be a behaviour change
        // wearing a refactor's clothes -- Adım 2 is where the "a day attempt is
        // atomic" rule (earnings reverted, spending not refunded, clamped at 0)
        // lands for both currencies at once.
        public void RevertToDayStart()
        {
            state.SoftMoney = dayStartSoftMoney;
        }
    }
}
