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
            ValidateTrayPreSeed(day, errors);
            ValidateItemIntros(day, allowedFoods, warnings);

            return new DayValidationResult(errors, warnings);
        }

        // WARNINGS ONLY, and that is the whole design of this check rather than a soft
        // option. An introduction is a greeting: every way of getting it wrong still leaves
        // a Day that plays exactly as it would have, so refusing the Save would be this
        // tool making a design call it is not entitled to (see DayValidationResult's own
        // note). Compare ValidateTutorial and ValidateDayStartBoard, which DO error --
        // those describe moves and boards the player is locked into.
        private static void ValidateItemIntros(
            DayDefinition day, IReadOnlyList<FoodItemConfig> allowedFoods, List<string> warnings)
        {
            var intros = day.ItemIntros;
            if (intros == null) return;

            // One set for both kinds: a FoodItemConfig and a ModificationConfig are different
            // objects, so a burger and a "no pickles" can never collide here, and the "said
            // twice" rule needs no second copy per kind.
            // System.Object, deliberately, not UnityEngine.Object: this set only ever asks
            // "is this the same reference", and going through UnityEngine.Object would drag
            // in its overloaded == and its destroyed-object "fake null", neither of which
            // means anything for two live configs read out of a catalog.
            var seen = new HashSet<object>();

            // The modifications this Day can actually put on a ticket -- every one offered by
            // a food in its selection. Built once rather than per intro, and only when there
            // is a pool to build it from: a null allowedFoods means the caller has no catalog
            // yet, which is the unconfigured-toolbar case, not an empty Day.
            var offeredModifications = allowedFoods == null
                ? null
                : new HashSet<ModificationConfig>(allowedFoods
                    .Where(food => food != null)
                    .SelectMany(food => food.AvailableModifications)
                    .Where(modification => modification != null));

            for (var i = 0; i < intros.Count; i++)
            {
                var intro = intros[i];
                if (intro == null) continue;

                object subject = intro.Item != null ? intro.Item : (object)intro.Modification;
                if (subject == null) continue;

                var id = intro.Item != null ? intro.Item.Id : intro.Modification.Id;

                // Introducing something the Day never uses is the mistake worth naming: the
                // popup promises a food the board will not hand out, or a modification no
                // ticket can ask for, for the rest of the morning. Both are checked against
                // the same food pool the ticket rule uses, so nothing here can disagree with
                // it about what "this Day serves" means.
                if (intro.Item != null && allowedFoods != null && !allowedFoods.Contains(intro.Item))
                {
                    warnings.Add(
                        $"Item intro {i + 1}: '{id}' is introduced but is not in this Day's food selection, "
                        + "so the player is shown something this Day never serves.");
                }
                else if (intro.Modification != null && offeredModifications != null
                                                    && !offeredModifications.Contains(intro.Modification))
                {
                    warnings.Add(
                        $"Item intro {i + 1}: modification '{id}' is introduced but no food in this Day's selection "
                        + "offers it, so no ticket this Day can ever ask for it.");
                }

                // A direction the modification does not allow. Checked against
                // ModificationConfig.AllowedDirection, which is the single authority for it --
                // the same one the Day Editor's right-click flip reads, so an intro authored
                // through the window cannot reach this and a hand-edited Day file can.
                if (intro.Modification != null && intro.ModificationIsAddition.HasValue)
                {
                    var wantsAddition = intro.ModificationIsAddition.Value;
                    var allowed = intro.Modification.AllowedDirection;
                    if ((wantsAddition && allowed == ModificationDirection.RemovalOnly)
                        || (!wantsAddition && allowed == ModificationDirection.AdditionOnly))
                    {
                        warnings.Add(
                            $"Item intro {i + 1}: modification '{id}' is introduced as "
                            + $"{(wantsAddition ? "an addition" : "a removal")}, but it is {allowed} -- "
                            + "the popup would teach a direction no ticket can ask for.");
                    }
                }

                // The same thing twice is two popups saying the same thing, one after the
                // other -- reachable by duplicating a row and forgetting to change it.
                if (!seen.Add(subject))
                {
                    warnings.Add($"Item intro {i + 1}: '{id}' is introduced more than once on this Day.");
                }

                // A popup with no picture is a frame with a name in it. Not an error -- it
                // still reads -- but the picture is the thing the player is supposed to
                // recognise on the board or on a ticket card a moment later.
                if (intro.Sprite == null)
                {
                    warnings.Add(
                        $"Item intro {i + 1}: '{id}' has no "
                        + (intro.Item != null ? "sprite on its FoodItemConfig" : "icon on its ModificationConfig")
                        + ", so its introduction popup will show no picture.");
                }
            }
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
                $"Day Start opening items: none of the first {ticketsOnScreenAtOpen} ticket(s) can be completed from them. " +
                "A Day that places items on the Day Start board opens with exactly those items plus whatever it seats in the trays " +
                "-- nothing is spawned on top -- so at least one of the tickets on screen must be servable from them as authored.");
        }

        // Everything the player can reach when the Day opens, as the multiset they get to
        // pick from. Counted the same way a tray is counted (RequiredItemKey: food +
        // modification combo, order-independent), so an item carrying a modification nobody
        // ordered is a DIFFERENT key rather than a loose match -- which is exactly how the
        // delivery check will read it when the player hands it in.
        //
        // THE TRAYS COUNT TOO (D-165), and leaving them out is not a conservative omission
        // but a wrong answer: a seeded item can be taken back out of its tray and carried to
        // another one -- that is the mechanic day_03 exists to teach -- so it is as available
        // to the opening tickets as anything lying on the board. Before this, day_03 was
        // reported as unplayable because the cola its first order needs opens in a tray
        // rather than on the board.
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
                Add(counts, spawn.Item, spawn.Modifications);
            }

            foreach (var seed in day.TrayPreSeed ?? Array.Empty<ResolvedTrayPreSeed>())
            {
                if (seed == null || seed.Item == null) continue;

                // WHICH tray it sits in is deliberately ignored: this rule asks what the
                // player can get their hands on, and every seated item can be dragged out of
                // its tray and into any other.
                Add(counts, seed.Item, seed.Modifications);
            }

            return counts;

            static void Add(
                Dictionary<RequiredItemKey, int> counts,
                FoodItemConfig item,
                IReadOnlyList<Modification> modifications)
            {
                var key = new RequiredItemKey(item, modifications ?? Array.Empty<Modification>());
                counts.TryGetValue(key, out var count);
                counts[key] = count + 1;
            }
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

                // The target tray is the one thing BOTH shapes have, so it is checked before
                // the kind splits them.
                if (step.TargetTraySlotIndex < 0 || step.TargetTraySlotIndex >= GameState.TicketSlotCount)
                {
                    errors.Add($"Tutorial step {i + 1}: target tray {step.TargetTraySlotIndex} does not exist -- there are {GameState.TicketSlotCount} trays, so it must be 0..{GameState.TicketSlotCount - 1}.");
                }

                // A TrayMove's source is a TRAY, so every board-cell rule below is not just
                // inapplicable to it but actively wrong: its cell is -1/-1 by construction
                // and no boardTimeline entry will ever match. Its own rules run instead and
                // then this step is done. (Since D-115 and until D-165 there was no kind
                // check here at all, because there was only one shape left.)
                if (step.Kind == TutorialStepKind.TrayMove)
                {
                    ValidateTrayMoveStep(day, tutorial, i, errors);
                    continue;
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
            //
            // ForcedMove steps only. Two TrayMoves share the cell -1/-1 by construction, so
            // without the kind filter this rule would report every pair of them as a duplicate
            // -- a false error on a Day that is perfectly authored.
            for (var i = 0; i < tutorial.Steps.Count; i++)
            {
                if (tutorial.Steps[i].Kind != TutorialStepKind.ForcedMove) continue;

                for (var j = i + 1; j < tutorial.Steps.Count; j++)
                {
                    if (tutorial.Steps[j].Kind != TutorialStepKind.ForcedMove) continue;
                    if (tutorial.Steps[i].SourceX != tutorial.Steps[j].SourceX) continue;
                    if (tutorial.Steps[i].SourceY != tutorial.Steps[j].SourceY) continue;

                    errors.Add($"Tutorial steps {i + 1} and {j + 1} both use source cell ({tutorial.Steps[i].SourceX}, {tutorial.Steps[i].SourceY}), but step {i + 1} takes that item off the board, so step {j + 1} could never be finished.");
                }
            }
        }

        // The Day's opening tray contents (D-165). ERRORS rather than warnings, on the same
        // line ValidateItemIntros draws: an introduction is a greeting and a bad one still
        // leaves a playable Day, but a seeded tray is part of the BOARD STATE the player is
        // handed -- a tutorial step can be locked on it, and a slot index that does not exist
        // means an item that silently never appears.
        //
        // What is NOT checked here: whether the seeded item is something that tray's ticket
        // wants. It usually is not -- day_03 seats a drink in the second tray precisely so
        // the tutorial can move it to the FIRST order -- so a rule about it would fire on
        // exactly the case this block was built for.
        private static void ValidateTrayPreSeed(DayDefinition day, List<string> errors)
        {
            var seeds = day.TrayPreSeed;
            if (seeds == null) return;

            for (var i = 0; i < seeds.Count; i++)
            {
                var seed = seeds[i];
                if (seed == null) continue;

                if (seed.TraySlotIndex < 0 || seed.TraySlotIndex >= GameState.TicketSlotCount)
                {
                    errors.Add($"Day Start tray pre-seed {i + 1}: tray {seed.TraySlotIndex} does not exist -- there are {GameState.TicketSlotCount} trays, so it must be 0..{GameState.TicketSlotCount - 1}.");
                    continue;
                }

                // Two items seated in one tray is legal (a tray holds several), but two
                // entries that name the SAME tray and the SAME food are almost always a
                // duplicated row rather than a deliberate pair, and the second one is
                // invisible on screen -- it stacks in the same slot.
                for (var j = i + 1; j < seeds.Count; j++)
                {
                    if (seeds[j] == null) continue;
                    if (seeds[j].TraySlotIndex != seed.TraySlotIndex) continue;
                    if (seeds[j].Item != seed.Item) continue;

                    errors.Add($"Day Start tray pre-seed {i + 1} and {j + 1} both put a '{(seed.Item != null ? seed.Item.Id : "?")}' in tray {seed.TraySlotIndex}, which stacks two items in one slot -- remove one, or seat the second in another tray.");
                }
            }
        }

        // A tray-to-tray step's own rules (D-165). The question this answers is the same one
        // the board-cell rule answers for a ForcedMove -- "will the one item the player is
        // allowed to touch actually be there when this step begins?" -- but the places an
        // item can come from are different, so the check has to be.
        private static void ValidateTrayMoveStep(
            DayDefinition day, ResolvedTutorial tutorial, int stepIndex, List<string> errors)
        {
            var step = tutorial.Steps[stepIndex];

            if (step.SourceTraySlotIndex < 0 || step.SourceTraySlotIndex >= GameState.TicketSlotCount)
            {
                errors.Add($"Tutorial step {stepIndex + 1}: source tray {step.SourceTraySlotIndex} does not exist -- there are {GameState.TicketSlotCount} trays, so it must be 0..{GameState.TicketSlotCount - 1}.");
                return;
            }

            // Moving an item out of a tray and back into the same tray is not a move. It
            // would also never complete: the runtime finishes the step on the target tray
            // ACCEPTING a drop, and a tray the item never left has nothing to accept.
            if (step.SourceTraySlotIndex == step.TargetTraySlotIndex)
            {
                errors.Add($"Tutorial step {stepIndex + 1}: source tray and target tray are both {step.SourceTraySlotIndex}, so there is no move to make.");
                return;
            }

            // The source tray has to be HOLDING something when this step begins, and there
            // are exactly two ways it can be: the Day seeded it, or an earlier step put an
            // item there. Anything else is a step the player is locked on with nothing to
            // pick up -- the tray-side twin of naming a cell the board never fills.
            //
            // "An earlier step" is a necessary rather than a sufficient condition, for the
            // reason the board-cell rule already gives: a step in between could have moved
            // that item on again. The runtime re-checks the live tray when the step begins;
            // this catches the author who never put anything there at all.
            foreach (var seed in day.TrayPreSeed ?? Array.Empty<ResolvedTrayPreSeed>())
            {
                if (seed != null && seed.TraySlotIndex == step.SourceTraySlotIndex) return;
            }

            for (var i = 0; i < stepIndex; i++)
            {
                if (tutorial.Steps[i].TargetTraySlotIndex == step.SourceTraySlotIndex) return;
            }

            errors.Add($"Tutorial step {stepIndex + 1}: nothing puts an item in the source tray {step.SourceTraySlotIndex} before this step runs -- no Day Start tray pre-seed fills it and no earlier step delivers into it, so there would be nothing to pick up.");
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
