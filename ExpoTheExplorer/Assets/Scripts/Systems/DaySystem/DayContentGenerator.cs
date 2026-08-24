using System;
using System.Collections.Generic;
using System.Linq;
using ExpoTheExplorer.Core;
using ExpoTheExplorer.Data;
using ExpoTheExplorer.Systems.TicketSystem;

namespace ExpoTheExplorer.Systems.DaySystem
{
    // Offline, one-shot ticket-sequence roller that drives the REAL TicketFactory logic (not a
    // reimplementation of it) to author a Day's ticketSequence ahead of time. Only ever called
    // from Editor tooling (PR-7's "Generate" button), but deliberately has no UnityEditor
    // dependency itself, so it stays EditMode-testable. Board content (Day Start only, since
    // Phase 2/3 reconnected BoardDistributor live -- see decisions.md D-001) is authored
    // separately by hand in the Day Editor, not generated here.
    public static class DayContentGenerator
    {
        private const string SimulatedCustomerName = "Simulated Customer";

        public static TicketEntryJson[] Generate(
            FoodCatalog catalog,
            TicketGenerationConfig baseTicketConfig,
            DayEditorMetaJson editorMeta,
            int ticketsRequiredForDay,
            int seed)
        {
            var random = new Random(seed);
            var ticketConfig = ResolveTicketGenerationConfig(baseTicketConfig, catalog, editorMeta);

            try
            {
                return GenerateCore(ResolveFoodPool(catalog, editorMeta), ticketConfig, ticketsRequiredForDay, random, editorMeta);
            }
            finally
            {
                if (ticketConfig != baseTicketConfig) UnityEngine.Object.DestroyImmediate(ticketConfig);
            }
        }

        // The items that exist in this Day: exactly the ones its food selection names.
        // Filters the catalog (rather than mapping ids through GetById) so catalog order
        // is preserved, duplicates in the id list can't duplicate an item, and an id left
        // over from a deleted/renamed FoodItemConfig is simply absent instead of becoming
        // a null entry TicketFactory would trip over. The null check covers an empty slot
        // in the catalog's own serialized list, for the same reason.
        //
        // No selection means an EMPTY pool, not "everything" -- the selection is the Day's
        // food set, so nothing selected is an empty Day. TicketFactory.Create then throws
        // on the missing Main, which is the honest outcome for a direct caller; the Day
        // Editor guards before ever reaching it (see DayEditorModel.HasSelectableMainDish).
        //
        // Public because the Day Editor's item pickers must offer exactly this same pool --
        // one implementation, two authoring-time readers, rather than the same filter rule
        // written out in two places that can drift apart.
        public static IReadOnlyList<FoodItemConfig> ResolveFoodPool(FoodCatalog catalog, DayEditorMetaJson editorMeta)
        {
            var allowedIds = editorMeta?.allowedFoodItemIds ?? Array.Empty<string>();
            return catalog.Items.Where(item => item != null && allowedIds.Contains(item.Id)).ToList();
        }

        // The presentation order the Day Editor authors against, and the order a generated
        // sequence comes out in. Deliberately NOT PatienceType's own declaration order,
        // which runs Impatient, Normal, Patient -- the exact reverse. Iterating the enum
        // would have produced a sequence that starts with the harshest tickets.
        private static readonly PatienceType[] PatienceOrder =
        {
            PatienceType.Patient, PatienceType.Normal, PatienceType.Impatient,
        };

        // Turns the Day's three authored counts into one patience per ticket, already in
        // PatienceOrder -- so the generated sequence is sorted by construction and no second
        // sort pass exists to disagree with this one.
        //
        // Returns an EMPTY list for "unauthored", which is what all three counts being zero
        // means (see TicketGenerationJson): the caller then falls back to the uniform roll
        // that was the only behaviour before this existed, so opening and re-generating an
        // old Day does not silently turn every ticket Normal.
        //
        // The counts are measured against ticketsRequiredForDay rather than replacing it --
        // that value stays the single authority for how long a Day is. A shortfall is padded
        // with Normal, and an excess is cut off the TAIL, which takes it from Impatient first
        // and then Normal. Both are deliberate and neither is silent: the editor shows the
        // effective counts next to the authored ones before Generate is ever pressed.
        public static List<PatienceType> BuildPatiencePlan(
            int patientCount, int normalCount, int impatientCount, int ticketsRequiredForDay)
        {
            var plan = new List<PatienceType>();
            if (ticketsRequiredForDay <= 0) return plan;
            if (patientCount <= 0 && normalCount <= 0 && impatientCount <= 0) return plan;

            var counts = new[] { Math.Max(0, patientCount), Math.Max(0, normalCount), Math.Max(0, impatientCount) };
            for (var i = 0; i < PatienceOrder.Length; i++)
            {
                for (var n = 0; n < counts[i] && plan.Count < ticketsRequiredForDay; n++)
                {
                    plan.Add(PatienceOrder[i]);
                }
            }

            // Padding with Normal rather than proportionally: the neutral type is the one
            // choice that does not quietly make the Day easier or harder than the counts
            // that were actually written down.
            while (plan.Count < ticketsRequiredForDay) plan.Add(PatienceType.Normal);

            return plan;
        }

        private static TicketEntryJson[] GenerateCore(IReadOnlyList<FoodItemConfig> pool, TicketGenerationConfig ticketConfig, int ticketsRequiredForDay, Random random, DayEditorMetaJson editorMeta)
        {
            var ticketFactory = new TicketFactory(ticketConfig, random);
            var entries = new TicketEntryJson[ticketsRequiredForDay];

            var generation = editorMeta?.ticketGeneration;
            var patiencePlan = BuildPatiencePlan(
                generation?.patientTicketCount ?? 0,
                generation?.normalTicketCount ?? 0,
                generation?.impatientTicketCount ?? 0,
                ticketsRequiredForDay);

            for (var i = 0; i < ticketsRequiredForDay; i++)
            {
                // Empty plan = no mix authored, so the patience stays a roll, exactly as it
                // was before Patience Mix existed.
                var patienceType = patiencePlan.Count > 0 ? patiencePlan[i] : ticketFactory.PickRandomPatienceType();
                var ticket = ticketFactory.Create(pool, SimulatedCustomerName, patienceType);
                entries[i] = ToTicketEntryJson(ticket);
            }

            return entries;
        }

        private static ModificationEntryJson ToModificationEntryJson(Modification mod)
        {
            return new ModificationEntryJson
            {
                modificationId = mod.Config.Id,
                isAddition = mod.IsAddition,
            };
        }

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

        // A Day's own generation settings, applied unconditionally -- there is no
        // "override on/off" any more (D-006). The one case that still falls back to the
        // base asset is a Day file written before this block existed: returning baseConfig
        // there means Generate keeps working on an old Day instead of rolling everything at
        // zero probability, and the Day Editor writes the block out on the next Save.
        private static TicketGenerationConfig ResolveTicketGenerationConfig(
            TicketGenerationConfig baseConfig, FoodCatalog catalog, DayEditorMetaJson editorMeta)
        {
            var generation = editorMeta?.ticketGeneration;
            if (generation == null) return baseConfig;

            return baseConfig.CloneForDayGeneration(
                generation.sideInclusionChance,
                generation.drinkInclusionChance,
                generation.modificationCountLambda,
                generation.modificationAdditionChance,
                ResolveMainDishWeights(generation.mainDishWeights, catalog));
        }

        // Public because the Day Editor has to render the same resolved list the generator
        // will actually use -- one rule, two authoring-time readers, rather than the id
        // lookup written out twice (same reasoning as ResolveFoodPool).
        public static List<MainDishWeight> ResolveMainDishWeights(MainDishWeightJson[] entries, FoodCatalog catalog)
        {
            var resolved = new List<MainDishWeight>();
            foreach (var entry in entries ?? Array.Empty<MainDishWeightJson>())
            {
                // An id left behind by a deleted/renamed FoodItemConfig drops out rather
                // than becoming a null-Food entry TicketFactory would trip over -- the same
                // stance ResolveFoodPool takes.
                var food = catalog.GetById(entry.foodItemId);
                if (food == null) continue;

                resolved.Add(new MainDishWeight(
                    food, entry.weight, entry.modificationCountLambda, entry.maxModificationCount));
            }

            return resolved;
        }
    }
}
