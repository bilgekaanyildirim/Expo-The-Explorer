using ExpoTheExplorer.Core;
using ExpoTheExplorer.Data;
using ExpoTheExplorer.Systems.ProgressionSystem;

namespace ExpoTheExplorer.Systems.LivesSystem
{
    // Centralizes life loss (GDD Section 3/6 — a wrong delivery or a ticket
    // timeout costs a life) so both TicketSlotManager (timeout) and TrayManager
    // (wrong delivery) call the same place instead of separately mutating
    // GameState.Lives. Plain C#, no MonoBehaviour dependency (CLAUDE.md
    // Section 5), so it's unit-testable in isolation. Deliberately does NOT
    // reset the board/tickets or scale down difficulty on a failed day — GDD
    // locks "the day ends, Gems can refill Lives to continue" as the concept,
    // but the difficulty scale-down mechanism itself is a still-open GDD
    // question (CLAUDE.md Section 4), so that reaction is left to a future
    // system rather than guessed here.
    public class LivesManager
    {
        private readonly GameState state;
        private readonly LivesConfig config;

        // The paid-Continue prices are charged through the Wallet, not by
        // touching GameState directly: SoftMoney/Gems have exactly one writer
        // (root CLAUDE.md invariant), and since Adım 1 the setters are internal
        // to ProgressionSystem, so this class could not assign them even if it
        // tried. New one-directional arrow LivesSystem -> ProgressionSystem;
        // ProgressionSystem knows nothing about lives.
        private readonly Wallet wallet;

        public LivesManager(GameState state, LivesConfig config, Wallet wallet)
        {
            this.state = state;
            this.config = config;
            this.wallet = wallet;
        }

        // Exposes the balancing knobs so UI (e.g. a Continue/Game Over popup)
        // can render the current costs without duplicating them.
        public LivesConfig Config => config;

        // No-op once Lives is already at 0 — guards against firing
        // LivesDepleted more than once, or Lives drifting further negative, if
        // a caller (e.g. a ticket timeout) still requests a life loss while
        // the day is already over and awaiting Continue.
        public void LoseLife()
        {
            if (state.Lives <= 0) return;

            state.Lives--;
            if (state.Lives <= 0)
            {
                state.IsAwaitingContinue = true;
                state.LivesDepleted.Publish(state.Gems);
            }
        }

        // Spends SoftMoney to refill Lives and resume the current day (GDD
        // Section 6). Returns false without spending anything if the player
        // can't afford ContinueSoftMoneyCost.
        public bool TryContinueWithSoftMoney()
        {
            if (!wallet.TrySpendSoftMoney(config.ContinueSoftMoneyCost)) return false;

            RefillLivesAndResume();
            return true;
        }

        // Spends Gems to refill Lives and resume the current day (GDD Section
        // 6). Returns false without spending anything if the player can't
        // afford ContinueGemCost.
        public bool TryContinueWithGems()
        {
            if (!wallet.TrySpendGems(config.ContinueGemCost)) return false;

            RefillLivesAndResume();
            return true;
        }

        // Free day-reset primitive -- refills Lives the same way a paid Continue does,
        // just with no affordability check, since starting a day over costs nothing.
        //
        // Named for what it DOES rather than after one of its callers, and that matters
        // since D-064: it is called by the failed-day retry, the voluntary redo of a
        // finished day, the abandon-for-the-main-screen path, AND the successful advance
        // to the next day. It was called RetryDay while three of those four were retries;
        // once the advance path started calling it, that name was wrong at the place it
        // most needed to be right. GameManager.RetryDay keeps its name -- the game action
        // really is a retry.
        //
        // There is deliberately no ApplyPersistedLives beside this any more. Lives left
        // the save file in v6 (D-064), so the load path has nothing to seed: a fresh
        // GameState already opens at a full bar, and this is now the ONLY way Lives ever
        // go back up. That leaves this class the single writer of Lives with one fewer
        // entry point than before, which is the direction that invariant should move in.
        public void RefillForNewDay() => RefillLivesAndResume();

        // Refills Lives back to MaxLives -- MaxLives already holds whatever
        // life count the day began with (GameState.DefaultStartingLives, and
        // nothing else mutates it), so Continue reads as a full bar without
        // needing its own separate "refill amount" knob.
        private void RefillLivesAndResume()
        {
            state.Lives = state.MaxLives;
            state.IsAwaitingContinue = false;
        }
    }
}
