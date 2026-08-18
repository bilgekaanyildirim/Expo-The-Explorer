using System;
using System.IO;
using UnityEngine;

namespace ExpoTheExplorer.Systems.ProgressionSystem
{
    // Load/save boundary for PlayerProfile. Deliberately ignorant of GameState --
    // callers build a PlayerProfile from whatever they want persisted and hand it
    // in, so this class doesn't need to change when the profile grows. The
    // file-format behaviour here -- missing, corrupt and unrecognised-version
    // files all fall back instead of throwing -- is what this class exists to keep.
    public class PlayerProfileStore
    {
        // Root CLAUDE.md invariant: "save data carries a version number;
        // unversioned saves are never written". Every Save stamps this.
        public const int CurrentVersion = 1;

        private const string FileName = "player_profile.json";

        private readonly string filePath;

        // Where the profile lives is a persistence detail, so it is decided here
        // rather than by the caller: GameManager should not have to know the
        // filename, the folder, or how to join them (which is also what kept
        // System.IO out of Bootstrap).
        public PlayerProfileStore()
            : this(Path.Combine(Application.persistentDataPath, FileName))
        {
        }

        // Explicit path, for tests that need a throwaway file.
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
                if (profile == null) return fallback ?? new PlayerProfile();

                return IsVersionReadable(profile.Version) ? profile : fallback ?? new PlayerProfile();
            }
            catch (Exception e)
            {
                Debug.LogError($"Failed to load player profile at {filePath}: {e.Message}");
                return fallback ?? new PlayerProfile();
            }
        }

        // Accepts anything from 1 up to what this build knows, and refuses only
        // the two cases it genuinely cannot read.
        //
        // The asymmetry is deliberate. Refusing everything except CurrentVersion
        // would wipe a real player's wallet the day a v2 field is added, since
        // their perfectly good v1 file would be thrown away -- an older file is
        // readable, it just lacks the newer fields, which JsonUtility leaves at 0.
        // Version 0 is the opposite case: it means no version was ever written, so
        // the numbers next to it have unknown meaning, and it is also
        // indistinguishable from a legitimately zero balance -- which is the whole
        // reason the invariant demands a version field in the first place.
        // A version above CurrentVersion was written by a newer build and may
        // reinterpret fields this one would misread.
        private static bool IsVersionReadable(int version)
        {
            if (version >= 1 && version <= CurrentVersion) return true;

            Debug.LogWarning(
                version == 0
                    ? "Player profile carries no schema version; ignoring it and using defaults."
                    : $"Player profile schema v{version} is newer than this build understands (v{CurrentVersion}); ignoring it and using defaults.");
            return false;
        }

        // Stamps CurrentVersion itself rather than trusting the caller, so an
        // unversioned profile can never reach disk however it was constructed.
        // Note this WRITES to the instance it is given: stamping a copy would mean
        // enumerating every field here, which is exactly the payload knowledge this
        // class is built to avoid -- adding a profile field would then require
        // editing Save.
        public void Save(PlayerProfile profile)
        {
            if (profile == null) return;

            profile.Version = CurrentVersion;

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
