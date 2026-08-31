using System;
using ExpoTheExplorer.Systems.TelemetrySystem;
using Firebase;
using Firebase.Auth;
using Firebase.Extensions;
using Firebase.Firestore;
using UnityEngine;

namespace ExpoTheExplorer.Telemetry
{
    // Brings Firebase up once per process and, if it comes up, installs the Firestore
    // sink (decisions.md D-150). It is the ONE writer of PlaytestTelemetry.Sink, which
    // that property's own note already promised.
    //
    // STATIC, WITH RuntimeInitializeOnLoadMethod, and both halves are forced rather
    // than chosen: initialisation must happen once per process before any scene needs
    // telemetry, and this project deliberately has no boot scene to hang it on (D-012).
    // The same reasoning made PlaytestTelemetry static in Step 2. Nothing here is a
    // service locator -- it resolves nothing on demand; it runs once and hands one
    // object to one property.
    //
    // FAILING IS A SUPPORTED OUTCOME, not an error path bolted on afterwards. If
    // dependencies are missing or sign-in fails, the LogTelemetrySink installed by
    // default simply stays, run tracking carries on into the Console, and the game
    // never learns that anything happened (plan §L). That is also what makes the whole
    // system safe to ship in a build with no network.
    public static class FirebaseBootstrap
    {
        public enum State
        {
            // Not started. In practice only visible for the moment before the runtime
            // hook fires, and permanently in a build where the hook is stripped.
            Idle,

            // Dependency check or sign-in in flight. Runs opened during this window are
            // logged, not lost: the sink swap is picked up by the next heartbeat, and
            // because every write carries the WHOLE document, that heartbeat also
            // carries everything the missed writes would have said.
            Initializing,

            // Firestore is installed and writing.
            Ready,

            // Something failed. The reason is in FailureReason and in the Console; the
            // Console sink is still running.
            Failed,
        }

        public static State Status { get; private set; } = State.Idle;

        public static string FailureReason { get; private set; }

        // Non-null only while Status is Ready. Exposed so the debug panel can show the
        // write counters -- during a playtest a rejected write is otherwise a single
        // red line in a Console nobody is watching.
        public static FirestoreTelemetrySink Sink { get; private set; }

        // A one-line answer for the SRDebugger readout.
        public static string StatusText =>
            Status == State.Failed ? $"FAILED: {FailureReason}" : Status.ToString();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Initialize()
        {
            Status = State.Initializing;
            FailureReason = null;
            Sink = null;

            try
            {
                FirebaseApp.CheckAndFixDependenciesAsync().ContinueWithOnMainThread(OnDependenciesChecked);
            }
            catch (Exception e)
            {
                // A throw here rather than a faulted task usually means the native
                // libraries did not load at all -- on macOS that is Gatekeeper
                // quarantining the .bundle files, which is documented in .gitignore
                // next to the re-import recipe.
                Fail($"{e.GetType().Name}: {e.Message}");
            }
        }

        private static void OnDependenciesChecked(System.Threading.Tasks.Task<DependencyStatus> task)
        {
            if (task.IsFaulted || task.IsCanceled)
            {
                Fail(Describe(task.Exception));
                return;
            }

            if (task.Result != DependencyStatus.Available)
            {
                Fail(task.Result.ToString());
                return;
            }

            SignIn();
        }

        // Anonymous auth, and it stays anonymous for the life of the install. The UID
        // is a WRITE PERMISSION, not an identity: the security rules check it, and who
        // is playing is answered by playerId, which lives in our own file and rotates
        // when a device is handed to the next tester (plan §C.1). Deliberately NOT
        // re-signed-in on "New Test Player": that would strand an unusable account per
        // reset and put an async wait in the middle of handing over a phone.
        private static void SignIn()
        {
            var auth = FirebaseAuth.DefaultInstance;

            // Reuses an existing session when the SDK already restored one, which it
            // does on every launch after the first -- so the usual path costs no
            // network round trip at all.
            if (auth.CurrentUser != null)
            {
                Install(auth.CurrentUser.UserId);
                return;
            }

            auth.SignInAnonymouslyAsync().ContinueWithOnMainThread(signIn =>
            {
                if (signIn.IsFaulted || signIn.IsCanceled)
                {
                    Fail($"{Describe(signIn.Exception)} (is Anonymous sign-in enabled in the console?)");
                    return;
                }

                // Read from `auth`, never from signIn.Result: the SDK changed that
                // task's type between versions (FirebaseUser -> AuthResult), and
                // touching Result would pin this file to one of them for no gain.
                var user = auth.CurrentUser;
                if (user == null)
                {
                    Fail("signed in, but CurrentUser is null");
                    return;
                }

                Install(user.UserId);
            });
        }

        private static void Install(string uid)
        {
            try
            {
                Sink = new FirestoreTelemetrySink(FirebaseFirestore.DefaultInstance, uid);
                PlaytestTelemetry.Sink = Sink;
                Status = State.Ready;

                Debug.Log($"[Telemetry] Firestore ready. uid={uid} player={PlaytestTelemetry.Identity.PlayerId}");
            }
            catch (Exception e)
            {
                Fail($"{e.GetType().Name}: {e.Message}");
            }
        }

        // One place to land, so the Console sink is never left half-replaced and the
        // reason is always both logged and readable from the debug panel.
        private static void Fail(string reason)
        {
            Status = State.Failed;
            FailureReason = reason;
            Sink = null;

            Debug.LogError(
                $"[Telemetry] Firebase unavailable ({reason}). Runs will be logged to the Console " +
                "instead; gameplay is unaffected.");
        }

        private static string Describe(Exception e)
        {
            if (e == null) return "unknown error";
            var inner = e is AggregateException aggregate ? aggregate.Flatten().InnerException ?? e : e;
            return $"{inner.GetType().Name}: {inner.Message}";
        }
    }
}
