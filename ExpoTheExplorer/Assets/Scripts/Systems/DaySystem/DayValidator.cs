using System.Collections.Generic;

namespace ExpoTheExplorer.Systems.DaySystem
{
    public class DayValidationResult
    {
        public bool IsValid => Errors.Count == 0;
        public IReadOnlyList<string> Errors { get; }

        public DayValidationResult(IReadOnlyList<string> errors)
        {
            Errors = errors;
        }
    }

    // Authoring-time gate for a Day (PR-7's "Save" button), not a runtime concern --
    // DayCatalogParser is never called from here, and this is never wired into the
    // runtime load path. No longer checks board-content solvability (see decisions.md
    // D-001 Phase 4): since BoardDistributor reconnected live (Phase 2/3), "is this Day
    // completable" is guaranteed BY CONSTRUCTION every OnOrderPlaced call at runtime,
    // not something to pre-validate from a per-step authored schedule -- the old
    // DaySolvabilityChecker-based check assumed exactly that kind of schedule and was
    // removed alongside it.
    public static class DayValidator
    {
        public static DayValidationResult Validate(DayDefinition day)
        {
            var errors = new List<string>();

            // Locked invariant since PR-1: an authored Day's ticketSequence must exactly
            // cover ticketsRequiredForDay -- no runtime fallback exists for a mismatch.
            if (day.TicketSequence.Count != day.TicketsRequiredForDay)
            {
                errors.Add($"ticketSequence has {day.TicketSequence.Count} entries but ticketsRequiredForDay is {day.TicketsRequiredForDay}.");
            }

            return new DayValidationResult(errors);
        }
    }
}
