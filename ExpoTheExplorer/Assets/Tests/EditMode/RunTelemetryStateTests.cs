using System.Collections.Generic;
using System.Linq;
using ExpoTheExplorer.Systems.TelemetrySystem;
using NUnit.Framework;

namespace ExpoTheExplorer.Tests.EditMode
{
    // What CAN be pinned here, and what deliberately cannot.
    //
    // The nine run scenarios in .claude/telemetry-plan.md §E.5 -- Continue keeping one
    // run alive, a lost day landing on `failed`, a Continue-then-quit landing on `quit`
    // -- all run through TelemetryBinder, which needs GameManager, which lives in
    // Assembly-CSharp and cannot be referenced by an asmdef test assembly (D-012). So
    // those are verified by playing the game and reading the Console, and the preflight
    // says so rather than pretending otherwise.
    //
    // What is pinned here is the part that would break SILENTLY: the shape of the
    // document. A renamed field does not throw, does not fail a build and does not look
    // wrong in the Console -- it just quietly stops matching every Firestore query and
    // every security rule that names it, and the first symptom is a report that has
    // been missing rows for a week.
    public class RunTelemetryStateTests
    {
        private static RunTelemetryState NewRun(int dayIndex = 7, int dayContentIndex = 8)
        {
            return new RunTelemetryState(
                "R_TESTAAAA", "P_TEST01", "I_TEST0001", dayIndex, dayContentIndex, "editor");
        }

        // ---- the document's shape ----------------------------------------------------

        [Test]
        public void ToFieldMap_CarriesEveryFieldTheSchemaPromises()
        {
            // This list is the schema (plan §F.3). If a field is renamed and this test is
            // "fixed" by renaming it here too, that is the moment to go and check the
            // security rules and every saved query -- not a formality.
            var expected = new[]
            {
                "schemaVersion", "environment",
                "runId", "playerId", "installationId",
                "dayIndex", "dayContentIndex", "dayNumberShown",
                "status", "elapsedSeconds",
                "mistakeCount", "wrongDeliveryCount", "timeoutCount",
                "ticketsDelivered", "ticketsExpired",
                "score", "stars", "starScore", "livesDepletedCount",
                "buildVersion", "platform", "deviceModel", "unityVersion",
            };

            var keys = NewRun().ToFieldMap().Keys;

            CollectionAssert.AreEquivalent(expected, keys);
        }

        [Test]
        public void ToFieldMap_DoesNotCarryAuthUid_BecauseTheSinkOwnsIt()
        {
            // The contract that keeps this assembly Firebase-free: authUid comes from
            // FirebaseAuth, so the sink adds it. If it ever appears here, something has
            // handed this assembly a Firebase reference it must not have -- and the
            // security rules would start rejecting writes the moment the two disagreed.
            Assert.IsFalse(NewRun().ToFieldMap().ContainsKey("authUid"));
        }

        [Test]
        public void ToFieldMap_DoesNotCarryTimestamps_BecauseTheyAreServerStamps()
        {
            // startedAt / lastUpdatedAt / completedAt are Firestore server values (plan
            // §F.3). A playtest device's clock can be hours off, so anything this class
            // stamped would order the report wrongly and look authoritative doing it.
            var map = NewRun().ToFieldMap();

            Assert.IsFalse(map.ContainsKey("startedAt"));
            Assert.IsFalse(map.ContainsKey("lastUpdatedAt"));
            Assert.IsFalse(map.ContainsKey("completedAt"));
        }

        [Test]
        public void ToFieldMap_WritesTheValuesItWasGiven()
        {
            var run = NewRun(dayIndex: 7, dayContentIndex: 8);
            run.Status = RunStatus.Failed;
            run.ElapsedSeconds = 81.5f;
            run.WrongDeliveryCount = 3;
            run.TimeoutCount = 2;
            run.TicketsDelivered = 5;
            run.Score = 1250;
            run.Stars = 2;
            run.LivesDepletedCount = 1;

            var map = run.ToFieldMap();

            Assert.AreEqual("R_TESTAAAA", map["runId"]);
            Assert.AreEqual("P_TEST01", map["playerId"]);
            Assert.AreEqual("I_TEST0001", map["installationId"]);
            Assert.AreEqual("editor", map["environment"]);
            Assert.AreEqual(7, map["dayIndex"]);
            Assert.AreEqual(8, map["dayContentIndex"]);
            Assert.AreEqual("failed", map["status"]);
            Assert.AreEqual(81.5f, map["elapsedSeconds"]);
            Assert.AreEqual(5, map["mistakeCount"]);
            Assert.AreEqual(1250, map["score"]);
            Assert.AreEqual(1, map["livesDepletedCount"]);
        }

        // ---- derived figures ---------------------------------------------------------

        [Test]
        public void MistakeCount_IsDerived_SoItCannotDisagreeWithItsTwoTerms()
        {
            var run = NewRun();
            run.WrongDeliveryCount = 4;
            run.TimeoutCount = 2;

            Assert.AreEqual(6, run.MistakeCount);

            run.TimeoutCount = 3;
            Assert.AreEqual(7, run.MistakeCount, "It is a property, not a stored total.");
        }

        [Test]
        public void TicketsExpired_IsTheTimeoutCount_UnderTheNameAReportAsksFor()
        {
            var run = NewRun();
            run.TimeoutCount = 3;

            Assert.AreEqual(3, run.TicketsExpired);
        }

        [Test]
        public void DayNumberShown_IsTheDayIndexPlusOne_BecausePlayersCountFromOne()
        {
            Assert.AreEqual(9, NewRun(dayIndex: 8).DayNumberShown);
            Assert.AreEqual(1, NewRun(dayIndex: 0).DayNumberShown);
        }

        [Test]
        public void ANewRun_StartsInProgress_WithNothingCounted()
        {
            var run = NewRun();

            Assert.AreEqual(RunStatus.InProgress, run.Status);
            Assert.AreEqual(0, run.MistakeCount);
            Assert.AreEqual(0, run.TicketsDelivered);
            Assert.AreEqual(0, run.LivesDepletedCount);
            Assert.AreEqual(0f, run.ElapsedSeconds);
        }

        // ---- the four status words ---------------------------------------------------

        [Test]
        public void EveryStatus_HasItsWireSpelling()
        {
            // These four strings are what every balancing query filters on. They are
            // spelled in exactly one place so a typo cannot invent a fifth status that
            // silently matches nothing.
            Assert.AreEqual("in_progress", RunStatus.InProgress.ToWireValue());
            Assert.AreEqual("completed", RunStatus.Completed.ToWireValue());
            Assert.AreEqual("failed", RunStatus.Failed.ToWireValue());
            Assert.AreEqual("quit", RunStatus.Quit.ToWireValue());
        }

        // ---- run ids -----------------------------------------------------------------

        [Test]
        public void NewRunId_CarriesItsPrefix_AndDoesNotRepeat()
        {
            // A run id is a Firestore DOCUMENT id, so a collision would merge two
            // attempts into one row rather than producing a visible error.
            var ids = Enumerable.Range(0, 200).Select(_ => TelemetryIds.NewRunId()).ToList();

            Assert.IsTrue(ids.All(id => id.StartsWith("R_")));
            Assert.AreEqual(ids.Count, ids.Distinct().Count());
        }

        // ---- the sink seam -----------------------------------------------------------

        [Test]
        public void ASink_ReceivesTheRunIdAndTheWholeDocument()
        {
            // Proves the seam Step 4 swaps Firestore into: whatever the sink is, it is
            // handed the document id and every field, never a delta -- which is what
            // makes a lost write self-healing, since the next heartbeat carries
            // everything the lost one did.
            var sink = new RecordingSink();
            var run = NewRun();
            run.TicketsDelivered = 4;

            sink.WriteRun(run.RunId, run.ToFieldMap());

            Assert.AreEqual(1, sink.Writes.Count);
            Assert.AreEqual("R_TESTAAAA", sink.Writes[0].Key);
            Assert.AreEqual(4, sink.Writes[0].Value["ticketsDelivered"]);
        }

        private class RecordingSink : ITelemetrySink
        {
            public readonly List<KeyValuePair<string, IReadOnlyDictionary<string, object>>> Writes =
                new List<KeyValuePair<string, IReadOnlyDictionary<string, object>>>();

            public bool IsReady => true;

            public void WriteRun(string runId, IReadOnlyDictionary<string, object> fields)
            {
                Writes.Add(new KeyValuePair<string, IReadOnlyDictionary<string, object>>(runId, fields));
            }
        }
    }
}
