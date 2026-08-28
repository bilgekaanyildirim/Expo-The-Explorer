using System;
using UnityEngine;
using UnityEngine.Serialization;

namespace ExpoTheExplorer.Data
{
    // Every number behind GDD Section 5.2, consumed by PowerupManager. They live here
    // rather than in code because the root invariant says content does, and because each
    // one is a pacing dial: how much help a player is handed for free, and what skipping
    // the wait costs.
    //
    // IT CARRIED TWO MORE FIELDS UNTIL 2026-08-25 -- clarityDurationSeconds and
    // clarityDimAlpha -- and they are worth a line here rather than a silent deletion.
    // They existed because the third powerup was believed to FADE the board's noise for a
    // few seconds. The user corrected what it does: it REMOVES those items. A removal has
    // no duration to author and no opacity to author, so both numbers stopped describing
    // anything, and nothing replaced them -- the effect needs no authored value at all.
    //
    // Unlike KeyConfig -- whose defaults ARE the user's authored values -- these are
    // PLACEHOLDERS, the shape LivesConfig and StarScoreConfig are in. They exist so
    // creating the asset needs no typing and so nothing is silently zero; the real
    // balance comes from play. Do not treat any number below as a design decision.
    //
    // THE STALE-ASSET TRAP APPLIES HERE (decisions.md D-075): adding a field to this
    // class does not add it to an already-saved PowerupConfig.asset, which then reads
    // back as zero with no error. After touching this file, run
    // Tools > ExpoTheExplorer > Re-serialize Data Configs.
    [CreateAssetMenu(fileName = "PowerupConfig", menuName = "ExpoTheExplorer/Data/Powerup Config")]
    public class PowerupConfig : ScriptableObject
    {
        // One block per powerup rather than an array indexed by the enum, and that is a
        // deliberate trade: an array would be shorter here but shows up in the Inspector
        // as "Element 0/1/2" with no way to tell which powerup is which, and can be
        // resized to the wrong length by a stray drag. Three named fields cannot.
        [Tooltip("GDD 5.2 #1 — auto-places the items active tickets still need into their trays.")]
        [SerializeField] private PowerupSettings autoCollect = new(startingCharges: 1, gemCost: 30);

        [Tooltip("GDD 5.2 #2 — pulls every active ticket's countdown back to its own limit.")]
        [SerializeField] private PowerupSettings timeReset = new(startingCharges: 1, gemCost: 20);

        // THESE NUMBERS ARE NOW BACKWARDS and are left that way on purpose rather than
        // guessed at again. They were set while this powerup was believed to be the
        // gentlest of the three (a few seconds of easier reading), so it got the cheapest
        // price and the largest free stock. Clearing the board outright is plausibly the
        // STRONGEST of the three, which makes cheapest-and-most exactly the wrong corner.
        // Correcting it is a balancing pass with real play behind it, not a second guess
        // typed in the same afternoon.
        //
        // [FormerlySerializedAs] is doing real work here, not decoration: the field was
        // called boardClarity when PowerupConfig.asset was first authored and TUNED, and a
        // plain rename would have orphaned that block -- Unity matches serialized data by
        // field name, so the authored numbers would have been silently replaced by the
        // defaults on this line. The attribute makes the existing asset load unchanged
        // with no Inspector work. (Its JSON counterpart, PlayerProfile, gets no such help:
        // JsonUtility ignores this attribute, which is why the save file needed a real v9
        // migration for the same rename.)
        [Tooltip("GDD 5.2 #3 — removes every board item no active ticket needs. Probably the strongest of the three: price and starting stock still need re-tuning.")]
        [FormerlySerializedAs("boardClarity")]
        [SerializeField] private PowerupSettings noiseClear = new(startingCharges: 2, gemCost: 15);

        // What a locked powerup says on the badge over it. Asset-level rather than per-powerup
        // because it is one sentence pattern, not three, and a string here rather than in code
        // because player-facing text is content (the root invariant's first line).
        //
        // {0} is the day number the powerup unlocks on, already converted to the number the
        // player counts. The word is deliberately "Day": this game has no XP and no levels
        // (root CLAUDE.md is explicit that the sequential-content concept is a Day), so a
        // label reading "Lvl 6" would name something that does not exist. Rewrite it freely --
        // that is what makes it a config field.
        [Tooltip("Shown on a locked powerup. {0} is the day number it unlocks on.")]
        [SerializeField] private string lockLabelFormat = "Day {0}";

        // Formatted here rather than at the two call sites, so the two can never disagree and
        // neither has to remember which argument {0} is. A format string an author has broken
        // (no placeholder, stray brace) degrades to the bare number instead of throwing: a
        // wrong-looking badge is survivable, an exception during a HUD render is not.
        public string LockLabelFor(PowerupSettings settings)
        {
            if (settings == null) return string.Empty;

            var dayNumber = settings.UnlocksOnDayNumber;
            if (string.IsNullOrEmpty(lockLabelFormat)) return dayNumber.ToString();

            try
            {
                return string.Format(lockLabelFormat, dayNumber);
            }
            catch (FormatException)
            {
                Debug.LogWarning(
                    $"PowerupConfig's Lock Label Format ('{lockLabelFormat}') is not a valid format string, so the " +
                    "lock badge shows the bare day number. It needs a single {0} placeholder.", this);
                return dayNumber.ToString();
            }
        }

        // The single lookup every consumer goes through, so nothing outside this class
        // has to know there are three separate fields. Written out as a switch rather
        // than an array index for the reason HapticConfig's mirror enum is: a new
        // PowerupType stops the build here instead of silently falling through to a
        // default that pays out zero charges forever.
        public PowerupSettings For(PowerupType type) => type switch
        {
            PowerupType.AutoCollect => autoCollect,
            PowerupType.TimeReset => timeReset,
            PowerupType.NoiseClear => noiseClear,
            _ => throw new ArgumentOutOfRangeException(nameof(type), type, "No PowerupSettings block on PowerupConfig for this type."),
        };
    }

    // One powerup's three stock numbers. A [Serializable] class rather than a struct so
    // the field initializers above can name their arguments; Unity serializes both the
    // same way, and nothing here is copied often enough for the reference to cost
    // anything (it is read on a button press, not per frame).
    //
    // Unity never calls this constructor when LOADING an asset -- it fills the fields
    // directly -- so the constructor's only job is making the defaults above readable.
    // That is exactly why the stale-asset warning on the class above matters.
    [Serializable]
    public class PowerupSettings
    {
        // The player-facing words for this powerup, and the reason they live HERE rather
        // than in the tutorial that first showed them: they describe the POWERUP, not the
        // moment it is explained. The tutorial's third step reads them, and the main-screen
        // shop -- whose rows are hand-labelled in the scene today -- can read the same two
        // strings instead of keeping a second copy that is free to disagree.
        [Tooltip("The powerup's name as the player sees it.")]
        [SerializeField] private string displayName;

        [Tooltip("One sentence saying what pressing it does. Shown by the tutorial that introduces the three powerups.")]
        [SerializeField, TextArea] private string description;

        [Tooltip("How many charges a brand-new player owns. Also what an older save (which predates powerups) resolves to.")]
        [Min(0)]
        [SerializeField] private int startingCharges;

        [Tooltip("Gem price of one charge, bought on the MAIN SCREEN. The day scene never sells.")]
        [Min(0)]
        [SerializeField] private int gemCost;

        // THE DAY-COMPLETION EARN PATH IS GONE (2026-08-28, the user's decision), and it is
        // worth a line here rather than a silent deletion because GDD 5.2 named it. It paid a
        // per-type amount every time a day was completed, and it had been authored at 0 on all
        // three powerups since the asset was first tuned -- so the path had never actually
        // paid anyone anything. A dial nobody had turned on, describing a reward nobody
        // received, is surface rather than design. Powerups are now earned two ways: the Gem
        // purchase, and the starting stock (plus the tutorial's floor, which is a teaching
        // guarantee rather than an earn path). Re-adding it is a design change, and GDD 5.2
        // and the root CLAUDE.md were both edited to stop promising it.

        // WHEN this powerup is taught, beside the words that teach it. The schedule lives
        // here rather than in the Day files because it is a property of the POWERUP -- one
        // question with one answer -- and spreading it across three Day JSONs would mean
        // moving an introduction was a two-file edit with nothing checking the halves
        // against each other. The Day files own forced MOVES and nothing else now.
        //
        // SIX FLAT FIELDS RATHER THAN A NESTED `PowerupTutorialSettings` BLOCK, and that is
        // the user's call plus a crash (2026-08-27). A nested [Serializable] class draws as a
        // FOLDOUT, and expanding that foldout killed the Editor every single time: Odin runs
        // with `enableUIToolkitSupport`, so IMGUI is drawn inside IMGUIContainers, and a
        // UIToolkit element's resolved geometry is NaN until its first layout pass -- exactly
        // the frame a foldout expands in. The NaN rect reaches AppKit's addCursorRect:, which
        // answers an invalid rect with an NSException that kills the process rather than a C#
        // error you could read in the console. Three attempts to keep the block and make it
        // safe (a pure getter, dropping [TextArea], dropping [Range]) all still crashed.
        // Flat fields have no foldout to expand, so the frame that produced the bad rect does
        // not exist. The user wanted them flat anyway -- "orayı açma kapamalı yapma".
        //
        // The `tutorial` prefix on each name is what the nesting used to provide: it keeps
        // the six readable as one group in an Inspector that no longer indents them.
        [Tooltip("The Day that introduces this powerup. NEGATIVE means it is never introduced, which is what an unscheduled powerup should read as.")]
        [SerializeField] private int tutorialIntroDayIndex = -1;

        [Tooltip("When the forced use is asked for, once the panel has been read.")]
        [SerializeField] private PowerupTutorialTrigger tutorialUseTrigger = PowerupTutorialTrigger.AtDayStart;

        // THERE IS NO PATIENCE-RATIO FIELD HERE, and its removal (2026-08-28, the user's
        // observation) fixed a dual authority rather than trimming clutter. It held 1/3 --
        // which is EconomyConfig.CriticalRatio, the single authority for where a ticket's
        // timer bar turns red and its tip tier drops (CLAUDE.md, Tip Tiers). A second copy
        // was free to drift, and a lesson that fired at a moment the bar did not mark would
        // be teaching against what the player can see. GameManager reads the economy's number
        // when it builds the deferred step, so "teach Time Reset when a ticket goes red" is
        // one number in one place. It also answered the field's other problem: it was drawn
        // on all three powerups while meaning something on only one.
        //
        // THERE IS NO USE-INSTRUCTION FIELD EITHER (same pass, same reason in miniature). It
        // held "Try it now - it drops what the orders still need into the trays" beside a
        // Description reading "Fills your trays with what the current orders still need" --
        // one sentence authored twice. The spotlight prints the Description now, so a powerup
        // has one sentence, shown at the two moments it matters.

        // A FLOOR, not an addition, and that is the whole reason the call site reads "ensure":
        // the introduction is re-armed on every day-start path, retries included, so a grant
        // that added would make replaying the introduction Day a charge farm. Topping up to a
        // floor is idempotent.
        //
        // ONE field rather than the two this shipped with. The second was a separate floor for
        // the forced press, and it was the same idea twice: how many charges the lesson hands
        // over is a balance dial, but "at least one, or the press is impossible" is a rule of
        // the mechanic, not a number to tune -- so it lives in GameManager as a constant and
        // this stays the only dial. Distinct from StartingCharges above, which is first-launch
        // stock and says nothing about a lesson that runs on Day 5.
        [Tooltip("The player is topped up to at least this many charges when this powerup is introduced, so the forced press is possible and they have one left to try again with. A FLOOR, not a gift: a player who already holds more keeps them, and replaying this Day grants nothing.")]
        [Min(0)]
        [SerializeField] private int tutorialCharges = 2;

        public PowerupSettings(int startingCharges, int gemCost)
        {
            this.startingCharges = startingCharges;
            this.gemCost = gemCost;
        }

        public string DisplayName => displayName;
        public string Description => description;
        public int StartingCharges => startingCharges;
        public int GemCost => gemCost;

        // The schedule, read-only. Every one of these is a PURE getter -- no assignment, no
        // lazy allocation. That is a rule rather than a style here: writing to a serialized
        // field from inside a getter puts a mutation in the middle of an IMGUI pass, and this
        // asset has already cost four Editor crashes in one evening.
        public int TutorialIntroDayIndex => tutorialIntroDayIndex;
        public PowerupTutorialTrigger TutorialUseTrigger => tutorialUseTrigger;
        public int TutorialCharges => tutorialCharges;

        // The one question every reader actually asks, kept here rather than spelled out as
        // `TutorialIntroDayIndex >= 0` at each call site so "unscheduled" has one definition.
        public bool IsTutorialScheduled => tutorialIntroDayIndex >= 0;

        public bool IntroducedOnDay(int dayIndex) => IsTutorialScheduled && tutorialIntroDayIndex == dayIndex;

        // LOCKED UNTIL TAUGHT. A powerup the player has not been introduced to cannot be
        // pressed and cannot be bought -- pressing something nobody explained is not a
        // discovery, it is a wasted charge, and selling it is a wasted purchase.
        //
        // Spelled ONCE, here, and taking the day as an argument, because neither side owns
        // both halves: this asset has the schedule and no idea what day it is, while each
        // screen has the day and no idea what the schedule says. A copy of the comparison at
        // the two call sites is the shape that drifts.
        //
        // AN UNSCHEDULED POWERUP IS UNLOCKED, not locked forever. A negative introDayIndex
        // means nobody has scheduled a lesson; reading that as "never unlocks" would mean
        // clearing a schedule silently deletes a powerup from the game. Fail open -- the same
        // direction the key gate and the first-run Play gate chose.
        //
        // The day passed in is the AUTHORED index (DayDefinition.DayIndex), which is what
        // GameManager compares tutorialIntroDayIndex against when it builds the lesson. Two
        // notions of "which day" is exactly how a lock opens a day early.
        public bool IsUnlockedOnDay(int dayIndex) => !IsTutorialScheduled || dayIndex >= tutorialIntroDayIndex;

        // What the lock says, as the player counts days: +1, matching SettingsPopupView and
        // MainScreenView, which both print a day number that way because CurrentDayIndex is a
        // catalog position and players count from one.
        public int UnlocksOnDayNumber => tutorialIntroDayIndex + 1;
    }

    // When the forced use of a powerup becomes due, once its panel has been read.
    //
    // Two values rather than a bool because they are not two settings of one thing: one
    // arms the moment the panel closes, the other waits for something to happen in the
    // day. A `bool isDeferred` would have to be read together with the ratio to mean
    // anything, and a ratio authored beside a false bool would silently do nothing.
    public enum PowerupTutorialTrigger
    {
        // The panel closes and the forced press is asked for immediately, with the day
        // still frozen. Auto-Collect and Noise Clear: both are useful the moment a day
        // opens, so there is nothing to wait for.
        AtDayStart = 0,

        // The panel closes, the DAY RUNS AGAIN, and the forced press is asked for the
        // first time an active ticket's remaining time falls to TriggerPatienceRatio of
        // its own limit. Time Reset: a powerup that buys time teaches nothing at a moment
        // when nobody is short of it.
        TicketPatienceBelow = 1,
    }
}
