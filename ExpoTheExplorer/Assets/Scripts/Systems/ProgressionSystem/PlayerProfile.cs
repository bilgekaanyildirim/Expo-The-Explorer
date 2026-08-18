using System;

namespace ExpoTheExplorer.Systems.ProgressionSystem
{
    // Persisted player profile. Deliberately EMPTY right now: its only fields
    // were Xp/Level and that system was removed, so nothing in the game
    // currently survives a session. The class is kept rather than deleted
    // because the two pieces of state that are already designed to live here
    // are queued up -- GameState.CurrentDayIndex (which Day the player is on,
    // DaySystem_Roadmap Q4) and the wallet plus a schema version field
    // (.claude/economy-plan.md Adım 2). The first of those to be implemented
    // adds its field here and re-wires GameManager to PlayerProfileStore.
    //
    // When fields come back they must stay public (not properties) --
    // UnityEngine.JsonUtility only serializes public fields or [SerializeField]
    // private fields; auto-properties silently round-trip as "{}" with no error.
    [Serializable]
    public class PlayerProfile
    {
    }
}
