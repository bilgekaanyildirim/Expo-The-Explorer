using System.Collections.Generic;
using ExpoTheExplorer.Data;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace ExpoTheExplorer.Tests.EditMode
{
    // Successor to TicketGenerationConfigCloneWithOverridesTests. The old method took
    // nullable parameters and left unspecified fields inherited from the base asset; there
    // is no override layer any more, so every value is required and the "no overrides
    // given" case it used to cover no longer exists (D-006). What still matters, and is
    // what this suite keeps: the clone gets all five values, and the shared asset is not
    // mutated by generating a Day.
    public class TicketGenerationConfigCloneForDayGenerationTests
    {
        private TicketGenerationConfig config;
        private FoodItemConfig burger;
        private readonly List<Object> spawned = new();

        [SetUp]
        public void SetUp()
        {
            burger = ScriptableObject.CreateInstance<FoodItemConfig>();
            spawned.Add(burger);

            config = ScriptableObject.CreateInstance<TicketGenerationConfig>();

            var serialized = new SerializedObject(config);
            serialized.FindProperty("sideInclusionChance").floatValue = 0.5f;
            serialized.FindProperty("drinkInclusionChance").floatValue = 0.5f;
            serialized.FindProperty("modificationCountLambda").floatValue = 1f;
            serialized.FindProperty("modificationAdditionChance").floatValue = 0.5f;
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        [TearDown]
        public void TearDown()
        {
            Object.DestroyImmediate(config);
            foreach (var o in spawned) Object.DestroyImmediate(o);
            spawned.Clear();
        }

        private TicketGenerationConfig CloneWith(
            float side = 0f, float drink = 1f, float modLambda = 3f, float modAddChance = 0.25f,
            List<MainDishWeight> weights = null)
        {
            var clone = config.CloneForDayGeneration(side, drink, modLambda, modAddChance, weights);
            spawned.Add(clone);
            return clone;
        }

        [Test]
        public void CloneForDayGeneration_AppliesEveryValue()
        {
            var clone = CloneWith(weights: new List<MainDishWeight> { new(burger, 3f, 2f, 4) });

            Assert.AreEqual(0f, clone.SideInclusionChance);
            Assert.AreEqual(1f, clone.DrinkInclusionChance);
            Assert.AreEqual(3f, clone.ModificationCountLambda);
            Assert.AreEqual(0.25f, clone.ModificationAdditionChance);
            Assert.AreEqual(1, clone.MainDishWeights.Count);
            Assert.AreSame(burger, clone.MainDishWeights[0].Food);
            Assert.AreEqual(3f, clone.MainDishWeights[0].Weight);
            Assert.AreEqual(2f, clone.MainDishWeights[0].ModificationCountLambda);
            Assert.AreEqual(4, clone.MainDishWeights[0].MaxModificationCount);
        }

        // The ctor takes the raw JSON value, so a Day file written before maxModificationCount
        // existed reaches it as 0. Read literally that would cap the dish at zero modifications
        // -- the getter normalizes it back to the default instead, which is what keeps every
        // already-authored Day generating the same tickets it did before the field.
        [Test]
        public void MainDishWeight_ZeroMaxModificationCount_ReadsBackAsTheDefault()
        {
            var legacy = new MainDishWeight(burger, 3f, 2f, 0);

            Assert.AreEqual(MainDishWeight.DefaultMaxModificationCount, legacy.MaxModificationCount);
        }

        // The asset is shared across every Day in the project, so generating one Day must
        // not change what the next one starts from.
        [Test]
        public void CloneForDayGeneration_ReturnsDistinctInstance_DoesNotMutateBase()
        {
            var clone = CloneWith(weights: new List<MainDishWeight> { new(burger, 3f, 2f, 4) });

            Assert.AreNotSame(config, clone);
            Assert.AreEqual(0.5f, config.SideInclusionChance);
            Assert.AreEqual(0.5f, config.DrinkInclusionChance);
            Assert.AreEqual(1f, config.ModificationCountLambda);
            Assert.AreEqual(0.5f, config.ModificationAdditionChance);
            CollectionAssert.IsEmpty(config.MainDishWeights);
        }

        // A Day that lists no main-dish weights is a real authoring state (every Main falls
        // back to DefaultWeight), so it has to produce an empty list rather than a null the
        // TicketFactory would enumerate straight into a NullReferenceException.
        [Test]
        public void CloneForDayGeneration_NullWeights_BecomesEmptyList()
        {
            var clone = CloneWith(weights: null);

            Assert.IsNotNull(clone.MainDishWeights);
            CollectionAssert.IsEmpty(clone.MainDishWeights);
        }
    }
}
