using System;
using System.Collections.Generic;
using System.Linq;
using ExpoTheExplorer.Core;
using ExpoTheExplorer.Data;

namespace ExpoTheExplorer.Systems.TicketSystem
{
    // Minimal ticket generation — enough to exercise TicketSlotManager. Which food
    // items exist and how required/noise pools decide what's available is a
    // future Board Distribution concern; this just assembles a Ticket from a
    // caller-supplied pool.
    public class TicketFactory
    {
        private readonly TicketGenerationConfig config;
        private readonly Random random;

        public TicketFactory(TicketGenerationConfig config, Random random = null)
        {
            this.config = config;
            this.random = random ?? new Random();
        }

        // Deliberately separate from Create: which PatienceType a ticket gets is
        // an unspecified balancing decision (GDD never states a distribution).
        // This uniform roll is a placeholder, not a design choice.
        public PatienceType PickRandomPatienceType()
        {
            return (PatienceType)random.Next(0, 3);
        }

        // Same rationale as PickRandomPatienceType: which name a ticket gets is
        // flavor, not a decision GameManager needs to own.
        public string PickRandomCustomerName()
        {
            return config.CustomerNames[random.Next(config.CustomerNames.Count)];
        }

        public Ticket Create(IReadOnlyList<FoodItemConfig> pool, string customerName, PatienceType patienceType)
        {
            var mains = pool.Where(item => item.Category == FoodCategory.Main).ToList();
            if (mains.Count == 0)
            {
                throw new ArgumentException("Pool must contain at least one Main item to build a ticket.", nameof(pool));
            }

            var main = PickWeightedMain(mains);
            var requiredItems = new List<FoodItemConfig> { main };

            TryAddRandomItem(pool, FoodCategory.Side, config.SideInclusionChance, requiredItems);
            TryAddRandomItem(pool, FoodCategory.Drink, config.DrinkInclusionChance, requiredItems);

            var lambda = ResolveModificationCountLambda(main);
            var modificationCount = PickModificationCount(main.AvailableModifications.Count, lambda);
            var chosenMods = ChooseModifications(main.AvailableModifications, modificationCount);
            var modifications = chosenMods.Select(CreateModification).ToList();

            var timeLimitSeconds = patienceType switch
            {
                PatienceType.Impatient => config.ImpatientTimeLimitSeconds,
                PatienceType.Patient => config.PatientTimeLimitSeconds,
                _ => config.NormalTimeLimitSeconds,
            };

            return new Ticket(customerName, patienceType, requiredItems, modifications, timeLimitSeconds);
        }

        // Weighted pick over MainDishWeights (falls back to DefaultWeight for any
        // candidate missing an entry, so forgetting to configure a newly added
        // dish doesn't silently exclude it). Accumulates in double even though
        // the serialized weight is a float, to avoid the cumulative-sum rounding
        // edge case landing just short of the roll.
        private FoodItemConfig PickWeightedMain(IReadOnlyList<FoodItemConfig> mains)
        {
            var weights = new double[mains.Count];
            var totalWeight = 0d;
            for (var i = 0; i < mains.Count; i++)
            {
                weights[i] = ResolveWeight(mains[i]);
                totalWeight += weights[i];
            }

            if (totalWeight <= 0d)
            {
                return mains[random.Next(mains.Count)];
            }

            var roll = random.NextDouble() * totalWeight;
            var cumulative = 0d;
            for (var i = 0; i < mains.Count; i++)
            {
                cumulative += weights[i];
                if (roll < cumulative) return mains[i];
            }

            return mains[^1];
        }

        private double ResolveWeight(FoodItemConfig food)
        {
            foreach (var entry in config.MainDishWeights)
            {
                if (entry.Food == food) return Math.Max(0d, entry.Weight);
            }

            return MainDishWeight.DefaultWeight;
        }

        // Same lookup shape as ResolveWeight, but falls back to the config-wide
        // ModificationCountLambda instead of a hardcoded constant — a Main dish
        // present in MainDishWeights always uses its own per-dish rate, one
        // absent from the list entirely uses the shared fallback.
        private float ResolveModificationCountLambda(FoodItemConfig food)
        {
            foreach (var entry in config.MainDishWeights)
            {
                if (entry.Food == food) return entry.ModificationCountLambda;
            }

            return config.ModificationCountLambda;
        }

        // How many modifications this ticket gets, drawn from a Poisson(lambda)
        // distribution truncated to k = 0..n and renormalized (the un-truncated
        // Poisson has no upper bound, but a dish only has n possible mods).
        // Uses the unnormalized term ratio lambda^k/k! directly — the e^-lambda
        // factor of the true Poisson pmf cancels out once we divide by the sum
        // of these n+1 terms, so it's never computed. term(0) is always exactly
        // 1, so the total is always >= 1 — no divide-by-zero risk, unlike
        // PickWeightedMain's weight sum (which a designer could zero out).
        private int PickModificationCount(int n, float lambda)
        {
            if (n == 0) return 0;

            var terms = new double[n + 1];
            terms[0] = 1d;
            for (var k = 1; k <= n; k++)
            {
                terms[k] = terms[k - 1] * lambda / k;
            }

            var total = 0d;
            foreach (var term in terms) total += term;

            var roll = random.NextDouble() * total;
            var cumulative = 0d;
            for (var k = 0; k <= n; k++)
            {
                cumulative += terms[k];
                if (roll < cumulative) return k;
            }

            return n;
        }

        // Uniform random subset without replacement, via a partial Fisher-Yates
        // shuffle on a copy of the available list — simplest correct approach
        // given a dish only ever has a handful of modifications to choose among.
        private List<ModificationConfig> ChooseModifications(IReadOnlyList<ModificationConfig> available, int count)
        {
            var shuffled = new List<ModificationConfig>(available);
            var limit = Math.Min(count, shuffled.Count);

            for (var i = 0; i < limit; i++)
            {
                var swapIndex = random.Next(i, shuffled.Count);
                (shuffled[i], shuffled[swapIndex]) = (shuffled[swapIndex], shuffled[i]);
            }

            return shuffled.GetRange(0, limit);
        }

        private void TryAddRandomItem(IReadOnlyList<FoodItemConfig> pool, FoodCategory category, float inclusionChance, List<FoodItemConfig> requiredItems)
        {
            if (random.NextDouble() >= inclusionChance) return;

            var candidates = pool.Where(item => item.Category == category).ToList();
            if (candidates.Count == 0) return;

            requiredItems.Add(candidates[random.Next(candidates.Count)]);
        }

        // A modification's direction is intrinsic to its type (ModificationConfig.AllowedDirection),
        // not a random per-ticket roll — only a Both-direction modification (e.g.
        // Cheese) still needs the extra coin flip. Whether this modConfig is
        // included at all was already decided by ChooseModifications; this just
        // resolves the direction for one that was.
        private Modification CreateModification(ModificationConfig modConfig)
        {
            var isAddition = modConfig.AllowedDirection switch
            {
                ModificationDirection.AdditionOnly => true,
                ModificationDirection.RemovalOnly => false,
                ModificationDirection.Both => random.NextDouble() < config.ModificationAdditionChance,
                _ => throw new ArgumentOutOfRangeException(nameof(modConfig), modConfig.AllowedDirection, "Unhandled ModificationDirection."),
            };

            return new Modification(modConfig, isAddition);
        }
    }
}
