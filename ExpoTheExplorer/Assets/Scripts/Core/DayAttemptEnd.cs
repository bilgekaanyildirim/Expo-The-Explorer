namespace ExpoTheExplorer.Core
{
    // How a day attempt STOPPED, published by GameState.DayAttemptEnded at the moment
    // it stops (decisions.md D-149, plan in .claude/telemetry-plan.md §E.3).
    //
    // WHY THIS EXISTS AT ALL: nothing in the game could answer "did this attempt end
    // because it was lost, or because the player walked away from one that was still
    // playable?" -- and the two are not the same event to anyone reading a playtest
    // report. Three things were tried before this enum and all three are wrong:
    //
    //   1. "LivesDepleted means failed" -- it does not. A paid Continue refills lives
    //      and the SAME attempt carries on (GameManager's own note: "a paid Continue
    //      never publishes DayRetried"). Losing every life is an EVENT INSIDE a run,
    //      and it can happen more than once in one.
    //
    //   2. "Read IsAwaitingContinue when the run is finalized" -- by then it is always
    //      false. LivesManager.RefillLivesAndResume clears the flag BEFORE it refills
    //      (D-103), and every terminal path calls it, so a late read reports every
    //      lost day as a voluntary quit. Universally wrong rather than occasionally.
    //
    //   3. "Use RetryDay's givingUpOnAttempt parameter" -- it answers a different
    //      question. Both the Game Over popup and the settings menu pass true, because
    //      both are surrenders and both owe a key; only one of them is a LOSS.
    //
    // So the fact is captured where it is still true -- the first line of the two
    // methods that end an attempt -- and carried here rather than re-derived later.
    public enum DayAttemptEnd
    {
        // The attempt ended with the player out of lives and not continuing: the Game
        // Over popup's Retry or Main Menu.
        Lost,

        // The attempt ended while it was still playable: the settings menu's retry or
        // Main Menu, or a debug retry. The player chose to stop, they did not lose.
        GivenUp,
    }
}
