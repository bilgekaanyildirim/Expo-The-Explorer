using UnityEngine;

namespace ExpoTheExplorer.Systems.TelemetrySystem
{
    // The one surface everything else in the game talks to about telemetry, and the
    // only thing that holds a TelemetryIdentityStore alive across a scene load.
    //
    // WHY IT IS STATIC, given that this project has no singletons anywhere else:
    // its two consumers cannot be handed a reference. SROptions is constructed by
    // SRDebugger from a [RuntimeInitializeOnLoadMethod] with no scene presence at
    // all -- there is no Inspector slot to drag anything into, so D-013's "serialize
    // it, never search the scene" cannot apply here; there is nothing to serialize
    // ONTO. And an identity outlives every scene by definition, while this project
    // deliberately has no persistent scene (D-012) for anything to live on.
    //
    // WHY IT CACHES: SRDebugger polls its readouts while the panel is open, so a
    // store constructed per read would put a File.ReadAllText on that path several
    // times a second. Reading the file once per process is the whole job.
    //
    // IT IS DELIBERATELY DUMB. Every rule worth testing -- what a new player keeps,
    // what an unreadable file means, when a version is refused -- lives in
    // TelemetryIdentityStore, which takes an explicit path and an explicit clock and
    // is covered by TelemetryIdentityTests. This class is a cache and a hand-off; it
    // has no behaviour of its own to pin down, and giving it a test seam would be
    // machinery in production code paying for nothing.
    //
    // Step 3 of .claude/telemetry-plan.md hangs the run layer here (the active
    // RunTelemetryState and the ITelemetrySink). Nothing about runs exists yet.
    public static class PlaytestTelemetry
    {
        private static TelemetryIdentityStore store;
        private static TelemetryIdentity identity;

        // Loads on first use and mints an installation if there is nothing on disk --
        // see TelemetryIdentityStore.LoadOrCreate for why that write is not optional.
        //
        // In Step 2 the first use is a debug-panel readout, which means a release
        // build never creates the file. That is correct rather than a gap: nothing
        // reads telemetry in a release build yet. Step 3's TelemetryBinder becomes the
        // real creation trigger, at day start, in the builds that actually report.
        public static TelemetryIdentity Identity => identity ??= Store.LoadOrCreate();

        public static string IdentityFilePath => Store.FilePath;

        private static TelemetryIdentityStore Store => store ??= new TelemetryIdentityStore();

        // ---- runs (Step 3) -----------------------------------------------------------

        // Where snapshots go. Defaults to the Console so that everything downstream of
        // here works, and is watchable, with no Firebase in the project at all -- which
        // is exactly how Step 3 was built and tested.
        //
        // ONE WRITER, and it is FirebaseBootstrap (Step 4), which swaps in the Firestore
        // sink once the SDK reports itself healthy and leaves this default in place when
        // it does not. A settable static is the kind of thing that grows second writers,
        // so: nothing else assigns this. If a second assignment ever appears, the
        // question it is really asking is "which sink is authoritative", and the answer
        // belongs in one place rather than in whichever line ran last.
        public static ITelemetrySink Sink { get; set; } = new LogTelemetrySink();

        // The attempt being played right now, or null between attempts and on the main
        // screen. Published here rather than kept private to the binder so the SRDebugger
        // panel can show it, and so Step 4 has somewhere to look.
        //
        // ITS SINGLE WRITER IS TelemetryBinder, which is also its lifetime: the binder
        // dies with the day scene and clears this on the way out. That is why this class
        // holds a REFERENCE and not the logic -- a static that owned run lifetime would
        // outlive the scene that gave it meaning and hand the next scene a stale run.
        public static RunTelemetryState CurrentRun { get; private set; }

        public static void SetCurrentRun(RunTelemetryState run) => CurrentRun = run;

        // Which bucket this data belongs in (plan §M.4). DERIVED, never authored: a
        // toggle someone can forget to flip is how editor noise ends up averaged into
        // playtest results. Editor play-mode runs are still recorded -- they are useful
        // while building this -- they are just filterable in one `where` clause.
        public static string Environment => Application.isEditor ? "editor" : "playtest";

        // Hands the device to the next playtester: a new playerId, the SAME
        // installationId, and not one byte of Firestore touched.
        //
        // It does not clear the player's saved progress and must not learn how --
        // that is PlayerProfileStore's file and PlayerProfileStore's Delete. The debug
        // command that does both calls both, in that order, which keeps each file
        // with exactly one writer (the ownership rule this task's preflight records).
        //
        // `testerId` null mints a random id. A caller passing one has already run it
        // through TelemetryIds.TrySanitizeTesterId.
        public static void StartNewPlayer(string testerId = null)
        {
            var next = Store.NewPlayer(Identity, testerId);
            Store.Save(next);
            identity = next;

            Debug.Log(
                $"[Telemetry] New test player: {next.PlayerId} (#{next.PlayerOrdinal} on installation " +
                $"{next.InstallationId}). Previous player's Firestore runs are untouched.");
        }
    }
}
