using UnityEngine;

namespace ExpoTheExplorer.Data
{
    // Lives/Continue balancing knobs consumed by LivesManager (GDD Section 6 /
    // CLAUDE.md Section 5 — Lives System). Both numbers here are placeholders,
    // not design decisions — GDD locks the CONCEPT (spend Gems to refill Lives
    // and keep playing the current day) but never states an amount; the exact
    // difficulty scale-down on a failed day is a separate, still-open GDD
    // question (CLAUDE.md Section 4) and is deliberately NOT handled by this
    // config or by LivesManager.
    [CreateAssetMenu(fileName = "LivesConfig", menuName = "ExpoTheExplorer/Data/Lives Config")]
    public class LivesConfig : ScriptableObject
    {
        [Tooltip("Gem cost to refill Lives and continue the current day after running out — placeholder, not balanced.")]
        [SerializeField] private int continueGemCost = 5;

        [Tooltip("How many Lives Continue refills to — placeholder, not balanced. Intentionally separate from GameConfig.StartingLives in case a designer wants Continue to grant a different amount than a fresh day starts with.")]
        [SerializeField] private int continueRefillAmount = 3;

        public int ContinueGemCost => continueGemCost;
        public int ContinueRefillAmount => continueRefillAmount;
    }
}
