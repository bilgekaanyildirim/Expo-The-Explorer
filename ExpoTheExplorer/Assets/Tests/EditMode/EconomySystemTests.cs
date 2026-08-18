using System.Collections.Generic;
using ExpoTheExplorer.Core;
using ExpoTheExplorer.Data;
using ExpoTheExplorer.Systems.EconomySystem;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace ExpoTheExplorer.Tests.EditMode
{
    // Builds configs/food via SerializedObject because every knob is a private
    // SerializeField. Ratios here are deliberately 0.5/0.25 against a 100s limit
    // rather than the shipped 0.666/0.333: boundary tests need the threshold
    // second to be exactly representable, so a float comparison at the boundary
    // is testing the rule and not testing float arithmetic.
    public class EconomySystemTests
    {
        private readonly List<Object> spawnedAssets = new();

        [TearDown]
        public void TearDown()
        {
            foreach (var asset in spawnedAssets)
            {
                Object.DestroyImmediate(asset);
            }
            spawnedAssets.Clear();
        }

        private EconomyConfig CreateEconomyConfig(
            float warningRatio = 0.5f,
            float criticalRatio = 0.25f,
            float tipRateFull = 0.2f,
            float tipRateWarning = 0.1f,
            float tipRateCritical = 0.05f)
        {
            var config = ScriptableObject.CreateInstance<EconomyConfig>();
            spawnedAssets.Add(config);

            var serialized = new SerializedObject(config);
            serialized.FindProperty("warningRatio").floatValue = warningRatio;
            serialized.FindProperty("criticalRatio").floatValue = criticalRatio;
            serialized.FindProperty("tipRateFull").floatValue = tipRateFull;
            serialized.FindProperty("tipRateWarning").floatValue = tipRateWarning;
            serialized.FindProperty("tipRateCritical").floatValue = tipRateCritical;
            serialized.ApplyModifiedPropertiesWithoutUndo();

            return config;
        }

        private FoodItemConfig CreateFood(int basePrice)
        {
            var food = ScriptableObject.CreateInstance<FoodItemConfig>();
            spawnedAssets.Add(food);

            var serialized = new SerializedObject(food);
            serialized.FindProperty("basePrice").intValue = basePrice;
            serialized.ApplyModifiedPropertiesWithoutUndo();

            return food;
        }

        private Ticket CreateTicket(
            IReadOnlyList<FoodItemConfig> requiredItems,
            float timeLimitSeconds = 100f,
            float remainingSeconds = 100f,
            PatienceType patienceType = PatienceType.Normal)
        {
            return new Ticket("Test Customer", patienceType, requiredItems, new List<Modification>(), timeLimitSeconds)
            {
                RemainingSeconds = remainingSeconds
            };
        }

        [Test]
        public void CalculatePayout_OrderValue_IsSummedItemPrices_NotItemCount()
        {
            var calculator = new EconomyCalculator(CreateEconomyConfig());
            var ticket = CreateTicket(new[] { CreateFood(14), CreateFood(4), CreateFood(3) });

            var result = calculator.CalculatePayout(ticket);

            Assert.AreEqual(21, result.OrderValue);
        }

        [Test]
        public void CalculatePayout_TwoTicketsSameItemCount_DifferentPrices_PayDifferently()
        {
            var calculator = new EconomyCalculator(CreateEconomyConfig());

            var cheap = calculator.CalculatePayout(CreateTicket(new[] { CreateFood(3), CreateFood(3) }));
            var pricey = calculator.CalculatePayout(CreateTicket(new[] { CreateFood(14), CreateFood(8) }));

            Assert.AreEqual(6, cheap.OrderValue);
            Assert.AreEqual(22, pricey.OrderValue);
            Assert.Greater(pricey.Total, cheap.Total);
        }

        [Test]
        public void CalculatePayout_AboveWarningRatio_IsFullTier()
        {
            var calculator = new EconomyCalculator(CreateEconomyConfig());
            var ticket = CreateTicket(new[] { CreateFood(10) }, timeLimitSeconds: 100f, remainingSeconds: 60f);

            var result = calculator.CalculatePayout(ticket);

            Assert.AreEqual(TipTier.Full, result.Tier);
            Assert.AreEqual(0.2f, result.TipRate, 0.0001f);
            Assert.AreEqual(2f, result.Tip, 0.0001f);
            Assert.AreEqual(12f, result.Total, 0.0001f);
        }

        // Inclusive bound, matching TicketCardsView.TimerFillColorFor -- a ratio
        // sitting exactly on the threshold must not land in a different bucket
        // than the colour the bar shows at that same instant.
        [Test]
        public void CalculatePayout_ExactlyAtWarningRatio_IsWarningTier()
        {
            var calculator = new EconomyCalculator(CreateEconomyConfig());
            var ticket = CreateTicket(new[] { CreateFood(10) }, timeLimitSeconds: 100f, remainingSeconds: 50f);

            var result = calculator.CalculatePayout(ticket);

            Assert.AreEqual(TipTier.Warning, result.Tier);
            Assert.AreEqual(1f, result.Tip, 0.0001f);
        }

        [Test]
        public void CalculatePayout_ExactlyAtCriticalRatio_IsCriticalTier()
        {
            var calculator = new EconomyCalculator(CreateEconomyConfig());
            var ticket = CreateTicket(new[] { CreateFood(10) }, timeLimitSeconds: 100f, remainingSeconds: 25f);

            var result = calculator.CalculatePayout(ticket);

            Assert.AreEqual(TipTier.Critical, result.Tier);
            Assert.AreEqual(0.5f, result.Tip, 0.0001f);
        }

        // The whole point of authoring ratios instead of seconds: 20s left is
        // "nearly out" on a 45s ticket and "barely started" on a 150s one, so the
        // same wall-clock remainder must NOT produce the same tier.
        [Test]
        public void CalculatePayout_SameRemainingSeconds_DifferentTimeLimit_DifferentTier()
        {
            var calculator = new EconomyCalculator(CreateEconomyConfig());

            var shortTicket = calculator.CalculatePayout(
                CreateTicket(new[] { CreateFood(10) }, timeLimitSeconds: 45f, remainingSeconds: 20f));
            var longTicket = calculator.CalculatePayout(
                CreateTicket(new[] { CreateFood(10) }, timeLimitSeconds: 150f, remainingSeconds: 20f));

            Assert.AreEqual(TipTier.Warning, shortTicket.Tier);
            Assert.AreEqual(TipTier.Critical, longTicket.Tier);
        }

        // Item count must not scale any threshold (it did under the old speed
        // tiers). Same times, wildly different order size -> same tier.
        [Test]
        public void CalculatePayout_ItemCount_DoesNotScaleTierThresholds()
        {
            var calculator = new EconomyCalculator(CreateEconomyConfig());

            var oneItem = calculator.CalculatePayout(
                CreateTicket(new[] { CreateFood(10) }, timeLimitSeconds: 100f, remainingSeconds: 40f));
            var fiveItems = calculator.CalculatePayout(
                CreateTicket(
                    new[] { CreateFood(2), CreateFood(2), CreateFood(2), CreateFood(2), CreateFood(2) },
                    timeLimitSeconds: 100f,
                    remainingSeconds: 40f));

            Assert.AreEqual(oneItem.Tier, fiveItems.Tier);
            Assert.AreEqual(oneItem.TipRate, fiveItems.TipRate, 0.0001f);
        }

        // Patience feeds the payout only through the time limit it grants. Hold
        // the limit and the remainder fixed and the type must not matter at all.
        [Test]
        public void CalculatePayout_PatienceType_DoesNotAffectPayoutDirectly()
        {
            var calculator = new EconomyCalculator(CreateEconomyConfig());

            var impatient = calculator.CalculatePayout(
                CreateTicket(new[] { CreateFood(10) }, timeLimitSeconds: 100f, remainingSeconds: 40f, patienceType: PatienceType.Impatient));
            var patient = calculator.CalculatePayout(
                CreateTicket(new[] { CreateFood(10) }, timeLimitSeconds: 100f, remainingSeconds: 40f, patienceType: PatienceType.Patient));

            Assert.AreEqual(impatient.Tier, patient.Tier);
            Assert.AreEqual(impatient.Total, patient.Total, 0.0001f);
        }

        // The guaranteed half of the contract: however late the delivery, and even
        // with the critical rate authored at 0, the food's own price is paid whole.
        [Test]
        public void CalculatePayout_LatestPossibleDelivery_StillPaysOrderValueInFull()
        {
            var calculator = new EconomyCalculator(CreateEconomyConfig(tipRateCritical: 0f));
            var ticket = CreateTicket(new[] { CreateFood(14), CreateFood(4) }, timeLimitSeconds: 100f, remainingSeconds: 0f);

            var result = calculator.CalculatePayout(ticket);

            Assert.AreEqual(TipTier.Critical, result.Tier);
            Assert.AreEqual(0f, result.Tip, 0.0001f);
            Assert.AreEqual(18f, result.Total, 0.0001f);
            Assert.GreaterOrEqual(result.Total, result.OrderValue);
        }

        // An unauthored/zero limit reads as "no time left" rather than dividing by
        // zero into a free Full-tier tip.
        [Test]
        public void CalculatePayout_ZeroTimeLimit_IsCriticalTier_NotFull()
        {
            var calculator = new EconomyCalculator(CreateEconomyConfig());
            var ticket = CreateTicket(new[] { CreateFood(10) }, timeLimitSeconds: 0f, remainingSeconds: 0f);

            var result = calculator.CalculatePayout(ticket);

            Assert.AreEqual(TipTier.Critical, result.Tier);
        }

        // A Day authored against a food asset that was later deleted already
        // survives elsewhere in the pipeline; a delivery must not be the one place
        // that throws over it.
        [Test]
        public void CalculatePayout_NullRequiredItem_IsSkipped_NotThrown()
        {
            var calculator = new EconomyCalculator(CreateEconomyConfig());
            var ticket = CreateTicket(new[] { CreateFood(10), null, CreateFood(5) });

            var result = calculator.CalculatePayout(ticket);

            Assert.AreEqual(15, result.OrderValue);
        }

        // An item nobody priced contributes nothing -- a content bug that
        // validation surfaces, deliberately not papered over with a fallback price.
        [Test]
        public void CalculatePayout_UnpricedItem_ContributesZero()
        {
            var calculator = new EconomyCalculator(CreateEconomyConfig());
            var ticket = CreateTicket(new[] { CreateFood(10), CreateFood(0) });

            var result = calculator.CalculatePayout(ticket);

            Assert.AreEqual(10, result.OrderValue);
        }
    }
}
