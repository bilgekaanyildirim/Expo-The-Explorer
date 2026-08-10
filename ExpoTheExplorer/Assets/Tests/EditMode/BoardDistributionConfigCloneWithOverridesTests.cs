using System.Collections.Generic;
using ExpoTheExplorer.Data;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace ExpoTheExplorer.Tests.EditMode
{
    public class BoardDistributionConfigCloneWithOverridesTests
    {
        private BoardDistributionConfig config;
        private readonly List<Object> spawned = new();

        [SetUp]
        public void SetUp()
        {
            config = ScriptableObject.CreateInstance<BoardDistributionConfig>();

            var serialized = new SerializedObject(config);
            serialized.FindProperty("noiseLeakCountLambda").floatValue = 0.5f;
            serialized.FindProperty("guaranteedTicketCount").intValue = 1;
            serialized.FindProperty("leakDepth").intValue = 10;
            serialized.FindProperty("maxLeakCount").intValue = 10;
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

            Assert.AreEqual(config.NoiseLeakCountLambda, clone.NoiseLeakCountLambda);
            Assert.AreEqual(config.GuaranteedTicketCount, clone.GuaranteedTicketCount);
            Assert.AreEqual(config.LeakDepth, clone.LeakDepth);
            Assert.AreEqual(config.MaxLeakCount, clone.MaxLeakCount);
        }

        [Test]
        public void CloneWithOverrides_GivenOverrides_AppliesThem()
        {
            var clone = config.CloneWithOverrides(noiseLeakCountLambda: 0f, guaranteedTicketCount: 4, leakDepth: 2, maxLeakCount: 3);
            spawned.Add(clone);

            Assert.AreEqual(0f, clone.NoiseLeakCountLambda);
            Assert.AreEqual(4, clone.GuaranteedTicketCount);
            Assert.AreEqual(2, clone.LeakDepth);
            Assert.AreEqual(3, clone.MaxLeakCount);
        }

        [Test]
        public void CloneWithOverrides_ReturnsDistinctInstance_DoesNotMutateBase()
        {
            var clone = config.CloneWithOverrides(guaranteedTicketCount: 4);
            spawned.Add(clone);

            Assert.AreNotSame(config, clone);
            Assert.AreEqual(1, config.GuaranteedTicketCount);
        }
    }
}
