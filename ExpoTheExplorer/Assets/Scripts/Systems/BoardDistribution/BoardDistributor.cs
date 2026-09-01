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
    //
    // Balancing arrives as plain BoardDistributionSettings, not as the config asset:
    // it is authored per Day in Day JSON and resolved by DayCatalogParser, so this
    // class no longer touches ScriptableObject at all (decisions.md D-004).
    public class BoardDistributor
    {
        private readonly GameState state;
        private readonly BoardDistributionSettings settings;
        private readonly Random random;
        private readonly Func<int, IReadOnlyList<BoardItem>> trayContentsForSlot;
        private readonly HashSet<Ticket> leakedTickets = new();
        private readonly HashSet<Ticket> guaranteedTickets = new();

        // trayContentsForSlot reads one ticket slot's tray at the moment a round
        // runs — a delegate rather than a TrayManager reference for the same
        // reason the ticket lists are parameters: this class stays on Core + Data
        // and never sees Systems.TraySystem (TrayManager itself takes
        // deliverTicket/loseLife delegates for the mirror-image reason). It is
        // read live, never snapshotted at construction: a tray's contents change
        // constantly, and this distributor outlives many drags.
        //
        // Omitting it means "every tray is empty", which is exactly the behavior
        // that existed before the tray was counted at all — the honest default
        // for a caller with no trays (EditMode tests, the main screen).
        public BoardDistributor(
            GameState state,
            BoardDistributionSettings settings,
            Random random = null,
            Func<int, IReadOnlyList<BoardItem>> trayContentsForSlot = null)
        {
            this.state = state;
            this.settings = settings;
            this.random = random ?? new Random();
            this.trayContentsForSlot = trayContentsForSlot;
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
                // An item the player already dragged into THIS ticket's tray is no
                // longer on the board, so without this the requirement reads as
                // unmet every round and a duplicate gets spawned on top of what the
                // player already banked — the board fills with food nobody needs.
                //
                // The credit is strictly per-ticket, and that is the whole design:
                // an item sitting in ticket A's tray is committed to A and
                // unavailable to B, so counting all trays into one pool alongside
                // the board would leave B permanently short. That is the exact
                // failure TrayManager.TryAddItem's onAccepted ordering exists to
                // prevent (see its comment), and it is how the D-044 stuck position
                // reappears. So: a ticket's own tray reduces only its own needs, and
                // the board below stays the only SHARED pool.
                //
                // A tray can never hold a ticket's complete correct set — TryAddItem
                // runs the batch check the instant the tray fills and delivers — so
                // this can never drive a guaranteed ticket's need to zero while it is
                // still unfinished. "At least one active ticket is completable" holds
                // by construction, now counting the tray as part of "completable".
                var bankedInOwnTray = CountItemsInTrayOf(ticket);

                foreach (var food in ticket.RequiredItems)
                {
                    var mods = food.Category == FoodCategory.Main ? ticket.Modifications : Array.Empty<Modification>();
                    var key = new RequiredItemKey(food, mods);

                    // Consumed, not just tested: a ticket wanting two of the same
                    // combo with one in its tray still needs the second one.
                    if (bankedInOwnTray.TryGetValue(key, out var banked) && banked > 0)
                    {
                        bankedInOwnTray[key] = banked - 1;
                        continue;
                    }

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
        // many tickets via an arrival-weighted lottery. The budget is spent on ACTIVE
        // tickets first and only reaches the queued lookahead once every active ticket
        // is already covered (decisions.md D-044 -- see the long comment at that code,
        // it is the fix for a real stuck position). Layered on top: any active
        // ticket under UrgentTimeThresholdSeconds is guaranteed unconditionally --
        // it consumes from the budget when the budget isn't exceeded, but is
        // guaranteed in full even past budget if urgent-ticket count exceeds it
        // (confirmed with the user: urgency always wins). That urgency check is
        // evaluated HERE, on the order-placed path, and deliberately nowhere else:
        // spawning stays order-triggered with no timer or per-frame poll anywhere
        // (the user's call, reaffirmed 2026-08-23). What makes that safe is the
        // active-first budget below -- every round already leaves at least one
        // ON-SCREEN ticket completable, so the board cannot reach a state where the
        // player has no move and no further round is coming.
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
                if (ticket != null && ticket.State == TicketState.Active && ticket.RemainingSeconds < settings.UrgentTimeThresholdSeconds)
                {
                    guaranteedTickets.Add(ticket);
                }
            }

            var budget = settings.GuaranteedTicketCountMode == GuaranteedTicketCountMode.Poisson
                ? TruncatedPoisson.Sample(GameState.TicketSlotCount - 1, settings.GuaranteedTicketCountLambda, random) + 1
                : settings.GuaranteedTicketCount;

            // The budget counts ACTIVE tickets only, and active candidates are drawn
            // before any queued one -- the locked rule is "the minimum set needed to
            // complete ACTIVE tickets: at least one ticket must always be completable"
            // (GDD Section 4 / ExpoTheExplorer CLAUDE.md), and a ticket the player
            // cannot even see yet completes nothing.
            //
            // Drawing from one combined active+upcoming pool was a real playtest bug.
            // With the shipped balancing (budget 1, decay 0.5, a 10-deep lookahead) the
            // pool is ~13 tickets, of which only 3 are on screen, so the single budget
            // slot regularly went to a QUEUED ticket -- and because selection is sticky
            // and a queued ticket never leaves the pool, it then held that slot for many
            // rounds while all three visible tickets got nothing. The board filled with
            // items for orders that had not arrived (a ketchup hotdog while all three
            // active tickets wanted something else), the player had no legal move, and
            // since nothing is delivered, no ticket is assigned and no further round
            // runs -- the position is unrecoverable until a ticket times out and pays a
            // life for it. Simulated over the authored Days: 18.7% of rounds left ZERO
            // active ticket guaranteed and 2.3% left all three slots full with nothing
            // completable, ~0.3 forced life losses per day; both go to zero here.
            var activeSet = new HashSet<Ticket>(
                activeTickets.Where(t => t != null && t.State == TicketState.Active));

            var slotsToFill = budget - guaranteedTickets.Count(t => activeSet.Contains(t));
            if (slotsToFill > 0)
            {
                // pool order (arrival) is preserved by the filter, which is what the
                // decay weighting in WeightedSampleWithoutReplacement reads.
                var activeCandidates = pool.Where(t => activeSet.Contains(t) && !guaranteedTickets.Contains(t)).ToList();
                foreach (var picked in WeightedSampleWithoutReplacement(activeCandidates, slotsToFill))
                {
                    guaranteedTickets.Add(picked);
                    slotsToFill--;
                }

                // Only once EVERY active ticket is covered does the lookahead get the
                // remainder -- that is the "budget larger than the number of active
                // tickets" case, where preparing the next arrival in advance costs an
                // on-screen ticket nothing. A queued ticket picked this way keeps its
                // spawned items and stays in the set, but never counts against the
                // active budget above; it starts counting once it reaches a slot.
                if (slotsToFill > 0)
                {
                    var upcomingCandidates = pool.Where(t => !activeSet.Contains(t) && !guaranteedTickets.Contains(t)).ToList();
                    foreach (var picked in WeightedSampleWithoutReplacement(upcomingCandidates, slotsToFill))
                    {
                        guaranteedTickets.Add(picked);
                    }
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
                    weights[i] = Math.Pow(settings.EarlyTicketWeightDecay, i);
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

        // What this ticket's OWN tray already holds, keyed the same way the board
        // is — so the two counts are comparable and a plain hotdog in the tray
        // never credits a ketchup hotdog's requirement.
        //
        // A ticket's tray is found by its slot: TrayManager allocates
        // GameState.TicketSlotCount trays and indexes them by the same slot index
        // TicketSlots uses, so IndexOf here IS the mapping. A guaranteed ticket
        // still sitting in the upcoming queue is in no slot, gets -1, and is
        // credited nothing — correct, since it has no tray to have banked into.
        //
        // Items the player dropped in that the ticket does not want produce keys
        // that appear in no requirement, so they credit nothing on their own. They
        // are still counted here rather than filtered, because the caller consumes
        // this per requirement and never asks about a key it doesn't need.
        private Dictionary<RequiredItemKey, int> CountItemsInTrayOf(Ticket ticket)
        {
            var counts = new Dictionary<RequiredItemKey, int>();
            if (trayContentsForSlot == null) return counts;

            var slotIndex = Array.IndexOf(state.TicketSlots, ticket);
            if (slotIndex < 0) return counts;

            var contents = trayContentsForSlot(slotIndex);
            if (contents == null) return counts;

            foreach (var item in contents)
            {
                if (item == null) continue;

                var key = new RequiredItemKey(item.Config, item.Modifications);
                counts.TryGetValue(key, out var count);
                counts[key] = count + 1;
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

            var candidates = upcomingTickets.Take(settings.LeakDepth).Where(t => !leakedTickets.Contains(t)).ToList();
            if (candidates.Count == 0) return;

            var leakCount = Math.Min(
                TruncatedPoisson.Sample(settings.MaxLeakCount, settings.NoiseLeakCountLambda, random),
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
