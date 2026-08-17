using ExpoTheExplorer.Data;

namespace ExpoTheExplorer.Core
{
    public static class TicketRuntimeSettingsExtensions
    {
        // Single source of truth for GDD Section 8's Impatient < Normal < Patient
        // time-limit rule -- TicketFactory.Create and Day-authored ticket resolution
        // (TicketEntryFactory) both funnel through this instead of duplicating the switch.
        //
        // Lives in Core rather than beside TicketRuntimeSettings in Data because it
        // switches on Core's PatienceType and Data must not reference Core. Replaces the
        // identical extension that used to hang off TicketGenerationConfig, from before
        // these four values became per-Day (decisions.md D-005).
        public static float TimeLimitSecondsFor(this TicketRuntimeSettings settings, PatienceType patienceType) => patienceType switch
        {
            PatienceType.Impatient => settings.ImpatientTimeLimitSeconds,
            PatienceType.Patient => settings.PatientTimeLimitSeconds,
            _ => settings.NormalTimeLimitSeconds,
        };
    }
}
