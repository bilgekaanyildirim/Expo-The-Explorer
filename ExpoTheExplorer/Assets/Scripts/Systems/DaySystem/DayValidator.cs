using System.Collections.Generic;
using System.Linq;

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
    // runtime load path. Deterministic and Random-free: BoardDistributor is never
    // invoked, only DaySolvabilityChecker's counting-based check, so this matches
    // runtime's actual board-mutation behavior exactly (real item removal only ever
    // happens via player drag-and-drop, which Day data never models).
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

            // Counting-based solvability check -- see DaySolvabilityChecker for why this
            // replaced the earlier (2026-08, PR-6.6) "board never shrinks" simulation, which
            // was a MORE generous supply assumption than real gameplay, not a stricter one,
            // and missed fungible (no-modification) items being under-supplied relative to
            // cumulative demand across the whole Day.
            foreach (var shortfall in DaySolvabilityChecker.FindShortfalls(day.TicketSequence, day.BoardTimeline))
            {
                errors.Add($"Step {shortfall.TicketIndex}: required item '{shortfall.MissingKey.Food.Id}' is not available in time for this ticket.");
            }

            if (day.RetryVariant != null)
            {
                var variantResult = Validate(day.RetryVariant);
                errors.AddRange(variantResult.Errors.Select(e => $"RetryVariant: {e}"));
            }

            return new DayValidationResult(errors);
        }
    }
}
