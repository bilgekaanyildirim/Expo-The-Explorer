using UnityEngine;

namespace ExpoTheExplorer.Data
{
    // Lives/Continue balancing knobs consumed by LivesManager (GDD Section 6 /
    // CLAUDE.md Section 5 — Lives System). Both numbers here are placeholders,
    // not design decisions — GDD locks the CONCEPT (spend Gems or SoftMoney to
    // refill Lives and keep playing the current day) but never states an
    // amount; the exact difficulty scale-down on a failed day is a separate,
    // still-open GDD question (CLAUDE.md Section 4) and is deliberately NOT
    // handled by this config or by LivesManager.
    [CreateAssetMenu(fileName = "LivesConfig", menuName = "ExpoTheExplorer/Data/Lives Config")]
    public class LivesConfig : ScriptableObject
    {
        [Tooltip("Gem cost to refill Lives and continue the current day after running out — placeholder, not balanced.")]
        [SerializeField] private int continueGemCost = 10;

        [Tooltip("SoftMoney cost to refill Lives and continue the current day after running out — placeholder, not balanced.")]
        [SerializeField] private int continueSoftMoneyCost = 250;

        public int ContinueGemCost => continueGemCost;
        public int ContinueSoftMoneyCost => continueSoftMoneyCost;
    }
}
