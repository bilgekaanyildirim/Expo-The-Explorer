using ExpoTheExplorer.Core;
using ExpoTheExplorer.Data;

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

        public LivesManager(GameState state, LivesConfig config)
        {
            this.state = state;
            this.config = config;
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
            if (state.SoftMoney < config.ContinueSoftMoneyCost) return false;

            state.SoftMoney -= config.ContinueSoftMoneyCost;
            RefillLivesAndResume();
            return true;
        }

        // Spends Gems to refill Lives and resume the current day (GDD Section
        // 6). Returns false without spending anything if the player can't
        // afford ContinueGemCost.
        public bool TryContinueWithGems()
        {
            if (state.Gems < config.ContinueGemCost) return false;

            state.Gems -= config.ContinueGemCost;
            RefillLivesAndResume();
            return true;
        }

        // Refills Lives back to MaxLives -- MaxLives already holds whatever
        // GameConfig.StartingLives the day began with (nothing else mutates
        // it), so Continue reads as a full bar without needing its own
        // separate "refill amount" knob.
        private void RefillLivesAndResume()
        {
            state.Lives = state.MaxLives;
            state.IsAwaitingContinue = false;
        }
    }
}
