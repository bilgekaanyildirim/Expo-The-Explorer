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
            ValidateDayStartBoard(day, errors);
            ValidateTutorial(day, errors);

            return new DayValidationResult(errors, warnings);
        }

        // THE OPENING BOARD IS THE WHOLE OPENING. A Day that authors items at Day Start
        // opens with exactly those items and nothing else: GameManager suppresses
        // BoardDistributor for the opening ticket fill (see ApplyDayStartBoardPreSeed), so
        // there is no spawner behind this board to cover an authoring mistake. That is the
        // only reason this check can exist at all -- while the distributor still ran at
        // open, "is this Day completable" was guaranteed BY CONSTRUCTION and pre-validating
        // it was meaningless (see the class comment, and decisions.md D-001 Phase 4, which
        // removed the old solvability checker for exactly that reason). Suppressing the
        // opening round hands that guarantee back to the author, and this is where it is
        // collected.
        //
        // The rule is deliberately "at least ONE of the tickets on screen", not "all three"
        // -- the same bar BoardDistributor holds itself to every round (GDD Section 4: the
        // player must always have the ingredients for at least one ticket). One completable
        // ticket is a move; a move frees a slot; a freed slot brings the next ticket, and
        // the distribution the opening skipped resumes from there. Zero completable tickets
        // is a player staring at a board with nothing to do until a ticket times out.
        //
        // An ERROR rather than a warning, unlike most of this file: this gate is entitled to
        // refuse a Day that cannot be played (the same call ValidateTutorial makes), and a
        // warning here would be read as a style note about the exact thing that makes the
        // Day unplayable.
        private static void ValidateDayStartBoard(DayDefinition day, List<string> errors)
        {
            // No authored opening board means the distributor opens the Day as it always
            // has, so there is nothing for this rule to protect.
            if (!DayBoardTimelinePlayer.HasEntriesForStep(day.BoardTimeline, -1)) return;
            if (day.TicketSequence == null || day.TicketSequence.Count == 0) return;

            var boardCounts = CountDayStartBoardItems(day);
            var ticketsOnScreenAtOpen = Math.Min(GameState.TicketSlotCount, day.TicketSequence.Count);

            for (var i = 0; i < ticketsOnScreenAtOpen; i++)
            {
                if (IsCompletableFrom(day.TicketSequence[i], boardCounts)) return;
            }

            errors.Add(
                $"Day Start board: none of the first {ticketsOnScreenAtOpen} ticket(s) can be completed from it. " +
                "A Day that places items on the Day Start board opens with exactly those items -- nothing is spawned on top -- " +
                "so at least one of the tickets on screen must be servable from the board as authored.");
        }

        // The board as the multiset the player actually gets to pick from. Counted the same
        // way a tray is counted (RequiredItemKey: food + modification combo, order-
        // independent), so an item carrying a modification nobody ordered is a DIFFERENT
        // key rather than a loose match -- which is exactly how the delivery check will
        // read it when the player hands it in.
        private static Dictionary<RequiredItemKey, int> CountDayStartBoardItems(DayDefinition day)
        {
            var counts = new Dictionary<RequiredItemKey, int>();

            foreach (var spawn in day.BoardTimeline ?? Array.Empty<ResolvedBoardSpawnEntry>())
            {
                if (spawn == null || spawn.TriggerStepIndex != -1 || spawn.Item == null) continue;

                // Every entry counts, useExactCell or not. An entry without it lands
                // wherever the board has room rather than at the authored cell -- which
                // matters to the tutorial rule above, because that one names a CELL. This
                // rule only asks whether the item is on the board at all, and it is.
                var key = new RequiredItemKey(spawn.Item, spawn.Modifications ?? Array.Empty<Modification>());
                counts.TryGetValue(key, out var count);
                counts[key] = count + 1;
            }

            return counts;
        }

        // Judged through TicketRequirements, the same rule TraySlot.Matches judges a real
        // delivery by -- modifications count for the Main dish and for nothing else. A
        // second copy of that rule here would be an authoring gate that passes Days the
        // player cannot complete (or refuses Days that play fine), which is worse than no
        // gate at all.
        private static bool IsCompletableFrom(ResolvedTicketEntry ticket, Dictionary<RequiredItemKey, int> boardCounts)
        {
            // An entry with no main dish is not a completable ticket, it is an unfinished
            // one. Reported by nothing here on purpose -- the editor already shows the empty
            // slot, and the Day is judged on the tickets that ARE authored.
            if (ticket?.MainItem == null) return false;

            foreach (var (key, requiredCount) in TicketRequirements.RequiredCounts(ticket.RequiredItems, ticket.Modifications))
            {
                if (!boardCounts.TryGetValue(key, out var availableCount) || availableCount < requiredCount)
                {
                    return false;
                }
            }

            return true;
        }

        // The one rule here is a PAIRING between two halves of the same file, which is
        // exactly the kind of check nothing else in the project is positioned to make: the
        // tutorial names a cell, the boardTimeline fills cells, and only a reader holding
        // both can see that they disagree. Getting it wrong is not a cosmetic mistake --
        // a tutorial pointing at an empty cell means nothing on the board can be picked up
        // and no tray will accept anything, which is a softlock on the game's first day.
        //
        // Errors rather than warnings for both, unlike most of this file: the Save button is
        // gated on IsValid, and "you may not save a Day that cannot be played" is a call this
        // gate IS entitled to make. The runtime keeps its own refusal anyway (TutorialDirector
        // will not start on an empty cell), because a hand-edited JSON never passes through
        // here at all -- these two guards cover different doors, not the same one twice.
        private static void ValidateTutorial(DayDefinition day, List<string> errors)
        {
            var tutorial = day.Tutorial;
            if (tutorial == null) return;

            for (var i = 0; i < tutorial.Steps.Count; i++)
            {
                var step = tutorial.Steps[i];

                // No kind check any more: since D-115 every step a Day may author IS a
                // forced move, so every rule below applies to every step. The skip that used
                // to stand here existed for the panel step, which has moved to PowerupConfig.

                if (step.TargetTraySlotIndex < 0 || step.TargetTraySlotIndex >= GameState.TicketSlotCount)
                {
                    errors.Add($"Tutorial step {i + 1}: target tray {step.TargetTraySlotIndex} does not exist -- there are {GameState.TicketSlotCount} trays, so it must be 0..{GameState.TicketSlotCount - 1}.");
                }

                // EVERY step's source cell is checked against the Day Start board, and for
                // steps after the first that is a NECESSARY condition rather than a
                // sufficient one: the board moves as the tutorial is played, so a later
                // step's item could be gone by the time its turn comes even though it was
                // authored here. The runtime re-checks each step against the live board when
                // it begins -- this check catches the author who names a cell the Day never
                // fills at all, which is the mistake that is invisible until someone plays.
                //
                // Day Start entries only (triggerStepIndex -1), and useExactCell only: an
                // entry without it lands wherever the board has room, so there is no
                // guarantee it will be the authored cell -- which is the same as having no
                // item there.
                var hasItemAtSourceCell = false;
                foreach (var spawn in day.BoardTimeline ?? Array.Empty<ResolvedBoardSpawnEntry>())
                {
                    if (spawn == null || spawn.TriggerStepIndex != -1 || !spawn.UseExactCell) continue;
                    if (spawn.X != step.SourceX || spawn.Y != step.SourceY) continue;

                    hasItemAtSourceCell = true;
                    break;
                }

                if (!hasItemAtSourceCell)
                {
                    errors.Add($"Tutorial step {i + 1}: no Day Start board entry places an item at the source cell ({step.SourceX}, {step.SourceY}) with Use Exact Cell on, so the one item the player is allowed to pick up would not be there.");
                }
            }

            // Two steps sending the player to the same cell is always an authoring slip: the
            // first one consumes that item, so the second could never be completed. A warning
            // would be too weak -- this is a guaranteed softlock, not a smell.
            for (var i = 0; i < tutorial.Steps.Count; i++)
            {
                for (var j = i + 1; j < tutorial.Steps.Count; j++)
                {
                    if (tutorial.Steps[i].SourceX != tutorial.Steps[j].SourceX) continue;
                    if (tutorial.Steps[i].SourceY != tutorial.Steps[j].SourceY) continue;

                    errors.Add($"Tutorial steps {i + 1} and {j + 1} both use source cell ({tutorial.Steps[i].SourceX}, {tutorial.Steps[i].SourceY}), but step {i + 1} takes that item off the board, so step {j + 1} could never be finished.");
                }
            }
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
