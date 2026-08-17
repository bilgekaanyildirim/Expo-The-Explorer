using UnityEngine;

namespace ExpoTheExplorer.Data
{
    // The TicketGenerationConfig values that matter while a Day is being PLAYED, as
    // opposed to the ones that only matter while a Day is being authored.
    //
    // The split is not arbitrary: a Day's ticketSequence is rolled ahead of time and baked
    // into its JSON, so the generation probabilities (side/drink inclusion, modification
    // count, main-dish weights) have already done their work by the time the game runs --
    // they stay in editorMeta as a record of how the Day was made. These four are
    // different: they are read live, every time a ticket is constructed or the lookahead
    // is refilled, so they are authored per Day under runtime (decisions.md D-005).
    //
    // Holds no PatienceType-dependent logic on purpose: Data must not reference Core (see
    // EconomyConfig's note on the same constraint), so the Impatient/Normal/Patient switch
    // lives in Core as TicketRuntimeSettingsExtensions.TimeLimitSecondsFor.
    public class TicketRuntimeSettings
    {
        public float ImpatientTimeLimitSeconds { get; }
        public float NormalTimeLimitSeconds { get; }
        public float PatientTimeLimitSeconds { get; }
        public int UpcomingQueueSize { get; }

        // UpcomingQueueSize's real floor is the active-slot count, but that constant lives
        // in Core (GameState.TicketSlotCount) which Data cannot reach, and hard-coding a 3
        // here would be a second copy of it free to drift. GameManager keeps applying that
        // floor at the point of use, exactly as it did before; this only rejects nonsense.
        public TicketRuntimeSettings(
            float impatientTimeLimitSeconds,
            float normalTimeLimitSeconds,
            float patientTimeLimitSeconds,
            int upcomingQueueSize)
        {
            ImpatientTimeLimitSeconds = Mathf.Max(0f, impatientTimeLimitSeconds);
            NormalTimeLimitSeconds = Mathf.Max(0f, normalTimeLimitSeconds);
            PatientTimeLimitSeconds = Mathf.Max(0f, patientTimeLimitSeconds);
            UpcomingQueueSize = Mathf.Max(1, upcomingQueueSize);
        }
    }
}
