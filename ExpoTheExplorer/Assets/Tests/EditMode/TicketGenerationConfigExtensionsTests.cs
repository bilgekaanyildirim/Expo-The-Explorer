using ExpoTheExplorer.Core;
using ExpoTheExplorer.Data;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace ExpoTheExplorer.Tests.EditMode
{
    public class TicketGenerationConfigExtensionsTests
    {
        private TicketGenerationConfig config;

        [SetUp]
        public void SetUp()
        {
            config = ScriptableObject.CreateInstance<TicketGenerationConfig>();

            var serialized = new SerializedObject(config);
            serialized.FindProperty("impatientTimeLimitSeconds").floatValue = 10f;
            serialized.FindProperty("normalTimeLimitSeconds").floatValue = 20f;
            serialized.FindProperty("patientTimeLimitSeconds").floatValue = 30f;
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        [TearDown]
        public void TearDown()
        {
            Object.DestroyImmediate(config);
        }

        [Test]
        public void TimeLimitSecondsFor_Impatient_ReturnsImpatientValue()
        {
            Assert.AreEqual(10f, config.TimeLimitSecondsFor(PatienceType.Impatient));
        }

        [Test]
        public void TimeLimitSecondsFor_Normal_ReturnsNormalValue()
        {
            Assert.AreEqual(20f, config.TimeLimitSecondsFor(PatienceType.Normal));
        }

        [Test]
        public void TimeLimitSecondsFor_Patient_ReturnsPatientValue()
        {
            Assert.AreEqual(30f, config.TimeLimitSecondsFor(PatienceType.Patient));
        }
    }
}
