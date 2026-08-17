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

            var boardDistribution = ResolveBoardDistribution(runtime.boardDistribution, fileName);
            if (boardDistribution == null)
            {
                return null;
            }

            var ticketRuntime = ResolveTicketRuntime(runtime.ticketRuntime, fileName);
            if (ticketRuntime == null)
            {
                return null;
            }

            return new DayDefinition(runtime.dayIndex, runtime.ticketsRequiredForDay, ticketSequence, boardTimeline,
                runtime.star1Threshold, runtime.star2Threshold, runtime.star3Threshold, boardDistribution, ticketRuntime);
        }

        // Same absence-by-content detection as ResolveBoardDistribution (JsonUtility gives
        // a zeroed instance, never null), but there is no enum to key off here, so the
        // check is that the numbers are usable at all. A 0-second limit is not a valid
        // authored value: the ticket would time out on the frame it arrives, costing a life
        // before the player could touch it. Requiring all three keeps a half-filled block
        // from passing on the strength of one field.
        private static TicketRuntimeSettings ResolveTicketRuntime(TicketRuntimeJson json, string fileName)
        {
            if (json == null || json.impatientTimeLimitSeconds <= 0f || json.normalTimeLimitSeconds <= 0f || json.patientTimeLimitSeconds <= 0f)
            {
                Debug.LogError($"Day file '{fileName}': runtime.ticketRuntime block is missing or incomplete -- impatient/normal/patient time limits must all be greater than 0.");
                return null;
            }

            if (json.upcomingQueueSize < 1)
            {
                Debug.LogError($"Day file '{fileName}': runtime.ticketRuntime.upcomingQueueSize ({json.upcomingQueueSize}) must be at least 1.");
                return null;
            }

            return new TicketRuntimeSettings(
                json.impatientTimeLimitSeconds,
                json.normalTimeLimitSeconds,
                json.patientTimeLimitSeconds,
                json.upcomingQueueSize);
        }

        // JsonUtility cannot express a null nested [Serializable] class -- an absent
        // boardDistribution block comes back as a non-null instance with every field at
        // its CLR default, indistinguishable from a deliberately-zeroed one. So absence
        // is detected by content instead of by a null check or a "has..." sentinel bool
        // (the sentinel pattern was removed with the retry variant, decisions.md D-003):
        // the mode string is empty on an unauthored block, and the three counts below are
        // documented as never-zero in BoardDistributionConfig. Getting this wrong is not
        // cosmetic -- a silently zeroed block disables the urgent-ticket guarantee and
        // flattens the arrival-weighted lottery, which is precisely how the shared config
        // asset ended up with two of D-001's features quietly switched off.
        private static BoardDistributionSettings ResolveBoardDistribution(BoardDistributionJson json, string fileName)
        {
            if (json == null || string.IsNullOrEmpty(json.guaranteedTicketCountMode))
            {
                Debug.LogError($"Day file '{fileName}': runtime.boardDistribution block is missing or has no guaranteedTicketCountMode.");
                return null;
            }

            if (!Enum.TryParse<GuaranteedTicketCountMode>(json.guaranteedTicketCountMode, out var mode))
            {
                Debug.LogError($"Day file '{fileName}': invalid guaranteedTicketCountMode '{json.guaranteedTicketCountMode}'.");
                return null;
            }

            if (json.guaranteedTicketCount < 1 || json.leakDepth < 1 || json.maxLeakCount < 1)
            {
                Debug.LogError($"Day file '{fileName}': runtime.boardDistribution is incomplete -- guaranteedTicketCount ({json.guaranteedTicketCount}), leakDepth ({json.leakDepth}) and maxLeakCount ({json.maxLeakCount}) must all be at least 1.");
                return null;
            }

            return new BoardDistributionSettings(
                json.noiseLeakCountLambda,
                mode,
                json.guaranteedTicketCount,
                json.guaranteedTicketCountLambda,
                json.earlyTicketWeightDecay,
                json.urgentTimeThresholdSeconds,
                json.leakDepth,
                json.maxLeakCount);
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
