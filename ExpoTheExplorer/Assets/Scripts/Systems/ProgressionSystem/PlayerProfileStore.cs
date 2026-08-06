using System;
using System.IO;
using UnityEngine;

namespace ExpoTheExplorer.Systems.ProgressionSystem
{
    // Load/save boundary for PlayerProfile. Deliberately ignorant of GameState --
    // callers build a PlayerProfile from whatever they want persisted and hand it
    // in, so this class doesn't need to change when the profile grows (SoftMoney/
    // Gems joining Xp/Level, etc.). No caller invokes Save yet: the real commit
    // trigger (day completed successfully) doesn't exist in the game yet and is
    // deferred to a later PR.
    public class PlayerProfileStore
    {
        private readonly string filePath;

        public PlayerProfileStore(string filePath)
        {
            this.filePath = filePath;
        }

        public PlayerProfile Load(PlayerProfile fallback = null)
        {
            if (!File.Exists(filePath))
            {
                Debug.Log($"No player profile found at {filePath}; using defaults.");
                return fallback ?? new PlayerProfile();
            }

            try
            {
                var json = File.ReadAllText(filePath);
                var profile = JsonUtility.FromJson<PlayerProfile>(json);
                return profile ?? fallback ?? new PlayerProfile();
            }
            catch (Exception e)
            {
                Debug.LogError($"Failed to load player profile at {filePath}: {e.Message}");
                return fallback ?? new PlayerProfile();
            }
        }

        public void Save(PlayerProfile profile)
        {
            try
            {
                var directory = Path.GetDirectoryName(filePath);
                if (!string.IsNullOrEmpty(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                File.WriteAllText(filePath, JsonUtility.ToJson(profile));
            }
            catch (Exception e)
            {
                Debug.LogError($"Failed to save player profile at {filePath}: {e.Message}");
            }
        }
    }
}
