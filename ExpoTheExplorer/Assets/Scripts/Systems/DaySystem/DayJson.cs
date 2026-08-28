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

        // This Day's forced first move, or absent for every Day that has none (which is
        // all of them but day_00). Under runtime rather than editorMeta for the same
        // reason boardDistribution is: it is read while the Day is PLAYED, and
        // DayCatalogParser is contractually blind to editorMeta.
        //
        // Its absence has to be detected by CONTENT, not by null -- JsonUtility hands back
        // a zeroed instance for a missing object, never null (the same trap
        // ResolveBoardDistribution and ResolveTicketRuntime already work around). A zeroed
        // block would read as "cell (0,0), tray 0", which is a real cell and a real tray,
        // so the absence marker cannot be a coordinate. That is what `enabled` is for:
        // false (the default for every Day file that has never heard of this block) means
        // no tutorial, and it has to be typed on purpose to turn one on.
        public TutorialJson tutorial;
    }

    // This Day's forced opening: a SEQUENCE of moves the player is walked through, each
    // locking the board to one cell and one tray until they make it.
    //
    // It became a list rather than a single step when the second step was authored (D-083),
    // which is the repetition abstraction-level.md asks for before generalising -- D-082
    // deliberately shipped one step as one step.
    [Serializable]
    public class TutorialJson
    {
        // The whole-tutorial absence marker, not a designer convenience toggle -- see the
        // `tutorial` field comment above for why a zeroed block cannot be told apart from
        // a real one by content. It survives the move to a list because it also lets a
        // tutorial be switched off during authoring without deleting the authored steps.
        public bool enabled;

        public TutorialStepJson[] steps;
    }

    // One forced move. The cell, the tray, and how that step teaches -- nothing about what
    // it LOOKS like: the dim opacity, the ghost's speed and the message's size and position
    // are presentation and live on BoardAnimationConfig.
    [Serializable]
    public class TutorialStepJson
    {
        // What SHAPE of step this is, and since D-115 there is exactly one shape a Day may
        // author: "ForcedMove", or empty, which means the same thing. It is kept as a field
        // rather than deleted precisely BECAUSE the other value is gone -- "PowerupIntro"
        // was legal here until D-115, and a Day file still carrying one must fail loudly.
        // Delete this field and JsonUtility would silently ignore that key, quietly turning
        // a powerup panel into a forced move on cell (0,0) into tray 0. DayCatalogParser
        // refuses anything but the two accepted spellings.
        //
        // A string rather than the enum itself, for the reason BoardDistributionJson's mode
        // already gives: JsonUtility writes an enum as a bare ordinal, which is unreadable
        // in a hand-edited Day file and degrades a typo to whatever happens to be 0.
        //
        // The powerup tutorial moved to PowerupConfig, where each powerup carries the Day
        // that introduces it. A Day file names no powerup any more, which is what leaves
        // exactly one authority for "what happens on this Day" on each side of that line.
        public string kind;

        // The only cell that can be picked up while this step is unfinished. It must hold
        // an item when the step BEGINS -- for the first step DayValidator can check that
        // against the Day's own boardTimeline, and for every step the runtime re-checks it
        // against the live board rather than trusting the author.
        public int sourceX;
        public int sourceY;

        // The only tray that will accept that item, 0..2. Authoring this is what decides
        // whether the forced move DELIVERS or costs a life, and nothing checks it: the
        // tray's ticket has to actually want what is on the source cell.
        public int targetTraySlotIndex;

        // Shown while the step runs, or empty for a step whose ghost speaks for itself.
        // Text is content and belongs here rather than in a string literal in code.
        public string message;

        // Points an arrow at the ingredient a modification added to the source item, and a
        // second one at that modification's box on the target tray's ticket card. Only
        // meaningful for a source item that actually carries a modification; a step that
        // asks for it on a plain item simply gets no arrows.
        public bool highlightModification;
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

        // How this Day's ticketSequence was generated. Purely a record plus the input for
        // the next Generate run: the rolled sequence is already baked into runtime, so
        // nothing here affects a Day being played. It lives in editorMeta for exactly that
        // reason, and the point of keeping it is being able to come back to a Day months
        // later and see -- and reuse -- the settings it was built with.
        public TicketGenerationJson ticketGeneration;
    }

    // No "has..." toggle and no "Override" suffixes: a Day carries the complete set, there
    // is no base to fall back to per-field (D-006). A Day file written before this block
    // existed is the one exception, and the Day Editor fills it from the config asset --
    // see DayEditorTicketGeneration.FromJson. Unlike the runtime blocks, an absent one here
    // is not fatal: DayCatalogParser never reads editorMeta at all.
    [Serializable]
    public class TicketGenerationJson
    {
        public float sideInclusionChance;
        public float drinkInclusionChance;
        public float modificationCountLambda;
        public float modificationAdditionChance;
        public MainDishWeightJson[] mainDishWeights;

        // How many tickets of each patience type a Generate should lay down. ALL THREE ZERO
        // means unauthored, not "zero tickets of every type" -- a Day file written before
        // these existed reads them as 0, and authoring 0/0/0 by hand would ask for an empty
        // Day, which the ticket count already governs. DayContentGenerator.BuildPatiencePlan
        // is the single reader of that rule and falls back to the old uniform roll there, so
        // no already-authored Day changes balance until someone sets a mix on purpose.
        public int patientTicketCount;
        public int normalTicketCount;
        public int impatientTicketCount;
    }

    [Serializable]
    public class MainDishWeightJson
    {
        // Food id, like every other food reference in Day JSON -- resolved against
        // FoodCatalog, and simply dropped if the FoodItemConfig behind it is gone.
        public string foodItemId;
        public float weight;
        public float modificationCountLambda;

        // Absent in a Day file written before this field existed, which deserializes to 0.
        // That is NOT clamped here -- MainDishWeight.NormalizeMaxModificationCount owns the
        // "0 means unauthored, use the default" rule for every reader, so re-stating it in
        // the schema would be a second, competing rule (same stance as BoardDistributionJson).
        public int maxModificationCount;
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
