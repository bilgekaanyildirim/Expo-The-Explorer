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
            Assert.IsNull(day.RetryVariant);

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
        }

        [Test]
        public void ParseAll_EditorMetaFieldsIgnored_DoesNotAffectResolution()
        {
            var runtimeA = BuildMinimalRuntime(dayIndex: 1);
            var runtimeB = BuildMinimalRuntime(dayIndex: 1);

            var dayJsonA = new DayJson { runtime = runtimeA, editorMeta = new DayEditorMetaJson { hasTicketGenerationOverride = true, sideInclusionChanceOverride = 0.9f } };
            var dayJsonB = new DayJson { runtime = runtimeB, editorMeta = new DayEditorMetaJson { hasTicketGenerationOverride = false, sideInclusionChanceOverride = 0.1f } };

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

        [Test]
        public void ParseAll_RetryVariant_ResolvesRecursively()
        {
            var parentRuntime = BuildMinimalRuntime(dayIndex: 1);
            parentRuntime.hasRetryVariant = true;
            parentRuntime.retryVariant = new DayJson { runtime = BuildMinimalRuntime(dayIndex: 1, ticketsRequiredForDay: 2) };

            var result = ParseSingle(new DayJson { runtime = parentRuntime }, "day_01");

            Assert.AreEqual(1, result.Count);
            Assert.IsNotNull(result[0].RetryVariant);
            Assert.AreEqual(2, result[0].RetryVariant.TicketsRequiredForDay);
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
                ticketSequence = new[] { new TicketEntryJson { mainItemId = "burger", patienceType = "Normal" } }
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
