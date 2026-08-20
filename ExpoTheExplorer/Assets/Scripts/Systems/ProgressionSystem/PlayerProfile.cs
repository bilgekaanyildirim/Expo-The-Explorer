using System;

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
    }
}
