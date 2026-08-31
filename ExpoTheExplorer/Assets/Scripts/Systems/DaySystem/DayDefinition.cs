using System.Collections.Generic;
using ExpoTheExplorer.Core;
using ExpoTheExplorer.Data;
using UnityEngine;

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

        // This Day's forced first move, or NULL for every Day that has none -- which is the
        // normal case and the reason this is a reference type rather than a struct with an
        // IsEnabled flag: "no tutorial" is then unrepresentable-as-half-configured, and
        // every reader's check is a null check rather than a convention it could forget.
        // DayCatalogParser produces one only for a block that says enabled.
        public ResolvedTutorial Tutorial { get; }

        // The food this Day introduces, shown at Day Start before the clock runs. NULL when
        // the Day introduces nothing, which is the normal case -- the same "absence is
        // unrepresentable-as-half-configured" choice Tutorial makes above, so every reader's
        // check is a null check rather than a convention it could forget.
        //
        // Never empty when it is non-null: DayCatalogParser produces one only for an enabled
        // block that resolved at least one item, so "there is a list" means "there is at
        // least one popup to show".
        public IReadOnlyList<ResolvedItemIntro> ItemIntros { get; }

        public DayDefinition(
            int dayIndex,
            int ticketsRequiredForDay,
            IReadOnlyList<ResolvedTicketEntry> ticketSequence,
            IReadOnlyList<ResolvedBoardSpawnEntry> boardTimeline,
            BoardDistributionSettings boardDistribution = null,
            TicketRuntimeSettings ticketRuntime = null,
            ResolvedTutorial tutorial = null,
            IReadOnlyList<ResolvedItemIntro> itemIntros = null)
        {
            DayIndex = dayIndex;
            TicketsRequiredForDay = ticketsRequiredForDay;
            TicketSequence = ticketSequence;
            BoardTimeline = boardTimeline;
            BoardDistribution = boardDistribution;
            TicketRuntime = ticketRuntime;
            Tutorial = tutorial;
            ItemIntros = itemIntros;
        }
    }

    // One "here is something new" popup: the item it is about, plus the words to put on it.
    //
    // It carries the FoodItemConfig rather than a copied name and sprite, which is the whole
    // point of resolving through the catalog: the picture on the popup and the item that
    // then lands on the board are the same asset by construction, so restyling the food
    // restyles its introduction and nothing has to be kept in step.
    public class ResolvedItemIntro
    {
        // The food being introduced, or NULL when this introduction is about a modification.
        public FoodItemConfig Item { get; }

        // The modification being introduced, or NULL when this is a food. Exactly one of the
        // two is set: the parser produces nothing at all for an entry that resolved neither,
        // so no reader has to handle an introduction about nothing.
        public ModificationConfig Modification { get; }

        // Which direction is being taught, or NULL when this is not a modification at all.
        // A nullable bool rather than a bool beside a flag, because "no direction" is a real
        // third state here and a plain false would be indistinguishable from a removal --
        // which is the mistake that would put a "-" badge on a burger.
        public bool? ModificationIsAddition { get; }

        // What the popup calls it: the Day's override when one is authored, else the config's
        // own DisplayName, else the id -- so a popup is never blank, even for a catalog entry
        // nobody has named. Resolved HERE rather than in the view, so the Day Editor's preview
        // and the running game cannot word it differently.
        public string DisplayName { get; }

        // Empty rather than null for an unauthored line, so every reader can ask
        // string.IsNullOrEmpty and none of them can dereference it -- same contract
        // ResolvedTutorialStep.Message carries.
        public string Message { get; }

        // The picture, asked of whichever config this is about rather than stored: a food's
        // is its Sprite and a modification's is its Icon, and each has exactly one.
        public Sprite Sprite => Item != null ? Item.Sprite : Modification == null ? null : Modification.Icon;

        // A food introduction. Kept as its own constructor rather than one taking both configs
        // and a null: two constructors make "exactly one of the two" a thing the compiler helps
        // with at every call site, instead of a rule stated in a comment.
        public ResolvedItemIntro(FoodItemConfig item, string nameOverride, string message)
        {
            Item = item;
            Modification = null;
            ModificationIsAddition = null;
            DisplayName = ResolveName(nameOverride, item?.DisplayName, item?.Id);
            Message = message ?? string.Empty;
        }

        // A modification introduction, with the direction it is being taught in.
        public ResolvedItemIntro(ModificationConfig modification, bool isAddition, string nameOverride, string message)
        {
            Item = null;
            Modification = modification;
            ModificationIsAddition = isAddition;
            DisplayName = ResolveName(nameOverride, modification?.DisplayName, modification?.Id);
            Message = message ?? string.Empty;
        }

        // One fallback chain for both kinds, spelled once: an authored override wins, then the
        // config's display name, then the id. Two copies of it would be two ways for the same
        // popup to be blank.
        private static string ResolveName(string nameOverride, string displayName, string id)
        {
            if (!string.IsNullOrEmpty(nameOverride)) return nameOverride;
            if (!string.IsNullOrEmpty(displayName)) return displayName;
            return id ?? string.Empty;
        }
    }

    // The resolved form of TutorialJson: the one cell that may be picked up and the one
    // tray that may accept it, for as long as the step is unfinished. Nothing here says
    // what it looks like -- the dim, the sorting lifts and the ghost are presentation, and
    // their tuning lives on BoardAnimationConfig with every other animation number.
    //
    // It exists only when the Day authored one, so its presence IS the "this Day has a
    // tutorial" answer; there is no enabled flag on this side, unlike the JSON, which needs
    // one because JsonUtility cannot express absence.
    public class ResolvedTutorial
    {
        // Never null and never empty when a ResolvedTutorial exists at all: the parser
        // produces one only for an enabled block with at least one usable step, so no
        // reader has to handle "a tutorial with nothing to do".
        public IReadOnlyList<ResolvedTutorialStep> Steps { get; }

        public ResolvedTutorial(IReadOnlyList<ResolvedTutorialStep> steps)
        {
            Steps = steps;
        }
    }

    // ONE authored shape: a move the player must make. There WAS a second, PowerupIntro,
    // between D-083 and D-115, and its removal is worth a line rather than a silent
    // deletion. It explained the three powerups, which meant a Day file could name a
    // powerup -- and once each powerup carried its own introduction Day on PowerupConfig
    // (the user's decision, 2026-08-27), the two could disagree about which Day teaches
    // what, with nothing checking them against each other. So this side gave it up: a Day
    // owns its board, its tickets and the moves it forces, and the powerup asset owns when
    // its powerup is taught.
    //
    // The kind survives as a STRING on TutorialStepJson, where it exists to reject a Day
    // file still carrying the old value rather than to select between shapes -- see that
    // field's comment for why deleting it would be the silent failure.
    public class ResolvedTutorialStep
    {
        public int SourceX { get; }
        public int SourceY { get; }
        public int TargetTraySlotIndex { get; }

        // Empty rather than null for an unauthored message, so every reader can ask
        // string.IsNullOrEmpty and none of them can dereference it.
        public string Message { get; }

        public bool HighlightModification { get; }

        public ResolvedTutorialStep(int sourceX, int sourceY, int targetTraySlotIndex, string message, bool highlightModification)
        {
            SourceX = sourceX;
            SourceY = sourceY;
            TargetTraySlotIndex = targetTraySlotIndex;
            Message = message ?? string.Empty;
            HighlightModification = highlightModification;
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
