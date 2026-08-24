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
                            new MainDishWeightJson { foodItemId = main.Id, weight = 3f, modificationCountLambda = 1.5f, maxModificationCount = 2 },
                        },
                        // Non-default and non-equal on purpose, like every other field here:
                        // three identical values would still pass if the model mixed two of
                        // them up on the way through.
                        patientTicketCount = 4,
                        normalTicketCount = 3,
                        impatientTicketCount = 2,
                    },
                },
            };
        }

        [Test]
        public void FromDayJson_ThenToDayJson_KeepsTheSettingsBlocks()
        {
            var model = DayEditorModel.FromDayJson(CreateFullDayJson(), catalog);

            var runtime = model.ToDayJson().runtime;

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
            Assert.AreEqual(2, generation.mainDishWeights[0].maxModificationCount);
        }

        // A Day authored before maxModificationCount existed carries no value for it. The
        // editor must not hand that 0 straight back on Save: the slider starts at 1, and a
        // saved 0 would be a cap the runtime normalizes away -- the file and the game would
        // disagree about the same dish.
        [Test]
        public void FromDayJson_MainDishWeightWithoutMaxModificationCount_SavesTheDefault()
        {
            var dayJson = CreateFullDayJson();
            dayJson.editorMeta.ticketGeneration.mainDishWeights[0].maxModificationCount = 0;

            var model = DayEditorModel.FromDayJson(dayJson, catalog);

            var generation = model.ToDayJson().editorMeta.ticketGeneration;
            Assert.AreEqual(MainDishWeight.DefaultMaxModificationCount, generation.mainDishWeights[0].maxModificationCount);
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

        // Replaces a DayValidator rule that could never fire: the validator sees the resolved
        // settings objects, whose constructors have already clamped these values, so it could
        // not tell a hand-edited 0 from a legitimate 1. Clamping on load is where the check
        // works -- without it the editor would read leakDepth 0 out of a file, write it
        // straight back on Save, and DayCatalogParser would drop the Day at runtime.
        [Test]
        public void FromDayJson_OutOfRangeCounts_AreClampedSoSaveCannotWriteThemBack()
        {
            var json = CreateFullDayJson();
            json.runtime.boardDistribution.guaranteedTicketCount = 0;
            json.runtime.boardDistribution.leakDepth = 0;
            json.runtime.boardDistribution.maxLeakCount = 99;
            json.runtime.ticketRuntime.upcomingQueueSize = 0;

            var runtime = DayEditorModel.FromDayJson(json, catalog).ToDayJson().runtime;

            Assert.AreEqual(1, runtime.boardDistribution.guaranteedTicketCount);
            Assert.AreEqual(1, runtime.boardDistribution.leakDepth);
            Assert.AreEqual(10, runtime.boardDistribution.maxLeakCount);
            Assert.AreEqual(1, runtime.ticketRuntime.upcomingQueueSize);
        }

        // Time limits are deliberately left alone, so a 0 stays visible to DayValidator and
        // blocks Save with a reason instead of being silently rounded up to something nobody
        // authored.
        [Test]
        public void FromDayJson_ZeroTimeLimit_IsNotSilentlyRepaired()
        {
            var json = CreateFullDayJson();
            json.runtime.ticketRuntime.normalTimeLimitSeconds = 0f;

            var model = DayEditorModel.FromDayJson(json, catalog);

            Assert.AreEqual(0f, model.TicketRuntime.NormalTimeLimitSeconds);
            Assert.IsFalse(DayValidator.Validate(model.ToDayDefinition(), null).IsValid);
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

        // The three Patience Mix counts are the newest editorMeta fields (D-055) and the ones
        // most likely to be dropped or transposed on the way through, being three ints of the
        // same type sitting next to each other.
        [Test]
        public void FromDayJson_ThenToDayJson_KeepsThePatienceMix()
        {
            var model = DayEditorModel.FromDayJson(CreateFullDayJson(), catalog);

            var generation = model.ToDayJson().editorMeta.ticketGeneration;

            Assert.AreEqual(4, generation.patientTicketCount);
            Assert.AreEqual(3, generation.normalTicketCount);
            Assert.AreEqual(2, generation.impatientTicketCount);
        }

        // A Day written before Patience Mix existed carries none of the three, which
        // JsonUtility reads as 0 -- and 0/0/0 has to keep meaning "unauthored" all the way
        // through the editor, or re-saving such a Day would write a mix nobody chose.
        [Test]
        public void FromDayJson_NoPatienceMix_StaysAllZero()
        {
            var json = CreateFullDayJson();
            json.editorMeta.ticketGeneration.patientTicketCount = 0;
            json.editorMeta.ticketGeneration.normalTicketCount = 0;
            json.editorMeta.ticketGeneration.impatientTicketCount = 0;

            var generation = DayEditorModel.FromDayJson(json, catalog).ToDayJson().editorMeta.ticketGeneration;

            Assert.AreEqual(0, generation.patientTicketCount);
            Assert.AreEqual(0, generation.normalTicketCount);
            Assert.AreEqual(0, generation.impatientTicketCount);
        }

        // The modification picker seeds direction from the modification's own type rather
        // than letting the designer pick (D-054), because TicketFactory.CreateModification
        // does exactly that and only ever rolls for a Both. These four pin the rule: the old
        // "+ Add Modification" button appended a blank entry with IsAddition left at false,
        // which authored "remove Extra Ketchup" for an AdditionOnly modification -- valid
        // data the generator could never produce.
        [Test]
        public void ForConfig_AdditionOnly_ComesInAsAnAddition()
        {
            var config = CreateModification("extra_ketchup", ModificationDirection.AdditionOnly);

            var authored = DayEditorModification.ForConfig(config);

            Assert.IsTrue(authored.IsAddition);
            Assert.IsFalse(DayEditorModification.CanFlipDirection(config), "its direction is intrinsic");
        }

        [Test]
        public void ForConfig_RemovalOnly_ComesInAsARemoval()
        {
            var config = CreateModification("no_lettuce", ModificationDirection.RemovalOnly);

            var authored = DayEditorModification.ForConfig(config);

            Assert.IsFalse(authored.IsAddition);
            Assert.IsFalse(DayEditorModification.CanFlipDirection(config), "its direction is intrinsic");
        }

        // Both is the only one the designer gets to decide, so it is the only one that may
        // be flipped -- and it still has to arrive at a defined value rather than whatever
        // the field defaults to.
        [Test]
        public void ForConfig_Both_ComesInAsAnAdditionAndCanBeFlipped()
        {
            var config = CreateModification("cheese", ModificationDirection.Both);

            var authored = DayEditorModification.ForConfig(config);

            Assert.IsTrue(authored.IsAddition);
            Assert.IsTrue(DayEditorModification.CanFlipDirection(config));
        }

        // A null never reaches the picker (it filters the food's list first), but ForConfig
        // must not throw for one either -- CanFlipDirection is what the tile asks before
        // offering the flip, and a missing asset has to answer "no" rather than crash a draw.
        [Test]
        public void CanFlipDirection_NullConfig_IsFalse()
        {
            Assert.IsFalse(DayEditorModification.CanFlipDirection(null));
            Assert.IsTrue(DayEditorModification.ForConfig(null).IsAddition);
        }

        // The Main Dish Weights list is DERIVED from Food Selection (D-052), so these five
        // pin the derivation itself: what gets added, in what order, what survives a re-sync
        // untouched, and what is dropped. The list used to be hand-authored, which is why
        // "keeps the authored numbers" is the one that matters most -- a sync that reset a
        // designer's weights every repaint would be worse than no sync at all.
        [Test]
        public void SyncMainDishWeights_AddsARowPerSelectedMain_InCatalogOrder()
        {
            var burger = CreateFoodItem("burger");
            var hotdog = CreateFoodItem("hotdog");
            var localCatalog = CreateCatalog(burger, hotdog);
            var meta = MetaWithSelection(hotdog, burger);

            meta.SyncMainDishWeights(localCatalog);

            var weights = meta.TicketGeneration.MainDishWeights;
            Assert.AreEqual(2, weights.Count);
            Assert.AreSame(burger, weights[0].Food, "catalog order, not the order they were picked in");
            Assert.AreSame(hotdog, weights[1].Food);
            Assert.AreEqual(MainDishWeight.DefaultWeight, weights[0].Weight);
            Assert.AreEqual(MainDishWeight.DefaultMaxModificationCount, weights[0].MaxModificationCount);
        }

        [Test]
        public void SyncMainDishWeights_KeepsTheAuthoredNumbersOfARowItAlreadyHad()
        {
            var burger = CreateFoodItem("burger");
            var hotdog = CreateFoodItem("hotdog");
            var localCatalog = CreateCatalog(burger, hotdog);
            var meta = MetaWithSelection(burger);
            meta.SyncMainDishWeights(localCatalog);

            var tuned = meta.TicketGeneration.MainDishWeights[0];
            tuned.Weight = 7f;
            tuned.ModificationCountLambda = 2.5f;
            tuned.MaxModificationCount = 3;

            // A second Main joins the Day: the burger's row must be the SAME object, not a
            // fresh one at defaults.
            meta.AllowedFoodItemIds.Add(hotdog.Id);
            meta.SyncMainDishWeights(localCatalog);

            var weights = meta.TicketGeneration.MainDishWeights;
            Assert.AreEqual(2, weights.Count);
            Assert.AreSame(tuned, weights[0]);
            Assert.AreEqual(7f, weights[0].Weight);
            Assert.AreEqual(2.5f, weights[0].ModificationCountLambda);
            Assert.AreEqual(3, weights[0].MaxModificationCount);
            Assert.AreEqual(MainDishWeight.DefaultWeight, weights[1].Weight);
        }

        [Test]
        public void SyncMainDishWeights_DropsTheRowOfADeselectedMain()
        {
            var burger = CreateFoodItem("burger");
            var hotdog = CreateFoodItem("hotdog");
            var localCatalog = CreateCatalog(burger, hotdog);
            var meta = MetaWithSelection(burger, hotdog);
            meta.SyncMainDishWeights(localCatalog);

            meta.AllowedFoodItemIds.Remove(burger.Id);
            meta.SyncMainDishWeights(localCatalog);

            var weights = meta.TicketGeneration.MainDishWeights;
            Assert.AreEqual(1, weights.Count);
            Assert.AreSame(hotdog, weights[0].Food);
        }

        // Only Mains get a weight -- a selected side or drink must not produce a row, and
        // neither must a Main the Day does not serve.
        [Test]
        public void SyncMainDishWeights_IgnoresSidesDrinksAndUnselectedMains()
        {
            var burger = CreateFoodItem("burger");
            var unpickedMain = CreateFoodItem("pizza");
            var fries = CreateFoodItem("fries", FoodCategory.Side);
            var cola = CreateFoodItem("cola", FoodCategory.Drink);
            var localCatalog = CreateCatalog(burger, unpickedMain, fries, cola);
            var meta = MetaWithSelection(burger, fries, cola);

            meta.SyncMainDishWeights(localCatalog);

            var weights = meta.TicketGeneration.MainDishWeights;
            Assert.AreEqual(1, weights.Count);
            Assert.AreSame(burger, weights[0].Food);
        }

        // The sync runs from a draw method, i.e. on every repaint. When the list already
        // matches it must not rebuild -- otherwise the inspector allocates a fresh list
        // several times a second, and anything holding on to the old one is left stale.
        [Test]
        public void SyncMainDishWeights_AlreadyInSync_DoesNotRebuildTheList()
        {
            var burger = CreateFoodItem("burger");
            var localCatalog = CreateCatalog(burger);
            var meta = MetaWithSelection(burger);
            meta.SyncMainDishWeights(localCatalog);
            var firstList = meta.TicketGeneration.MainDishWeights;

            meta.SyncMainDishWeights(localCatalog);

            Assert.AreSame(firstList, meta.TicketGeneration.MainDishWeights);
        }

        // The category argument matters more than it looks: FoodCategory.Main is 0, so the
        // three items the SetUp above builds are ALL Main-category. Harmless for the
        // round-trip tests (nothing there reads Category), but any test about the Main-dish
        // sync has to say what it means -- hence the explicit catalogs below.
        private FoodItemConfig CreateFoodItem(string id, FoodCategory category = FoodCategory.Main)
        {
            var item = ScriptableObject.CreateInstance<FoodItemConfig>();
            spawned.Add(item);

            var serialized = new SerializedObject(item);
            serialized.FindProperty("id").stringValue = id;
            serialized.FindProperty("category").enumValueIndex = (int)category;
            serialized.ApplyModifiedPropertiesWithoutUndo();

            return item;
        }

        private FoodCatalog CreateCatalog(params FoodItemConfig[] items)
        {
            var created = ScriptableObject.CreateInstance<FoodCatalog>();
            spawned.Add(created);
            SetItemsList(created, items);
            return created;
        }

        private static DayEditorMetaModel MetaWithSelection(params FoodItemConfig[] selected)
        {
            var meta = new DayEditorMetaModel();
            foreach (var item in selected) meta.AllowedFoodItemIds.Add(item.Id);
            return meta;
        }

        private ModificationConfig CreateModification(string id, ModificationDirection direction = ModificationDirection.Both)
        {
            var mod = ScriptableObject.CreateInstance<ModificationConfig>();
            spawned.Add(mod);

            var serialized = new SerializedObject(mod);
            serialized.FindProperty("id").stringValue = id;
            serialized.FindProperty("allowedDirection").enumValueIndex = (int)direction;
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
