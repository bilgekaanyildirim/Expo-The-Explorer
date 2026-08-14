using System;
using ExpoTheExplorer.Data;

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
        public bool hasTicketGenerationOverride;
        public float sideInclusionChanceOverride;
        public float drinkInclusionChanceOverride;
        public float modificationCountLambdaOverride;
        public bool hasBoardDistributionOverride;
        public float noiseLeakCountLambdaOverride;
        public GuaranteedTicketCountMode guaranteedTicketCountModeOverride;
        public int guaranteedTicketCountOverride;
        public float guaranteedTicketCountLambdaOverride;
        public float earlyTicketWeightDecayOverride;
        public float urgentTimeThresholdSecondsOverride;
        public int leakDepthOverride;
        public int maxLeakCountOverride;
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
