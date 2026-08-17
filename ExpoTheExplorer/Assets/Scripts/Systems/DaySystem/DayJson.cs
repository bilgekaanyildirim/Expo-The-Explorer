using System;

namespace ExpoTheExplorer.Systems.DaySystem
{
    [Serializable]
    public class DayJson
    {
        public DayRuntimeJson runtime;
        public DayEditorMetaJson editorMeta; // DayCatalogParser never reads this -- Day Editor only.
    }

    [Serializable]
    public class DayRuntimeJson
    {
        public int dayIndex;
        public int ticketsRequiredForDay;

        // This Day's own BoardDistributor balancing. Authored per Day rather than read
        // from the shared BoardDistributionConfig asset: the asset is now only a seed
        // template for new Days, and the Day is the single runtime authority (there is
        // no "override" layer -- every Day carries a complete block).
        public BoardDistributionJson boardDistribution;

        // This Day's play-time ticket values. Same reasoning as boardDistribution: read
        // live while the Day runs, so authored per Day under runtime rather than taken
        // from the shared TicketGenerationConfig asset (decisions.md D-005). The
        // generation PROBABILITIES stay in editorMeta -- they have already been spent by
        // the time this Day is played.
        public TicketRuntimeJson ticketRuntime;

        public TicketEntryJson[] ticketSequence;
        public BoardSpawnEntryJson[] boardTimeline;

        // Day Complete popup star rating (score = sum of delivered BaseTip + tip
        // bonus for the day) -- ascending score needed for 1/2/3 stars, authored
        // per Day alongside ticketSequence/boardTimeline rather than as a global
        // config, since thresholds are expected to scale with day difficulty.
        public int star1Threshold;
        public int star2Threshold;
        public int star3Threshold;
    }

    [Serializable]
    public class DayEditorMetaJson
    {
        // The set of FoodCatalog items that exist in this Day -- selected means present,
        // absent means the Day never serves it. Deliberately has no accompanying
        // "restrict: on/off" bool: the list IS the answer, so an empty one means an
        // empty Day (Generate refuses it) rather than a silent "use everything".
        // Stored as ids, like every other food reference in Day JSON, and resolved
        // against FoodCatalog.Items. Authoring-time only -- runtime replays the
        // resolved ticketSequence and never needs to know what was selectable.
        public string[] allowedFoodItemIds;

        public bool hasTicketGenerationOverride;
        public float sideInclusionChanceOverride;
        public float drinkInclusionChanceOverride;
        public float modificationCountLambdaOverride;
    }

    // Mirrors BoardDistributionConfig's serialized fields, minus the defensive clamping
    // (the config's public getters already clamp on read, so clamping here too would be
    // a second, competing rule). Lives under runtime, NOT editorMeta: BoardDistributor
    // runs live at play time, and DayCatalogParser is contractually blind to editorMeta,
    // which is exactly why the previous editorMeta-based board-distribution fields were
    // written, saved, and then read by nothing at all.
    [Serializable]
    public class BoardDistributionJson
    {
        public float noiseLeakCountLambda;

        // A string, not the enum itself: JsonUtility would serialize the enum as a bare
        // ordinal, which is unreadable in a hand-edited Day file and silently degrades a
        // typo to Manual(0). Parsed via Enum.TryParse, same as TicketEntryJson.patienceType.
        public string guaranteedTicketCountMode;

        public int guaranteedTicketCount;
        public float guaranteedTicketCountLambda;
        public float earlyTicketWeightDecay;
        public float urgentTimeThresholdSeconds;
        public int leakDepth;
        public int maxLeakCount;
    }

    // Mirrors the play-time half of TicketGenerationConfig (see TicketRuntimeSettings for
    // why only these four). No enum here, so absence is detected numerically instead: an
    // unauthored block deserializes to all-zeros, and a 0-second time limit would time the
    // ticket out the instant it arrives -- see DayCatalogParser.ResolveTicketRuntime.
    [Serializable]
    public class TicketRuntimeJson
    {
        public float impatientTimeLimitSeconds;
        public float normalTimeLimitSeconds;
        public float patientTimeLimitSeconds;
        public int upcomingQueueSize;
    }

    [Serializable]
    public class TicketEntryJson
    {
        public string mainItemId;
        public string sideItemId;
        public string drinkItemId;
        public ModificationEntryJson[] modifications;
        public string patienceType;
        public string customerNameOverride;
        public float timeLimitSecondsOverride;
    }

    [Serializable]
    public class ModificationEntryJson
    {
        public string modificationId;
        public bool isAddition;
    }

    [Serializable]
    public class BoardSpawnEntryJson
    {
        public int triggerStepIndex;
        public string itemId;
        public ModificationEntryJson[] modifications;
        public bool useExactCell;
        public int x;
        public int y;
    }
}
