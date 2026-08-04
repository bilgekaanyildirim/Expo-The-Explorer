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

        // Spends Gems to refill Lives and resume the current day (GDD Section
        // 6). Returns false without spending anything if the player can't
        // afford ContinueGemCost.
        public bool TryContinue()
        {
            if (state.Gems < config.ContinueGemCost) return false;

            state.Gems -= config.ContinueGemCost;
            state.Lives = config.ContinueRefillAmount;
            // ContinueRefillAmount is a deliberately separate knob from
            // GameConfig.StartingLives -- MaxLives (the X/Y HUD's "out of" half)
            // has to follow it here so a Continue-granted amount different from
            // the day's starting Lives still reads as a full bar, not a partial one.
            state.MaxLives = config.ContinueRefillAmount;
            state.IsAwaitingContinue = false;
            return true;
        }
    }
}
