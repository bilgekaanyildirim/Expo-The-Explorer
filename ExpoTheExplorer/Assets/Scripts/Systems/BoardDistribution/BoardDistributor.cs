using System;
using System.Collections.Generic;
using System.Linq;
using ExpoTheExplorer.Core;
using ExpoTheExplorer.Data;

namespace ExpoTheExplorer.Systems.BoardDistribution
{
    // Required-pool + noise-pool board spawning (GDD Section 4). Deliberately
    // plain C# with no MonoBehaviour/Unity lifecycle dependency (CLAUDE.md
    // Section 5 — the Food Distribution Module must be unit-testable in
    // isolation). Takes the active/upcoming ticket lists as parameters rather
    // than holding a TicketSlotManager reference, so this only depends on Core
    // (Ticket) and Data (FoodItemConfig/ModificationConfig), not on
    // Systems.TicketSystem.
    public class BoardDistributor
    {
        private readonly GameState state;
        private readonly BoardDistributionConfig config;
        private readonly Random random;
        private readonly HashSet<Ticket> leakedTickets = new();

        public BoardDistributor(GameState state, BoardDistributionConfig config, Random random = null)
        {
            this.state = state;
            this.config = config;
            this.random = random ?? new Random();
        }

        // Called whenever a new order (ticket) is assigned into an active slot —
        // production is order-triggered, not a continuous per-frame poll or a
        // fixed-interval timer.
        public void OnOrderPlaced(IReadOnlyList<Ticket> activeTickets, IReadOnlyList<Ticket> upcomingTickets)
        {
            // Required pool always spawns before noise (GDD Section 4).
            SpawnMissingRequiredItems(activeTickets);
            TryLeakNoiseItem(upcomingTickets);
        }

        private void SpawnMissingRequiredItems(IReadOnlyList<Ticket> activeTickets)
        {
            var neededCounts = new Dictionary<RequiredItemKey, int>();
            foreach (var ticket in activeTickets)
            {
                if (ticket == null || ticket.State != TicketState.Active) continue;

                foreach (var food in ticket.RequiredItems)
                {
                    var mods = food.Category == FoodCategory.Main ? ticket.Modifications : Array.Empty<Modification>();
                    var key = new RequiredItemKey(food, mods);
                    neededCounts.TryGetValue(key, out var count);
                    neededCounts[key] = count + 1;
                }
            }

            if (neededCounts.Count == 0) return;

            var presentCounts = CountItemsOnBoard();

            foreach (var (key, neededCount) in neededCounts)
            {
                presentCounts.TryGetValue(key, out var presentCount);
                for (var i = presentCount; i < neededCount; i++)
                {
                    state.Board.RequestSpawn(new BoardItem(key.Food, key.Modifications), random);
                }
            }
        }

        private Dictionary<RequiredItemKey, int> CountItemsOnBoard()
        {
            var counts = new Dictionary<RequiredItemKey, int>();
            for (var x = 0; x < state.Board.Width; x++)
            {
                for (var y = 0; y < state.Board.Height; y++)
                {
                    var item = state.Board.ItemAt(x, y);
                    if (item == null) continue;

                    var key = new RequiredItemKey(item.Config, item.Modifications);
                    counts.TryGetValue(key, out var count);
                    counts[key] = count + 1;
                }
            }

            return counts;
        }

        // Each upcoming ticket leaks at most one item for as long as it sits in
        // the queue — without this, the same ticket could be re-picked forever
        // and flood the board with duplicates of one item instead of a variety.
        // Pruning against the current upcomingTickets list also means a ticket
        // that got dequeued into an active slot stops being tracked, and the
        // fresh ticket that replaces it in the queue is immediately eligible.
        // Rolled once per order placed (NoiseLeakChance), not on a timer.
        private void TryLeakNoiseItem(IReadOnlyList<Ticket> upcomingTickets)
        {
            leakedTickets.IntersectWith(upcomingTickets);

            if (random.NextDouble() >= config.NoiseLeakChance) return;

            var candidates = upcomingTickets.Where(t => !leakedTickets.Contains(t)).ToList();
            if (candidates.Count == 0) return;

            var sourceTicket = candidates[random.Next(candidates.Count)];
            var food = sourceTicket.RequiredItems[random.Next(sourceTicket.RequiredItems.Count)];
            var mods = food.Category == FoodCategory.Main ? sourceTicket.Modifications : Array.Empty<Modification>();

            state.Board.RequestSpawn(new BoardItem(food, mods), random);
            leakedTickets.Add(sourceTicket);
        }
    }
}
