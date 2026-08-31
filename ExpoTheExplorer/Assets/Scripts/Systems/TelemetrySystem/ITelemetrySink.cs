using System.Collections.Generic;

namespace ExpoTheExplorer.Systems.TelemetrySystem
{
    // Where a run snapshot goes. The project's ONE telemetry abstraction, and it was
    // deliberately not created until it had three jobs to do at once
    // (.claude/telemetry-plan.md §B.1):
    //
    //   1. it is the Firebase boundary -- FirestoreTelemetrySink is the only file in
    //      the project allowed to name FirebaseFirestore, and it lives on the far side
    //      of this interface, in Assembly-CSharp where the SDK's DLLs are reachable;
    //   2. it is what lets the whole run lifecycle be built and watched BEFORE Firebase
    //      exists, through LogTelemetrySink;
    //   3. it is the seam a test writes a fake against.
    //
    // Step 2 of the plan explicitly refused to add it early, when it would have had one
    // implementation and no consumer -- an interface with a single implementation is a
    // guess about the future wearing the costume of a design.
    //
    // ONE METHOD, and no Create/Update split. The distinction exists in Firestore only
    // as an argument to SetAsync, and pushing it up here would make every caller decide
    // whether a document already exists -- a question the caller cannot answer and does
    // not need to, since a merge write creates or updates identically.
    public interface ITelemetrySink
    {
        // False while a backend is still starting up, or permanently once it has failed.
        // Callers check it to skip work, never to retry: a failed telemetry backend must
        // stay quiet rather than turn into a background job (plan §14 -- no offline
        // queue, no retry pipeline).
        bool IsReady { get; }

        // Writes (or merges) the whole document. Always the WHOLE document, never a
        // delta: it makes a lost write self-healing, because the next heartbeat carries
        // everything the lost one did.
        //
        // Implementations must not throw. Telemetry failing is never allowed to reach
        // gameplay, so an implementation logs and returns (plan §L).
        void WriteRun(string runId, IReadOnlyDictionary<string, object> fields);
    }
}
