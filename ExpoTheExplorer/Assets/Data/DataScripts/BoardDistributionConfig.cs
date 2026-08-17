using UnityEngine;

namespace ExpoTheExplorer.Data
{
    public enum GuaranteedTicketCountMode
    {
        Manual,
        Poisson
    }

    // Balancing knobs for BoardDistributor (GDD Section 4). Every default here is
    // a placeholder, not a design decision. Deliberately has no "max noise items"
    // field — GDD explicitly treats grid capacity itself as the upper bound
    // (BoardGrid.RequestSpawn already queues once the board is full), so adding
    // an extra cap would contradict that design choice.
    [CreateAssetMenu(fileName = "BoardDistributionConfig", menuName = "ExpoTheExplorer/Data/Board Distribution Config")]
    public class BoardDistributionConfig : ScriptableObject
    {
        [Tooltip("Expected average number of items leaked from the upcoming (not-yet-active) ticket queue each time an order is placed. Poisson-distributed and truncated/renormalized to MaxLeakCount — see the preview below.")]
        [SerializeField, Range(0f, 10f)] private float noiseLeakCountLambda = 0.5f;

        [Tooltip("Manual: GuaranteedTicketCount is used directly. Poisson: each round's guaranteed-ticket budget is instead sampled from a Poisson(GuaranteedTicketCountLambda) distribution, truncated to the 1-3 range (never 0 — at least one ticket must always be completable).")]
        [SerializeField] private GuaranteedTicketCountMode guaranteedTicketCountMode = GuaranteedTicketCountMode.Manual;

        [Tooltip("How many tickets must always have their full required-item content present on the board each round, when GuaranteedTicketCountMode is Manual. Which specific tickets get chosen is a separate, arrival-weighted lottery — see EarlyTicketWeightDecay.")]
        [SerializeField, Range(1, 3)] private int guaranteedTicketCount = 1;

        [Tooltip("Expected average guaranteed-ticket budget when GuaranteedTicketCountMode is Poisson, before truncation to 1-3.")]
        [SerializeField, Range(0f, 3f)] private float guaranteedTicketCountLambda = 1f;

        [Tooltip("How strongly earlier-arrived tickets are favored when the per-round budget picks which tickets get guaranteed items. Weight for the ticket at arrival-rank r (0 = earliest among remaining candidates) is this value raised to the r-th power: 0 always favors the earliest first (fully deterministic), 1 is uniform-random (arrival order ignored).")]
        [SerializeField, Range(0f, 1f)] private float earlyTicketWeightDecay = 0.5f;

        [Tooltip("Any active ticket whose remaining time drops below this threshold is guaranteed its required items unconditionally, regardless of the arrival-weighted lottery or the budget — consumes from the budget when it isn't exceeded, but is guaranteed in full even past budget if urgent-ticket count exceeds it.")]
        [SerializeField, Range(0f, 30f)] private float urgentTimeThresholdSeconds = 10f;

        [Tooltip("How many of the nearest upcoming (not-yet-active) tickets the noise-leak system looks at. Tickets further back in the queue than this are never eligible as leak sources, even if they haven't leaked yet.")]
        [SerializeField, Range(1, 10)] private int leakDepth = 10;

        [Tooltip("Maximum number of items that can leak from a single OnOrderPlaced call — the truncation ceiling for the Poisson(NoiseLeakCountLambda) distribution, independent of LeakDepth. The actual leak count may still be capped lower than this by how many un-leaked tickets are currently available within LeakDepth.")]
        [SerializeField, Range(1, 10)] private int maxLeakCount = 10;

        public float NoiseLeakCountLambda => noiseLeakCountLambda;

        public GuaranteedTicketCountMode GuaranteedTicketCountMode => guaranteedTicketCountMode;

        // Clamped defensively rather than trusting the serialized value outright —
        // a pre-existing asset that predates this field can deserialize it at the
        // raw CLR default (0) instead of running the declared initializer, which
        // would silently disable the required-pool guarantee entirely. Tightened
        // to 1-3 (was 1-4) to match GameState.TicketSlotCount -- 4 was a value the
        // system could never fully use.
        public int GuaranteedTicketCount => Mathf.Clamp(guaranteedTicketCount, 1, 3);

        public float GuaranteedTicketCountLambda => guaranteedTicketCountLambda;

        public float EarlyTicketWeightDecay => Mathf.Clamp01(earlyTicketWeightDecay);

        public float UrgentTimeThresholdSeconds => Mathf.Max(0f, urgentTimeThresholdSeconds);

        // Same defensive-clamp rationale as GuaranteedTicketCount.
        public int LeakDepth => Mathf.Clamp(leakDepth, 1, 10);

        // Same defensive-clamp rationale as GuaranteedTicketCount.
        public int MaxLeakCount => Mathf.Clamp(maxLeakCount, 1, 10);

        // This asset is no longer on the runtime path -- BoardDistributor is fed per-Day
        // settings parsed from Day JSON (see BoardDistributionSettings, decisions.md D-004).
        // What is left here is the authoring seed: the values a brand-new Day starts from
        // in the Day Editor, plus this asset's Inspector preview. ToSettings exists so that
        // seeding, and any tooling that wants to see what the asset would actually do, goes
        // through the same clamping the runtime uses.
        //
        // The old CloneWithOverrides was deleted with the override layer it existed for: a
        // Day now carries a complete block, so there is nothing to overlay onto a base.
        public BoardDistributionSettings ToSettings()
        {
            return new BoardDistributionSettings(
                NoiseLeakCountLambda,
                GuaranteedTicketCountMode,
                GuaranteedTicketCount,
                GuaranteedTicketCountLambda,
                EarlyTicketWeightDecay,
                UrgentTimeThresholdSeconds,
                LeakDepth,
                MaxLeakCount);
        }

#if UNITY_EDITOR
        private void OnValidate()
        {
            guaranteedTicketCount = Mathf.Clamp(guaranteedTicketCount, 1, 3);
            guaranteedTicketCountLambda = Mathf.Clamp(guaranteedTicketCountLambda, 0f, 3f);
            earlyTicketWeightDecay = Mathf.Clamp01(earlyTicketWeightDecay);
            urgentTimeThresholdSeconds = Mathf.Max(0f, urgentTimeThresholdSeconds);
            leakDepth = Mathf.Clamp(leakDepth, 1, 10);
            maxLeakCount = Mathf.Clamp(maxLeakCount, 1, 10);
        }
#endif
    }
}
