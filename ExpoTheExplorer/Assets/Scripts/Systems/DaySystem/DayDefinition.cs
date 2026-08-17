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

        // This Day's BoardDistributor balancing (see BoardDistributionJson). Optional in
        // the constructor only so the authoring-side callers that build a DayDefinition
        // purely to run it past DayValidator don't have to invent one; the runtime load
        // path (DayCatalogParser) always supplies it, and a Day file missing the block is
        // dropped rather than defaulted.
        //
        // Deliberately the Data-layer BoardDistributionSettings rather than a DaySystem
        // type of its own: BoardDistributor consumes it directly, and a second value type
        // holding the same eight numbers would be exactly the duplicated authority the
        // data-source procedure forbids.
        public BoardDistributionSettings BoardDistribution { get; }

        // This Day's play-time ticket balancing (time limits + lookahead depth). Optional
        // on the ctor for the same reason as BoardDistribution: authoring-side callers that
        // only need a DayDefinition to run DayValidator over don't have to invent one.
        public TicketRuntimeSettings TicketRuntime { get; }

        public DayDefinition(
            int dayIndex,
            int ticketsRequiredForDay,
            IReadOnlyList<ResolvedTicketEntry> ticketSequence,
            IReadOnlyList<ResolvedBoardSpawnEntry> boardTimeline,
            BoardDistributionSettings boardDistribution = null,
            TicketRuntimeSettings ticketRuntime = null)
        {
            DayIndex = dayIndex;
            TicketsRequiredForDay = ticketsRequiredForDay;
            TicketSequence = ticketSequence;
            BoardTimeline = boardTimeline;
            BoardDistribution = boardDistribution;
            TicketRuntime = ticketRuntime;
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
