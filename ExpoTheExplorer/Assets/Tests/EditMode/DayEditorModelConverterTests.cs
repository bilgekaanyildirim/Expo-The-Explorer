using System;
using System.Collections.Generic;
using ExpoTheExplorer.Core;
using ExpoTheExplorer.Data;
using ExpoTheExplorer.Editor;
using ExpoTheExplorer.Systems.DaySystem;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace ExpoTheExplorer.Tests.EditMode
{
    public class DayEditorModelConverterTests
    {
        private readonly List<UnityEngine.Object> spawned = new();

        private FoodCatalog catalog;
        private FoodItemConfig main;
        private FoodItemConfig side;
        private FoodItemConfig drink;
        private ModificationConfig modification;

        [SetUp]
        public void SetUp()
        {
            main = CreateFoodItem("main");
            side = CreateFoodItem("side");
            drink = CreateFoodItem("drink");
            modification = CreateModification("no_pickles");

            catalog = ScriptableObject.CreateInstance<FoodCatalog>();
            spawned.Add(catalog);
            SetModificationsList(main, modification);
            SetItemsList(catalog, main, side, drink);
        }

        [TearDown]
        public void TearDown()
        {
            foreach (var o in spawned) UnityEngine.Object.DestroyImmediate(o);
            spawned.Clear();
        }

        private DayJson CreateFullDayJson()
        {
            var ticketEntry = new TicketEntryJson
            {
                mainItemId = main.Id,
                sideItemId = side.Id,
                drinkItemId = drink.Id,
                modifications = new[] { new ModificationEntryJson { modificationId = modification.Id, isAddition = true } },
                patienceType = "Impatient",
                customerNameOverride = "Bob",
                timeLimitSecondsOverride = 42f,
            };

            var boardSpawnEntry = new BoardSpawnEntryJson
            {
                triggerStepIndex = 3,
                itemId = main.Id,
                modifications = new[] { new ModificationEntryJson { modificationId = modification.Id, isAddition = false } },
                useExactCell = true,
                x = 1,
                y = 2,
            };

            var retryVariant = new DayJson
            {
                runtime = new DayRuntimeJson
                {
                    dayIndex = 0,
                    ticketsRequiredForDay = 1,
                    ticketSequence = new[] { ticketEntry },
                    boardTimeline = new[] { boardSpawnEntry },
                    hasRetryVariant = false,
                    retryVariant = null,
                },
                editorMeta = new DayEditorMetaJson(),
            };

            return new DayJson
            {
                runtime = new DayRuntimeJson
                {
                    dayIndex = 5,
                    ticketsRequiredForDay = 1,
                    ticketSequence = new[] { ticketEntry },
                    boardTimeline = new[] { boardSpawnEntry },
                    hasRetryVariant = true,
                    retryVariant = retryVariant,
                },
                editorMeta = new DayEditorMetaJson
                {
                    hasTicketGenerationOverride = true,
                    sideInclusionChanceOverride = 0.25f,
                    drinkInclusionChanceOverride = 0.75f,
                    modificationCountLambdaOverride = 2f,
                    hasBoardDistributionOverride = true,
                    noiseLeakCountLambdaOverride = 0.1f,
                    guaranteedTicketCountOverride = 2,
                    leakDepthOverride = 4,
                    maxLeakCountOverride = 6,
                },
            };
        }

        [Test]
        public void FromDayJson_ThenToDayJson_RoundTripsAllFields()
        {
            var original = CreateFullDayJson();

            var model = DayEditorModel.FromDayJson(original, catalog);
            var roundTripped = model.ToDayJson();

            Assert.AreEqual(JsonUtility.ToJson(original), JsonUtility.ToJson(roundTripped));
        }

        [Test]
        public void ToDayDefinition_FeedsDayValidatorWithoutError()
        {
            var gameConfig = CreateGameConfig();
            var ticketConfig = CreateTicketGenerationConfig(upcomingQueueSize: 3);

            var model = new DayEditorModel
            {
                DayIndex = 0,
                TicketsRequiredForDay = 3,
            };
            for (var step = 0; step < 3; step++)
            {
                model.TicketSequence.Add(new DayEditorTicketEntry { MainItem = main });
                model.BoardTimeline.Add(new DayEditorBoardSpawnEntry { TriggerStepIndex = step, Item = main, UseExactCell = true, X = step % 6, Y = step / 6 });
            }

            var result = DayValidator.Validate(model.ToDayDefinition(), gameConfig, ticketConfig);

            CollectionAssert.IsEmpty(result.Errors);
        }

        [Test]
        public void Clone_ProducesIndependentCopy_MutatingCloneDoesNotAffectOriginal()
        {
            var original = DayEditorModel.FromDayJson(CreateFullDayJson(), catalog);

            var clone = original.Clone(catalog);
            clone.DayIndex = 999;
            clone.TicketSequence.Add(new DayEditorTicketEntry { MainItem = side });

            Assert.AreEqual(5, original.DayIndex);
            Assert.AreEqual(1, original.TicketSequence.Count);
        }

        [Test]
        public void FromDayJson_UnresolvableId_LeavesFieldNullWithoutDroppingTheDay()
        {
            var json = CreateFullDayJson();
            json.runtime.ticketSequence[0].mainItemId = "does_not_exist";

            var model = DayEditorModel.FromDayJson(json, catalog);

            Assert.AreEqual(1, model.TicketSequence.Count);
            Assert.IsNull(model.TicketSequence[0].MainItem);
            Assert.AreSame(side, model.TicketSequence[0].SideItem);
        }

        private FoodItemConfig CreateFoodItem(string id)
        {
            var item = ScriptableObject.CreateInstance<FoodItemConfig>();
            spawned.Add(item);

            var serialized = new SerializedObject(item);
            serialized.FindProperty("id").stringValue = id;
            serialized.ApplyModifiedPropertiesWithoutUndo();

            return item;
        }

        private ModificationConfig CreateModification(string id)
        {
            var mod = ScriptableObject.CreateInstance<ModificationConfig>();
            spawned.Add(mod);

            var serialized = new SerializedObject(mod);
            serialized.FindProperty("id").stringValue = id;
            serialized.ApplyModifiedPropertiesWithoutUndo();

            return mod;
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

        private GameConfig CreateGameConfig()
        {
            var config = ScriptableObject.CreateInstance<GameConfig>();
            spawned.Add(config);
            return config;
        }

        private TicketGenerationConfig CreateTicketGenerationConfig(int upcomingQueueSize)
        {
            var config = ScriptableObject.CreateInstance<TicketGenerationConfig>();
            spawned.Add(config);

            var serialized = new SerializedObject(config);
            serialized.FindProperty("upcomingQueueSize").intValue = upcomingQueueSize;
            serialized.ApplyModifiedPropertiesWithoutUndo();

            return config;
        }
    }
}
