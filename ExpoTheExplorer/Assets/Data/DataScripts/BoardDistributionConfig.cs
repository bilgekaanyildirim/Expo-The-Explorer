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
        [Tooltip("Chance that a newly placed order leaks one item from the upcoming (not-yet-active) ticket queue onto the board.")]
        [SerializeField, Range(0f, 1f)] private float noiseLeakChance = 0.5f;

        public float NoiseLeakChance => noiseLeakChance;
    }
}
