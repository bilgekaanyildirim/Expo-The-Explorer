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
            var ticketConfig = ApplyTicketGenerationOverrides(baseTicketConfig, editorMeta);

            try
            {
                return GenerateCore(ResolveFoodPool(catalog, editorMeta), ticketConfig, ticketsRequiredForDay, random);
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

        private static TicketEntryJson[] GenerateCore(IReadOnlyList<FoodItemConfig> pool, TicketGenerationConfig ticketConfig, int ticketsRequiredForDay, Random random)
        {
            var ticketFactory = new TicketFactory(ticketConfig, random);
            var entries = new TicketEntryJson[ticketsRequiredForDay];

            for (var i = 0; i < ticketsRequiredForDay; i++)
            {
                var patienceType = ticketFactory.PickRandomPatienceType();
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
    }
}
