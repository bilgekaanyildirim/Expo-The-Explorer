using System;
using System.Collections.Generic;

namespace ExpoTheExplorer.Systems.ProgressionSystem
{
    // Everything the game keeps between sessions: the wallet (economy-plan.md
    // Adım 4) and, since the main screen exists, which Day the player is on
    // (decisions.md D-012) -- the Xp/Level fields this class used to hold went away
    // with that system (decisions.md D-009).
    //
    // Fields must stay public FIELDS, not properties: UnityEngine.JsonUtility
    // only serializes public fields or [SerializeField] private ones, and an
    // auto-property round-trips as "{}" with no error at all.
    //
    // Adding a field: bump PlayerProfileStore.CurrentVersion, and remember that
    // every already-saved file simply lacks the new field and will read it as 0.
    // If 0 is not a safe default for what you are adding, the reader owes it an
    // explicit per-version upgrade rather than trusting the zero. CurrentDayIndex
    // (v2) is the worked example: 0 means "the first Day", which is exactly what a
    // v1 file's missing field should mean, so it needed no upgrade code at all.
    [Serializable]
    public class PlayerProfile
    {
        // Schema version of the file this came from. 0 means "no version was ever
        // written", i.e. a file from before versioning existed OR a hand-made
        // one -- PlayerProfileStore refuses those rather than guessing what the
        // numbers beside them mean.
        public int Version;

        public int SoftMoney;
        public int Gems;

        // Position in the resolved Day catalog the player should be dropped into
        // next -- the same number GameState.CurrentDayIndex holds at runtime, not a
        // Day file's own dayIndex (that only decides sort order). Added in v2, when
        // the main screen made "which day does Play open" a question that had to
        // survive quitting the game. It is written on day advance and on the two
        // paths that leave a day for the main screen; a save whose index has run
        // past the last authored Day is clamped by GameManager on load rather than
        // trusted, since the Day catalog can shrink between builds.
        public int CurrentDayIndex;

        // Lives the player has left, added in v3 (decisions.md D-014). Lives already
        // carried from one day to the next inside a session -- AdvanceToNextDay
        // deliberately skips LivesManager, so only a failed-day retry or a paid
        // Continue refills them -- and this extends that same rule across sessions
        // rather than inventing a new one.
        //
        // This is the field that broke the "a missing field reads as 0 and that is
        // fine" pattern: 0 lives is a dead player, not a fresh one, so
        // PlayerProfileStore.Load carries an explicit upgrade for files older than
        // v3. It is the reason the version number exists at all.
        //
        // MaxLives is deliberately NOT here: nothing varies it, so it stays seeded
        // from GameState.DefaultStartingLives. The day it becomes upgradable it needs
        // a field of its own and another version bump.
        public int Lives;

        // Meta props the player has BOUGHT, added in v4. Each entry is the qualified
        // "<location>.<item>" key MetaCatalog.OwnershipKey composes -- local ids alone
        // would collide the moment a second location reuses a name, which it is expected
        // to (a fountain in two restaurants is two purchases).
        //
        // This field's format is NOT decided here. MetaCatalog.OwnershipKey is the single
        // place that spells the key, and this class only carries the strings: writing the
        // format down twice is exactly what a joined key invites and what one authority
        // for it prevents.
        //
        // A LIST rather than a HashSet because JsonUtility serializes public fields and
        // List<>, and quietly round-trips a HashSet as "{}" with no error at all -- the
        // same trap this class's own comment names about auto-properties. Callers that
        // want set semantics convert on read; MetaResolver already takes an ISet<string>.
        //
        // Unlike Lives (v3), an absent value here needs no real migration: nothing owned
        // is exactly what an older save means, and an empty list says that correctly.
        // PlayerProfileStore still normalizes it, but defensively rather than semantically
        // -- see the comment on UpgradeToCurrent.
        //
        // Deliberately holds only PURCHASES. Whether a location is unlocked, and whether
        // a Day-unlocked prop is on screen, are derived from the day index against an
        // authored one (decisions.md D-015/D-017) and must never be written here: a second
        // copy would start lying the moment the catalog is re-authored.
        public List<string> OwnedMetaItemIds = new();

        // How far the meta screen has already congratulated the player (v5, decisions.md
        // D-041). A Day-unlocked prop that opened after this day and at or before
        // CurrentDayIndex is one the player has not been shown yet, so the grounds zoom in on
        // it once and then move this marker up.
        //
        // Why it is stored at all: "has this been celebrated" is not derivable from anything
        // else. Everything else about a Day-unlocked prop falls out of comparing day indices
        // (D-015/D-017), which is why it is not written here -- but a one-time event that has
        // or has not happened yet is exactly the kind of fact that has to be remembered.
        //
        // ZERO IS NOT A SAFE DEFAULT for this one, which is why v5 carries a real migration
        // rather than only a bump. An existing save read as 0 would mean every prop the
        // player unlocked days ago is still owed a celebration, and they would be shown a
        // queue of them on next launch. UpgradeToCurrent sets it to CurrentDayIndex instead:
        // whatever they have, they have already seen. This is the second migration in the
        // project's history and the same shape as v3's Lives.
        //
        // A NEW player starts at 0 and is correct without a migration: the comparison is
        // strictly-after, so a prop authored at day 0 is not treated as having just arrived.
        public int LastCelebratedDayIndex;
    }
}
