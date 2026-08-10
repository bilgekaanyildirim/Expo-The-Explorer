using System;
using System.Collections.Generic;
using System.Linq;
using ExpoTheExplorer.Core;

namespace ExpoTheExplorer.Systems.DaySystem
{
    public readonly struct DayShortfall
    {
        public int TicketIndex { get; }
        public RequiredItemKey MissingKey { get; }

        public DayShortfall(int ticketIndex, RequiredItemKey missingKey)
        {
            TicketIndex = ticketIndex;
            MissingKey = missingKey;
        }
    }

    // Counting-based solvability check -- deliberately does NOT simulate board cells/removal.
    // Round-robin delivery consumes tickets in strict index order (ticket i is delivered by step
    // min(i + GameState.TicketSlotCount, N-1) at the latest -- the tightest, most-demanding real
    // cadence, matching DayContentGenerator's own delivery simulation). Items sharing the same
    // RequiredItemKey are fully fungible, so only running counts matter: the j-th ticket (in
    // arrival order) needing a key must have at least j+1 units of that key ever spawned by the
    // time it's delivered. No cell-level simulation needed for detection -- only for placing a
    // repair spawn (see DayContentGenerator), which needs an actual (x,y).
    //
    // This replaces DayValidator's old "board never shrinks" playability check (PR-6.6), which
    // was wrong: never removing anything is a MORE generous supply assumption than real
    // gameplay, not a stricter one -- it missed exactly this class of bug (fungible items like a
    // plain side/drink getting under-supplied relative to cumulative demand across a whole Day).
    public static class DaySolvabilityChecker
    {
        public static List<DayShortfall> FindShortfalls(IReadOnlyList<ResolvedTicketEntry> ticketSequence, IReadOnlyList<ResolvedBoardSpawnEntry> boardTimeline)
        {
            var demandByKey = new Dictionary<RequiredItemKey, List<int>>();
            for (var i = 0; i < ticketSequence.Count; i++)
            {
                foreach (var key in RequiredKeysOf(ticketSequence[i]))
                {
                    if (!demandByKey.TryGetValue(key, out var indices))
                    {
                        demandByKey[key] = indices = new List<int>();
                    }
                    indices.Add(i);
                }
            }

            var supplyByKey = new Dictionary<RequiredItemKey, List<int>>();
            foreach (var spawn in boardTimeline)
            {
                var key = new RequiredItemKey(spawn.Item, spawn.Modifications);
                if (!supplyByKey.TryGetValue(key, out var steps))
                {
                    supplyByKey[key] = steps = new List<int>();
                }
                steps.Add(spawn.TriggerStepIndex);
            }
            foreach (var steps in supplyByKey.Values)
            {
                steps.Sort();
            }

            var shortfalls = new List<DayShortfall>();
            foreach (var (key, demandIndices) in demandByKey)
            {
                var supplySteps = supplyByKey.TryGetValue(key, out var s) ? s : new List<int>();
                for (var j = 0; j < demandIndices.Count; j++)
                {
                    var ticketIndex = demandIndices[j];
                    var deadline = Math.Min(ticketIndex + GameState.TicketSlotCount, ticketSequence.Count - 1);
                    var availableByDeadline = supplySteps.Count(step => step <= deadline);
                    if (availableByDeadline < j + 1)
                    {
                        shortfalls.Add(new DayShortfall(ticketIndex, key));
                    }
                }
            }

            return shortfalls.OrderBy(shortfall => shortfall.TicketIndex).ToList();
        }

        private static IEnumerable<RequiredItemKey> RequiredKeysOf(ResolvedTicketEntry ticket)
        {
            yield return new RequiredItemKey(ticket.MainItem, ticket.Modifications);
            if (ticket.SideItem != null)
            {
                yield return new RequiredItemKey(ticket.SideItem, Array.Empty<Modification>());
            }
            if (ticket.DrinkItem != null)
            {
                yield return new RequiredItemKey(ticket.DrinkItem, Array.Empty<Modification>());
            }
        }
    }
}
