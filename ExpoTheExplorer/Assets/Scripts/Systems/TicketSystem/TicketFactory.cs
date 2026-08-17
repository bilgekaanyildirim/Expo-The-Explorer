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
        private readonly TicketRuntimeSettings runtimeSettings;
        private readonly Random random;
        private long nextArrivalSequence;

        public TicketFactory(TicketGenerationConfig config, Random random = null)
        {
            this.config = config;
            // Built once here rather than per Create call: this factory is the AUTHORING
            // path (Day generation, the Day Editor's single-ticket reroll), where the seed
            // asset's own time limits are the right source. The played Day gets its limits
            // from its own JSON via TicketEntryFactory (decisions.md D-005). Going through
            // the same settings type keeps the patience switch in one place.
            runtimeSettings = config.ToRuntimeSettings();
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
            var modificationCount = TruncatedPoisson.Sample(main.AvailableModifications.Count, lambda, random);
            var chosenMods = ChooseModifications(main.AvailableModifications, modificationCount);
            var modifications = chosenMods.Select(CreateModification).ToList();

            var timeLimitSeconds = runtimeSettings.TimeLimitSecondsFor(patienceType);

            return new Ticket(customerName, patienceType, requiredItems, modifications, timeLimitSeconds, nextArrivalSequence++);
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
