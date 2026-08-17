using UnityEngine;

namespace ExpoTheExplorer.Data
{
    // The eight values BoardDistributor actually runs on, as plain data rather than a
    // ScriptableObject. Two reasons it is not just "the config asset":
    //
    // 1. Balancing is authored PER DAY (Day JSON's runtime.boardDistribution), so the
    //    runtime needs a value object it can build straight from a parsed Day. Cloning
    //    the shared asset per Day instead would mean an Instantiate whose lifetime
    //    someone has to remember to end -- one leaked ScriptableObject per Day change
    //    and per retry, for no gain.
    // 2. CLAUDE.md Section 5 wants the Food Distribution Module unit-testable in
    //    isolation, with no Unity dependency. A plain settings object gets it closer;
    //    BoardDistributor no longer touches ScriptableObject at all.
    //
    // Immutable: a Day's balancing is fixed for that Day's lifetime, and BoardDistributor
    // holds onto this across many OnOrderPlaced calls.
    public class BoardDistributionSettings
    {
        public float NoiseLeakCountLambda { get; }
        public GuaranteedTicketCountMode GuaranteedTicketCountMode { get; }
        public int GuaranteedTicketCount { get; }
        public float GuaranteedTicketCountLambda { get; }
        public float EarlyTicketWeightDecay { get; }
        public float UrgentTimeThresholdSeconds { get; }
        public int LeakDepth { get; }
        public int MaxLeakCount { get; }

        // The single place these bounds are enforced for anything the runtime will use --
        // every producer (BoardDistributionConfig.ToSettings, DayCatalogParser) goes
        // through here, so an out-of-range authored value cannot reach BoardDistributor by
        // some other route. Clamping is idempotent, so the config asset's own defensive
        // getters (kept for its Inspector preview) re-clamping first changes nothing.
        //
        // GuaranteedTicketCount's floor of 1 is not cosmetic: 0 would mean "no ticket is
        // guaranteed completable", contradicting GDD Section 4's "at least one ticket".
        // NoiseLeakCountLambda is deliberately unclamped -- it is a Poisson rate, and
        // TruncatedPoisson already bounds the outcome by MaxLeakCount.
        public BoardDistributionSettings(
            float noiseLeakCountLambda,
            GuaranteedTicketCountMode guaranteedTicketCountMode,
            int guaranteedTicketCount,
            float guaranteedTicketCountLambda,
            float earlyTicketWeightDecay,
            float urgentTimeThresholdSeconds,
            int leakDepth,
            int maxLeakCount)
        {
            NoiseLeakCountLambda = noiseLeakCountLambda;
            GuaranteedTicketCountMode = guaranteedTicketCountMode;
            GuaranteedTicketCount = Mathf.Clamp(guaranteedTicketCount, 1, 3);
            GuaranteedTicketCountLambda = Mathf.Clamp(guaranteedTicketCountLambda, 0f, 3f);
            EarlyTicketWeightDecay = Mathf.Clamp01(earlyTicketWeightDecay);
            UrgentTimeThresholdSeconds = Mathf.Max(0f, urgentTimeThresholdSeconds);
            LeakDepth = Mathf.Clamp(leakDepth, 1, 10);
            MaxLeakCount = Mathf.Clamp(maxLeakCount, 1, 10);
        }
    }
}
