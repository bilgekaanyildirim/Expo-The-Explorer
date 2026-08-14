using System;
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
                return GenerateCore(catalog, ticketConfig, ticketsRequiredForDay, random);
            }
            finally
            {
                if (ticketConfig != baseTicketConfig) UnityEngine.Object.DestroyImmediate(ticketConfig);
            }
        }

        private static TicketEntryJson[] GenerateCore(FoodCatalog catalog, TicketGenerationConfig ticketConfig, int ticketsRequiredForDay, Random random)
        {
            var ticketFactory = new TicketFactory(ticketConfig, random);
            var entries = new TicketEntryJson[ticketsRequiredForDay];

            for (var i = 0; i < ticketsRequiredForDay; i++)
            {
                var patienceType = ticketFactory.PickRandomPatienceType();
                var ticket = ticketFactory.Create(catalog.Items, SimulatedCustomerName, patienceType);
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
