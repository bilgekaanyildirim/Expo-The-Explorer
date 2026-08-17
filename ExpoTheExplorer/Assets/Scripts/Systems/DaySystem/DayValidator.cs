using System;
using System.Collections.Generic;
using System.Linq;
using ExpoTheExplorer.Data;
using ExpoTheExplorer.Core;

namespace ExpoTheExplorer.Systems.DaySystem
{
    public class DayValidationResult
    {
        // Warnings deliberately do NOT affect this. They cover Days that work but probably
        // are not what the designer meant, and the Save button is gated on IsValid -- making
        // them block would turn "this looks odd" into "you may not save", which is not this
        // tool's call to make about design decisions.
        public bool IsValid => Errors.Count == 0;

        public IReadOnlyList<string> Errors { get; }
        public IReadOnlyList<string> Warnings { get; }

        public DayValidationResult(IReadOnlyList<string> errors, IReadOnlyList<string> warnings = null)
        {
            Errors = errors;
            Warnings = warnings ?? Array.Empty<string>();
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
            var warnings = new List<string>();

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

            ValidateTicketRuntime(day.TicketRuntime, errors, warnings);
            ValidateBoardDistribution(day.BoardDistribution, day.TicketRuntime, warnings);

            return new DayValidationResult(errors, warnings);
        }

        // WHAT THIS METHOD CAN AND CANNOT SEE -- worth stating, because getting it wrong
        // produced three rules that could never fire:
        //
        // DayCatalogParser inspects the RAW JSON and rejects out-of-range values. This
        // validator inspects the RESOLVED settings objects, whose constructors have already
        // clamped everything into range. So a rule shaped like "reject what the parser
        // rejects" is dead on arrival here for every field the constructor clamps --
        // UpcomingQueueSize (Max(1, x)), GuaranteedTicketCount (Clamp 1..3), LeakDepth and
        // MaxLeakCount (Clamp 1..10). Those checks live where they can work instead: the Day
        // Editor clamps on load, so a hand-edited file cannot be carried back out unchanged.
        //
        // Time limits are the exception and the reason the one error below survives: the
        // constructor uses Max(0f, x), so a 0 passes straight through, and a 0-second ticket
        // times out on arrival while the parser refuses the Day outright.
        //
        // Null blocks are not an error: authoring-side callers build a DayDefinition purely
        // to run it past this method and never fill them (see the ctor's optional params).
        private static void ValidateTicketRuntime(TicketRuntimeSettings runtime, List<string> errors, List<string> warnings)
        {
            if (runtime == null) return;

            if (runtime.ImpatientTimeLimitSeconds <= 0f || runtime.NormalTimeLimitSeconds <= 0f || runtime.PatientTimeLimitSeconds <= 0f)
            {
                errors.Add("Ticket Runtime: every patience time limit must be greater than 0 -- a 0-second ticket times out the instant it arrives, and the runtime refuses to load the Day.");
            }

            // GDD Section 8 states the ordering as a locked rule. Warning, not an error: the
            // Day still plays, and overruling a designer on a design decision is not this
            // gate's job -- surfacing it is.
            if (runtime.ImpatientTimeLimitSeconds > 0f && runtime.PatientTimeLimitSeconds > 0f &&
                !(runtime.ImpatientTimeLimitSeconds < runtime.NormalTimeLimitSeconds && runtime.NormalTimeLimitSeconds < runtime.PatientTimeLimitSeconds))
            {
                warnings.Add($"Ticket Runtime: GDD Section 8 expects Impatient < Normal < Patient, but this Day has {runtime.ImpatientTimeLimitSeconds:0.##} / {runtime.NormalTimeLimitSeconds:0.##} / {runtime.PatientTimeLimitSeconds:0.##}.");
            }
        }

        // Every range check that belongs to this block is already guaranteed by
        // BoardDistributionSettings' constructor (see the note above), so the only thing left
        // worth saying is about the relationship BETWEEN two settings, which no constructor
        // can enforce on its own.
        private static void ValidateBoardDistribution(BoardDistributionSettings board, TicketRuntimeSettings runtime, List<string> warnings)
        {
            if (board == null || runtime == null) return;

            // Leak sources are drawn from the upcoming queue, so reach beyond that queue can
            // never find anything -- harmless, but it reads as a bigger noise radius than the
            // Day actually has.
            if (board.LeakDepth > runtime.UpcomingQueueSize)
            {
                warnings.Add($"Board Distribution: Leak Depth ({board.LeakDepth}) reaches past Upcoming Queue Size ({runtime.UpcomingQueueSize}); only the queued tickets can ever leak, so the extra depth does nothing.");
            }
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
