using ExpoTheExplorer.Data;

namespace ExpoTheExplorer.Core
{
    public static class TicketGenerationConfigExtensions
    {
        // Single source of truth for GDD Section 8's Impatient < Normal < Patient
        // time-limit rule -- TicketFactory.Create and Day-authored ticket
        // resolution (PR-6) both funnel through this instead of duplicating the
        // switch.
        public static float TimeLimitSecondsFor(this TicketGenerationConfig config, PatienceType patienceType) => patienceType switch
        {
            PatienceType.Impatient => config.ImpatientTimeLimitSeconds,
            PatienceType.Patient => config.PatientTimeLimitSeconds,
            _ => config.NormalTimeLimitSeconds,
        };
    }
}
