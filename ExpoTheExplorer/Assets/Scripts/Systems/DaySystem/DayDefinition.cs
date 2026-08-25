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

        // The whole day's clock: every authored ticket's own time limit, summed. This is
        // the denominator of the star score (decisions.md D-060) -- the player's score is
        // the fraction of it they handed back by delivering early.
        //
        // Deliberately summed from the ticket sequence rather than stored as an authored
        // number: a Day already states its length twice over (TicketsRequiredForDay and a
        // sequence of exactly that many entries, which DayValidator enforces), and a third
        // hand-typed total would be the one free to disagree with the tickets actually
        // played. It is read once per day start, so the loop costs nothing worth naming.
        //
        // NOT wall-clock time, and it must not be read as a target duration: three slots
        // run concurrently, so a day whose tickets total 390s is over in well under half
        // that. What the number measures is customer patience granted, all of it added up.
        public float TotalTicketSeconds
        {
            get
            {
                if (TicketSequence == null) return 0f;

                var total = 0f;
                foreach (var entry in TicketSequence)
                {
                    if (entry == null) continue;
                    total += entry.TimeLimitSecondsWith(TicketRuntime);
                }

                return total;
            }
        }

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

        // How long this entry's ticket gets: its own override if one is authored, else the
        // PLAYED Day's limit for its patience type (decisions.md D-005, so two Days can
        // give the same type different limits).
        //
        // The single home of that rule. TicketEntryFactory used to hold its own copy and
        // DayDefinition.TotalTicketSeconds would have needed a second one -- two places
        // deciding how long a ticket is, one of them feeding the ticket the player plays
        // and the other the score they are graded on, is exactly the drift the data-source
        // procedure forbids. A null ticketRuntime (the authoring-side DayDefinition that
        // exists only to be validated) leaves an un-overridden entry at 0 rather than
        // throwing, since nothing plays that object.
        public float TimeLimitSecondsWith(TicketRuntimeSettings ticketRuntime)
        {
            if (TimeLimitSecondsOverride > 0f) return TimeLimitSecondsOverride;

            return ticketRuntime == null ? 0f : ticketRuntime.TimeLimitSecondsFor(PatienceType);
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
