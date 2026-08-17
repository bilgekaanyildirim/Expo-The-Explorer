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

            // Every value here is deliberately non-default: the round-trip test compares whole
            // JsonUtility output, so a field left at its default would still pass if the model
            // dropped it entirely. That is exactly how the star thresholds went unnoticed --
            // the model had no fields for them and every Save silently zeroed all three.
            return new DayJson
            {
                runtime = new DayRuntimeJson
                {
                    dayIndex = 5,
                    ticketsRequiredForDay = 1,
                    boardDistribution = new BoardDistributionJson
                    {
                        noiseLeakCountLambda = 0.1f,
                        guaranteedTicketCountMode = "Poisson",
                        guaranteedTicketCount = 2,
                        guaranteedTicketCountLambda = 1.5f,
                        earlyTicketWeightDecay = 0.25f,
                        urgentTimeThresholdSeconds = 7f,
                        leakDepth = 4,
                        maxLeakCount = 6,
                    },
                    ticketRuntime = new TicketRuntimeJson
                    {
                        impatientTimeLimitSeconds = 11f,
                        normalTimeLimitSeconds = 22f,
                        patientTimeLimitSeconds = 33f,
                        upcomingQueueSize = 7,
                    },
                    ticketSequence = new[] { ticketEntry },
                    boardTimeline = new[] { boardSpawnEntry },
                    star1Threshold = 100,
                    star2Threshold = 200,
                    star3Threshold = 300,
                },
                editorMeta = new DayEditorMetaJson
                {
                    allowedFoodItemIds = new[] { main.Id, drink.Id },
                    ticketGeneration = new TicketGenerationJson
                    {
                        sideInclusionChance = 0.25f,
                        drinkInclusionChance = 0.75f,
                        modificationCountLambda = 2f,
                        modificationAdditionChance = 0.8f,
                        mainDishWeights = new[]
                        {
                            new MainDishWeightJson { foodItemId = main.Id, weight = 3f, modificationCountLambda = 1.5f },
                        },
                    },
                },
            };
        }

        [Test]
        public void FromDayJson_ThenToDayJson_KeepsStarThresholdsAndBoardDistribution()
        {
            var model = DayEditorModel.FromDayJson(CreateFullDayJson(), catalog);

            var runtime = model.ToDayJson().runtime;

            Assert.AreEqual(100, runtime.star1Threshold);
            Assert.AreEqual(200, runtime.star2Threshold);
            Assert.AreEqual(300, runtime.star3Threshold);
            Assert.AreEqual("Poisson", runtime.boardDistribution.guaranteedTicketCountMode);
            Assert.AreEqual(7f, runtime.boardDistribution.urgentTimeThresholdSeconds);
            Assert.AreEqual(4, runtime.boardDistribution.leakDepth);
            Assert.AreEqual(11f, runtime.ticketRuntime.impatientTimeLimitSeconds);
            Assert.AreEqual(33f, runtime.ticketRuntime.patientTimeLimitSeconds);
            Assert.AreEqual(7, runtime.ticketRuntime.upcomingQueueSize);
        }

        // mainDishWeights is the only editorMeta field holding a food reference, so it is
        // the one that can silently lose its identity on a round trip (id -> object -> id).
        [Test]
        public void FromDayJson_ThenToDayJson_KeepsMainDishWeights()
        {
            var model = DayEditorModel.FromDayJson(CreateFullDayJson(), catalog);

            var generation = model.ToDayJson().editorMeta.ticketGeneration;

            Assert.AreEqual(0.8f, generation.modificationAdditionChance);
            Assert.AreEqual(1, generation.mainDishWeights.Length);
            Assert.AreEqual(main.Id, generation.mainDishWeights[0].foodItemId);
            Assert.AreEqual(3f, generation.mainDishWeights[0].weight);
            Assert.AreEqual(1.5f, generation.mainDishWeights[0].modificationCountLambda);
        }

        // An old Day file has no boardDistribution block at all. The editor must still open
        // it -- and hand back the designed defaults rather than CLR zeros, so re-saving it
        // produces a Day the runtime parser will accept.
        [Test]
        public void FromDayJson_NoBoardDistributionBlock_FallsBackToDesignedDefaults()
        {
            var json = CreateFullDayJson();
            json.runtime.boardDistribution = null;

            var model = DayEditorModel.FromDayJson(json, catalog);

            Assert.AreEqual(GuaranteedTicketCountMode.Manual, model.BoardDistribution.GuaranteedTicketCountMode);
            Assert.AreEqual(1, model.BoardDistribution.GuaranteedTicketCount);
            Assert.AreEqual(0.5f, model.BoardDistribution.EarlyTicketWeightDecay);
            Assert.AreEqual(10f, model.BoardDistribution.UrgentTimeThresholdSeconds);
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

            // null = "no catalog to resolve the selection against", which skips the
            // food-selection check; this test is about the DayDefinition conversion.
            var result = DayValidator.Validate(model.ToDayDefinition(), null);

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
    }
}
