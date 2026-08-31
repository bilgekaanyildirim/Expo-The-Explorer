using System;
using System.Collections.Generic;
using ExpoTheExplorer.Systems.TelemetrySystem;
using Firebase.Extensions;
using Firebase.Firestore;
using UnityEngine;

namespace ExpoTheExplorer.Telemetry
{
    // Writes run snapshots to Cloud Firestore. THE ONLY FILE IN THIS PROJECT THAT NAMES
    // FirebaseFirestore, which is the entire point of ITelemetrySink existing: swapping
    // backends means replacing this class, and nothing above it -- not the binder, not
    // the run state, and certainly not gameplay -- knows what a document is.
    //
    // It lives in Assembly-CSharp because the SDK's DLLs under Assets/Firebase/Plugins
    // are auto-referenced by the predefined assembly and by no asmdef. That constraint
    // is what keeps the TelemetrySystem assembly Firebase-free and testable
    // (decisions.md D-150).
    //
    // IT NEVER THROWS. A telemetry failure must not reach gameplay (plan §L), so every
    // path logs and returns -- and there is no retry: the next heartbeat carries the
    // whole document again, which makes a lost write self-healing without a queue,
    // a backoff or a background job (plan §14).
    public class FirestoreTelemetrySink : ITelemetrySink
    {
        // Matches the security rules published for this project: `runs/{runId}` is the
        // only path any client may write, and even there only create and update.
        private const string Collection = "runs";

        // Spelled through RunStatus rather than as a literal, so the sink and the state
        // cannot disagree about what "completed" is -- ToWireValue stays the one place
        // those four words exist.
        private static readonly string CompletedStatus = RunStatus.Completed.ToWireValue();

        private readonly FirebaseFirestore firestore;
        private readonly string authUid;

        // Which runs this process has already written once. It exists for one reason:
        // every write is a MERGE, so re-sending startedAt on each heartbeat would push
        // a run's start time forward every 15 seconds and make every run in the report
        // look instantaneous. Runs are minted per Day attempt, so this holds a handful
        // of short strings per session and dies with the process.
        private readonly HashSet<string> startedRuns = new HashSet<string>();

        public int WritesOk { get; private set; }
        public int WritesFailed { get; private set; }

        // Always ready once constructed: FirebaseBootstrap only builds this after
        // dependencies check out AND an anonymous sign-in succeeds, so a live instance
        // is by definition usable. Reporting false here would make callers skip writes
        // for a condition that cannot happen.
        public bool IsReady => true;

        public FirestoreTelemetrySink(FirebaseFirestore firestore, string authUid)
        {
            this.firestore = firestore;
            this.authUid = authUid;
        }

        public void WriteRun(string runId, IReadOnlyDictionary<string, object> fields)
        {
            if (string.IsNullOrEmpty(runId) || fields == null) return;

            try
            {
                // Copied rather than passed through, because the four fields below are
                // this layer's to add: RunTelemetryState has no Firebase reference and
                // must not gain one, so it cannot produce a server timestamp or know
                // what a uid is. Two Step 3 tests exist purely to keep it that way.
                var document = new Dictionary<string, object>(fields.Count + 4);
                foreach (var pair in fields) document[pair.Key] = pair.Value;

                // The rules require this to equal the caller's own uid. It is also the
                // only link back to the Firebase account that wrote the row -- the
                // playtester identity is playerId, which is ours and means something
                // different (plan §C).
                document["authUid"] = authUid;

                // SERVER time, not device time, and that is deliberate for every one of
                // these: a playtest phone's clock can be hours off, and a report that
                // orders attempts by a lying clock is worse than one with no times.
                document["lastUpdatedAt"] = FieldValue.ServerTimestamp;

                if (startedRuns.Add(runId))
                {
                    document["startedAt"] = FieldValue.ServerTimestamp;
                }

                // Only a COMPLETED run gets this. A failed or abandoned one leaves it
                // null and is read through lastUpdatedAt, which the terminal write has
                // just set -- so "when did this end" is answerable for every run, while
                // "when was it finished" stays honest about only the ones that were.
                if (IsCompleted(fields))
                {
                    document["completedAt"] = FieldValue.ServerTimestamp;
                }

                firestore
                    .Collection(Collection)
                    .Document(runId)
                    .SetAsync(document, SetOptions.MergeAll)
                    .ContinueWithOnMainThread(task => OnWriteFinished(runId, task));
            }
            catch (Exception e)
            {
                // Reached only if the call could not even be STARTED. A fault in the
                // write itself arrives at OnWriteFinished instead.
                WritesFailed++;
                Debug.LogError($"[Telemetry] Could not start a write for {runId}: {Describe(e)}");
            }
        }

        // Runs on the main thread (ContinueWithOnMainThread), which matters because it
        // touches Unity's logging and this object's counters. Nothing in this class may
        // be mutated from a background continuation.
        private void OnWriteFinished(string runId, System.Threading.Tasks.Task task)
        {
            if (task.IsFaulted || task.IsCanceled)
            {
                WritesFailed++;

                // Named rather than swallowed, because the two likely causes need
                // opposite responses: PERMISSION_DENIED means the rules rejected the
                // document's shape (a code or rules problem, and every write will fail
                // the same way), while a transport error is usually transient and the
                // next heartbeat resends everything anyway.
                Debug.LogError(
                    $"[Telemetry] Write failed for {runId}: {Describe(task.Exception)}. " +
                    "PERMISSION_DENIED here means the security rules rejected it.");
                return;
            }

            WritesOk++;
        }

        private static bool IsCompleted(IReadOnlyDictionary<string, object> fields)
        {
            return fields.TryGetValue("status", out var status)
                   && status as string == CompletedStatus;
        }

        // Firebase wraps failures in AggregateException, whose own message is the
        // useless "One or more errors occurred." The inner exception is the diagnosis.
        private static string Describe(Exception e)
        {
            if (e == null) return "unknown error";
            var inner = e is AggregateException aggregate ? aggregate.Flatten().InnerException ?? e : e;
            return $"{inner.GetType().Name}: {inner.Message}";
        }
    }
}
