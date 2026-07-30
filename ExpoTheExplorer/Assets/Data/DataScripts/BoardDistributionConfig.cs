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
        [Tooltip("How often the noise pool attempts to leak one item from an upcoming (not-yet-active) ticket onto the board.")]
        [SerializeField] private float noiseSpawnIntervalSeconds = 4f;

        public float NoiseSpawnIntervalSeconds => noiseSpawnIntervalSeconds;
    }
}
