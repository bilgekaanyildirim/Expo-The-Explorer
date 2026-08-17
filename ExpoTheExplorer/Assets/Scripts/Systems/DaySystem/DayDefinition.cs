using System.Collections.Generic;
using ExpoTheExplorer.Core;
using ExpoTheExplorer.Data;

namespace ExpoTheExplorer.Systems.DaySystem
{
    public class DayDefinition
    {
        public int DayIndex { get; }
        public int TicketsRequiredForDay { get; }
        public IReadOnlyList<ResolvedTicketEntry> TicketSequence { get; }
        public IReadOnlyList<ResolvedBoardSpawnEntry> BoardTimeline { get; }
        public int Star1Threshold { get; }
        public int Star2Threshold { get; }
        public int Star3Threshold { get; }

        // This Day's BoardDistributor balancing (see BoardDistributionJson). Optional in
        // the constructor only so the authoring-side callers that build a DayDefinition
        // purely to run it past DayValidator don't have to invent one; the runtime load
        // path (DayCatalogParser) always supplies it, and a Day file missing the block is
        // dropped rather than defaulted.
        public ResolvedBoardDistribution BoardDistribution { get; }

        public DayDefinition(
            int dayIndex,
            int ticketsRequiredForDay,
            IReadOnlyList<ResolvedTicketEntry> ticketSequence,
            IReadOnlyList<ResolvedBoardSpawnEntry> boardTimeline,
            int star1Threshold = 0,
            int star2Threshold = 0,
            int star3Threshold = 0,
            ResolvedBoardDistribution boardDistribution = null)
        {
            DayIndex = dayIndex;
            TicketsRequiredForDay = ticketsRequiredForDay;
            TicketSequence = ticketSequence;
            BoardTimeline = boardTimeline;
            Star1Threshold = star1Threshold;
            Star2Threshold = star2Threshold;
            Star3Threshold = star3Threshold;
            BoardDistribution = boardDistribution;
        }
    }

    // Resolved counterpart of BoardDistributionJson: the enum is parsed, everything else
    // carried across as authored. Deliberately does no clamping -- BoardDistributionConfig's
    // getters are the single place those bounds are enforced, and a second copy of the rule
    // here would be free to drift away from it.
    public class ResolvedBoardDistribution
    {
        public float NoiseLeakCountLambda { get; }
        public GuaranteedTicketCountMode GuaranteedTicketCountMode { get; }
        public int GuaranteedTicketCount { get; }
        public float GuaranteedTicketCountLambda { get; }
        public float EarlyTicketWeightDecay { get; }
        public float UrgentTimeThresholdSeconds { get; }
        public int LeakDepth { get; }
        public int MaxLeakCount { get; }

        public ResolvedBoardDistribution(
            float noiseLeakCountLambda,
            GuaranteedTicketCountMode guaranteedTicketCountMode,
            int guaranteedTicketCount,
            float guaranteedTicketCountLambda,
            float earlyTicketWeightDecay,
            float urgentTimeThresholdSeconds,
            int leakDepth,
            int maxLeakCount)
        {
            NoiseLeakCountLambda = noiseLeakCountLambda;
            GuaranteedTicketCountMode = guaranteedTicketCountMode;
            GuaranteedTicketCount = guaranteedTicketCount;
            GuaranteedTicketCountLambda = guaranteedTicketCountLambda;
            EarlyTicketWeightDecay = earlyTicketWeightDecay;
            UrgentTimeThresholdSeconds = urgentTimeThresholdSeconds;
            LeakDepth = leakDepth;
            MaxLeakCount = maxLeakCount;
        }
    }

    public class ResolvedTicketEntry
    {
        public FoodItemConfig MainItem { get; }
        public FoodItemConfig SideItem { get; }
        public FoodItemConfig DrinkItem { get; }
        public IReadOnlyList<Modification> Modifications { get; }
        public PatienceType PatienceType { get; }
        public string CustomerNameOverride { get; }
        public float TimeLimitSecondsOverride { get; }

        // Main, then Side/Drink if set -- same order TicketEntryFactory.Create feeds
        // into a real Ticket, and what DayContentGenerator's delivery simulation
        // (RemoveTicketItemsFromBoard) needs without a live Ticket to read it from.
        public IReadOnlyList<FoodItemConfig> RequiredItems
        {
            get
            {
                var items = new List<FoodItemConfig> { MainItem };
                if (SideItem != null) items.Add(SideItem);
                if (DrinkItem != null) items.Add(DrinkItem);
                return items;
            }
        }

        public ResolvedTicketEntry(
            FoodItemConfig mainItem,
            FoodItemConfig sideItem,
            FoodItemConfig drinkItem,
            IReadOnlyList<Modification> modifications,
            PatienceType patienceType,
            string customerNameOverride,
            float timeLimitSecondsOverride)
        {
            MainItem = mainItem;
            SideItem = sideItem;
            DrinkItem = drinkItem;
            Modifications = modifications;
            PatienceType = patienceType;
            CustomerNameOverride = customerNameOverride;
            TimeLimitSecondsOverride = timeLimitSecondsOverride;
        }
    }

    public class ResolvedBoardSpawnEntry
    {
        public int TriggerStepIndex { get; }
        public FoodItemConfig Item { get; }
        public IReadOnlyList<Modification> Modifications { get; }
        public bool UseExactCell { get; }
        public int X { get; }
        public int Y { get; }

        public ResolvedBoardSpawnEntry(
            int triggerStepIndex,
            FoodItemConfig item,
            IReadOnlyList<Modification> modifications,
            bool useExactCell,
            int x,
            int y)
        {
            TriggerStepIndex = triggerStepIndex;
            Item = item;
            Modifications = modifications;
            UseExactCell = useExactCell;
            X = x;
            Y = y;
        }
    }
}
