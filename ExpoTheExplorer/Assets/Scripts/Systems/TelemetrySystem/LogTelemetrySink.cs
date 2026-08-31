using System.Collections.Generic;
using UnityEngine;

namespace ExpoTheExplorer.Systems.TelemetrySystem
{
    // Prints run snapshots to the Console instead of sending them anywhere.
    //
    // It is not a stub or a placeholder -- it is how Step 3 of the plan is BUILT and
    // VERIFIED, before Firebase is wired in at all. The whole run lifecycle (when a run
    // opens, what it counts, which of completed/failed/quit it lands on, and the nine
    // scenarios in plan §E.5) is watchable here, with no network, no auth, and no
    // possibility of confusing a telemetry bug with a connectivity one. When
    // FirestoreTelemetrySink arrives, the only thing that changes is which sink is
    // installed; if the numbers were right here they are right there.
    //
    // It also stays useful afterwards, as the fallback when Firebase fails to
    // initialise: the counters keep running and stay visible, and the game does not
    // care either way.
    //
    // Always ready -- Debug.Log cannot fail to be available, and a sink that reported
    // false here would make the caller skip writes for no reason.
    public class LogTelemetrySink : ITelemetrySink
    {
        public bool IsReady => true;

        private readonly bool verbose;

        // verbose:false prints one readable line per snapshot, which is what a playtest
        // wants -- this fires every 15 seconds, between real gameplay logs, and a
        // 20-line dump would bury them. verbose:true prints every field, which is what
        // you want exactly once: while checking that the document going to Firestore
        // has the shape and the field NAMES the schema expects.
        public LogTelemetrySink(bool verbose = false)
        {
            this.verbose = verbose;
        }

        public void WriteRun(string runId, IReadOnlyDictionary<string, object> fields)
        {
            if (!verbose)
            {
                Debug.Log($"[Telemetry] {Summarize(runId, fields)}");
                return;
            }

            var text = new System.Text.StringBuilder();
            text.Append("[Telemetry] ").Append(runId).Append('\n');
            foreach (var pair in fields)
            {
                text.Append("    ").Append(pair.Key).Append(": ").Append(pair.Value).Append('\n');
            }

            Debug.Log(text.ToString());
        }

        // Reads the same map the Firestore document is built from rather than taking a
        // RunTelemetryState, so that what gets printed is literally what would be SENT.
        // A summary built from the object instead could stay correct while ToFieldMap
        // was broken, which is the one bug this sink exists to catch early.
        private static string Summarize(string runId, IReadOnlyDictionary<string, object> fields)
        {
            return $"{runId} " +
                   $"day={Field(fields, "dayContentIndex")} " +
                   $"{Field(fields, "status")} " +
                   $"{Field(fields, "elapsedSeconds")}s " +
                   $"delivered={Field(fields, "ticketsDelivered")} " +
                   $"mistakes={Field(fields, "mistakeCount")} " +
                   $"score={Field(fields, "score")} " +
                   $"stars={Field(fields, "stars")} " +
                   $"player={Field(fields, "playerId")}";
        }

        private static string Field(IReadOnlyDictionary<string, object> fields, string key)
        {
            return fields.TryGetValue(key, out var value) ? value?.ToString() ?? "-" : "?";
        }
    }
}
