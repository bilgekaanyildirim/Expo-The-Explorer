using System;
using System.Collections.Generic;
using System.IO;
using ExpoTheExplorer.Core;
using UnityEngine;

namespace ExpoTheExplorer.Systems.ProgressionSystem
{
    // Load/save boundary for PlayerProfile. Ignorant of GameState's SHAPE --
    // callers build a PlayerProfile from whatever they want persisted and hand it
    // in, so Save doesn't change when the profile grows. The file-format behaviour
    // here -- missing, corrupt and unrecognised-version files all fall back instead
    // of throwing -- is what this class exists to keep.
    //
    // It does read one compile-time constant from Core, GameState.DefaultStartingLives,
    // because "a file older than v3 means the player had full lives" and "a brand-new
    // player starts with full lives" are both SCHEMA facts and belong here. The
    // alternative was a second copy of that number living beside the first, which the
    // single-authority invariant rules out.
    public class PlayerProfileStore
    {
        // Root CLAUDE.md invariant: "save data carries a version number;
        // unversioned saves are never written". Every Save stamps this.
        //
        // v1: Version + SoftMoney + Gems (economy-plan.md Adım 4).
        // v2: + CurrentDayIndex (decisions.md D-012). A v1 file lacks it and reads
        //     it as 0, which is precisely "start at the first Day", so v2 needed no
        //     upgrade branch -- the version RANGE check below is what carries that
        //     file forward intact instead of discarding a real player's wallet.
        // v3: + Lives (decisions.md D-014). The FIRST field where 0 is not a safe
        //     default: an older file lacks Lives, would read 0, and 0 lives is a dead
        //     player rather than a fresh one. So this one gets a real upgrade in
        //     UpgradeToCurrent below -- the case the invariant's version number exists
        //     for.
        // v4: + OwnedMetaItemIds (decisions.md D-020). Back to a SAFE default: nothing
        //     owned is exactly what an older save means. So unlike v3 this needs no
        //     semantic migration -- only a null guard, because the safe default is an
        //     empty list rather than a zero and a reference type can arrive as null.
        public const int CurrentVersion = 4;

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
            // Normalize wraps EVERY return, including the fallback paths: a caller-supplied
            // fallback is an ordinary object someone else built, so its list can be null
            // just as easily as a deserialized one's.
            if (!File.Exists(filePath))
            {
                Debug.Log($"No player profile found at {filePath}; using defaults.");
                return Normalize(fallback ?? Defaults());
            }

            try
            {
                var json = File.ReadAllText(filePath);
                var profile = JsonUtility.FromJson<PlayerProfile>(json);
                if (profile == null) return Normalize(fallback ?? Defaults());

                if (!IsVersionReadable(profile.Version)) return Normalize(fallback ?? Defaults());

                UpgradeToCurrent(profile);
                return Normalize(profile);
            }
            catch (Exception e)
            {
                Debug.LogError($"Failed to load player profile at {filePath}: {e.Message}");
                return Normalize(fallback ?? Defaults());
            }
        }

        // What a brand-new player gets, and what every fallback path returns when the
        // caller supplied none. Money and day index are plain zero -- own nothing,
        // start at the first Day -- but lives must be FULL, because 0 lives is a dead
        // player and a fresh profile would otherwise be unplayable.
        private static PlayerProfile Defaults()
        {
            return new PlayerProfile { Lives = GameState.DefaultStartingLives };
        }

        // Brings a readable older file up to the current schema, in place. This is the
        // explicit per-version upgrade PlayerProfile's comment demands for any field
        // whose absent-reads-as-0 is not a safe default -- do not replace it with a
        // field initializer on PlayerProfile: that would work by relying on JsonUtility
        // leaving absent fields alone, which is implicit, untestable from the file's
        // point of view, and silently wrong the day someone constructs a profile
        // another way.
        //
        // Only fields ADDED by a version are touched, never the ones the file already
        // carries -- a v2 file's SoftMoney, Gems and CurrentDayIndex must survive this
        // untouched, which is exactly what PlayerProfileStoreTests pins down against a
        // literal v2 payload.
        private static void UpgradeToCurrent(PlayerProfile profile)
        {
            if (profile.Version >= CurrentVersion) return;

            if (profile.Version < 3)
            {
                profile.Lives = GameState.DefaultStartingLives;
            }
        }

        // Not part of UpgradeToCurrent, and the difference is not cosmetic: that method
        // returns early once the file is already current, which is correct for a
        // version-specific fix like v3's Lives but wrong for this. A REFERENCE type can
        // arrive null from any version -- a hand-edited file, an interrupted write, a
        // future field someone forgets to initialize -- and every one of those would
        // reach a caller as a NullReferenceException rather than as an empty inventory.
        // So this runs on every load, whatever the version says.
        //
        // It is a normalization, not a migration: "owns nothing" is the correct meaning of
        // an absent list, so there is no semantic decision here to get wrong.
        private static PlayerProfile Normalize(PlayerProfile profile)
        {
            profile.OwnedMetaItemIds ??= new List<string>();
            return profile;
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
