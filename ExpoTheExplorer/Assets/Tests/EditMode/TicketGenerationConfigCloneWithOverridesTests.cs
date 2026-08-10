using System.Collections.Generic;
using ExpoTheExplorer.Data;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace ExpoTheExplorer.Tests.EditMode
{
    public class TicketGenerationConfigCloneWithOverridesTests
    {
        private TicketGenerationConfig config;
        private readonly List<Object> spawned = new();

        [SetUp]
        public void SetUp()
        {
            config = ScriptableObject.CreateInstance<TicketGenerationConfig>();

            var serialized = new SerializedObject(config);
            serialized.FindProperty("sideInclusionChance").floatValue = 0.5f;
            serialized.FindProperty("drinkInclusionChance").floatValue = 0.5f;
            serialized.FindProperty("modificationCountLambda").floatValue = 1f;
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        [TearDown]
        public void TearDown()
        {
            Object.DestroyImmediate(config);
            foreach (var o in spawned) Object.DestroyImmediate(o);
            spawned.Clear();
        }

        [Test]
        public void CloneWithOverrides_NoOverridesGiven_MatchesBaseValues()
        {
            var clone = config.CloneWithOverrides();
            spawned.Add(clone);

            Assert.AreEqual(config.SideInclusionChance, clone.SideInclusionChance);
            Assert.AreEqual(config.DrinkInclusionChance, clone.DrinkInclusionChance);
            Assert.AreEqual(config.ModificationCountLambda, clone.ModificationCountLambda);
        }

        [Test]
        public void CloneWithOverrides_GivenOverrides_AppliesThem()
        {
            var clone = config.CloneWithOverrides(sideInclusionChance: 0f, drinkInclusionChance: 1f, modificationCountLambda: 3f);
            spawned.Add(clone);

            Assert.AreEqual(0f, clone.SideInclusionChance);
            Assert.AreEqual(1f, clone.DrinkInclusionChance);
            Assert.AreEqual(3f, clone.ModificationCountLambda);
        }

        [Test]
        public void CloneWithOverrides_ReturnsDistinctInstance_DoesNotMutateBase()
        {
            var clone = config.CloneWithOverrides(sideInclusionChance: 0f);
            spawned.Add(clone);

            Assert.AreNotSame(config, clone);
            Assert.AreEqual(0.5f, config.SideInclusionChance);
        }
    }
}
