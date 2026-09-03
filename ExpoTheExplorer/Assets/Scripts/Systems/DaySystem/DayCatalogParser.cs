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
                boardDistribution, ticketRuntime, ResolveTutorial(runtime.tutorial),
                ResolveItemIntros(runtime.itemIntro, catalog, fileName),
                ResolveTrayPreSeed(runtime.trayPreSeed, catalog, fileName));
        }

        // What this Day seats in a tray before the player touches anything (D-165). Follows
        // ResolveItemIntros rather than ResolveTutorial on the two questions that matter:
        //
        // A bad block is NOT fatal to the Day, and a bad ENTRY costs only itself. The
        // entries are independent -- two seeded trays are two trays, not one sequence -- so
        // an unknown id costs its own item and the rest still land. The tutorial cannot do
        // that because its steps are a path and a hole in the middle strands the player;
        // that difference is exactly why a TrayMove step naming an unseeded tray is caught
        // by DayValidator at authoring time instead of being shrugged off here.
        //
        // Public for the reason the other two are: DayEditorModel.ToDayDefinition resolves
        // the same block for its validation preview, and one rule with one implementation is
        // what stops a Day validating in the editor and behaving differently at runtime.
        public static IReadOnlyList<ResolvedTrayPreSeed> ResolveTrayPreSeed(
            TrayPreSeedJson trayPreSeed, FoodCatalog catalog, string fileName)
        {
            if (trayPreSeed == null || !trayPreSeed.enabled) return null;

            var seeded = new List<ResolvedTrayPreSeed>();
            foreach (var entry in trayPreSeed.entries ?? Array.Empty<TrayPreSeedEntryJson>())
            {
                if (entry == null || string.IsNullOrEmpty(entry.itemId)) continue;

                var item = catalog == null ? null : catalog.GetById(entry.itemId);
                if (item == null)
                {
                    Debug.LogError(
                        $"Day file '{fileName}': runtime.trayPreSeed names food id '{entry.itemId}', which is not in " +
                        "the FoodCatalog. That tray opens empty.");
                    continue;
                }

                var modifications = ResolveModifications(entry.modifications, catalog, fileName, out var modsOk);
                if (!modsOk) continue;

                seeded.Add(new ResolvedTrayPreSeed(entry.traySlotIndex, item, modifications));
            }

            // An enabled block that resolved nothing is treated as no block rather than as an
            // empty one, which is what lets every reader take "there is a list" as "there is
            // at least one tray to fill".
            return seeded.Count > 0 ? seeded : null;
        }

        // The Day's "here is something new" popups. Like ResolveTutorial and unlike every
        // other Resolve* here, a bad block is NOT fatal to the Day: an introduction is a
        // greeting, and taking a playable day off the calendar because its greeting names a
        // deleted food would punish the content for the decoration.
        //
        // It goes further than the tutorial does, and drops a bad ENTRY rather than the whole
        // block: the entries are independent of each other -- three items introduced on one
        // morning are three popups, not one sequence with a shared meaning -- so an unknown
        // id costs its own popup and the others still play. The tutorial cannot do that,
        // because its steps are a path through one board and a hole in the middle is a step
        // the player is then locked on.
        //
        // Public for the reason ResolveTutorial is: the Day Editor resolves the same block
        // for its validation preview (DayEditorModel.ToDayDefinition), and one rule with one
        // implementation is what stops a Day validating in the editor and behaving
        // differently at runtime.
        public static IReadOnlyList<ResolvedItemIntro> ResolveItemIntros(
            ItemIntroJson itemIntro, FoodCatalog catalog, string fileName)
        {
            if (itemIntro == null || !itemIntro.enabled) return null;

            var intros = new List<ResolvedItemIntro>();
            foreach (var entry in itemIntro.items ?? Array.Empty<ItemIntroEntryJson>())
            {
                if (entry == null) continue;

                // A NON-EMPTY modificationId is what makes this a modification introduction,
                // and it is checked FIRST so the two ids can never both be honoured. An entry
                // carrying both is authored ambiguously, which the Day Editor cannot produce
                // and DayValidator warns about -- reading the modification is the choice that
                // keeps the popup showing the more specific of the two things named.
                if (!string.IsNullOrEmpty(entry.modificationId))
                {
                    var modification = catalog == null ? null : catalog.GetModificationById(entry.modificationId);
                    if (modification == null)
                    {
                        Debug.LogError(
                            $"Day file '{fileName}': item intro names unknown modificationId '{entry.modificationId}'. " +
                            "Skipping this introduction; the rest of the Day is unaffected.");
                        continue;
                    }

                    intros.Add(new ResolvedItemIntro(modification, entry.isAddition, entry.nameOverride, entry.message));
                    continue;
                }

                // A half-authored row -- the box is ticked, nothing picked yet -- is the
                // ordinary mid-edit state of the Day Editor, so it is skipped in silence.
                // Only a NAMED id that does not resolve is worth a sentence.
                if (string.IsNullOrEmpty(entry.itemId)) continue;

                var item = catalog == null ? null : catalog.GetById(entry.itemId);
                if (item == null)
                {
                    Debug.LogError(
                        $"Day file '{fileName}': item intro names unknown itemId '{entry.itemId}'. " +
                        "Skipping this introduction; the rest of the Day is unaffected.");
                    continue;
                }

                intros.Add(new ResolvedItemIntro(item, entry.nameOverride, entry.message));
            }

            // An enabled block that resolved nothing is treated as no introductions rather
            // than as an empty list, which is what lets every reader take "there is a list"
            // as "there is at least one popup to show".
            return intros.Count > 0 ? intros : null;
        }

        // Unlike every other Resolve* here, a bad block is NOT fatal to the Day: null means
        // "this Day has no forced first move", which is the correct outcome both for the 39
        // Days that never authored one and for a Day whose tutorial is unusable. Dropping
        // the whole Day over a broken tutorial would take a playable day off the calendar to
        // punish a decoration -- and the two ways it can be broken are already caught where
        // they can be acted on: DayValidator errors at authoring time, and TutorialDirector
        // refuses to start when the source cell turns out to be empty at Day Start.
        //
        // The absence check is `enabled`, not a coordinate: see TutorialJson's own comment
        // for why a zeroed block cannot be told apart from a real (0,0)/tray-0 tutorial.
        // Public because the Day Editor resolves the same block for its validation preview
        // (DayEditorModel.ToDayDefinition). One rule, one implementation: a second copy of
        // "enabled means present" would be free to disagree with this one about what a Day
        // file means, and the disagreement would only show up as a Day that validates in
        // the editor and behaves differently at runtime.
        // The two values TutorialStepJson.kind may carry, spelled once each. Const strings
        // rather than nameof() on the enum members, because what the Day files say is the
        // thing being pinned, not a C# identifier that happens to match it today -- renaming
        // ResolvedTutorialStep's enum must not silently invalidate every authored Day.
        private const string ForcedMoveKind = "ForcedMove";
        private const string TrayMoveKind = "TrayMove";

        public static ResolvedTutorial ResolveTutorial(TutorialJson tutorial)
        {
            if (tutorial == null || !tutorial.enabled) return null;

            var steps = new List<ResolvedTutorialStep>();
            foreach (var step in tutorial.steps ?? Array.Empty<TutorialStepJson>())
            {
                if (step == null) continue;

                // An unreadable kind is NOT silently downgraded to the default: a typo would
                // otherwise turn whatever was meant into a forced move at cell (0,0), which
                // is a step the player can never complete. Same stance ResolveBoardDistribution
                // takes on its own enum string, and the same reason the field is a string in
                // the first place.
                //
                // "ForcedMove" (or empty, which has always meant it) and "TrayMove" (D-165)
                // are the accepted values, and this is the ONE place that says so. A Day file
                // still carrying the removed "PowerupIntro" lands here and loses its tutorial
                // with a sentence naming the value, which is the loud failure that removing
                // the field entirely would have thrown away -- JsonUtility ignores keys it
                // has no field for, so the step would have survived as a forced move on cell
                // (0,0).
                var isTrayMove = step.kind == TrayMoveKind;
                if (!isTrayMove && !string.IsNullOrEmpty(step.kind) && step.kind != ForcedMoveKind)
                {
                    Debug.LogError(
                        $"Tutorial step has kind '{step.kind}', and a Day may only author '{ForcedMoveKind}' or " +
                        $"'{TrayMoveKind}' steps. The powerup tutorial moved to PowerupConfig, where each powerup " +
                        "names the Day that introduces it. Dropping this Day's tutorial.");
                    return null;
                }

                // The kind picks the source fields, and nothing else can: an unset int is 0,
                // which is a real cell AND a real tray, so a step built from the wrong pair
                // would be a plausible-looking step the player can never finish.
                steps.Add(isTrayMove
                    ? ResolvedTutorialStep.TrayMove(
                        step.sourceTraySlotIndex, step.targetTraySlotIndex, step.message)
                    : new ResolvedTutorialStep(
                        step.sourceX, step.sourceY, step.targetTraySlotIndex, step.message, step.highlightModification));
            }

            // An enabled tutorial with no steps is treated as no tutorial rather than as an
            // empty one, which is what lets every reader take "a ResolvedTutorial exists" as
            // "there is at least one move to make".
            return steps.Count > 0 ? new ResolvedTutorial(steps) : null;
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
