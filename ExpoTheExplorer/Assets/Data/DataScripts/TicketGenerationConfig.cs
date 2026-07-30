using System;
using System.Collections.Generic;
using UnityEngine;

namespace ExpoTheExplorer.Data
{
    // One main-dish candidate's relative spawn weight. Weights don't need to sum
    // to 100 — TicketFactory normalizes them at selection time — so a designer
    // can add a new dish at DefaultWeight and rebalance later without touching
    // every other entry.
    [Serializable]
    public class MainDishWeight
    {
        public const float DefaultWeight = 1f;
        public const float DefaultModificationCountLambda = 1f;

        [SerializeField] private FoodItemConfig food;
        [SerializeField] private float weight = DefaultWeight;

        [Tooltip("Poisson rate λ — the EXPECTED average modification count for this dish's tickets. The " +
                 "distribution is truncated to k = 0..AvailableModifications.Count and renormalized. λ = 0 " +
                 "always yields 0 modifications; a λ larger than the available count piles mass onto the " +
                 "maximum. Overrides TicketGenerationConfig.ModificationCountLambda, which only applies as a " +
                 "fallback to Main dishes missing from this list entirely.")]
        [SerializeField, Range(0f, 10f)] private float modificationCountLambda = DefaultModificationCountLambda;

        public FoodItemConfig Food => food;
        public float Weight => weight;
        public float ModificationCountLambda => modificationCountLambda;
    }

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

        [Tooltip("Fallback Poisson rate λ used only for a Main dish missing from Main Dish Weights below — a dish present in that list always uses its own per-dish ModificationCountLambda instead.")]
        [SerializeField, Range(0f, 10f)] private float modificationCountLambda = MainDishWeight.DefaultModificationCountLambda;

        [Tooltip("Only consulted for Both-direction modifications (e.g. Cheese) — AdditionOnly/RemovalOnly modifications get their direction from ModificationConfig.AllowedDirection directly, no roll needed.")]
        [SerializeField, Range(0f, 1f)] private float modificationAdditionChance = 0.5f;

        [Header("Main Dish Weights — placeholders, not balanced")]
        [Tooltip("Relative spawn weight per Main-category food (see the read-only preview below for the resulting chance and per-food modification-count breakdown). A Main-category food missing from this list falls back to MainDishWeight.DefaultWeight.")]
        [SerializeField] private List<MainDishWeight> mainDishWeights = new();

        [Header("Customer Names")]
        [Tooltip("Pool of names randomly assigned to generated tickets.")]
        [SerializeField] private string[] customerNames = { "Alice", "Bob", "Charlie", "Diana", "Ethan" };

        [Header("Lookahead — placeholder, not balanced")]
        [Tooltip("How many tickets are pre-generated and held in a lookahead queue ahead of the 3 active slots. BoardDistributor's noise pool leaks items from these not-yet-active tickets (GDD Section 4). Must be at least 3 (TicketSlotManager clamps it).")]
        [SerializeField] private int upcomingQueueSize = 10;

        public float ImpatientTimeLimitSeconds => impatientTimeLimitSeconds;
        public float NormalTimeLimitSeconds => normalTimeLimitSeconds;
        public float PatientTimeLimitSeconds => patientTimeLimitSeconds;
        public float SideInclusionChance => sideInclusionChance;
        public float DrinkInclusionChance => drinkInclusionChance;
        public float ModificationCountLambda => modificationCountLambda;
        public float ModificationAdditionChance => modificationAdditionChance;
        public IReadOnlyList<MainDishWeight> MainDishWeights => mainDishWeights;
        public IReadOnlyList<string> CustomerNames => customerNames;
        public int UpcomingQueueSize => upcomingQueueSize;
    }
}
