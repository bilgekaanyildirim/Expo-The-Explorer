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

        // NO Lives FIELD, and its absence is a decision rather than an oversight
        // (v6, decisions.md D-064). Lives were persisted from v3 to v5 (D-014), on the
        // reasoning that they already carried from one day to the next inside a session
        // so carrying them across sessions was the same rule. Both halves of that were
        // reversed: lives are a per-day resource now, refilled at the start of every day
        // including a successful advance, so there is nothing left to carry and a saved
        // count would be a second, staler authority on a number GameState already opens
        // at full. MaxLives was never here either, for the reason it still is not:
        // nothing varies it, so it stays seeded from GameState.DefaultStartingLives.
        //
        // v6 REMOVES a field, which is the one schema change this file's "adding a
        // field" note above does not cover. It needs no upgrade branch: JsonUtility
        // ignores a key with no matching field, so every v3-v5 file's Lives is simply
        // dropped on read, which is exactly the intent.

        // Keys the player holds, added in v7 (decisions.md D-065). Keys gate PLAYING --
        // starting a day needs one, and leaving a lost day spends one -- so unlike Lives,
        // which D-064 made a per-day allowance that is deliberately not saved, this is
        // exactly the kind of thing that has to survive quitting the game.
        //
        // **-1 IS THE "ABSENT" VALUE HERE, NOT 0**, and that is the whole subtlety of this
        // field. 0 is a real, reachable count meaning "locked out, go wait" -- so it
        // cannot double as "this file predates keys" the way an absent CurrentDayIndex
        // could safely read as Day 0. The correct value for an older file is the CAP,
        // which lives on KeyConfig, which PlayerProfileStore has no reference to and must
        // never gain: the store is a file boundary and knows nothing about what a key
        // means. So UpgradeToCurrent writes -1, the marker crosses the boundary, and
        // KeyManager.ApplyPersisted -- the one place the cap is known -- resolves it.
        public int Keys;

        // When the CURRENT partial regen interval started, as UTC ticks (v7). Not a
        // countdown: a remaining-seconds field would stop the moment the game closed,
        // and the whole design is that keys accrue while it is closed.
        //
        // Ticks rather than a DateTime because JsonUtility cannot serialize a DateTime at
        // all -- it round-trips as "{}" with no error, the same trap this class already
        // documents about auto-properties and HashSet.
        //
        // Needs NO migration branch, unlike Keys above: 0 means "no anchor recorded" and
        // ApplyPersisted starts the clock at load time, which is the correct reading for
        // an older file. That is also what keeps System.DateTime out of PlayerProfileStore
        // -- the file never has to invent a timestamp, it just says it has none.
        public long LastKeyRegenUtcTicks;

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
        // Unlike LastCelebratedDayIndex (v5), an absent value here needs no real
        // migration: nothing owned is exactly what an older save means, and an empty
        // list says that correctly.
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
        // whatever they have, they have already seen. It was the second migration in the
        // project's history and the same shape as v3's Lives; since v6 dropped that field
        // and its upgrade with it, this is the ONLY semantic migration left.
        //
        // A NEW player starts at 0 and is correct without a migration: the comparison is
        // strictly-after, so a prop authored at day 0 is not treated as having just arrived.
        public int LastCelebratedDayIndex;
    }
}
