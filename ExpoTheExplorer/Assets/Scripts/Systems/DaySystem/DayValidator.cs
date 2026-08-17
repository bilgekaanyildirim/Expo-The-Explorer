using System.Collections.Generic;
using System.Linq;
using ExpoTheExplorer.Data;

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
        // allowedFoods is the Day's own food selection, resolved to items (see
        // DayContentGenerator.ResolveFoodPool). It is a required parameter rather than an
        // optional one so no caller can silently skip the check by forgetting it; null is
        // reserved for "the caller has no catalog to resolve against yet", which is only
        // the unconfigured-toolbar case the Day Editor short-circuits before getting here.
        public static DayValidationResult Validate(DayDefinition day, IReadOnlyList<FoodItemConfig> allowedFoods)
        {
            var errors = new List<string>();

            // Locked invariant since PR-1: an authored Day's ticketSequence must exactly
            // cover ticketsRequiredForDay -- no runtime fallback exists for a mismatch.
            if (day.TicketSequence.Count != day.TicketsRequiredForDay)
            {
                errors.Add($"ticketSequence has {day.TicketSequence.Count} entries but ticketsRequiredForDay is {day.TicketsRequiredForDay}.");
            }

            if (allowedFoods != null)
            {
                // A ticket asking for food the Day doesn't serve is unplayable: the board
                // only ever spawns from the Day's own pool, so that item can never arrive.
                // Deliberately does NOT cover Day Start board entries -- their picker is
                // unfiltered by design, and failing Save for something the UI invites
                // would be worse than the gap.
                for (var i = 0; i < day.TicketSequence.Count; i++)
                {
                    foreach (var item in RequiredItemsOf(day.TicketSequence[i]))
                    {
                        if (!allowedFoods.Contains(item))
                        {
                            errors.Add($"Ticket {i}: '{item.Id}' is not in this Day's food selection.");
                        }
                    }
                }
            }

            return new DayValidationResult(errors);
        }

        private static IEnumerable<FoodItemConfig> RequiredItemsOf(ResolvedTicketEntry ticket)
        {
            // Nulls are a separate authoring state (an unfilled ticket slot), not a
            // selection violation -- reporting them here would double up on the empty-Day
            // feedback the editor already gives.
            if (ticket.MainItem != null) yield return ticket.MainItem;
            if (ticket.SideItem != null) yield return ticket.SideItem;
            if (ticket.DrinkItem != null) yield return ticket.DrinkItem;
        }
    }
}
