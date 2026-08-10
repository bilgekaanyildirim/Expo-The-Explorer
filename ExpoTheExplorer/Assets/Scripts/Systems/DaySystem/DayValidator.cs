using System;
using System.Collections.Generic;
using System.Linq;
using ExpoTheExplorer.Core;
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
    // runtime load path. Deterministic and Random-free: BoardDistributor is never
    // invoked, only DayBoardTimelinePlayer's own additive-only playback logic, so
    // this matches runtime's actual board-mutation behavior exactly (real item
    // removal only ever happens via player drag-and-drop, which Day data never models).
    public static class DayValidator
    {
        public static DayValidationResult Validate(DayDefinition day, GameConfig gameConfig)
        {
            var errors = new List<string>();

            // Locked invariant since PR-1: an authored Day's ticketSequence must exactly
            // cover ticketsRequiredForDay -- no runtime fallback exists for a mismatch.
            // (A lookahead-buffer-starvation check briefly lived here too, PR-6.6 --
            // removed once the root cause turned out to be TicketSlotManager's own
            // now-deleted lookahead buffer, not authored Day content. See TicketSlotManager.cs.)
            if (day.TicketSequence.Count != day.TicketsRequiredForDay)
            {
                errors.Add($"ticketSequence has {day.TicketSequence.Count} entries but ticketsRequiredForDay is {day.TicketsRequiredForDay}.");
            }

            errors.AddRange(ValidatePlayability(day, gameConfig));

            if (day.RetryVariant != null)
            {
                var variantResult = Validate(day.RetryVariant, gameConfig);
                errors.AddRange(variantResult.Errors.Select(e => $"RetryVariant: {e}"));
            }

            return new DayValidationResult(errors);
        }

        // Round-robin active-slot window, same as DayContentGenerator (PR-6.5) -- the
        // board here only ever grows (DayBoardTimelinePlayer never removes anything, just
        // like runtime), so this is the tightest, least-forgiving timing assumption: if
        // playable under round-robin turnover, it stays playable for any slower player.
        private static List<string> ValidatePlayability(DayDefinition day, GameConfig gameConfig)
        {
            var errors = new List<string>();
            var board = new BoardGrid(gameConfig);
            DayBoardTimelinePlayer.ApplyForStep(board, day.BoardTimeline, -1);

            var activeSlots = new ResolvedTicketEntry[GameState.TicketSlotCount];
            for (var step = 0; step < day.TicketSequence.Count; step++)
            {
                activeSlots[step % GameState.TicketSlotCount] = day.TicketSequence[step];
                DayBoardTimelinePlayer.ApplyForStep(board, day.BoardTimeline, step);

                if (!activeSlots.Where(t => t != null).Any(t => IsCompletable(board, t)))
                {
                    errors.Add($"Step {step}: no active ticket is completable from the current board contents.");
                }
            }

            return errors;
        }

        // Same Main-gets-modifications, Side/Drink-are-modification-free rule as
        // BoardDistributor/DayContentGenerator -- a mismatched rule here would produce
        // false positives/negatives against what runtime actually requires.
        private static bool IsCompletable(BoardGrid board, ResolvedTicketEntry ticket)
        {
            if (!BoardHasMatch(board, ticket.MainItem, ticket.Modifications)) return false;
            if (ticket.SideItem != null && !BoardHasMatch(board, ticket.SideItem, Array.Empty<Modification>())) return false;
            if (ticket.DrinkItem != null && !BoardHasMatch(board, ticket.DrinkItem, Array.Empty<Modification>())) return false;
            return true;
        }

        private static bool BoardHasMatch(BoardGrid board, FoodItemConfig food, IReadOnlyList<Modification> modifications)
        {
            var key = new RequiredItemKey(food, modifications);
            for (var x = 0; x < board.Width; x++)
            {
                for (var y = 0; y < board.Height; y++)
                {
                    var item = board.ItemAt(x, y);
                    if (item != null && new RequiredItemKey(item.Config, item.Modifications).Equals(key))
                    {
                        return true;
                    }
                }
            }

            return false;
        }
    }
}
