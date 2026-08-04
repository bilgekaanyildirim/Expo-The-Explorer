using System;
using System.Collections.Generic;
using UnityEngine;

namespace ExpoTheExplorer.Data
{
    // One held step of a patience type's tip decay curve (GDD Section 8): once
    // elapsed time crosses this step's threshold, the tip multiplier drops to
    // DecayCoefficient and holds flat until the next step's threshold — this is
    // what makes the curve stepped rather than linear. Steps within a curve are
    // expected to be authored in ascending SecondsPerItemThreshold order —
    // EconomyCalculator walks them in list order and does not sort defensively
    // (same convention as TicketGenerationConfig.MainDishWeights).
    [Serializable]
    public class PatienceDecayStep
    {
        [Tooltip("Elapsed-time-per-required-item threshold — actual threshold = this x the ticket's item count (GDD Section 8: thresholds scale with ticket complexity, not fixed seconds).")]
        [SerializeField] private float secondsPerItemThreshold;

        [Tooltip("Tip multiplier held from this threshold until the next step's threshold (or forever, if this is the last step).")]
        [SerializeField, Range(0f, 1f)] private float decayCoefficient = 1f;

        public float SecondsPerItemThreshold => secondsPerItemThreshold;
        public float DecayCoefficient => decayCoefficient;
    }

    // Tip/speed/patience balancing knobs consumed by EconomyCalculator (GDD
    // Section 9 / CLAUDE.md Section 5 — Economy Module). Every default here is a
    // placeholder, not a design decision — the GDD locks the SHAPE of the tip
    // formula and the fact that both speed-tier and patience-decay thresholds
    // scale with ticket item count, but never states concrete numbers. Tune from
    // the Inspector once real balancing starts (same posture as
    // TicketGenerationConfig).
    [CreateAssetMenu(fileName = "EconomyConfig", menuName = "ExpoTheExplorer/Data/Economy Config")]
    public class EconomyConfig : ScriptableObject
    {
        [Header("Base Tip — placeholder, not balanced")]
        [Tooltip("Base Tip = this x the ticket's required item count. The GDD's tip formula (Section 9) never defines Base Tip itself — this scales it with complexity so bigger orders aren't worth the same as small ones.")]
        [SerializeField] private float baseTipPerItem = 10f;

        [Header("Speed Bonus Tiers (GDD Section 9) — placeholders, not balanced")]
        [SerializeField] private float lightningMultiplier = 2f;
        [SerializeField] private float fastMultiplier = 1.5f;
        [Tooltip("GDD Section 9: Standard delivery is base tip with no multiplier.")]
        [SerializeField] private float standardMultiplier = 1f;

        [Tooltip("Elapsed-time-per-item threshold at or below which a delivery counts as Lightning. Actual threshold = this x item count (GDD Section 9: dynamic, scales with complexity).")]
        [SerializeField] private float lightningSecondsPerItem = 3f;

        [Tooltip("Elapsed-time-per-item threshold at or below which a delivery counts as Fast (must be > LightningSecondsPerItem). Anything slower is Standard.")]
        [SerializeField] private float fastSecondsPerItem = 6f;

        // One curve per patience type, as a fixed set of 3 fields rather than a
        // PatienceType-keyed list: GDD Section 8 locks the count at exactly 3
        // patience types, and Core.PatienceType lives in the Core assembly,
        // which itself depends on Data (GameState references FoodItemConfig) —
        // Data referencing Core back would be circular. EconomyCalculator (in
        // the EconomySystem assembly, which can see both) maps PatienceType to
        // the matching field below.
        [Header("Patience Decay Curves (GDD Section 8) — placeholders, not balanced")]
        [SerializeField] private List<PatienceDecayStep> impatientDecaySteps = new();
        [SerializeField] private List<PatienceDecayStep> normalDecaySteps = new();
        [SerializeField] private List<PatienceDecayStep> patientDecaySteps = new();

        public float BaseTipPerItem => baseTipPerItem;
        public float LightningMultiplier => lightningMultiplier;
        public float FastMultiplier => fastMultiplier;
        public float StandardMultiplier => standardMultiplier;
        public float LightningSecondsPerItem => lightningSecondsPerItem;
        public float FastSecondsPerItem => fastSecondsPerItem;
        public IReadOnlyList<PatienceDecayStep> ImpatientDecaySteps => impatientDecaySteps;
        public IReadOnlyList<PatienceDecayStep> NormalDecaySteps => normalDecaySteps;
        public IReadOnlyList<PatienceDecayStep> PatientDecaySteps => patientDecaySteps;
    }
}
