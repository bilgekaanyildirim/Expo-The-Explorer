using UnityEngine;

namespace ExpoTheExplorer.Data
{
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

        [Tooltip("How many of the earliest-arrived tickets (active slots first, then the upcoming queue once this exceeds 3) must always have their full required-item content present on the board.")]
        [SerializeField, Range(1, 4)] private int guaranteedTicketCount = 1;

        [Tooltip("How many of the nearest upcoming (not-yet-active) tickets the noise-leak system looks at. Tickets further back in the queue than this are never eligible as leak sources, even if they haven't leaked yet.")]
        [SerializeField, Range(1, 10)] private int leakDepth = 10;

        [Tooltip("Maximum number of items that can leak from a single OnOrderPlaced call — the truncation ceiling for the Poisson(NoiseLeakCountLambda) distribution, independent of LeakDepth. The actual leak count may still be capped lower than this by how many un-leaked tickets are currently available within LeakDepth.")]
        [SerializeField, Range(1, 10)] private int maxLeakCount = 10;

        public float NoiseLeakCountLambda => noiseLeakCountLambda;

        // Clamped defensively rather than trusting the serialized value outright —
        // a pre-existing asset that predates this field can deserialize it at the
        // raw CLR default (0) instead of running the declared initializer, which
        // would silently disable the required-pool guarantee entirely.
        public int GuaranteedTicketCount => Mathf.Clamp(guaranteedTicketCount, 1, 4);

        // Same defensive-clamp rationale as GuaranteedTicketCount.
        public int LeakDepth => Mathf.Clamp(leakDepth, 1, 10);

        // Same defensive-clamp rationale as GuaranteedTicketCount.
        public int MaxLeakCount => Mathf.Clamp(maxLeakCount, 1, 10);

#if UNITY_EDITOR
        private void OnValidate()
        {
            guaranteedTicketCount = Mathf.Clamp(guaranteedTicketCount, 1, 4);
            leakDepth = Mathf.Clamp(leakDepth, 1, 10);
            maxLeakCount = Mathf.Clamp(maxLeakCount, 1, 10);
        }
#endif
    }
}
