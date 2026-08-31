using System.Collections.Generic;
using UnityEngine;

namespace ExpoTheExplorer.Systems.TelemetrySystem
{
    // One Day attempt, in RAM. The whole telemetry system's working memory
    // (.claude/telemetry-plan.md §I): gameplay never writes to Firestore, it changes
    // this, and this is snapshotted on a heartbeat and at the moments a run ends.
    //
    // IT HOLDS ALMOST NO COUNTERS OF ITS OWN, and that is a deliberate change from the
    // plan's §J table. That table had the binder subscribe to TicketDelivered,
    // TicketCancelled and TraySlotScatterBegin and keep its own tallies. But
    // DayLifecycleManager already counts deliveries, timeouts and wrong deliveries and
    // is their single authority -- a second tally would be a second answer to the same
    // question, free to drift, and it would have rested on an assumption nobody
    // checked (that TraySlotScatterBegin fires only on wrong deliveries). So those
    // numbers are READ from that authority at snapshot time and merely parked here.
    //
    // LivesDepletedCount is the exception and the only figure this class truly owns:
    // DayLifecycleManager does not keep it, because losing every life is not a day
    // failure -- a paid Continue carries the same attempt on, possibly more than once.
    //
    // THERE ARE NO TIMESTAMPS HERE. startedAt / lastUpdatedAt / completedAt are
    // Firestore SERVER stamps, written by the sink, because a playtest device's clock
    // can be hours off and a report that orders attempts by a lying clock is worse
    // than one with no times at all. This class carries elapsed SECONDS, which is a
    // duration and needs no agreement about what time it is.
    public class RunTelemetryState
    {
        // Bumped when the shape of what reaches Firestore changes, so a query can tell
        // a v1 document from a v3 one instead of guessing from which fields are present.
        public const int SchemaVersion = 1;

        public string RunId { get; }
        public string PlayerId { get; }
        public string InstallationId { get; }

        // Both day numbers, because they are not the same thing and the difference has
        // already bitten this project once (D-091/D-092). DayIndex is a POSITION in the
        // resolved catalog -- it shifts if a Day is added, removed or fails to parse.
        // DayContentIndex is the Day file's own number and is the canonical content
        // identity for balancing: it survives the catalog being re-ordered, which is
        // exactly when old telemetry would otherwise start pointing at the wrong Day.
        public int DayIndex { get; }
        public int DayContentIndex { get; }

        // What the player saw on screen. Derived, and stored anyway: reports get read
        // by humans who count days from one, and re-deriving it in every query is how
        // an off-by-one eventually gets into one of them.
        public int DayNumberShown { get; }

        public string Environment { get; }
        public string BuildVersion { get; }

        // Optional diagnostics (plan §F.3). Useful for "which device was this", never
        // required for the telemetry to be meaningful -- installationId already answers
        // which INSTALL a run came from. Note what is absent and stays absent:
        // SystemInfo.deviceUniqueIdentifier, advertising ids, anything that identifies
        // a person rather than a device model.
        public string Platform { get; }
        public string DeviceModel { get; }
        public string UnityVersion { get; }

        public RunStatus Status { get; set; } = RunStatus.InProgress;

        // Wall-clock seconds since the run opened, accumulated from unscaled delta time
        // by the binder. Unscaled on purpose: a paused day (settings menu, powerup
        // shop, the Game Over popup) is still time the player spent in front of this
        // Day, and abandonment analysis wants that. It is NOT the ticket clock
        // DayLifecycleManager measures -- those two must never be compared.
        public float ElapsedSeconds { get; set; }

        // The one counter this class owns. Incremented every time the player runs out
        // of lives, which can happen repeatedly in one run if they pay to continue.
        // It never decides the run's status -- see RunStatus.Failed.
        public int LivesDepletedCount { get; set; }

        // Parked from DayLifecycleManager at snapshot time; see the class note.
        public int WrongDeliveryCount { get; set; }
        public int TimeoutCount { get; set; }
        public int TicketsDelivered { get; set; }
        public int Score { get; set; }
        public int Stars { get; set; }
        public float StarScore { get; set; }

        // Deliberately derived rather than stored: a mistake IS a wrong delivery or a
        // timeout, and storing the sum beside its two terms invites the three to
        // disagree after somebody edits one of them.
        public int MistakeCount => WrongDeliveryCount + TimeoutCount;

        // A timed-out ticket is an expired one; the same event, named for the ticket
        // rather than for the clock. Kept as its own field name in the document
        // because "ticketsExpired" is what a report about ticket difficulty asks for.
        public int TicketsExpired => TimeoutCount;

        public RunTelemetryState(
            string runId,
            string playerId,
            string installationId,
            int dayIndex,
            int dayContentIndex,
            string environment)
        {
            RunId = runId;
            PlayerId = playerId;
            InstallationId = installationId;
            DayIndex = dayIndex;
            DayContentIndex = dayContentIndex;
            DayNumberShown = dayIndex + 1;
            Environment = environment;

            BuildVersion = Application.version;
            Platform = Application.platform.ToString();
            DeviceModel = SystemInfo.deviceModel;
            UnityVersion = Application.unityVersion;
        }

        // The document, as fields. Everything the sink needs and nothing it has to
        // understand -- which is what lets the Firestore sink stay a dumb writer and
        // lets LogTelemetrySink print the identical payload without a second mapping.
        //
        // `authUid` is NOT here: this assembly has no Firebase reference and must not
        // gain one, so the sink adds it along with the server timestamps. Adding a new
        // metric is a field here plus a line in this method, and nothing else --
        // Firestore is schemaless, so old documents simply lack it.
        public Dictionary<string, object> ToFieldMap()
        {
            return new Dictionary<string, object>
            {
                { "schemaVersion", SchemaVersion },
                { "environment", Environment },

                { "runId", RunId },
                { "playerId", PlayerId },
                { "installationId", InstallationId },

                { "dayIndex", DayIndex },
                { "dayContentIndex", DayContentIndex },
                { "dayNumberShown", DayNumberShown },

                { "status", Status.ToWireValue() },
                { "elapsedSeconds", ElapsedSeconds },

                { "mistakeCount", MistakeCount },
                { "wrongDeliveryCount", WrongDeliveryCount },
                { "timeoutCount", TimeoutCount },
                { "ticketsDelivered", TicketsDelivered },
                { "ticketsExpired", TicketsExpired },
                { "score", Score },
                { "stars", Stars },
                { "starScore", StarScore },
                { "livesDepletedCount", LivesDepletedCount },

                { "buildVersion", BuildVersion },
                { "platform", Platform },
                { "deviceModel", DeviceModel },
                { "unityVersion", UnityVersion },
            };
        }

        // What a human reads in the Console. Deliberately short and on one line: during
        // a playtest this prints every 15 seconds, and a multi-line dump would bury the
        // gameplay logs it sits between.
        public override string ToString()
        {
            return $"{RunId} day={DayContentIndex} {Status.ToWireValue()} " +
                   $"{ElapsedSeconds:F0}s delivered={TicketsDelivered} mistakes={MistakeCount} " +
                   $"(wrong={WrongDeliveryCount} timeout={TimeoutCount}) score={Score} " +
                   $"stars={Stars} depleted={LivesDepletedCount}";
        }
    }
}
