using System.Collections.Generic;
using UnityEngine;

namespace ExpoTheExplorer.Data
{
    // Ticket-balancing knobs consumed by TicketFactory. Every default here is a
    // placeholder, not a design decision — GDD Section 8 only guarantees
    // Impatient < Normal < Patient time limits, and never states item/modification
    // inclusion ratios. Tune from the Inspector once real balancing starts.
    [CreateAssetMenu(fileName = "TicketGenerationConfig", menuName = "ExpoTheExplorer/Data/Ticket Generation Config")]
    public class TicketGenerationConfig : ScriptableObject
    {
        [Header("Time Limits (seconds) — placeholders, not balanced")]
        [SerializeField] private float impatientTimeLimitSeconds = 45f;
        [SerializeField] private float normalTimeLimitSeconds = 90f;
        [SerializeField] private float patientTimeLimitSeconds = 150f;

        [Header("Item Selection Chances — placeholders, not balanced")]
        [SerializeField, Range(0f, 1f)] private float sideInclusionChance = 0.5f;
        [SerializeField, Range(0f, 1f)] private float drinkInclusionChance = 0.5f;
        [SerializeField, Range(0f, 1f)] private float modificationInclusionChance = 0.3f;
        [SerializeField, Range(0f, 1f)] private float modificationAdditionChance = 0.5f;

        [Header("Customer Names")]
        [Tooltip("Pool of names randomly assigned to generated tickets.")]
        [SerializeField] private string[] customerNames = { "Alice", "Bob", "Charlie", "Diana", "Ethan" };

        public float ImpatientTimeLimitSeconds => impatientTimeLimitSeconds;
        public float NormalTimeLimitSeconds => normalTimeLimitSeconds;
        public float PatientTimeLimitSeconds => patientTimeLimitSeconds;
        public float SideInclusionChance => sideInclusionChance;
        public float DrinkInclusionChance => drinkInclusionChance;
        public float ModificationInclusionChance => modificationInclusionChance;
        public float ModificationAdditionChance => modificationAdditionChance;
        public IReadOnlyList<string> CustomerNames => customerNames;
    }
}
