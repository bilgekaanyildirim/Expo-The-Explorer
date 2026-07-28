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

        public Ticket Create(IReadOnlyList<FoodItemConfig> pool, string customerName, PatienceType patienceType)
        {
            var mains = pool.Where(item => item.Category == FoodCategory.Main).ToList();
            if (mains.Count == 0)
            {
                throw new ArgumentException("Pool must contain at least one Main item to build a ticket.", nameof(pool));
            }

            var main = mains[random.Next(mains.Count)];
            var requiredItems = new List<FoodItemConfig> { main };

            TryAddRandomItem(pool, FoodCategory.Side, config.SideInclusionChance, requiredItems);
            TryAddRandomItem(pool, FoodCategory.Drink, config.DrinkInclusionChance, requiredItems);

            var modifications = new List<Modification>();
            foreach (var modConfig in main.AvailableModifications)
            {
                if (random.NextDouble() >= config.ModificationInclusionChance) continue;
                var isAddition = random.NextDouble() < config.ModificationAdditionChance;
                modifications.Add(new Modification(modConfig, isAddition));
            }

            var timeLimitSeconds = patienceType switch
            {
                PatienceType.Impatient => config.ImpatientTimeLimitSeconds,
                PatienceType.Patient => config.PatientTimeLimitSeconds,
                _ => config.NormalTimeLimitSeconds,
            };

            return new Ticket(customerName, patienceType, requiredItems, modifications, timeLimitSeconds);
        }

        private void TryAddRandomItem(IReadOnlyList<FoodItemConfig> pool, FoodCategory category, float inclusionChance, List<FoodItemConfig> requiredItems)
        {
            if (random.NextDouble() >= inclusionChance) return;

            var candidates = pool.Where(item => item.Category == category).ToList();
            if (candidates.Count == 0) return;

            requiredItems.Add(candidates[random.Next(candidates.Count)]);
        }
    }
}
