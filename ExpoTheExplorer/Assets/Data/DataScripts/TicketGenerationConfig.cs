using System;
using System.Collections.Generic;
using System.Linq;
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

        // 10 is "no cap of my own" in practice: no dish in the project has anywhere near
        // ten modifications, and TicketFactory truncates to AvailableModifications.Count
        // anyway, so a fresh entry behaves exactly as it did before this field existed.
        public const int DefaultMaxModificationCount = 10;

        [SerializeField] private FoodItemConfig food;
        [SerializeField] private float weight = DefaultWeight;

        [Tooltip("Poisson rate λ — the EXPECTED average modification count for this dish's tickets. The " +
                 "distribution is truncated to k = 0..Max Modification Count and renormalized. λ = 0 " +
                 "always yields 0 modifications; a λ larger than the ceiling piles mass onto the " +
                 "maximum. Overrides TicketGenerationConfig.ModificationCountLambda, which only applies as a " +
                 "fallback to Main dishes missing from this list entirely.")]
        [SerializeField, Range(0f, 10f)] private float modificationCountLambda = DefaultModificationCountLambda;

        [Tooltip("Hard ceiling on this dish's modification count — the Poisson truncation point, the same " +
                 "role Max Leak Count plays for the noise-leak roll. The effective ceiling is the SMALLER of " +
                 "this and the dish's own AvailableModifications count, so raising it can never invent a " +
                 "modification the dish does not have. To make a dish never carry modifications, set λ = 0 " +
                 "rather than lowering this (the range starts at 1 so that 0 stays readable as 'unauthored').")]
        [SerializeField, Range(1, 10)] private int maxModificationCount = DefaultMaxModificationCount;

        public FoodItemConfig Food => food;
        public float Weight => weight;
        public float ModificationCountLambda => modificationCountLambda;

        // Normalized on read rather than on write: this value arrives from two directions
        // (Unity's own YAML for the seed asset, Day JSON via the ctor below), and clamping
        // at each entry point would be two competing rules for one field.
        public int MaxModificationCount => NormalizeMaxModificationCount(maxModificationCount);

        // A Day file or asset written before this field existed carries no value for it,
        // which deserializes to 0 — and 0 read literally means "this dish never gets a
        // modification", a silent balance change on every already-authored Day. Every
        // reader of that legacy data routes through here, so the rule cannot drift: the
        // Day Editor's own model calls it too, for the value it shows on the slider.
        public static int NormalizeMaxModificationCount(int value) =>
            value < 1 ? DefaultMaxModificationCount : value;

        // Serialized-by-Unity default ctor stays for the Inspector; this one exists so a
        // Day's own authored weights (resolved from food ids in editorMeta) can be built in
        // code and handed to CloneForDayGeneration.
        public MainDishWeight() { }

        public MainDishWeight(FoodItemConfig food, float weight, float modificationCountLambda, int maxModificationCount)
        {
            this.food = food;
            this.weight = weight;
            this.modificationCountLambda = modificationCountLambda;
            this.maxModificationCount = maxModificationCount;
        }
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
        [Tooltip("{id, name, gender} JSON array (see Assets/Database/names.json) TicketFactory draws customer names from.")]
        [SerializeField] private TextAsset namesDatabase;

        private IReadOnlyList<string> namesFromDatabase;

        [Header("Customer Portraits")]
        [Tooltip("Faces drawn for a ticket's customer photo frame (Assets/Art/Characters). Drawn INDEPENDENTLY of the " +
                 "name — there is no id lining the two up, since the names database holds hundreds of entries and this " +
                 "list holds a couple of dozen faces, so two tickets can wear the same face exactly as they can already " +
                 "wear the same name. Leave it empty and every ticket gets a null portrait, which leaves the card's photo " +
                 "frame as authored — this is a legal state, not a misconfiguration, and it is what the card looked like " +
                 "before portraits existed.")]
        [SerializeField] private List<Sprite> customerPortraits = new();

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
        public IReadOnlyList<string> CustomerNames =>
            namesFromDatabase ??= ParseNamesDatabase() ?? throw new InvalidOperationException(
                $"{name}: Names Database is not assigned or contains no valid entries (see Assets/Database/names.json).");
        // Unlike CustomerNames, this one never throws on an empty list: a missing names
        // database means TicketFactory cannot name a customer at all, while a missing
        // portrait list means the photo frame stays as authored — a degraded look, not a
        // broken ticket, and the exact state the card was in before D-139.
        public IReadOnlyList<Sprite> CustomerPortraits => customerPortraits;
        public int UpcomingQueueSize => upcomingQueueSize;

        // The four play-time values, packaged for the runtime (decisions.md D-005). Since
        // those are authored per Day now, this asset is only their SEED: the starting
        // values a brand-new Day gets, and what TicketFactory uses on the authoring path.
        // The live game reads DayDefinition.TicketRuntime, never this.
        public TicketRuntimeSettings ToRuntimeSettings()
        {
            return new TicketRuntimeSettings(
                impatientTimeLimitSeconds, normalTimeLimitSeconds, patientTimeLimitSeconds, upcomingQueueSize);
        }

        // Applies a Day's own generation settings on top of this asset for one Generate run
        // (DayContentGenerator). Every parameter is required: there is no override layer any
        // more -- a Day carries all five values, so nothing is left to inherit (D-006).
        //
        // Still an Instantiate-clone, unlike the board-distribution path in D-004, for one
        // reason: TicketFactory needs namesDatabase, a TextAsset reference that cannot live
        // in Day JSON. The clone is safe here because this runs only at authoring time and
        // DayContentGenerator destroys it in a finally -- there is no per-Day runtime
        // lifetime to leak.
        public TicketGenerationConfig CloneForDayGeneration(
            float sideInclusionChance,
            float drinkInclusionChance,
            float modificationCountLambda,
            float modificationAdditionChance,
            List<MainDishWeight> mainDishWeights)
        {
            var clone = Instantiate(this);
            clone.sideInclusionChance = sideInclusionChance;
            clone.drinkInclusionChance = drinkInclusionChance;
            clone.modificationCountLambda = modificationCountLambda;
            clone.modificationAdditionChance = modificationAdditionChance;
            clone.mainDishWeights = mainDishWeights ?? new List<MainDishWeight>();
            return clone;
        }

        // names.json is a raw JSON array ({id, name, gender} per GDD's Database asset), but
        // JsonUtility only parses object roots — wrapping it in a single-field object is the
        // standard workaround rather than pulling in a JSON library for one file.
        private List<string> ParseNamesDatabase()
        {
            if (namesDatabase == null) return null;

            var wrapped = "{\"entries\":" + namesDatabase.text + "}";
            var parsed = JsonUtility.FromJson<NameEntryList>(wrapped);
            var names = parsed?.entries?
                .Select(entry => entry.name)
                .Where(name => !string.IsNullOrEmpty(name))
                .ToList();

            return names is { Count: > 0 } ? names : null;
        }

        [Serializable]
        private class NameEntry
        {
            public int id;
            public string name;
            public string gender;
        }

        [Serializable]
        private class NameEntryList
        {
            public List<NameEntry> entries;
        }
    }
}
