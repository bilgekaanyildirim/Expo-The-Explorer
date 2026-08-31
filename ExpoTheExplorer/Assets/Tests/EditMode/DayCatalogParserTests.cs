using System.Collections.Generic;
using ExpoTheExplorer.Core;
using ExpoTheExplorer.Data;
using ExpoTheExplorer.Systems.DaySystem;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;

namespace ExpoTheExplorer.Tests.EditMode
{
    public class DayCatalogParserTests
    {
        private FoodCatalog catalog;
        private FoodItemConfig burger;
        private FoodItemConfig fries;
        private FoodItemConfig cola;
        private ModificationConfig noPickles;

        [SetUp]
        public void SetUp()
        {
            burger = CreateFoodItem("burger");
            fries = CreateFoodItem("fries");
            cola = CreateFoodItem("cola");
            noPickles = CreateModification("no_pickles");
            SetModificationsList(burger, noPickles);

            catalog = ScriptableObject.CreateInstance<FoodCatalog>();
            SetItemsList(catalog, burger, fries, cola);
        }

        [TearDown]
        public void TearDown()
        {
            Object.DestroyImmediate(burger);
            Object.DestroyImmediate(fries);
            Object.DestroyImmediate(cola);
            Object.DestroyImmediate(noPickles);
            Object.DestroyImmediate(catalog);
        }

        [Test]
        public void ParseAll_ValidSingleDay_ResolvesAllFields()
        {
            var dayJson = new DayJson
            {
                runtime = new DayRuntimeJson
                {
                    dayIndex = 1,
                    ticketsRequiredForDay = 1,
                    boardDistribution = ValidBoardDistribution(),
                    ticketRuntime = ValidTicketRuntime(),
                    ticketSequence = new[]
                    {
                        new TicketEntryJson
                        {
                            mainItemId = "burger",
                            sideItemId = "fries",
                            drinkItemId = "cola",
                            modifications = new[] { new ModificationEntryJson { modificationId = "no_pickles", isAddition = false } },
                            patienceType = "Normal",
                            customerNameOverride = "Ada",
                            timeLimitSecondsOverride = 42f
                        }
                    },
                    boardTimeline = new[]
                    {
                        new BoardSpawnEntryJson
                        {
                            triggerStepIndex = -1,
                            itemId = "burger",
                            useExactCell = true,
                            x = 2,
                            y = 3
                        }
                    }
                },
                editorMeta = new DayEditorMetaJson()
            };

            var result = ParseSingle(dayJson, "day_01");

            Assert.AreEqual(1, result.Count);
            var day = result[0];
            Assert.AreEqual(1, day.DayIndex);
            Assert.AreEqual(1, day.TicketsRequiredForDay);

            Assert.AreEqual(1, day.TicketSequence.Count);
            var entry = day.TicketSequence[0];
            Assert.AreSame(burger, entry.MainItem);
            Assert.AreSame(fries, entry.SideItem);
            Assert.AreSame(cola, entry.DrinkItem);
            Assert.AreEqual(1, entry.Modifications.Count);
            Assert.AreSame(noPickles, entry.Modifications[0].Config);
            Assert.IsFalse(entry.Modifications[0].IsAddition);
            Assert.AreEqual(PatienceType.Normal, entry.PatienceType);
            Assert.AreEqual("Ada", entry.CustomerNameOverride);
            Assert.AreEqual(42f, entry.TimeLimitSecondsOverride);

            Assert.AreEqual(1, day.BoardTimeline.Count);
            var spawn = day.BoardTimeline[0];
            Assert.AreEqual(-1, spawn.TriggerStepIndex);
            Assert.AreSame(burger, spawn.Item);
            Assert.IsTrue(spawn.UseExactCell);
            Assert.AreEqual(2, spawn.X);
            Assert.AreEqual(3, spawn.Y);

            var distribution = day.BoardDistribution;
            Assert.IsNotNull(distribution);
            Assert.AreEqual(0.5f, distribution.NoiseLeakCountLambda);
            Assert.AreEqual(GuaranteedTicketCountMode.Manual, distribution.GuaranteedTicketCountMode);
            Assert.AreEqual(1, distribution.GuaranteedTicketCount);
            Assert.AreEqual(1f, distribution.GuaranteedTicketCountLambda);
            Assert.AreEqual(0.5f, distribution.EarlyTicketWeightDecay);
            Assert.AreEqual(10f, distribution.UrgentTimeThresholdSeconds);
            Assert.AreEqual(10, distribution.LeakDepth);
            Assert.AreEqual(10, distribution.MaxLeakCount);

            var ticketRuntime = day.TicketRuntime;
            Assert.IsNotNull(ticketRuntime);
            Assert.AreEqual(45f, ticketRuntime.ImpatientTimeLimitSeconds);
            Assert.AreEqual(90f, ticketRuntime.NormalTimeLimitSeconds);
            Assert.AreEqual(150f, ticketRuntime.PatientTimeLimitSeconds);
            Assert.AreEqual(10, ticketRuntime.UpcomingQueueSize);
        }

        // Same absence-by-content problem as boardDistribution: JsonUtility hands back a
        // zeroed block rather than null. A 0-second limit would time the ticket out on
        // arrival, so it is rejected instead of quietly played.
        [Test]
        public void ParseAll_MissingTicketRuntimeBlock_LogsErrorAndSkipsDay()
        {
            var runtime = BuildMinimalRuntime(dayIndex: 1);
            runtime.ticketRuntime = null;

            LogAssert.Expect(LogType.Error, "Day file 'day_01': runtime.ticketRuntime block is missing or incomplete -- impatient/normal/patient time limits must all be greater than 0.");
            var result = ParseSingle(new DayJson { runtime = runtime }, "day_01");

            Assert.AreEqual(0, result.Count);
        }

        [Test]
        public void ParseAll_TicketRuntimeWithZeroTimeLimit_LogsErrorAndSkipsDay()
        {
            var runtime = BuildMinimalRuntime(dayIndex: 1);
            runtime.ticketRuntime.normalTimeLimitSeconds = 0f;

            LogAssert.Expect(LogType.Error, "Day file 'day_01': runtime.ticketRuntime block is missing or incomplete -- impatient/normal/patient time limits must all be greater than 0.");
            var result = ParseSingle(new DayJson { runtime = runtime }, "day_01");

            Assert.AreEqual(0, result.Count);
        }

        [Test]
        public void ParseAll_TicketRuntimeWithZeroQueueSize_LogsErrorAndSkipsDay()
        {
            var runtime = BuildMinimalRuntime(dayIndex: 1);
            runtime.ticketRuntime.upcomingQueueSize = 0;

            LogAssert.Expect(LogType.Error, "Day file 'day_01': runtime.ticketRuntime.upcomingQueueSize (0) must be at least 1.");
            var result = ParseSingle(new DayJson { runtime = runtime }, "day_01");

            Assert.AreEqual(0, result.Count);
        }

        // The point of moving these per Day: two Days must be able to disagree.
        [Test]
        public void ParseAll_TwoDaysWithDifferentTimeLimits_EachKeepsItsOwn()
        {
            var strict = BuildMinimalRuntime(dayIndex: 1);
            strict.ticketRuntime.normalTimeLimitSeconds = 30f;
            var lenient = BuildMinimalRuntime(dayIndex: 2);
            lenient.ticketRuntime.normalTimeLimitSeconds = 120f;

            var parsed = DayCatalogParser.ParseAll(
                new[] { ToFile(new DayJson { runtime = strict }, "day_01"), ToFile(new DayJson { runtime = lenient }, "day_02") },
                catalog);

            Assert.AreEqual(2, parsed.Count);
            Assert.AreEqual(30f, parsed[0].TicketRuntime.NormalTimeLimitSeconds);
            Assert.AreEqual(120f, parsed[1].TicketRuntime.NormalTimeLimitSeconds);
        }

        [Test]
        public void ParseAll_PoissonGuaranteedTicketCountMode_ParsesFromTheStringForm()
        {
            var runtime = BuildMinimalRuntime(dayIndex: 1);
            runtime.boardDistribution.guaranteedTicketCountMode = "Poisson";

            var result = ParseSingle(new DayJson { runtime = runtime }, "day_01");

            Assert.AreEqual(1, result.Count);
            Assert.AreEqual(GuaranteedTicketCountMode.Poisson, result[0].BoardDistribution.GuaranteedTicketCountMode);
        }

        // A Day file written before the block existed deserializes to a zeroed
        // BoardDistributionJson, not to null -- JsonUtility cannot represent a null nested
        // class. Silently accepting it would switch off the urgent-ticket guarantee and
        // flatten the arrival-weighted lottery, so the Day is dropped instead.
        [Test]
        public void ParseAll_MissingBoardDistributionBlock_LogsErrorAndSkipsDay()
        {
            var runtime = BuildMinimalRuntime(dayIndex: 1);
            runtime.boardDistribution = null;

            LogAssert.Expect(LogType.Error, "Day file 'day_01': runtime.boardDistribution block is missing or has no guaranteedTicketCountMode.");
            var result = ParseSingle(new DayJson { runtime = runtime }, "day_01");

            Assert.AreEqual(0, result.Count);
        }

        [Test]
        public void ParseAll_InvalidGuaranteedTicketCountMode_LogsErrorAndSkipsDay()
        {
            var runtime = BuildMinimalRuntime(dayIndex: 1);
            runtime.boardDistribution.guaranteedTicketCountMode = "Bogus";

            LogAssert.Expect(LogType.Error, "Day file 'day_01': invalid guaranteedTicketCountMode 'Bogus'.");
            var result = ParseSingle(new DayJson { runtime = runtime }, "day_01");

            Assert.AreEqual(0, result.Count);
        }

        [Test]
        public void ParseAll_BoardDistributionWithZeroedCounts_LogsErrorAndSkipsDay()
        {
            var runtime = BuildMinimalRuntime(dayIndex: 1);
            runtime.boardDistribution.guaranteedTicketCount = 0;
            runtime.boardDistribution.leakDepth = 0;
            runtime.boardDistribution.maxLeakCount = 0;

            LogAssert.Expect(LogType.Error, "Day file 'day_01': runtime.boardDistribution is incomplete -- guaranteedTicketCount (0), leakDepth (0) and maxLeakCount (0) must all be at least 1.");
            var result = ParseSingle(new DayJson { runtime = runtime }, "day_01");

            Assert.AreEqual(0, result.Count);
        }

        [Test]
        public void ParseAll_EditorMetaFieldsIgnored_DoesNotAffectResolution()
        {
            var runtimeA = BuildMinimalRuntime(dayIndex: 1);
            var runtimeB = BuildMinimalRuntime(dayIndex: 1);

            var dayJsonA = new DayJson { runtime = runtimeA, editorMeta = new DayEditorMetaJson { ticketGeneration = new TicketGenerationJson { sideInclusionChance = 0.9f } } };
            var dayJsonB = new DayJson { runtime = runtimeB, editorMeta = new DayEditorMetaJson { ticketGeneration = new TicketGenerationJson { sideInclusionChance = 0.1f } } };

            var resultA = ParseSingle(dayJsonA, "day_a");
            var resultB = ParseSingle(dayJsonB, "day_b");

            Assert.AreEqual(1, resultA.Count);
            Assert.AreEqual(1, resultB.Count);
            Assert.AreEqual(resultA[0].TicketsRequiredForDay, resultB[0].TicketsRequiredForDay);
            Assert.AreSame(resultA[0].TicketSequence[0].MainItem, resultB[0].TicketSequence[0].MainItem);
        }

        [Test]
        public void ParseAll_UnknownMainItemId_LogsErrorAndSkipsDay()
        {
            var dayJson = new DayJson
            {
                runtime = new DayRuntimeJson
                {
                    dayIndex = 1,
                    ticketsRequiredForDay = 1,
                    ticketSequence = new[] { new TicketEntryJson { mainItemId = "does_not_exist", patienceType = "Normal" } }
                }
            };

            LogAssert.Expect(LogType.Error, "Day file 'day_01': unknown mainItemId 'does_not_exist'.");
            var result = ParseSingle(dayJson, "day_01");

            Assert.AreEqual(0, result.Count);
        }

        [Test]
        public void ParseAll_UnknownModificationId_LogsErrorAndSkipsDay()
        {
            var dayJson = new DayJson
            {
                runtime = new DayRuntimeJson
                {
                    dayIndex = 1,
                    ticketsRequiredForDay = 1,
                    ticketSequence = new[]
                    {
                        new TicketEntryJson
                        {
                            mainItemId = "burger",
                            modifications = new[] { new ModificationEntryJson { modificationId = "does_not_exist", isAddition = true } },
                            patienceType = "Normal"
                        }
                    }
                }
            };

            LogAssert.Expect(LogType.Error, "Day file 'day_01': unknown modificationId 'does_not_exist'.");
            var result = ParseSingle(dayJson, "day_01");

            Assert.AreEqual(0, result.Count);
        }

        [Test]
        public void ParseAll_InvalidPatienceTypeString_LogsErrorAndSkipsDay()
        {
            var dayJson = new DayJson
            {
                runtime = new DayRuntimeJson
                {
                    dayIndex = 1,
                    ticketsRequiredForDay = 1,
                    ticketSequence = new[] { new TicketEntryJson { mainItemId = "burger", patienceType = "Bogus" } }
                }
            };

            LogAssert.Expect(LogType.Error, "Day file 'day_01': invalid patienceType 'Bogus'.");
            var result = ParseSingle(dayJson, "day_01");

            Assert.AreEqual(0, result.Count);
        }

        [Test]
        public void ParseAll_DuplicateDayIndex_LogsErrorButKeepsBothDays()
        {
            var files = new[]
            {
                ToFile(new DayJson { runtime = BuildMinimalRuntime(dayIndex: 1) }, "day_01"),
                ToFile(new DayJson { runtime = BuildMinimalRuntime(dayIndex: 1) }, "day_02")
            };

            LogAssert.Expect(LogType.Error, "Day file 'day_02' has dayIndex 1, already used by another Day file.");
            var result = DayCatalogParser.ParseAll(files, catalog);

            Assert.AreEqual(2, result.Count);
        }

        [Test]
        public void ParseAll_SortsByDayIndexAscending_RegardlessOfInputOrder()
        {
            var files = new[]
            {
                ToFile(new DayJson { runtime = BuildMinimalRuntime(dayIndex: 5) }, "day_05"),
                ToFile(new DayJson { runtime = BuildMinimalRuntime(dayIndex: 2) }, "day_02")
            };

            var result = DayCatalogParser.ParseAll(files, catalog);

            Assert.AreEqual(2, result.Count);
            Assert.AreEqual(2, result[0].DayIndex);
            Assert.AreEqual(5, result[1].DayIndex);
        }

        // --- item introductions (D-142) -------------------------------------------------
        //
        // The block's contract in four tests: absence is the FLAG, not the content; the
        // name falls back through override -> DisplayName -> id; a bad entry costs its own
        // popup and nothing else; and an enabled block that resolved nothing is the same as
        // no block, so no reader has to handle an empty list.

        [Test]
        public void ParseAll_ItemIntroDisabled_ResolvesToNoIntros()
        {
            var runtime = BuildMinimalRuntime(dayIndex: 1);

            // Entries present and the flag off: the authored rows survive in the file (that
            // is what makes unticking the Day Editor's box non-destructive) and the runtime
            // shows none of them.
            runtime.itemIntro = new ItemIntroJson
            {
                enabled = false,
                items = new[] { new ItemIntroEntryJson { itemId = "burger" } }
            };

            var result = ParseSingle(new DayJson { runtime = runtime }, "day_01");

            Assert.AreEqual(1, result.Count);
            Assert.IsNull(result[0].ItemIntros);
        }

        [Test]
        public void ParseAll_ItemIntroEnabled_ResolvesItemAndWords()
        {
            var runtime = BuildMinimalRuntime(dayIndex: 1);
            runtime.itemIntro = new ItemIntroJson
            {
                enabled = true,
                items = new[]
                {
                    new ItemIntroEntryJson { itemId = "burger", nameOverride = "Bacon Deluxe", message = "Now serving!" },
                    new ItemIntroEntryJson { itemId = "cola" }
                }
            };

            var result = ParseSingle(new DayJson { runtime = runtime }, "day_01");

            var intros = result[0].ItemIntros;
            Assert.AreEqual(2, intros.Count);

            Assert.AreSame(burger, intros[0].Item);
            Assert.AreEqual("Bacon Deluxe", intros[0].DisplayName);
            Assert.AreEqual("Now serving!", intros[0].Message);

            // No override and no authored DisplayName on the test item, so the name falls
            // all the way through to the id -- a popup is never blank.
            Assert.AreSame(cola, intros[1].Item);
            Assert.AreEqual("cola", intros[1].DisplayName);
            Assert.AreEqual(string.Empty, intros[1].Message);
        }

        [Test]
        public void ParseAll_ItemIntroWithUnknownId_DropsThatEntryAndKeepsTheDay()
        {
            var runtime = BuildMinimalRuntime(dayIndex: 1);
            runtime.itemIntro = new ItemIntroJson
            {
                enabled = true,
                items = new[]
                {
                    new ItemIntroEntryJson { itemId = "ghost_item" },
                    new ItemIntroEntryJson { itemId = "burger" }
                }
            };

            LogAssert.Expect(LogType.Error, new System.Text.RegularExpressions.Regex("ghost_item"));

            var result = ParseSingle(new DayJson { runtime = runtime }, "day_01");

            // The Day SURVIVES, unlike a bad ticket or board id: an introduction is a
            // greeting, and dropping the day would punish the content for the decoration.
            Assert.AreEqual(1, result.Count);
            Assert.AreEqual(1, result[0].ItemIntros.Count);
            Assert.AreSame(burger, result[0].ItemIntros[0].Item);
        }

        [Test]
        public void ParseAll_ItemIntroNamingAModification_ResolvesIconAndDirection()
        {
            var runtime = BuildMinimalRuntime(dayIndex: 1);
            runtime.itemIntro = new ItemIntroJson
            {
                enabled = true,
                items = new[]
                {
                    new ItemIntroEntryJson { modificationId = "no_pickles", isAddition = false, message = "Hold the pickles." }
                }
            };

            var result = ParseSingle(new DayJson { runtime = runtime }, "day_01");

            var intro = result[0].ItemIntros[0];
            Assert.IsNull(intro.Item);
            Assert.AreSame(noPickles, intro.Modification);

            // The direction survives as a VALUE, not as a flag beside a bool: false here means
            // "a removal", and null would have meant "not a modification at all".
            Assert.AreEqual(false, intro.ModificationIsAddition);
            Assert.AreEqual("no_pickles", intro.DisplayName);
            Assert.AreEqual("Hold the pickles.", intro.Message);
        }

        [Test]
        public void ParseAll_ItemIntroNamingBothIds_ReadsTheModification()
        {
            var runtime = BuildMinimalRuntime(dayIndex: 1);

            // Not reachable through the Day Editor, which writes exactly one id -- this is the
            // hand-edited Day file. It resolves to the more specific of the two things named
            // rather than being dropped, and DayValidator is what says so out loud.
            runtime.itemIntro = new ItemIntroJson
            {
                enabled = true,
                items = new[]
                {
                    new ItemIntroEntryJson { itemId = "burger", modificationId = "no_pickles", isAddition = true }
                }
            };

            var result = ParseSingle(new DayJson { runtime = runtime }, "day_01");

            var intro = result[0].ItemIntros[0];
            Assert.IsNull(intro.Item);
            Assert.AreSame(noPickles, intro.Modification);
            Assert.AreEqual(true, intro.ModificationIsAddition);
        }

        [Test]
        public void ParseAll_ItemIntroWithUnknownModificationId_DropsThatEntryAndKeepsTheDay()
        {
            var runtime = BuildMinimalRuntime(dayIndex: 1);
            runtime.itemIntro = new ItemIntroJson
            {
                enabled = true,
                items = new[]
                {
                    new ItemIntroEntryJson { modificationId = "ghost_mod" },
                    new ItemIntroEntryJson { itemId = "burger" }
                }
            };

            LogAssert.Expect(LogType.Error, new System.Text.RegularExpressions.Regex("ghost_mod"));

            var result = ParseSingle(new DayJson { runtime = runtime }, "day_01");

            Assert.AreEqual(1, result.Count);
            Assert.AreEqual(1, result[0].ItemIntros.Count);
            Assert.AreSame(burger, result[0].ItemIntros[0].Item);

            // A food introduction carries NO direction, which is what keeps a "-" badge off a
            // burger: the popup asks HasValue, not the bool.
            Assert.IsFalse(result[0].ItemIntros[0].ModificationIsAddition.HasValue);
        }

        [Test]
        public void ParseAll_ItemIntroEnabledButEmpty_ResolvesToNoIntros()
        {
            var runtime = BuildMinimalRuntime(dayIndex: 1);

            // A half-authored row -- the box ticked, the food not yet picked -- is the Day
            // Editor's ordinary mid-edit state, so it is skipped in silence rather than
            // logged, and an enabled block that resolved nothing reads as no block at all.
            runtime.itemIntro = new ItemIntroJson
            {
                enabled = true,
                items = new[] { new ItemIntroEntryJson { itemId = string.Empty } }
            };

            var result = ParseSingle(new DayJson { runtime = runtime }, "day_01");

            Assert.IsNull(result[0].ItemIntros);
        }

        private List<DayDefinition> ParseSingle(DayJson dayJson, string fileName)
        {
            return DayCatalogParser.ParseAll(new[] { ToFile(dayJson, fileName) }, catalog);
        }

        private static DayJsonFile ToFile(DayJson dayJson, string fileName)
        {
            return new DayJsonFile(fileName, JsonUtility.ToJson(dayJson));
        }

        private static DayRuntimeJson BuildMinimalRuntime(int dayIndex, int ticketsRequiredForDay = 1)
        {
            return new DayRuntimeJson
            {
                dayIndex = dayIndex,
                ticketsRequiredForDay = ticketsRequiredForDay,
                boardDistribution = ValidBoardDistribution(),
                ticketRuntime = ValidTicketRuntime(),
                ticketSequence = new[] { new TicketEntryJson { mainItemId = "burger", patienceType = "Normal" } }
            };
        }

        private static BoardDistributionJson ValidBoardDistribution()
        {
            return new BoardDistributionJson
            {
                noiseLeakCountLambda = 0.5f,
                guaranteedTicketCountMode = "Manual",
                guaranteedTicketCount = 1,
                guaranteedTicketCountLambda = 1f,
                earlyTicketWeightDecay = 0.5f,
                urgentTimeThresholdSeconds = 10f,
                leakDepth = 10,
                maxLeakCount = 10
            };
        }

        private static TicketRuntimeJson ValidTicketRuntime()
        {
            return new TicketRuntimeJson
            {
                impatientTimeLimitSeconds = 45f,
                normalTimeLimitSeconds = 90f,
                patientTimeLimitSeconds = 150f,
                upcomingQueueSize = 10
            };
        }

        private static FoodItemConfig CreateFoodItem(string id)
        {
            var item = ScriptableObject.CreateInstance<FoodItemConfig>();
            var serialized = new SerializedObject(item);
            serialized.FindProperty("id").stringValue = id;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            return item;
        }

        private static ModificationConfig CreateModification(string id)
        {
            var modification = ScriptableObject.CreateInstance<ModificationConfig>();
            var serialized = new SerializedObject(modification);
            serialized.FindProperty("id").stringValue = id;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            return modification;
        }

        private static void SetItemsList(FoodCatalog target, params FoodItemConfig[] values)
        {
            var serialized = new SerializedObject(target);
            var property = serialized.FindProperty("items");
            property.arraySize = values.Length;
            for (var i = 0; i < values.Length; i++)
            {
                property.GetArrayElementAtIndex(i).objectReferenceValue = values[i];
            }
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        private static void SetModificationsList(FoodItemConfig target, params ModificationConfig[] values)
        {
            var serialized = new SerializedObject(target);
            var property = serialized.FindProperty("availableModifications");
            property.arraySize = values.Length;
            for (var i = 0; i < values.Length; i++)
            {
                property.GetArrayElementAtIndex(i).objectReferenceValue = values[i];
            }
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }
    }
}
