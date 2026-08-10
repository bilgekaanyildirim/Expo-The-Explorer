using System;
using System.Collections.Generic;
using ExpoTheExplorer.Core;
using ExpoTheExplorer.Data;
using UnityEngine;

namespace ExpoTheExplorer.Systems.DaySystem
{
    // Parses raw Day JSON text into resolved DayDefinitions. A Day whose ids/enums
    // don't resolve is dropped entirely (loudly) rather than producing a partially
    // broken DayDefinition -- other Day files are unaffected.
    public static class DayCatalogParser
    {
        public static List<DayDefinition> ParseAll(IReadOnlyList<DayJsonFile> files, FoodCatalog catalog)
        {
            var results = new List<DayDefinition>();
            var seenIndices = new HashSet<int>();

            foreach (var file in files)
            {
                DayJson dayJson;
                try
                {
                    dayJson = JsonUtility.FromJson<DayJson>(file.Json);
                }
                catch (Exception e)
                {
                    Debug.LogError($"Day file '{file.FileName}' is not valid JSON: {e.Message}");
                    continue;
                }

                var resolved = ResolveDay(dayJson, catalog, file.FileName);
                if (resolved == null)
                {
                    continue; // error already logged inside ResolveDay
                }

                if (!seenIndices.Add(resolved.DayIndex))
                {
                    Debug.LogError($"Day file '{file.FileName}' has dayIndex {resolved.DayIndex}, already used by another Day file.");
                }

                results.Add(resolved);
            }

            results.Sort((a, b) => a.DayIndex.CompareTo(b.DayIndex));
            return results;
        }

        private static DayDefinition ResolveDay(DayJson dayJson, FoodCatalog catalog, string fileName)
        {
            var runtime = dayJson?.runtime;
            if (runtime == null)
            {
                Debug.LogError($"Day file '{fileName}' has no runtime section.");
                return null;
            }

            var ticketSequence = new List<ResolvedTicketEntry>();
            foreach (var entry in runtime.ticketSequence ?? Array.Empty<TicketEntryJson>())
            {
                var resolvedEntry = ResolveTicketEntry(entry, catalog, fileName);
                if (resolvedEntry == null)
                {
                    return null;
                }
                ticketSequence.Add(resolvedEntry);
            }

            var boardTimeline = new List<ResolvedBoardSpawnEntry>();
            foreach (var spawn in runtime.boardTimeline ?? Array.Empty<BoardSpawnEntryJson>())
            {
                var resolvedSpawn = ResolveBoardSpawn(spawn, catalog, fileName);
                if (resolvedSpawn == null)
                {
                    return null;
                }
                boardTimeline.Add(resolvedSpawn);
            }

            DayDefinition retryVariant = null;
            if (runtime.hasRetryVariant)
            {
                retryVariant = ResolveDay(runtime.retryVariant, catalog, $"{fileName} (retryVariant)");
            }

            return new DayDefinition(runtime.dayIndex, runtime.ticketsRequiredForDay, ticketSequence, boardTimeline, retryVariant,
                runtime.star1Threshold, runtime.star2Threshold, runtime.star3Threshold);
        }

        private static ResolvedTicketEntry ResolveTicketEntry(TicketEntryJson entry, FoodCatalog catalog, string fileName)
        {
            var main = catalog.GetById(entry.mainItemId);
            if (main == null)
            {
                Debug.LogError($"Day file '{fileName}': unknown mainItemId '{entry.mainItemId}'.");
                return null;
            }

            var side = ResolveOptionalItem(entry.sideItemId, catalog, fileName, "sideItemId", out var sideOk);
            if (!sideOk)
            {
                return null;
            }

            var drink = ResolveOptionalItem(entry.drinkItemId, catalog, fileName, "drinkItemId", out var drinkOk);
            if (!drinkOk)
            {
                return null;
            }

            var modifications = ResolveModifications(entry.modifications, catalog, fileName, out var modsOk);
            if (!modsOk)
            {
                return null;
            }

            if (!Enum.TryParse<PatienceType>(entry.patienceType, out var patienceType))
            {
                Debug.LogError($"Day file '{fileName}': invalid patienceType '{entry.patienceType}'.");
                return null;
            }

            return new ResolvedTicketEntry(main, side, drink, modifications, patienceType,
                entry.customerNameOverride, entry.timeLimitSecondsOverride);
        }

        private static ResolvedBoardSpawnEntry ResolveBoardSpawn(BoardSpawnEntryJson spawn, FoodCatalog catalog, string fileName)
        {
            var item = catalog.GetById(spawn.itemId);
            if (item == null)
            {
                Debug.LogError($"Day file '{fileName}': unknown board spawn itemId '{spawn.itemId}'.");
                return null;
            }

            var modifications = ResolveModifications(spawn.modifications, catalog, fileName, out var modsOk);
            if (!modsOk)
            {
                return null;
            }

            return new ResolvedBoardSpawnEntry(spawn.triggerStepIndex, item, modifications, spawn.useExactCell, spawn.x, spawn.y);
        }

        private static FoodItemConfig ResolveOptionalItem(string id, FoodCatalog catalog, string fileName, string fieldName, out bool ok)
        {
            ok = true;
            if (string.IsNullOrEmpty(id))
            {
                return null;
            }

            var item = catalog.GetById(id);
            if (item == null)
            {
                Debug.LogError($"Day file '{fileName}': unknown {fieldName} '{id}'.");
                ok = false;
            }
            return item;
        }

        private static List<Modification> ResolveModifications(ModificationEntryJson[] entries, FoodCatalog catalog, string fileName, out bool ok)
        {
            ok = true;
            var modifications = new List<Modification>();
            foreach (var mod in entries ?? Array.Empty<ModificationEntryJson>())
            {
                var config = catalog.GetModificationById(mod.modificationId);
                if (config == null)
                {
                    Debug.LogError($"Day file '{fileName}': unknown modificationId '{mod.modificationId}'.");
                    ok = false;
                    return modifications;
                }
                modifications.Add(new Modification(config, mod.isAddition));
            }
            return modifications;
        }
    }
}
