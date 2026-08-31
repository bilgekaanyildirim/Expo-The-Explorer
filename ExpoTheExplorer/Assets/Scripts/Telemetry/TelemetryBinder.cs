using ExpoTheExplorer.Bootstrap;
using ExpoTheExplorer.Core;
using ExpoTheExplorer.Systems.TelemetrySystem;
using UnityEngine;

namespace ExpoTheExplorer.Telemetry
{
    // The one bridge between a running Day and the telemetry layer
    // (.claude/telemetry-plan.md Step 3). Everything Firebase-shaped lives behind
    // PlaytestTelemetry.Sink; everything gameplay-shaped is read through GameManager;
    // and this class is the only place the two meet.
    //
    // IT LIVES IN Assembly-CSharp AND MUST: it needs GameManager, which has no asmdef,
    // and an asmdef assembly cannot reference a predefined one. That is the same
    // constraint that puts HapticsBinder and SROptions.Expo here, and it is why the
    // TelemetrySystem assembly stays free of every gameplay type -- it is testable
    // precisely because it never learns what a GameState is.
    //
    // THE REFERENCE IS SERIALIZED AND A HUMAN DRAGS IT IN (D-013): nothing in this
    // project searches the scene at runtime. An empty slot is not an error, it is a
    // wiring step that has not happened yet, so it says so once, loudly, and switches
    // itself off -- telemetry going quiet must never be able to stop a Day being played.
    public class TelemetryBinder : MonoBehaviour
    {
        [SerializeField]
        [Tooltip("The GameManager on this object. Drag it in -- telemetry is inert without it.")]
        private GameManager gameManager;

        [SerializeField]
        [Tooltip("Seconds between snapshots while a Day is being played. This is the " +
                 "resolution of an abandoned run: a player who quits is last seen at " +
                 "most this long before they left.")]
        [Min(1f)]
        private float heartbeatSeconds = 15f;

        private GameState state;
        private RunTelemetryState run;
        private float secondsSinceSnapshot;

        // Start, never Awake, and this is the same rule DebugMenuBinder and the HUD
        // views follow: GameManager builds its session and its DayLifecycleManager in
        // its OWN Awake, and Unity does not order Awake across GameObjects. Reading any
        // of it in Awake is a race that resolves differently per scene load.
        private void Start()
        {
            if (gameManager == null)
            {
                Debug.LogWarning(
                    "[Telemetry] TelemetryBinder has no GameManager assigned, so no runs will be " +
                    "recorded. Drag the GameManager on this object into its slot.");
                enabled = false;
                return;
            }

            state = gameManager.State;
            if (state == null)
            {
                Debug.LogWarning("[Telemetry] TelemetryBinder found no GameState; no runs will be recorded.");
                enabled = false;
                return;
            }

            state.DaySessionStarted.Subscribe(OnDaySessionStarted);
            state.DayAttemptEnded.Subscribe(OnDayAttemptEnded);
            state.DayCompleted.Subscribe(OnDayCompleted);
            state.LivesDepleted.Subscribe(OnLivesDepleted);

            // THE FIRST RUN IS OPENED HERE, NOT BY THE EVENT, and that is not a
            // shortcut. DaySessionStarted for this Day already fired inside GameManager's
            // Awake, before this component could subscribe to anything -- so waiting for
            // it would miss every scene's opening run and record only its retries.
            // Every LATER run does come from the event, and no run is opened twice
            // because the event handler always closes the previous one first.
            OpenRun();
        }

        private void OnDestroy()
        {
            if (state != null)
            {
                state.DaySessionStarted.Unsubscribe(OnDaySessionStarted);
                state.DayAttemptEnded.Unsubscribe(OnDayAttemptEnded);
                state.DayCompleted.Unsubscribe(OnDayCompleted);
                state.LivesDepleted.Unsubscribe(OnLivesDepleted);
            }

            // FLUSHES BUT DOES NOT DECIDE. If the run is still in_progress here, nothing
            // in the game ever said how it ended -- the editor stopped, or the app is
            // going down -- and "in_progress with a stale timestamp" is the honest answer
            // analytics turns into an abandonment. Writing "quit" here would put a
            // confident word on a guess, and would make a real quit (which arrives
            // through DayAttemptEnded, with the flag still readable) indistinguishable
            // from a crash.
            Snapshot();
            PlaytestTelemetry.SetCurrentRun(null);
            run = null;
        }

        // The most reliable "the player is leaving" signal a phone gives: iOS and Android
        // both send this on backgrounding, while OnApplicationQuit frequently never
        // arrives at all. Status is untouched for the same reason as OnDestroy -- coming
        // back is just as likely as not.
        private void OnApplicationPause(bool paused)
        {
            if (paused) Snapshot();
        }

        private void OnApplicationQuit() => Snapshot();

        private void Update()
        {
            if (run == null || run.Status != RunStatus.InProgress) return;

            // UNSCALED, so a paused day still counts: the settings menu, the powerup
            // shop and the Game Over popup all freeze Time.timeScale, and time spent
            // staring at a Game Over popup is very much part of how this Day went.
            // Backgrounded time is excluded for free, because Update does not run --
            // which is what makes this "time spent playing" rather than "time since
            // the scene loaded".
            var delta = Time.unscaledDeltaTime;
            run.ElapsedSeconds += delta;
            secondsSinceSnapshot += delta;

            if (secondsSinceSnapshot < heartbeatSeconds) return;

            secondsSinceSnapshot = 0f;
            Snapshot();
        }

        // ---- run lifecycle -----------------------------------------------------------

        private void OnDaySessionStarted(int _)
        {
            // Reachable with a run still open only through a debug cheat (Next Day
            // pressed mid-Day). On every real path DayAttemptEnded or DayCompleted has
            // already closed this run, and re-closing a finished one is a no-op.
            // "quit" is right for the cheat case by definition: a new attempt starting
            // means the previous one was left without being completed.
            FinalizeRun(RunStatus.Quit);

            run = null;
            OpenRun();
        }

        // The whole point of D-149. `Lost` means the attempt ended with the player out
        // of lives and declining to continue; `GivenUp` means they walked away from a
        // Day that was still playable. A player who paid to continue and left later
        // lands here as GivenUp, which is why this cannot be inferred from
        // livesDepletedCount.
        private void OnDayAttemptEnded(DayAttemptEnd how)
        {
            FinalizeRun(how == DayAttemptEnd.Lost ? RunStatus.Failed : RunStatus.Quit);
        }

        private void OnDayCompleted(int _) => FinalizeRun(RunStatus.Completed);

        // The one figure this layer counts itself, because DayLifecycleManager does not:
        // running out of lives is not a day failure. It can happen several times in one
        // run if the player keeps paying to continue, and a run it happened in can still
        // end `completed`.
        private void OnLivesDepleted(int _)
        {
            if (run == null) return;
            run.LivesDepletedCount++;
        }

        private void OpenRun()
        {
            var identity = PlaytestTelemetry.Identity;

            run = new RunTelemetryState(
                TelemetryIds.NewRunId(),
                identity.PlayerId,
                identity.InstallationId,
                state.CurrentDayIndex,
                gameManager.Session?.CurrentDay?.DayIndex ?? -1,
                PlaytestTelemetry.Environment);

            secondsSinceSnapshot = 0f;
            PlaytestTelemetry.SetCurrentRun(run);

            // Written immediately rather than at the first heartbeat: a player who quits
            // in the first fifteen seconds is exactly the signal a difficulty problem
            // looks like, and a run that was never written cannot be counted as an
            // abandonment at all -- it just never happened.
            Snapshot();
        }

        private void FinalizeRun(RunStatus status)
        {
            if (run == null || run.Status != RunStatus.InProgress) return;

            PullCounters();
            run.Status = status;
            Snapshot();
        }

        // Reads the day's figures from their OWNER instead of keeping a second tally
        // (see RunTelemetryState's note). Called before every write, so a heartbeat and
        // a finalize are built the same way and cannot disagree.
        private void PullCounters()
        {
            var day = gameManager.DayLifecycleManager;
            if (day == null) return;

            run.WrongDeliveryCount = day.WrongDeliveryCount;
            run.TimeoutCount = day.TimeoutCount;
            run.TicketsDelivered = day.OrdersDeliveredCount;
            run.Score = day.Total;
            run.Stars = day.StarCount;
            run.StarScore = day.StarScore;
        }

        private void Snapshot()
        {
            if (run == null) return;

            var sink = PlaytestTelemetry.Sink;
            if (sink == null || !sink.IsReady) return;

            PullCounters();
            sink.WriteRun(run.RunId, run.ToFieldMap());
        }
    }
}
