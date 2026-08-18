using System;
using System.Collections.Generic;
using System.Linq;
using ExpoTheExplorer.Core;
using ExpoTheExplorer.Data;
using ExpoTheExplorer.Systems.DayLifecycle;
using ExpoTheExplorer.Systems.EconomySystem;
using ExpoTheExplorer.Systems.TicketSystem;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace ExpoTheExplorer.Tests.EditMode
{
    public class TicketSystemTests
    {
        private GameConfig gameConfig;
        private readonly List<UnityEngine.Object> spawnedAssets = new();

        [SetUp]
        public void SetUp()
        {
            gameConfig = ScriptableObject.CreateInstance<GameConfig>();
        }

        [TearDown]
        public void TearDown()
        {
            UnityEngine.Object.DestroyImmediate(gameConfig);
            foreach (var asset in spawnedAssets)
            {
                UnityEngine.Object.DestroyImmediate(asset);
            }
            spawnedAssets.Clear();
        }

        private Ticket CreateSimpleTicket(float timeLimitSeconds = 10f)
        {
            return new Ticket(
                "Test Customer",
                PatienceType.Normal,
                new List<FoodItemConfig>(),
                new List<Modification>(),
                timeLimitSeconds);
        }

        // Stands in for LivesManager.LoseLife -- this suite tests
        // TicketSlotManager's OWN behavior (does it request a life loss and
        // cancel on timeout), not LivesSystem, so a plain state.Lives--
        // keeps existing assertions on state.Lives valid without depending
        // on the real LivesManager implementation.
        private TicketSlotManager CreateManager(GameState state, Func<Ticket> provider = null)
        {
            return new TicketSlotManager(state, provider ?? (() => CreateSimpleTicket()), () => state.Lives--);
        }

        private FoodItemConfig CreateFoodItem(FoodCategory category, List<ModificationConfig> availableModifications = null)
        {
            var foodConfig = ScriptableObject.CreateInstance<FoodItemConfig>();
            spawnedAssets.Add(foodConfig);

            var serialized = new SerializedObject(foodConfig);
            serialized.FindProperty("category").enumValueIndex = (int)category;

            if (availableModifications is { Count: > 0 })
            {
                var modsProperty = serialized.FindProperty("availableModifications");
                modsProperty.ClearArray();
                for (var i = 0; i < availableModifications.Count; i++)
                {
                    modsProperty.InsertArrayElementAtIndex(i);
                    modsProperty.GetArrayElementAtIndex(i).objectReferenceValue = availableModifications[i];
                }
            }

            serialized.ApplyModifiedPropertiesWithoutUndo();
            return foodConfig;
        }

        private ModificationConfig CreateModification(ModificationDirection direction = ModificationDirection.Both)
        {
            var modConfig = ScriptableObject.CreateInstance<ModificationConfig>();
            spawnedAssets.Add(modConfig);

            var serialized = new SerializedObject(modConfig);
            serialized.FindProperty("allowedDirection").enumValueIndex = (int)direction;
            serialized.ApplyModifiedPropertiesWithoutUndo();

            return modConfig;
        }

        // Poisson rate far beyond the [0,10] Inspector-facing Range hint (which only
        // constrains the slider UI, not values set directly via SerializedObject) —
        // used to force near-certain inclusion/maximum-count outcomes in tests
        // without relying on an exact-1.0 probability that doesn't exist under a
        // Poisson pmf for any finite lambda.
        private const float ExtremeLambda = 1_000_000f;

        private TicketGenerationConfig CreateGenerationConfig(float modificationCountLambda, float modificationAdditionChance = 0.5f)
        {
            var genConfig = ScriptableObject.CreateInstance<TicketGenerationConfig>();
            spawnedAssets.Add(genConfig);

            var namesDatabase = new TextAsset(
                "[{\"id\":1,\"name\":\"Alice\",\"gender\":\"f\"},{\"id\":2,\"name\":\"Bob\",\"gender\":\"m\"}]");
            spawnedAssets.Add(namesDatabase);

            var serialized = new SerializedObject(genConfig);
            serialized.FindProperty("modificationCountLambda").floatValue = modificationCountLambda;
            serialized.FindProperty("modificationAdditionChance").floatValue = modificationAdditionChance;
            serialized.FindProperty("namesDatabase").objectReferenceValue = namesDatabase;
            serialized.ApplyModifiedPropertiesWithoutUndo();

            return genConfig;
        }

        // Every suite below except the two cap tests is about weight or lambda, so it gets
        // the "no cap of my own" default. Spelled as an overload rather than a per-entry
        // default (a params tuple cannot carry one) so those tests stay readable and cannot
        // accidentally assert against a literal 0, which the getter would rescue anyway.
        private void SetMainDishWeights(
            TicketGenerationConfig genConfig,
            params (FoodItemConfig food, float weight, float modificationCountLambda)[] entries) =>
            SetMainDishWeights(genConfig, entries
                .Select(e => (e.food, e.weight, e.modificationCountLambda, MainDishWeight.DefaultMaxModificationCount))
                .ToArray());

        private void SetMainDishWeights(
            TicketGenerationConfig genConfig,
            params (FoodItemConfig food, float weight, float modificationCountLambda, int maxModificationCount)[] entries)
        {
            var serialized = new SerializedObject(genConfig);
            var listProperty = serialized.FindProperty("mainDishWeights");
            listProperty.ClearArray();
            for (var i = 0; i < entries.Length; i++)
            {
                listProperty.InsertArrayElementAtIndex(i);
                var element = listProperty.GetArrayElementAtIndex(i);
                element.FindPropertyRelative("food").objectReferenceValue = entries[i].food;
                element.FindPropertyRelative("weight").floatValue = entries[i].weight;
                element.FindPropertyRelative("modificationCountLambda").floatValue = entries[i].modificationCountLambda;
                element.FindPropertyRelative("maxModificationCount").intValue = entries[i].maxModificationCount;
            }
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        // Shared by the direction-resolution tests below: a Poisson pmf never
        // assigns EXACTLY probability 1 to inclusion for any finite lambda (unlike
        // the old Bernoulli mechanism's chance=1f), so instead of asserting a
        // single deterministic outcome, this samples many seeds, verifies the
        // direction on every seed where inclusion actually happened, and confirms
        // the loop wasn't vacuous (at least one seed did include it).
        private void AssertIncludedModificationsMatchDirection(TicketGenerationConfig genConfig, List<FoodItemConfig> pool, bool expectedIsAddition, int seedCount = 30)
        {
            var inclusions = 0;
            for (var seed = 0; seed < seedCount; seed++)
            {
                var factory = new TicketFactory(genConfig, new System.Random(seed));
                var ticket = factory.Create(pool, "Test Customer", PatienceType.Normal);
                if (ticket.Modifications.Count == 0) continue;

                inclusions++;
                Assert.AreEqual(expectedIsAddition, ticket.Modifications[0].IsAddition);
            }

            Assert.Greater(inclusions, 0, "Expected at least one seed to include the modification.");
        }

        [Test]
        public void FillEmptySlots_OnFreshGameState_FillsAllThreeSlots()
        {
            var state = new GameState(gameConfig);
            var manager = CreateManager(state);

            manager.FillEmptySlots();

            Assert.IsTrue(state.TicketSlots.All(t => t != null));
        }

        [Test]
        public void DeliverTicket_MarksDeliveredAndRefillsSameSlotWithNewTicket()
        {
            var state = new GameState(gameConfig);
            var manager = CreateManager(state);
            manager.FillEmptySlots();
            var original = state.TicketSlots[0];

            manager.DeliverTicket(0);

            Assert.AreEqual(TicketState.Delivered, original.State);
            Assert.IsNotNull(state.TicketSlots[0]);
            Assert.AreNotSame(original, state.TicketSlots[0]);
        }

        [Test]
        public void DeliverTicket_PublishesTicketDeliveredEvent_WithTheDeliveredTicketAndSlotIndex()
        {
            var state = new GameState(gameConfig);
            var manager = CreateManager(state);
            manager.FillEmptySlots();
            var original = state.TicketSlots[0];

            (int SlotIndex, Ticket Ticket)? published = null;
            state.TicketDelivered.Subscribe(p => published = p);

            manager.DeliverTicket(0);

            Assert.IsNotNull(published);
            Assert.AreEqual(0, published.Value.SlotIndex);
            Assert.AreSame(original, published.Value.Ticket);
        }

        [Test]
        public void FillEmptySlots_PublishesTicketAssigned_ForEachFilledSlot()
        {
            var state = new GameState(gameConfig);
            var manager = CreateManager(state);

            var assignedCount = 0;
            state.TicketAssigned.Subscribe(_ => assignedCount++);

            manager.FillEmptySlots();

            Assert.AreEqual(GameState.TicketSlotCount, assignedCount);
        }

        [Test]
        public void DeliverTicket_PublishesTicketAssigned_WithTheSlotIndexAndNewTicket()
        {
            var state = new GameState(gameConfig);
            var manager = CreateManager(state);
            manager.FillEmptySlots();

            (int SlotIndex, Ticket Ticket)? assigned = null;
            state.TicketAssigned.Subscribe(a => assigned = a);

            manager.DeliverTicket(0);

            Assert.AreEqual(0, assigned?.SlotIndex);
            Assert.AreSame(state.TicketSlots[0], assigned?.Ticket);
        }

        [Test]
        public void CancelTicket_PublishesTicketAssigned_WithTheSlotIndexAndNewTicket()
        {
            var state = new GameState(gameConfig);
            var manager = CreateManager(state);
            manager.FillEmptySlots();

            (int SlotIndex, Ticket Ticket)? assigned = null;
            state.TicketAssigned.Subscribe(a => assigned = a);

            manager.CancelTicket(0);

            Assert.AreEqual(0, assigned?.SlotIndex);
            Assert.AreSame(state.TicketSlots[0], assigned?.Ticket);
        }

        [Test]
        public void Tick_WhenTicketTimesOut_DecrementsLivesAndCancelsAndRefillsSameSlot()
        {
            var state = new GameState(gameConfig);
            var manager = CreateManager(state);
            var ticket = CreateSimpleTicket(1f);
            state.TicketSlots[0] = ticket;
            var initialLives = state.Lives;

            manager.Tick(2f);

            Assert.AreEqual(initialLives - 1, state.Lives);
            Assert.AreEqual(TicketState.Cancelled, ticket.State);
            Assert.IsNotNull(state.TicketSlots[0]);
            Assert.AreNotSame(ticket, state.TicketSlots[0]);
        }

        [Test]
        public void Tick_BeforeTimeExpires_OnlyDecrementsRemainingSeconds_DoesNotCancelOrTouchLives()
        {
            var state = new GameState(gameConfig);
            var manager = CreateManager(state);
            var ticket = CreateSimpleTicket(10f);
            state.TicketSlots[0] = ticket;
            var initialLives = state.Lives;

            manager.Tick(1f);

            Assert.AreEqual(9f, ticket.RemainingSeconds, 0.0001f);
            Assert.AreEqual(TicketState.Active, ticket.State);
            Assert.AreEqual(initialLives, state.Lives);
        }

        [Test]
        public void CancelTicket_PublishesTicketCancelledEvent_AndDoesNotTouchLives()
        {
            var state = new GameState(gameConfig);
            var manager = CreateManager(state);
            var ticket = CreateSimpleTicket();
            state.TicketSlots[0] = ticket;
            var initialLives = state.Lives;

            Ticket published = null;
            state.TicketCancelled.Subscribe(t => published = t);

            manager.CancelTicket(0);

            Assert.AreSame(ticket, published);
            Assert.AreEqual(initialLives, state.Lives);
        }

        [Test]
        public void TicketSlots_StayAtThreeConcurrentActiveTickets_AcrossRepeatedDeliverCancelCycles()
        {
            var state = new GameState(gameConfig);
            var manager = CreateManager(state);
            manager.FillEmptySlots();

            for (var i = 0; i < 10; i++)
            {
                var slotIndex = i % GameState.TicketSlotCount;
                if (i % 2 == 0) manager.DeliverTicket(slotIndex);
                else manager.CancelTicket(slotIndex);

                Assert.AreEqual(GameState.TicketSlotCount, state.TicketSlots.Count(t => t != null));
            }
        }

        [Test]
        public void ResetSlotsForNewDay_AllThreeSlotsGetNewTickets_PublishesTicketAssignedThreeTimes_NeverPublishesTicketCancelled()
        {
            var state = new GameState(gameConfig);
            var manager = CreateManager(state);
            manager.FillEmptySlots();
            var oldTickets = new List<Ticket>(state.TicketSlots);

            var assignedCount = 0;
            state.TicketAssigned.Subscribe(_ => assignedCount++);
            var cancelledCount = 0;
            state.TicketCancelled.Subscribe(_ => cancelledCount++);

            manager.ResetSlotsForNewDay();

            Assert.AreEqual(GameState.TicketSlotCount, assignedCount);
            Assert.AreEqual(0, cancelledCount);
            Assert.IsTrue(state.TicketSlots.All(t => t != null));
            Assert.IsTrue(state.TicketSlots.All(t => !oldTickets.Contains(t)));
        }

        [Test]
        public void ResetSlotsForNewDay_NullsAllSlotsBeforeReassigning_NoStaleTicketVisibleDuringAnyPublish()
        {
            var state = new GameState(gameConfig);
            var manager = CreateManager(state);
            manager.FillEmptySlots();
            var oldTickets = new HashSet<Ticket>(state.TicketSlots);

            var sawStaleTicket = false;
            state.TicketAssigned.Subscribe(_ =>
            {
                foreach (var t in state.TicketSlots)
                {
                    if (t != null && oldTickets.Contains(t)) sawStaleTicket = true;
                }
            });

            manager.ResetSlotsForNewDay();

            Assert.IsFalse(sawStaleTicket);
        }

        [Test]
        public void AssignTicket_AfterPauseForDayComplete_DoesNotRefillSlot()
        {
            var state = new GameState(gameConfig);
            var manager = CreateManager(state);
            manager.FillEmptySlots();
            var original = state.TicketSlots[0];

            manager.PauseForDayComplete();

            var assignedAgain = false;
            state.TicketAssigned.Subscribe(_ => assignedAgain = true);

            manager.DeliverTicket(0);

            Assert.IsFalse(assignedAgain);
            Assert.AreSame(original, state.TicketSlots[0]);
            Assert.AreEqual(TicketState.Delivered, original.State);
        }

        [Test]
        public void Tick_AfterPauseForDayComplete_DoesNotDecrementRemainingSecondsOrLoseLife()
        {
            var state = new GameState(gameConfig);
            var manager = CreateManager(state);
            var ticket = CreateSimpleTicket(1f);
            state.TicketSlots[0] = ticket;
            var initialLives = state.Lives;

            manager.PauseForDayComplete();
            manager.Tick(5f);

            Assert.AreEqual(1f, ticket.RemainingSeconds, 0.0001f);
            Assert.AreEqual(initialLives, state.Lives);
            Assert.AreEqual(TicketState.Active, ticket.State);
        }

        [Test]
        public void ResetSlotsForNewDay_ClearsIsDayComplete_SubsequentAssignTicketWorksAgain()
        {
            var state = new GameState(gameConfig);
            var manager = CreateManager(state);
            manager.FillEmptySlots();
            manager.PauseForDayComplete();

            manager.ResetSlotsForNewDay();

            Assert.IsFalse(manager.IsDayComplete);
            Assert.IsTrue(state.TicketSlots.All(t => t != null));
        }

        [Test]
        public void AssignTicket_ProviderReturnsNull_LeavesSlotEmptyWithoutThrowing()
        {
            var state = new GameState(gameConfig);
            var manager = CreateManager(state, () => null);

            Assert.DoesNotThrow(() => manager.FillEmptySlots());

            Assert.IsNull(state.TicketSlots[0]);
            Assert.IsNull(state.TicketSlots[1]);
            Assert.IsNull(state.TicketSlots[2]);
        }

        [Test]
        public void DeliverTicket_SequenceExhaustedAndAllSlotsEmpty_SetsIsDayCompleteAndPublishesDayCompleted()
        {
            var state = new GameState(gameConfig);
            var callCount = 0;
            var manager = CreateManager(state, () => callCount++ < GameState.TicketSlotCount ? CreateSimpleTicket() : null);
            manager.FillEmptySlots(); // consumes all 3 -- provider is now exhausted

            var dayCompletedCount = 0;
            state.DayCompleted.Subscribe(_ => dayCompletedCount++);

            manager.DeliverTicket(0);
            Assert.IsFalse(manager.IsDayComplete);
            manager.DeliverTicket(1);
            Assert.IsFalse(manager.IsDayComplete);
            manager.DeliverTicket(2); // last active slot resolves -> all empty -> day complete

            Assert.IsTrue(manager.IsDayComplete);
            Assert.AreEqual(1, dayCompletedCount);
            Assert.IsNull(state.TicketSlots[0]);
            Assert.IsNull(state.TicketSlots[1]);
            Assert.IsNull(state.TicketSlots[2]);
        }

        [Test]
        public void DeliverTicket_OneTicketTimesOutInsteadOfDelivered_DayStillCompletesOnceSequenceExhausted()
        {
            var state = new GameState(gameConfig);
            var callCount = 0;
            var manager = CreateManager(state, () => callCount++ < GameState.TicketSlotCount ? CreateSimpleTicket() : null);
            manager.FillEmptySlots(); // consumes all 3 -- provider is now exhausted

            var dayLifecycle = new DayLifecycleManager(state);
            var samplePayout = new DeliveryPayoutResult(orderValue: 10, tier: TipTier.Critical, tipRate: 0f);
            state.TicketDelivered.Subscribe(_ => dayLifecycle.RecordDelivery(samplePayout));

            var dayCompletedCount = 0;
            state.DayCompleted.Subscribe(_ => dayCompletedCount++);

            manager.DeliverTicket(0);
            manager.CancelTicket(1); // e.g. a timeout -- never counted as a delivery
            Assert.IsFalse(manager.IsDayComplete);
            manager.DeliverTicket(2);

            Assert.IsTrue(manager.IsDayComplete);
            Assert.AreEqual(1, dayCompletedCount);
            Assert.AreEqual(2, state.TicketsDeliveredToday);
        }

        [Test]
        public void TicketFactory_Create_OnlyUsesItemsFromSuppliedPool_AndProducesPlausibleItemCount()
        {
            var modA = CreateModification();
            var modB = CreateModification();
            var main = CreateFoodItem(FoodCategory.Main, new List<ModificationConfig> { modA, modB });
            var side = CreateFoodItem(FoodCategory.Side);
            var drink = CreateFoodItem(FoodCategory.Drink);
            var pool = new List<FoodItemConfig> { main, side, drink };

            var genConfig = ScriptableObject.CreateInstance<TicketGenerationConfig>();
            spawnedAssets.Add(genConfig);

            var factory = new TicketFactory(genConfig, new System.Random(12345));
            var ticket = factory.Create(pool, "Test Customer", PatienceType.Normal);

            Assert.That(ticket.RequiredItems.Count, Is.InRange(1, 3));
            Assert.IsTrue(ticket.RequiredItems.All(item => pool.Contains(item)));
            Assert.AreEqual(genConfig.NormalTimeLimitSeconds, ticket.TimeLimitSeconds);
            Assert.IsTrue(ticket.Modifications.All(m => main.AvailableModifications.Contains(m.Config)));
        }

        [Test]
        public void TicketFactory_Create_RemovalOnlyModification_AlwaysGeneratesRemovalWhenIncluded()
        {
            var mod = CreateModification(ModificationDirection.RemovalOnly);
            var main = CreateFoodItem(FoodCategory.Main, new List<ModificationConfig> { mod });
            var pool = new List<FoodItemConfig> { main };
            var genConfig = CreateGenerationConfig(modificationCountLambda: 0f, modificationAdditionChance: 1f);
            SetMainDishWeights(genConfig, (main, 1f, ExtremeLambda));

            AssertIncludedModificationsMatchDirection(genConfig, pool, expectedIsAddition: false);
        }

        [Test]
        public void TicketFactory_Create_AdditionOnlyModification_AlwaysGeneratesAdditionWhenIncluded()
        {
            var mod = CreateModification(ModificationDirection.AdditionOnly);
            var main = CreateFoodItem(FoodCategory.Main, new List<ModificationConfig> { mod });
            var pool = new List<FoodItemConfig> { main };
            var genConfig = CreateGenerationConfig(modificationCountLambda: 0f, modificationAdditionChance: 0f);
            SetMainDishWeights(genConfig, (main, 1f, ExtremeLambda));

            AssertIncludedModificationsMatchDirection(genConfig, pool, expectedIsAddition: true);
        }

        [Test]
        public void TicketFactory_Create_BothDirectionModification_RespectsAdditionChanceExtremes()
        {
            var mod = CreateModification(ModificationDirection.Both);
            var main = CreateFoodItem(FoodCategory.Main, new List<ModificationConfig> { mod });
            var pool = new List<FoodItemConfig> { main };

            var alwaysAddition = CreateGenerationConfig(modificationCountLambda: 0f, modificationAdditionChance: 1f);
            SetMainDishWeights(alwaysAddition, (main, 1f, ExtremeLambda));
            AssertIncludedModificationsMatchDirection(alwaysAddition, pool, expectedIsAddition: true);

            var alwaysRemoval = CreateGenerationConfig(modificationCountLambda: 0f, modificationAdditionChance: 0f);
            SetMainDishWeights(alwaysRemoval, (main, 1f, ExtremeLambda));
            AssertIncludedModificationsMatchDirection(alwaysRemoval, pool, expectedIsAddition: false);
        }

        [Test]
        public void TicketFactory_PickRandomCustomerName_ReturnsNameFromConfigPool()
        {
            var genConfig = CreateGenerationConfig(modificationCountLambda: 0f);

            var factory = new TicketFactory(genConfig, new System.Random(12345));
            var name = factory.PickRandomCustomerName();

            Assert.IsTrue(genConfig.CustomerNames.Contains(name));
        }

        [Test]
        public void TicketFactory_Create_ZeroWeightedMain_IsNeverPicked()
        {
            var mainA = CreateFoodItem(FoodCategory.Main);
            var mainB = CreateFoodItem(FoodCategory.Main);
            var pool = new List<FoodItemConfig> { mainA, mainB };
            var genConfig = CreateGenerationConfig(modificationCountLambda: 0f);
            SetMainDishWeights(genConfig, (mainA, 1f, 0.3f), (mainB, 0f, 0.3f));

            for (var seed = 0; seed < 50; seed++)
            {
                var factory = new TicketFactory(genConfig, new System.Random(seed));
                var ticket = factory.Create(pool, "Test Customer", PatienceType.Normal);
                Assert.AreSame(mainA, ticket.RequiredItems[0]);
            }
        }

        [Test]
        public void TicketFactory_Create_MainMissingFromWeightList_UsesDefaultWeight_AndIsPickedOverExplicitZeroWeight()
        {
            var mainA = CreateFoodItem(FoodCategory.Main); // explicitly zeroed out
            var mainB = CreateFoodItem(FoodCategory.Main); // absent -> MainDishWeight.DefaultWeight
            var pool = new List<FoodItemConfig> { mainA, mainB };
            var genConfig = CreateGenerationConfig(modificationCountLambda: 0f);
            SetMainDishWeights(genConfig, (mainA, 0f, 0.3f));

            for (var seed = 0; seed < 50; seed++)
            {
                var factory = new TicketFactory(genConfig, new System.Random(seed));
                var ticket = factory.Create(pool, "Test Customer", PatienceType.Normal);
                Assert.AreSame(mainB, ticket.RequiredItems[0]);
            }
        }

        [Test]
        public void TicketFactory_Create_AllMainWeightsZero_FallsBackToUniformPick_WithoutThrowing()
        {
            var mainA = CreateFoodItem(FoodCategory.Main);
            var mainB = CreateFoodItem(FoodCategory.Main);
            var pool = new List<FoodItemConfig> { mainA, mainB };
            var genConfig = CreateGenerationConfig(modificationCountLambda: 0f);
            SetMainDishWeights(genConfig, (mainA, 0f, 0.3f), (mainB, 0f, 0.3f));

            var picked = new HashSet<FoodItemConfig>();
            for (var seed = 0; seed < 20; seed++)
            {
                var factory = new TicketFactory(genConfig, new System.Random(seed));
                Ticket ticket = null;
                Assert.DoesNotThrow(() => ticket = factory.Create(pool, "Test Customer", PatienceType.Normal));
                picked.Add(ticket.RequiredItems[0]);
            }

            Assert.That(picked, Has.Member(mainA));
            Assert.That(picked, Has.Member(mainB));
        }

        [Test]
        public void TicketFactory_Create_MainWithNoAvailableModifications_ProducesEmptyModificationsList()
        {
            var main = CreateFoodItem(FoodCategory.Main);
            var pool = new List<FoodItemConfig> { main };
            // N=0 short-circuits regardless of lambda — verified with two very different values.
            var genConfigLowLambda = CreateGenerationConfig(modificationCountLambda: 0f);
            SetMainDishWeights(genConfigLowLambda, (main, 1f, 0f));
            var genConfigHighLambda = CreateGenerationConfig(modificationCountLambda: 0f);
            SetMainDishWeights(genConfigHighLambda, (main, 1f, ExtremeLambda));

            foreach (var genConfig in new[] { genConfigLowLambda, genConfigHighLambda })
            {
                var factory = new TicketFactory(genConfig, new System.Random(1));
                var ticket = factory.Create(pool, "Test Customer", PatienceType.Normal);

                Assert.AreSame(main, ticket.RequiredItems[0]);
                Assert.IsEmpty(ticket.Modifications);
            }
        }

        [Test]
        public void TicketFactory_Create_ZeroLambda_AlwaysProducesZeroModifications()
        {
            var mods = new List<ModificationConfig> { CreateModification(), CreateModification() };
            var main = CreateFoodItem(FoodCategory.Main, mods);
            var pool = new List<FoodItemConfig> { main };
            var genConfig = CreateGenerationConfig(modificationCountLambda: 0f);
            SetMainDishWeights(genConfig, (main, 1f, 0f));

            for (var seed = 0; seed < 20; seed++)
            {
                var factory = new TicketFactory(genConfig, new System.Random(seed));
                var ticket = factory.Create(pool, "Test Customer", PatienceType.Normal);
                Assert.IsEmpty(ticket.Modifications);
            }
        }

        [Test]
        public void TicketFactory_Create_ExtremeLambda_AlwaysProducesMaximumModificationCount()
        {
            var mods = new List<ModificationConfig> { CreateModification(), CreateModification(), CreateModification(), CreateModification() };
            var main = CreateFoodItem(FoodCategory.Main, mods);
            var pool = new List<FoodItemConfig> { main };
            var genConfig = CreateGenerationConfig(modificationCountLambda: 0f);
            SetMainDishWeights(genConfig, (main, 1f, ExtremeLambda));

            for (var seed = 0; seed < 20; seed++)
            {
                var factory = new TicketFactory(genConfig, new System.Random(seed));
                var ticket = factory.Create(pool, "Test Customer", PatienceType.Normal);
                Assert.AreEqual(mods.Count, ticket.Modifications.Count);
            }
        }

        [Test]
        public void TicketFactory_Create_ModerateLambda_ModificationCountClustersNearLambda_NotMonotonicallyDecreasing()
        {
            var mods = new List<ModificationConfig> { CreateModification(), CreateModification(), CreateModification(), CreateModification() };
            var main = CreateFoodItem(FoodCategory.Main, mods);
            var pool = new List<FoodItemConfig> { main };
            var genConfig = CreateGenerationConfig(modificationCountLambda: 0f);
            SetMainDishWeights(genConfig, (main, 1f, 2f)); // Poisson(2) truncated to 0..4: P(0)~14%, P(2)~29%

            var countAtZero = 0;
            var countAtTwo = 0;
            const int sampleSize = 4000;
            for (var seed = 0; seed < sampleSize; seed++)
            {
                var factory = new TicketFactory(genConfig, new System.Random(seed));
                var ticket = factory.Create(pool, "Test Customer", PatienceType.Normal);
                if (ticket.Modifications.Count == 0) countAtZero++;
                if (ticket.Modifications.Count == 2) countAtTwo++;
            }

            // A monotonically-decreasing distribution (the old Bernoulli/Binomial
            // mechanism's shape) would never show count(2) clearly beating
            // count(0) — Poisson(lambda=2) does, confirming the intended shape.
            Assert.Greater(countAtTwo, countAtZero * 1.3);
        }

        [Test]
        public void TicketFactory_Create_ChosenModifications_AreAlwaysSubsetOfAvailableModifications_WithNoDuplicates()
        {
            var mods = new List<ModificationConfig> { CreateModification(), CreateModification(), CreateModification(), CreateModification(), CreateModification() };
            var main = CreateFoodItem(FoodCategory.Main, mods);
            var pool = new List<FoodItemConfig> { main };
            var genConfig = CreateGenerationConfig(modificationCountLambda: 0f);
            SetMainDishWeights(genConfig, (main, 1f, 2.5f));

            for (var seed = 0; seed < 100; seed++)
            {
                var factory = new TicketFactory(genConfig, new System.Random(seed));
                var ticket = factory.Create(pool, "Test Customer", PatienceType.Normal);

                var chosenConfigs = ticket.Modifications.Select(m => m.Config).ToList();
                Assert.IsTrue(chosenConfigs.All(mods.Contains));
                Assert.AreEqual(chosenConfigs.Count, chosenConfigs.Distinct().Count());
            }
        }

        [Test]
        public void TicketFactory_Create_PerMainDishModificationCountLambda_OverridesGlobalDefault()
        {
            var mod = CreateModification(ModificationDirection.AdditionOnly);
            var main = CreateFoodItem(FoodCategory.Main, new List<ModificationConfig> { mod });
            var pool = new List<FoodItemConfig> { main };
            // Global fallback lambda=0 would deterministically exclude the modification (N=1);
            // the per-dish override uses an extreme lambda to force near-certain inclusion.
            var genConfig = CreateGenerationConfig(modificationCountLambda: 0f);
            SetMainDishWeights(genConfig, (main, 1f, ExtremeLambda));

            AssertIncludedModificationsMatchDirection(genConfig, pool, expectedIsAddition: true, seedCount: 20);
        }

        // The cap is the Poisson truncation point, so an extreme lambda -- which otherwise
        // piles all the mass onto the top of the distribution -- lands on the cap instead
        // of on the dish's full modification list. That is the whole point of the field:
        // "as many as possible, but never more than N".
        [Test]
        public void TicketFactory_Create_MaxModificationCount_CapsTheCountBelowAvailableModifications()
        {
            var mods = new List<ModificationConfig> { CreateModification(), CreateModification(), CreateModification(), CreateModification() };
            var main = CreateFoodItem(FoodCategory.Main, mods);
            var pool = new List<FoodItemConfig> { main };
            var genConfig = CreateGenerationConfig(modificationCountLambda: 0f);
            SetMainDishWeights(genConfig, (main, 1f, ExtremeLambda, 2));

            for (var seed = 0; seed < 50; seed++)
            {
                var factory = new TicketFactory(genConfig, new System.Random(seed));
                var ticket = factory.Create(pool, "Test Customer", PatienceType.Normal);
                Assert.AreEqual(2, ticket.Modifications.Count);
            }
        }

        // The dish's own list is the other half of the Math.Min. A cap above it must not
        // widen the roll -- there is nothing there to roll -- so this is the case that
        // catches the cap being used as the truncation point on its own.
        [Test]
        public void TicketFactory_Create_MaxModificationCountAboveAvailable_StillCapsAtAvailable()
        {
            var mods = new List<ModificationConfig> { CreateModification(), CreateModification() };
            var main = CreateFoodItem(FoodCategory.Main, mods);
            var pool = new List<FoodItemConfig> { main };
            var genConfig = CreateGenerationConfig(modificationCountLambda: 0f);
            SetMainDishWeights(genConfig, (main, 1f, ExtremeLambda, 10));

            for (var seed = 0; seed < 50; seed++)
            {
                var factory = new TicketFactory(genConfig, new System.Random(seed));
                var ticket = factory.Create(pool, "Test Customer", PatienceType.Normal);
                Assert.AreEqual(mods.Count, ticket.Modifications.Count);
            }
        }

        [Test]
        public void TicketFactory_Create_MainAbsentFromWeightList_FallsBackToGlobalModificationCountLambda()
        {
            var mod = CreateModification(ModificationDirection.AdditionOnly);
            var main = CreateFoodItem(FoodCategory.Main, new List<ModificationConfig> { mod });
            var pool = new List<FoodItemConfig> { main };
            // main is never added to mainDishWeights, so it must fall back to this global lambda.
            var genConfig = CreateGenerationConfig(modificationCountLambda: ExtremeLambda);

            AssertIncludedModificationsMatchDirection(genConfig, pool, expectedIsAddition: true, seedCount: 20);
        }
    }
}
