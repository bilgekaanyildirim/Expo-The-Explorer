using System;

namespace ExpoTheExplorer.Systems.ProgressionSystem
{
    // Everything the game keeps between sessions. Currently that is the wallet
    // and nothing else (economy-plan.md Adım 4) -- the Xp/Level fields this class
    // used to hold went away with that system (decisions.md D-009).
    //
    // Fields must stay public FIELDS, not properties: UnityEngine.JsonUtility
    // only serializes public fields or [SerializeField] private ones, and an
    // auto-property round-trips as "{}" with no error at all.
    //
    // Adding a field: bump PlayerProfileStore.CurrentVersion, and remember that
    // every already-saved file simply lacks the new field and will read it as 0.
    // If 0 is not a safe default for what you are adding, the reader owes it an
    // explicit per-version upgrade rather than trusting the zero.
    // GameState.CurrentDayIndex is the next candidate (DaySystem_Roadmap Q4) and
    // 0 happens to be exactly right for it: "start at the first Day".
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
    }
}
