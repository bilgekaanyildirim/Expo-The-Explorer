namespace ExpoTheExplorer.Systems.TelemetrySystem
{
    // How a run ended, or that it has not (.claude/telemetry-plan.md §E.4).
    //
    // AN ENUM RATHER THAN STRINGS, even though the wire format is a string: these four
    // values are what every balancing query filters on, and a typo in a string literal
    // would invent a fifth status that silently matches nothing and disappears from
    // every report. The one place the spelling lives is ToWireValue below.
    public enum RunStatus
    {
        // Still being played -- OR the client died before it could say otherwise.
        // Those two are deliberately the same value: a crash, a force-quit and an
        // app the OS killed all leave exactly this, and the client has no honest way
        // to tell them apart. Analytics reads a STALE in_progress (old lastUpdatedAt)
        // as an abandonment, which is why the client never writes "abandoned" itself.
        InProgress,

        // The Day's authored ticket sequence ran out with every slot empty.
        Completed,

        // The attempt ended with the player out of lives and not continuing.
        // NOT "the player lost all their lives at some point" -- that is
        // livesDepletedCount, and a paid Continue means it can happen inside a run
        // that goes on to be completed.
        Failed,

        // The player left a still-playable attempt: Main Menu, or the settings
        // menu's retry. They chose to stop; they did not lose.
        Quit,
    }

    public static class RunStatusExtensions
    {
        // The single place these four words are spelled. Firestore stores them as
        // strings because that is what reads well in a console and in a query, but
        // nothing upstream of here ever handles the string.
        public static string ToWireValue(this RunStatus status)
        {
            switch (status)
            {
                case RunStatus.Completed: return "completed";
                case RunStatus.Failed: return "failed";
                case RunStatus.Quit: return "quit";
                default: return "in_progress";
            }
        }
    }
}
