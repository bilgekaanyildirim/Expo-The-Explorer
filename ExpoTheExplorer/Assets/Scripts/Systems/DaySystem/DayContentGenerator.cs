using System;
using System.Collections.Generic;
using System.Linq;
using ExpoTheExplorer.Core;
using ExpoTheExplorer.Data;
using ExpoTheExplorer.Systems.BoardDistribution;
using ExpoTheExplorer.Systems.TicketSystem;

namespace ExpoTheExplorer.Systems.DaySystem
{
    public class DayContentGenerationResult
    {
        public TicketEntryJson[] TicketSequence { get; }
        public BoardSpawnEntryJson[] BoardTimeline { get; }
        public IReadOnlyList<string> Warnings { get; }

        public DayContentGenerationResult(TicketEntryJson[] ticketSequence, BoardSpawnEntryJson[] boardTimeline, IReadOnlyList<string> warnings)
        {
            TicketSequence = ticketSequence;
            BoardTimeline = boardTimeline;
            Warnings = warnings;
        }
    }

    // Offline, one-shot simulation that drives the REAL TicketFactory/BoardDistributor
    // logic (not a reimplementation of it) to author a Day's ticketSequence/boardTimeline
    // ahead of time. Only ever called from Editor tooling (PR-7's "Generate" button), but
    // deliberately has no UnityEditor dependency itself, so it stays EditMode-testable.
    public static class DayContentGenerator
    {
        private const string SimulatedCustomerName = "Simulated Customer";

        public static DayContentGenerationResult Generate(
            FoodCatalog catalog,
            GameConfig gameConfig,
            TicketGenerationConfig baseTicketConfig,
            BoardDistributionConfig baseBoardConfig,
            DayEditorMetaJson editorMeta,
            int ticketsRequiredForDay,
            int seed)
        {
            var random = new Random(seed);
            var ticketConfig = ApplyTicketGenerationOverrides(baseTicketConfig, editorMeta);
            var boardConfig = ApplyBoardDistributionOverrides(baseBoardConfig, editorMeta);

            try
            {
                return GenerateCore(catalog, gameConfig, ticketConfig, boardConfig, ticketsRequiredForDay, random);
            }
            finally
            {
                if (ticketConfig != baseTicketConfig) UnityEngine.Object.DestroyImmediate(ticketConfig);
                if (boardConfig != baseBoardConfig) UnityEngine.Object.DestroyImmediate(boardConfig);
            }
        }

        // One step == exactly one ticket, mirroring TicketSlotManager.AssignTicket: the
        // oldest-occupied slot (round-robin) is assumed delivered and freed before the next
        // ticket takes its place -- this is a simulated "ideal" playthrough, not a prediction
        // of any specific real player's pacing (see roadmap notes for why that's fine: replay
        // fidelity doesn't depend on it, only this simulation's own board-capacity realism does).
        private static DayContentGenerationResult GenerateCore(
            FoodCatalog catalog, GameConfig gameConfig, TicketGenerationConfig ticketConfig,
            BoardDistributionConfig boardConfig, int ticketsRequiredForDay, Random random)
        {
            var state = new GameState(gameConfig);
            var ticketFactory = new TicketFactory(ticketConfig, random);
            var distributor = new BoardDistributor(state, boardConfig, random);

            var lookaheadCount = Math.Max(GameState.TicketSlotCount, ticketConfig.UpcomingQueueSize);
            var upcomingTickets = new List<Ticket>();
            EnsureQueueFilled(upcomingTickets, lookaheadCount, catalog, ticketFactory);

            var activeSlots = new Ticket[GameState.TicketSlotCount];
            var ticketEntries = new List<TicketEntryJson>(ticketsRequiredForDay);
            var boardSpawnEntries = new List<BoardSpawnEntryJson>();
            var warnings = new List<string>();

            for (var step = 0; step < ticketsRequiredForDay; step++)
            {
                var slotIndex = step % GameState.TicketSlotCount;
                var outgoingTicket = activeSlots[slotIndex];
                if (outgoingTicket != null)
                {
                    RemoveTicketItemsFromBoard(state.Board, outgoingTicket, warnings, step);
                }

                // Spans removal+backfill+OnOrderPlaced as a single diff, so a pending-queue
                // item that backfills into a cell this same delivery just freed is captured
                // too, not just cells that were empty for the whole step.
                var before = SnapshotBoard(state.Board);

                EnsureQueueFilled(upcomingTickets, lookaheadCount, catalog, ticketFactory);
                var newTicket = upcomingTickets[0];
                upcomingTickets.RemoveAt(0);
                EnsureQueueFilled(upcomingTickets, lookaheadCount, catalog, ticketFactory);
                activeSlots[slotIndex] = newTicket;

                var activeForCall = activeSlots.Where(t => t != null).ToList();
                distributor.OnOrderPlaced(activeForCall, upcomingTickets);

                var after = SnapshotBoard(state.Board);
                foreach (var (x, y, item) in DiffChangedCells(before, after))
                {
                    boardSpawnEntries.Add(ToBoardSpawnEntryJson(step, x, y, item));
                }

                ticketEntries.Add(ToTicketEntryJson(newTicket));
            }

            return new DayContentGenerationResult(ticketEntries.ToArray(), boardSpawnEntries.ToArray(), warnings);
        }

        // Mirrors TicketSlotManager.EnsureQueueFilled exactly -- there's no live
        // TicketSlotManager to delegate to here (runtime never constructs a
        // BoardDistributor anymore, see PR-6), so this simulation owns its own copy of
        // the lookahead-queue mechanics to feed OnOrderPlaced the same shapes runtime used to.
        private static void EnsureQueueFilled(List<Ticket> upcomingTickets, int lookaheadCount, FoodCatalog catalog, TicketFactory ticketFactory)
        {
            while (upcomingTickets.Count < lookaheadCount)
            {
                var patienceType = ticketFactory.PickRandomPatienceType();
                upcomingTickets.Add(ticketFactory.Create(catalog.Items, SimulatedCustomerName, patienceType));
            }
        }

        // Simulates "this ticket was just delivered" so the board doesn't grow
        // monotonically for the whole simulation -- without this, long Days would
        // exhaust board capacity and strand required items in BoardGrid's pending-spawn
        // queue forever, since nothing else ever calls RemoveItem in this simulation.
        // Best-effort: GuaranteedTicketCount always covers the oldest active ticket (the
        // next one due for "delivery" here), so a missing match should be rare -- if it
        // happens anyway, it's recorded as a warning rather than thrown, since this is a
        // preview/authoring aid, not a hard correctness gate (that's DayValidator's job).
        private static void RemoveTicketItemsFromBoard(BoardGrid board, Ticket ticket, List<string> warnings, int step)
        {
            foreach (var food in ticket.RequiredItems)
            {
                var mods = food.Category == FoodCategory.Main ? ticket.Modifications : Array.Empty<Modification>();
                var key = new RequiredItemKey(food, mods);
                if (TryFindMatchingCell(board, key, out var x, out var y))
                {
                    board.RemoveItem(x, y);
                }
                else
                {
                    warnings.Add($"Step {step}: expected required item '{food.Id}' for the simulated delivery was not found on the board.");
                }
            }
        }

        private static bool TryFindMatchingCell(BoardGrid board, RequiredItemKey key, out int x, out int y)
        {
            for (var scanX = 0; scanX < board.Width; scanX++)
            {
                for (var scanY = 0; scanY < board.Height; scanY++)
                {
                    var item = board.ItemAt(scanX, scanY);
                    if (item != null && new RequiredItemKey(item.Config, item.Modifications).Equals(key))
                    {
                        x = scanX;
                        y = scanY;
                        return true;
                    }
                }
            }

            x = -1;
            y = -1;
            return false;
        }

        private static Dictionary<(int X, int Y), BoardItem> SnapshotBoard(BoardGrid board)
        {
            var snapshot = new Dictionary<(int X, int Y), BoardItem>();
            for (var x = 0; x < board.Width; x++)
            {
                for (var y = 0; y < board.Height; y++)
                {
                    var item = board.ItemAt(x, y);
                    if (item != null)
                    {
                        snapshot[(x, y)] = item;
                    }
                }
            }

            return snapshot;
        }

        private static IEnumerable<(int X, int Y, BoardItem Item)> DiffChangedCells(
            Dictionary<(int X, int Y), BoardItem> before, Dictionary<(int X, int Y), BoardItem> after)
        {
            foreach (var (cell, item) in after)
            {
                if (!before.TryGetValue(cell, out var previous) || !SameContent(previous, item))
                {
                    yield return (cell.X, cell.Y, item);
                }
            }
        }

        private static bool SameContent(BoardItem a, BoardItem b) =>
            new RequiredItemKey(a.Config, a.Modifications).Equals(new RequiredItemKey(b.Config, b.Modifications));

        private static TicketEntryJson ToTicketEntryJson(Ticket ticket)
        {
            var main = ticket.RequiredItems.First(item => item.Category == FoodCategory.Main);
            var side = ticket.RequiredItems.FirstOrDefault(item => item.Category == FoodCategory.Side);
            var drink = ticket.RequiredItems.FirstOrDefault(item => item.Category == FoodCategory.Drink);

            return new TicketEntryJson
            {
                mainItemId = main.Id,
                sideItemId = side != null ? side.Id : string.Empty,
                drinkItemId = drink != null ? drink.Id : string.Empty,
                modifications = ticket.Modifications.Select(ToModificationEntryJson).ToArray(),
                patienceType = ticket.PatienceType.ToString(),
                customerNameOverride = string.Empty,
                timeLimitSecondsOverride = 0f,
            };
        }

        private static BoardSpawnEntryJson ToBoardSpawnEntryJson(int step, int x, int y, BoardItem item)
        {
            return new BoardSpawnEntryJson
            {
                triggerStepIndex = step,
                itemId = item.Config.Id,
                modifications = item.Modifications.Select(ToModificationEntryJson).ToArray(),
                useExactCell = true,
                x = x,
                y = y,
            };
        }

        private static ModificationEntryJson ToModificationEntryJson(Modification mod)
        {
            return new ModificationEntryJson
            {
                modificationId = mod.Config.Id,
                isAddition = mod.IsAddition,
            };
        }

        private static TicketGenerationConfig ApplyTicketGenerationOverrides(TicketGenerationConfig baseConfig, DayEditorMetaJson editorMeta)
        {
            if (editorMeta is not { hasTicketGenerationOverride: true })
            {
                return baseConfig;
            }

            return baseConfig.CloneWithOverrides(
                sideInclusionChance: editorMeta.sideInclusionChanceOverride,
                drinkInclusionChance: editorMeta.drinkInclusionChanceOverride,
                modificationCountLambda: editorMeta.modificationCountLambdaOverride);
        }

        private static BoardDistributionConfig ApplyBoardDistributionOverrides(BoardDistributionConfig baseConfig, DayEditorMetaJson editorMeta)
        {
            if (editorMeta is not { hasBoardDistributionOverride: true })
            {
                return baseConfig;
            }

            return baseConfig.CloneWithOverrides(
                noiseLeakCountLambda: editorMeta.noiseLeakCountLambdaOverride,
                guaranteedTicketCount: editorMeta.guaranteedTicketCountOverride,
                leakDepth: editorMeta.leakDepthOverride,
                maxLeakCount: editorMeta.maxLeakCountOverride);
        }
    }
}
