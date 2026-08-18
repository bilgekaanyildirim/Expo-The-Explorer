using UnityEngine;

namespace ExpoTheExplorer.Data
{
    // Tip balancing knobs consumed by EconomyCalculator (GDD Section 9 /
    // CLAUDE.md Section 5 — Economy Module). Every default here is a
    // placeholder, not a design decision: the GDD locks the SHAPE of the payout
    // (fixed Order Value from food prices + a tip that steps down through three
    // tiers) but never states concrete numbers. Tune from the Inspector once
    // real balancing starts (same posture as TicketGenerationConfig).
    //
    // What this file deliberately does NOT hold: the food prices that make up
    // Order Value (they live per item on FoodItemConfig) and the timer bar's
    // divider spacing (TicketCardVisualsConfig.TimerSegmentSeconds, which is
    // purely cosmetic and must never be wired to a payout).
    [CreateAssetMenu(fileName = "EconomyConfig", menuName = "ExpoTheExplorer/Data/Economy Config")]
    public class EconomyConfig : ScriptableObject
    {
        // The two ratios below are the single authority for BOTH the tip tier and
        // the timer bar's colour (TicketCardsView reads them through
        // GameManager.EconomyConfig). They live here, on the economy config,
        // because they decide money -- keeping a second copy next to the bar's
        // colours is what would let the colour the player sees drift away from
        // the tip they actually get.
        [Header("Tip Tier Thresholds — fraction of the ticket's OWN limit still left")]
        [Tooltip("At or below this remaining-time fraction the tip drops to Tip Rate Warning and the bar turns to its warning colour. A ratio rather than a second count, so every patience type steps down at the same point in its own life: a 45s Impatient ticket and a 150s Patient one both drop a tier at two thirds remaining.")]
        [SerializeField, Range(0f, 1f)] private float warningRatio = 0.666f;

        [Tooltip("At or below this remaining-time fraction the tip drops to Tip Rate Critical and the bar turns to its critical colour. Takes precedence over Warning Ratio wherever both would apply, so authoring this ABOVE Warning Ratio makes the warning tier unreachable rather than throwing.")]
        [SerializeField, Range(0f, 1f)] private float criticalRatio = 0.333f;

        // Tip = Order Value x one of these three, and nothing else: no speed
        // tier (removed, GDD v1.1), no patience coefficient (removed, GDD v0.9),
        // no item-count scaling. Order Value itself is never multiplied by any
        // of them -- a late delivery loses tip, never the food's own price.
        [Header("Tip Rates — placeholders, not balanced")]
        [Tooltip("Tip as a fraction of Order Value while the ticket still has more than Warning Ratio of its time left (bar is green). 0.2 = a fresh delivery tips 20% of the order.")]
        [SerializeField, Min(0f)] private float tipRateFull = 0.2f;

        [Tooltip("Tip fraction once remaining time is at or below Warning Ratio (bar is orange).")]
        [SerializeField, Min(0f)] private float tipRateWarning = 0.15f;

        [Tooltip("Tip fraction once remaining time is at or below Critical Ratio (bar is red). Set this to 0 for 'a last-second delivery earns the food's price and no tip at all'.")]
        [SerializeField, Min(0f)] private float tipRateCritical = 0.1f;

        public float WarningRatio => warningRatio;
        public float CriticalRatio => criticalRatio;
        public float TipRateFull => tipRateFull;
        public float TipRateWarning => tipRateWarning;
        public float TipRateCritical => tipRateCritical;
    }
}
