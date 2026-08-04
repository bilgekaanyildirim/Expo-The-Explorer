using System.Collections.Generic;
using ExpoTheExplorer.Core;
using ExpoTheExplorer.Data;
using ExpoTheExplorer.Systems.EconomySystem;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace ExpoTheExplorer.Tests.EditMode
{
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
            float baseTipPerItem = 10f,
            float lightningMultiplier = 2f,
            float fastMultiplier = 1.5f,
            float standardMultiplier = 1f,
            float lightningSecondsPerItem = 3f,
            float fastSecondsPerItem = 6f)
        {
            var config = ScriptableObject.CreateInstance<EconomyConfig>();
            spawnedAssets.Add(config);

            var serialized = new SerializedObject(config);
            serialized.FindProperty("baseTipPerItem").floatValue = baseTipPerItem;
            serialized.FindProperty("lightningMultiplier").floatValue = lightningMultiplier;
            serialized.FindProperty("fastMultiplier").floatValue = fastMultiplier;
            serialized.FindProperty("standardMultiplier").floatValue = standardMultiplier;
            serialized.FindProperty("lightningSecondsPerItem").floatValue = lightningSecondsPerItem;
            serialized.FindProperty("fastSecondsPerItem").floatValue = fastSecondsPerItem;
            serialized.ApplyModifiedPropertiesWithoutUndo();

            return config;
        }

        private static string DecayStepsFieldFor(PatienceType patienceType)
        {
            return patienceType switch
            {
                PatienceType.Impatient => "impatientDecaySteps",
                PatienceType.Patient => "patientDecaySteps",
                _ => "normalDecaySteps",
            };
        }

        private void SetPatienceDecaySteps(EconomyConfig config, PatienceType patienceType, params (float threshold, float coefficient)[] steps)
        {
            var serialized = new SerializedObject(config);
            var stepsProperty = serialized.FindProperty(DecayStepsFieldFor(patienceType));
            stepsProperty.ClearArray();
            for (var i = 0; i < steps.Length; i++)
            {
                stepsProperty.InsertArrayElementAtIndex(i);
                var stepElement = stepsProperty.GetArrayElementAtIndex(i);
                stepElement.FindPropertyRelative("secondsPerItemThreshold").floatValue = steps[i].threshold;
                stepElement.FindPropertyRelative("decayCoefficient").floatValue = steps[i].coefficient;
            }

            serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        private Ticket CreateTicket(int itemCount, PatienceType patienceType, float timeLimitSeconds, float elapsedSeconds)
        {
            var requiredItems = new List<FoodItemConfig>(new FoodItemConfig[itemCount]);
            var ticket = new Ticket("Test Customer", patienceType, requiredItems, new List<Modification>(), timeLimitSeconds)
            {
                RemainingSeconds = timeLimitSeconds - elapsedSeconds
            };
            return ticket;
        }

        [Test]
        public void CalculateTip_BaseTip_ScalesWithRequiredItemCount()
        {
            var config = CreateEconomyConfig(baseTipPerItem: 10f);
            var calculator = new EconomyCalculator(config);

            var oneItem = calculator.CalculateTip(CreateTicket(1, PatienceType.Normal, 100f, elapsedSeconds: 0f));
            var threeItems = calculator.CalculateTip(CreateTicket(3, PatienceType.Normal, 100f, elapsedSeconds: 0f));

            Assert.AreEqual(10f, oneItem.BaseTip, 0.0001f);
            Assert.AreEqual(30f, threeItems.BaseTip, 0.0001f);
        }

        [Test]
        public void CalculateTip_ElapsedAtOrUnderLightningThreshold_IsLightningTier()
        {
            var config = CreateEconomyConfig(lightningSecondsPerItem: 3f, fastSecondsPerItem: 6f);
            var calculator = new EconomyCalculator(config);

            // itemCount=2 -> Lightning threshold = 3*2 = 6 seconds.
            var result = calculator.CalculateTip(CreateTicket(2, PatienceType.Normal, 100f, elapsedSeconds: 6f));

            Assert.AreEqual(SpeedTier.Lightning, result.SpeedTier);
            Assert.AreEqual(config.LightningMultiplier, result.SpeedMultiplier, 0.0001f);
        }

        [Test]
        public void CalculateTip_ElapsedBetweenThresholds_IsFastTier()
        {
            var config = CreateEconomyConfig(lightningSecondsPerItem: 3f, fastSecondsPerItem: 6f);
            var calculator = new EconomyCalculator(config);

            // itemCount=2 -> Lightning threshold=6, Fast threshold=12.
            var result = calculator.CalculateTip(CreateTicket(2, PatienceType.Normal, 100f, elapsedSeconds: 7f));

            Assert.AreEqual(SpeedTier.Fast, result.SpeedTier);
            Assert.AreEqual(config.FastMultiplier, result.SpeedMultiplier, 0.0001f);
        }

        [Test]
        public void CalculateTip_ElapsedBeyondBothThresholds_IsStandardTier_WithNoMultiplier()
        {
            var config = CreateEconomyConfig(lightningSecondsPerItem: 3f, fastSecondsPerItem: 6f, standardMultiplier: 1f);
            var calculator = new EconomyCalculator(config);

            // itemCount=2 -> Fast threshold=12.
            var result = calculator.CalculateTip(CreateTicket(2, PatienceType.Normal, 100f, elapsedSeconds: 13f));

            Assert.AreEqual(SpeedTier.Standard, result.SpeedTier);
            Assert.AreEqual(1f, result.SpeedMultiplier, 0.0001f);
        }

        [Test]
        public void CalculateTip_SpeedThresholds_ScaleWithItemCount_NotFixedSeconds()
        {
            var config = CreateEconomyConfig(lightningSecondsPerItem: 3f, fastSecondsPerItem: 6f);
            var calculator = new EconomyCalculator(config);

            // Same 4s elapsed: 1-item threshold is 3s (already past it, so Fast),
            // 2-item threshold is 6s (still within it, so Lightning).
            var oneItem = calculator.CalculateTip(CreateTicket(1, PatienceType.Normal, 100f, elapsedSeconds: 4f));
            var twoItems = calculator.CalculateTip(CreateTicket(2, PatienceType.Normal, 100f, elapsedSeconds: 4f));

            Assert.AreEqual(SpeedTier.Fast, oneItem.SpeedTier);
            Assert.AreEqual(SpeedTier.Lightning, twoItems.SpeedTier);
        }

        [Test]
        public void CalculateTip_PatienceDecay_IsFullBeforeFirstThreshold()
        {
            var config = CreateEconomyConfig();
            SetPatienceDecaySteps(config, PatienceType.Impatient, (2f, 0.7f), (4f, 0.4f));
            var calculator = new EconomyCalculator(config);

            var result = calculator.CalculateTip(CreateTicket(1, PatienceType.Impatient, 100f, elapsedSeconds: 1f));

            Assert.AreEqual(1f, result.PatienceDecayCoefficient, 0.0001f);
        }

        [Test]
        public void CalculateTip_PatienceDecay_HoldsFlatBetweenSteps_InsteadOfInterpolating()
        {
            var config = CreateEconomyConfig();
            SetPatienceDecaySteps(config, PatienceType.Impatient, (2f, 0.7f), (4f, 0.4f));
            var calculator = new EconomyCalculator(config);

            var atFirstStep = calculator.CalculateTip(CreateTicket(1, PatienceType.Impatient, 100f, elapsedSeconds: 2f));
            var stillHoldingBeforeSecondStep = calculator.CalculateTip(CreateTicket(1, PatienceType.Impatient, 100f, elapsedSeconds: 3f));

            Assert.AreEqual(0.7f, atFirstStep.PatienceDecayCoefficient, 0.0001f);
            Assert.AreEqual(0.7f, stillHoldingBeforeSecondStep.PatienceDecayCoefficient, 0.0001f);
        }

        [Test]
        public void CalculateTip_PatienceDecay_DropsAtNextThreshold_AndHoldsThereafter()
        {
            var config = CreateEconomyConfig();
            SetPatienceDecaySteps(config, PatienceType.Impatient, (2f, 0.7f), (4f, 0.4f));
            var calculator = new EconomyCalculator(config);

            var atSecondStep = calculator.CalculateTip(CreateTicket(1, PatienceType.Impatient, 100f, elapsedSeconds: 4f));
            var wellPastLastStep = calculator.CalculateTip(CreateTicket(1, PatienceType.Impatient, 100f, elapsedSeconds: 100f));

            Assert.AreEqual(0.4f, atSecondStep.PatienceDecayCoefficient, 0.0001f);
            Assert.AreEqual(0.4f, wellPastLastStep.PatienceDecayCoefficient, 0.0001f);
        }

        [Test]
        public void CalculateTip_PatienceDecayThresholds_ScaleWithItemCount()
        {
            var config = CreateEconomyConfig();
            SetPatienceDecaySteps(config, PatienceType.Impatient, (2f, 0.7f));
            var calculator = new EconomyCalculator(config);

            // Same 3s elapsed: 1-item threshold is 2s (already crossed -> decayed),
            // 2-item threshold is 4s (not yet crossed -> still full tip).
            var oneItem = calculator.CalculateTip(CreateTicket(1, PatienceType.Impatient, 100f, elapsedSeconds: 3f));
            var twoItems = calculator.CalculateTip(CreateTicket(2, PatienceType.Impatient, 100f, elapsedSeconds: 3f));

            Assert.AreEqual(0.7f, oneItem.PatienceDecayCoefficient, 0.0001f);
            Assert.AreEqual(1f, twoItems.PatienceDecayCoefficient, 0.0001f);
        }

        [Test]
        public void CalculateTip_PatienceTypeWithEmptyStepsList_DefaultsToFullTip()
        {
            var config = CreateEconomyConfig();
            SetPatienceDecaySteps(config, PatienceType.Impatient, (2f, 0.7f));
            var calculator = new EconomyCalculator(config);

            var result = calculator.CalculateTip(CreateTicket(1, PatienceType.Patient, 100f, elapsedSeconds: 50f));

            Assert.AreEqual(1f, result.PatienceDecayCoefficient, 0.0001f);
        }

        [Test]
        public void CalculateTip_TotalTip_IsProductOfBaseTipSpeedMultiplierAndPatienceDecay()
        {
            var config = CreateEconomyConfig(baseTipPerItem: 10f, fastMultiplier: 1.5f, lightningSecondsPerItem: 1f, fastSecondsPerItem: 6f);
            SetPatienceDecaySteps(config, PatienceType.Normal, (2f, 0.5f));
            var calculator = new EconomyCalculator(config);

            // itemCount=1 -> Fast tier (elapsed 3 > lightning threshold 1, <= fast threshold 6),
            // and past the 2s decay threshold -> coefficient 0.5.
            var result = calculator.CalculateTip(CreateTicket(1, PatienceType.Normal, 100f, elapsedSeconds: 3f));

            Assert.AreEqual(10f, result.BaseTip, 0.0001f);
            Assert.AreEqual(1.5f, result.SpeedMultiplier, 0.0001f);
            Assert.AreEqual(0.5f, result.PatienceDecayCoefficient, 0.0001f);
            Assert.AreEqual(10f * 1.5f * 0.5f, result.TotalTip, 0.0001f);
        }
    }
}
