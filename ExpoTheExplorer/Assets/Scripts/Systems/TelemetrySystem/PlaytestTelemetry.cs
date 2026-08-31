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
