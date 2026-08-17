using ExpoTheExplorer.Core;
using ExpoTheExplorer.Data;
using NUnit.Framework;

namespace ExpoTheExplorer.Tests.EditMode
{
    // Successor to TicketGenerationConfigExtensionsTests: the patience -> time-limit switch
    // now hangs off TicketRuntimeSettings, because those three values are authored per Day
    // rather than read from the shared config asset (decisions.md D-005). No ScriptableObject
    // scaffolding needed any more -- the settings object is plain data.
    public class TicketRuntimeSettingsExtensionsTests
    {
        private static TicketRuntimeSettings CreateSettings() => new(
            impatientTimeLimitSeconds: 10f,
            normalTimeLimitSeconds: 20f,
            patientTimeLimitSeconds: 30f,
            upcomingQueueSize: 10);

        [Test]
        public void TimeLimitSecondsFor_Impatient_ReturnsImpatientValue()
        {
            Assert.AreEqual(10f, CreateSettings().TimeLimitSecondsFor(PatienceType.Impatient));
        }

        [Test]
        public void TimeLimitSecondsFor_Normal_ReturnsNormalValue()
        {
            Assert.AreEqual(20f, CreateSettings().TimeLimitSecondsFor(PatienceType.Normal));
        }

        [Test]
        public void TimeLimitSecondsFor_Patient_ReturnsPatientValue()
        {
            Assert.AreEqual(30f, CreateSettings().TimeLimitSecondsFor(PatienceType.Patient));
        }

        // The three limits are what a Day authors; two Days are free to disagree, and this
        // is the seam that has to carry that difference through to a Ticket.
        [Test]
        public void TimeLimitSecondsFor_TwoDifferentSettings_ReturnDifferentLimits()
        {
            var lenient = new TicketRuntimeSettings(60f, 120f, 200f, 10);

            Assert.AreEqual(10f, CreateSettings().TimeLimitSecondsFor(PatienceType.Impatient));
            Assert.AreEqual(60f, lenient.TimeLimitSecondsFor(PatienceType.Impatient));
        }
    }
}
