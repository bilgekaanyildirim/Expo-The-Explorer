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
        private readonly HashSet<Ticket> guaranteedTickets = new();

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
            SpawnMissingRequiredItems(activeTickets, upcomingTickets);
            LeakNoiseItems(upcomingTickets);
        }

        // Guarantees a per-round budget of tickets are completable from the
        // required pool (GDD Section 4 — "oyuncu her zaman en az bir bileti
        // tamamlayabilecek malzemeye sahip olmalı": AT LEAST ONE ticket, not
        // every active ticket). See SelectGuaranteedTickets for exactly which
        // tickets that budget picks.
        private void SpawnMissingRequiredItems(IReadOnlyList<Ticket> activeTickets, IReadOnlyList<Ticket> upcomingTickets)
        {
            var guaranteedTickets = SelectGuaranteedTickets(activeTickets, upcomingTickets);
            if (guaranteedTickets.Count == 0) return;

            var neededCounts = new Dictionary<RequiredItemKey, int>();
            foreach (var ticket in guaranteedTickets)
            {
                foreach (var food in ticket.RequiredItems)
                {
                    var mods = food.Category == FoodCategory.Main ? ticket.Modifications : Array.Empty<Modification>();
                    var key = new RequiredItemKey(food, mods);
                    neededCounts.TryGetValue(key, out var count);
                    neededCounts[key] = count + 1;
                }
            }

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

        // Active tickets always precede queued ones in true arrival order (the
        // upcoming queue is strict FIFO — TicketSlotManager always dequeues the
        // front and enqueues at the back), so concatenating the two lists and
        // sorting by ArrivalSequence reconstructs full arrival order without
        // needing a separate global ticket registry.
        //
        // Selection is a two-layer probabilistic scheme (GDD Section 4 refinement,
        // decisions.md D-001 Phase 2a): a per-round budget (Manual fixed value, or
        // Poisson-sampled -- always 1..TicketSlotCount, never 0, so "at least one
        // ticket must always be completable" holds even in Poisson mode) picks that
        // many tickets via an arrival-weighted lottery. Layered on top: any active
        // ticket under UrgentTimeThresholdSeconds is guaranteed unconditionally --
        // it consumes from the budget when the budget isn't exceeded, but is
        // guaranteed in full even past budget if urgent-ticket count exceeds it
        // (confirmed with the user: urgency always wins).
        //
        // Selection is STICKY across calls (guaranteedTickets is instance state,
        // pruned/topped-up here rather than recomputed from scratch each time) --
        // re-rolling the lottery on every single OnOrderPlaced call, even when the
        // active-ticket set hasn't changed at all, would spawn a fresh required
        // item for whatever ticket randomly wins THIS round on top of whatever a
        // PREVIOUS round already spawned (nothing here ever un-spawns an item),
        // silently drifting "at least one ticket" toward "eventually every
        // ticket" purely from repeated re-rolling. A ticket only leaves the
        // guaranteed set when it leaves the pool entirely (delivered/cancelled),
        // which frees its slot for the next event to fill.
        private List<Ticket> SelectGuaranteedTickets(IReadOnlyList<Ticket> activeTickets, IReadOnlyList<Ticket> upcomingTickets)
        {
            var pool = activeTickets
                .Where(t => t != null && t.State == TicketState.Active)
                .Concat(upcomingTickets)
                .OrderBy(t => t.ArrivalSequence)
                .ToList();

            guaranteedTickets.IntersectWith(pool);

            // Only active tickets tick down -- an upcoming/queued ticket's
            // RemainingSeconds stays frozen at TimeLimitSeconds until it's
            // assigned into a slot, so it can never be "urgent" yet.
            foreach (var ticket in activeTickets)
            {
                if (ticket != null && ticket.State == TicketState.Active && ticket.RemainingSeconds < config.UrgentTimeThresholdSeconds)
                {
                    guaranteedTickets.Add(ticket);
                }
            }

            var budget = config.GuaranteedTicketCountMode == GuaranteedTicketCountMode.Poisson
                ? TruncatedPoisson.Sample(GameState.TicketSlotCount - 1, config.GuaranteedTicketCountLambda, random) + 1
                : config.GuaranteedTicketCount;

            var slotsToFill = budget - guaranteedTickets.Count;
            if (slotsToFill > 0)
            {
                var candidates = pool.Where(t => !guaranteedTickets.Contains(t)).ToList();
                foreach (var picked in WeightedSampleWithoutReplacement(candidates, slotsToFill))
                {
                    guaranteedTickets.Add(picked);
                }
            }

            return guaranteedTickets.ToList();
        }

        // Standard weighted draw-without-replacement, fine for the small
        // candidate counts here (active slots + a short upcoming lookahead).
        // Weight for the candidate at index i of what's currently left is
        // EarlyTicketWeightDecay^i -- since `remaining` stays in arrival order
        // throughout (only ever shrinks via removal, never reordered), index 0
        // is always "earliest among what hasn't been picked yet" at each draw.
        // Decay 0 makes every rank >= 1 exactly zero weight (deterministic
        // earliest-first); decay 1 makes every rank equal weight (uniform).
        private List<Ticket> WeightedSampleWithoutReplacement(List<Ticket> pool, int count)
        {
            var remaining = new List<Ticket>(pool);
            var result = new List<Ticket>(Math.Min(count, remaining.Count));

            for (var picks = 0; picks < count && remaining.Count > 0; picks++)
            {
                var weights = new double[remaining.Count];
                var totalWeight = 0d;
                for (var i = 0; i < remaining.Count; i++)
                {
                    weights[i] = Math.Pow(config.EarlyTicketWeightDecay, i);
                    totalWeight += weights[i];
                }

                var roll = random.NextDouble() * totalWeight;
                var cumulative = 0d;
                var chosenIndex = remaining.Count - 1;
                for (var i = 0; i < remaining.Count; i++)
                {
                    cumulative += weights[i];
                    if (roll < cumulative)
                    {
                        chosenIndex = i;
                        break;
                    }
                }

                result.Add(remaining[chosenIndex]);
                remaining.RemoveAt(chosenIndex);
            }

            return result;
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
        // LeakDepth restricts WHICH tickets are candidates — the nearest
        // upcomingTickets entries only (index 0 is the front of the FIFO queue,
        // arrives soonest), so Take(LeakDepth) means "only look this many
        // tickets ahead", leaving tickets further back in the queue untouched
        // until they age forward into the window. HOW MANY items leak is a
        // separate concern: Poisson-sampled once per order placed
        // (NoiseLeakCountLambda), truncated to MaxLeakCount — independent of
        // LeakDepth/candidate availability, so the preview's probabilities
        // never depend on how many tickets happen to be queued. The sampled
        // count can still exceed the actual candidates on hand (e.g.
        // MaxLeakCount=5 but only 2 un-leaked tickets are within LeakDepth), so
        // it's capped down to candidates.Count before leaking.
        private void LeakNoiseItems(IReadOnlyList<Ticket> upcomingTickets)
        {
            leakedTickets.IntersectWith(upcomingTickets);

            var candidates = upcomingTickets.Take(config.LeakDepth).Where(t => !leakedTickets.Contains(t)).ToList();
            if (candidates.Count == 0) return;

            var leakCount = Math.Min(
                TruncatedPoisson.Sample(config.MaxLeakCount, config.NoiseLeakCountLambda, random),
                candidates.Count);

            for (var i = 0; i < leakCount; i++)
            {
                var index = random.Next(candidates.Count);
                var sourceTicket = candidates[index];
                candidates.RemoveAt(index);

                var food = sourceTicket.RequiredItems[random.Next(sourceTicket.RequiredItems.Count)];
                var mods = food.Category == FoodCategory.Main ? sourceTicket.Modifications : Array.Empty<Modification>();

                state.Board.RequestSpawn(new BoardItem(food, mods), random);
                leakedTickets.Add(sourceTicket);
            }
        }
    }
}
