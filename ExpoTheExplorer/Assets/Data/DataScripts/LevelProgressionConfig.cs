using System.Collections.Generic;
using UnityEngine;

namespace ExpoTheExplorer.Data
{
    // XP/level balancing knobs (GDD Section 10 -- Progression / CLAUDE.md
    // Section 5 -- Progression Module). Every default here is a placeholder,
    // not a design decision, same posture as EconomyConfig. This config has
    // no consumer yet -- a LevelManager/calculator class and this config's
    // .asset instance are deferred to a later PR, once something actually
    // reads these fields (mirrors how EconomyConfig's asset landed together
    // with EconomyCalculator, not before it).
    [CreateAssetMenu(fileName = "LevelProgressionConfig", menuName = "ExpoTheExplorer/Data/Level Progression Config")]
    public class LevelProgressionConfig : ScriptableObject
    {
        [Header("XP Thresholds Per Level — placeholder, not balanced")]
        [Tooltip("Indexed directly by current Level, NOT walked/searched like PatienceDecayStep's threshold list: XpToNextLevel[N] is the XP required to advance from level N to level N+1 (index 0 = level 0->1, index 1 = level 1->2, etc). Because this is a direct lookup by level rather than a threshold scan, entries don't need ascending order the way PatienceDecayStep's do -- but the list must have an entry for every level the player can actually reach; a consumer indexing past the end (player at the max authored level) has an undefined lookup, and that 'max level' behavior is deliberately left unhandled here and deferred to the calculator PR that consumes this config.")]
        [SerializeField] private List<int> xpToNextLevel = new();

        [Header("Per-Delivery XP — placeholder, not balanced")]
        [Tooltip("Per-delivery XP = this x the ticket's required item count x the matching patience multiplier below (same item-count scaling idea as EconomyConfig.BaseTipPerItem, extended with a patience-type factor).")]
        [SerializeField] private float xpPerItem = 5f;

        // One multiplier per patience type, as three separate flat fields rather
        // than a PatienceType-keyed dictionary -- same constraint that already
        // forced EconomyConfig's decay curves into three separate lists:
        // Core.PatienceType lives in the Core assembly, and Data cannot
        // reference Core back without creating a circular assembly reference.
        // Whatever calculator consumes this config (Core+Data-visible, e.g. a
        // future ProgressionSystem class) maps PatienceType to the matching
        // field below, the same way EconomyCalculator.ResolveDecaySteps does.
        [Header("Patience XP Multipliers — placeholders, not balanced")]
        [SerializeField] private float impatientXpMultiplier = 1f;
        [SerializeField] private float normalXpMultiplier = 1f;
        [SerializeField] private float patientXpMultiplier = 1f;

        public IReadOnlyList<int> XpToNextLevel => xpToNextLevel;
        public float XpPerItem => xpPerItem;
        public float ImpatientXpMultiplier => impatientXpMultiplier;
        public float NormalXpMultiplier => normalXpMultiplier;
        public float PatientXpMultiplier => patientXpMultiplier;
    }
}
