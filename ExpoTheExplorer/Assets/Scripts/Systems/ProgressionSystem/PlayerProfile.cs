using System;

namespace ExpoTheExplorer.Systems.ProgressionSystem
{
    // Persisted player profile. Fields must stay public (not properties) --
    // UnityEngine.JsonUtility only serializes public fields or [SerializeField]
    // private fields; auto-properties silently round-trip as "{}" with no error.
    [Serializable]
    public class PlayerProfile
    {
        public int Xp;
        public int Level;
    }
}
