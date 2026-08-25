using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace ExpoTheExplorer.Systems.ProgressionSystem
{
    // Load/save boundary for PlayerProfile. Ignorant of GameState's SHAPE --
    // callers build a PlayerProfile from whatever they want persisted and hand it
    // in, so Save doesn't change when the profile grows. The file-format behaviour
    // here -- missing, corrupt and unrecognised-version files all fall back instead
    // of throwing -- is what this class exists to keep.
    //
    // It used to read one compile-time constant from Core, GameState.DefaultStartingLives,
    // for the two schema facts "a file older than v3 means the player had full lives" and
    // "a brand-new player starts with full lives". Neither is a schema fact any more (v6,
    // decisions.md D-064): lives left the profile, so this class no longer knows what a
    // life is and does not reference Core at all. The asmdef reference stays because
    // Wallet next door needs it.
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
        // v3: + Lives (decisions.md D-014), REMOVED again in v6. It was the FIRST field
        //     where 0 was not a safe default -- an older file lacked Lives, read it as 0,
        //     and 0 lives is a dead player rather than a fresh one -- so it carried a real
        //     upgrade in UpgradeToCurrent, the case the invariant's version number exists
        //     for. That upgrade went out with the field; see v6.
        // v4: + OwnedMetaItemIds (decisions.md D-020). Back to a SAFE default: nothing
        //     owned is exactly what an older save means. So unlike v3 this needs no
        //     semantic migration -- only a null guard, because the safe default is an
        //     empty list rather than a zero and a reference type can arrive as null.
        // v5: + LastCelebratedDayIndex (decisions.md D-041). Back to a DANGEROUS default,
        //     like v3: read as 0, an existing save claims every Day-unlocked prop the
        //     player got days ago is still owed a celebration, and they would be shown a
        //     queue of them on next launch. So this one belongs in UpgradeToCurrent, and
        //     the value it takes is CurrentDayIndex -- whatever they have, they have seen.
        // v6: - Lives (decisions.md D-064). The first version that REMOVES a field rather
        //     than adding one, and the asymmetry is worth stating: a removal needs no
        //     upgrade branch at all, because JsonUtility drops a JSON key with no matching
        //     field. Every v3-v5 file's saved life count is simply ignored from now on,
        //     which is the intent -- lives are a per-day resource again and GameState opens
        //     each day at a full bar, so a restored count would be a staler second answer.
        //     The bump is still owed even though nothing has to be migrated: the version is
        //     what tells a reader which shape the file is, and a v6 file genuinely differs
        //     from a v5 one.
        // v7: + Keys and LastKeyRegenUtcTicks (decisions.md D-065). Keys is the third
        //     field where 0 is not a safe default -- 0 keys is a locked-out player, the
        //     same trap as v3's Lives -- but it differs from every migration before it in
        //     one way: THE CORRECT VALUE IS NOT KNOWN HERE. It is KeyConfig's cap, and
        //     this class has no config reference and must not gain one. So the upgrade
        //     writes -1, an "absent" marker, and KeyManager.ApplyPersisted resolves it
        //     where the cap actually lives. That is what lets this class stay a file
        //     boundary that knows nothing about what a key means.
        //     The anchor needs no branch: 0 means "no timestamp recorded" and the load
        //     path starts the clock then, which also keeps System.DateTime out of here.
        public const int CurrentVersion = 7;

        // What this class writes into PlayerProfile.Keys when it cannot know the real
        // answer -- an older save, or a player who has never played. The real answer is
        // KeyConfig's cap, and resolving it HERE would mean handing a file boundary a
        // content reference and a second copy of a balancing number.
        //
        // Public so the marker has ONE name rather than a bare -1 appearing in the
        // upgrade, in NewPlayer and in the tests. The reading end does not reference this
        // constant: KeyManager.ApplyPersisted treats ANY negative count as absent, which
        // is deliberately more permissive than an equality check -- a hand-edited or
        // half-written file carrying -2 means the same thing and must not lock a player
        // out. So this names the value written; it does not narrow what is accepted.
        public const int KeysAbsentMarker = -1;

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

        // What a brand-new player gets. The single place that answers "what does a
        // player who has never played hold", which is why the starting grant is applied
        // HERE and not in the wallet: seeding a balance anywhere else would put a second
        // answer to that question in the codebase, and the two would drift.
        //
        // The day index stays zero -- start at the first Day. Lives are NOT seeded here
        // any more (v6, decisions.md D-064): they left the profile entirely, and a new
        // GameState already opens at a full bar, so seeding them here would be a second
        // answer to a question the runtime already answers. The opening coin balance is
        // authored content, so it arrives as an argument rather than being written down
        // here (GameConfig.StartingSoftMoney; decisions.md D-026). Negative is clamped
        // for the same reason Wallet.ApplyPersistedBalances clamps: broke, never in debt.
        public static PlayerProfile NewPlayer(int startingSoftMoney)
        {
            return new PlayerProfile
            {
                SoftMoney = Math.Max(0, startingSoftMoney),

                // The same marker an older save gets, and for the same reason: a brand-new
                // player must start with a FULL bar of keys, that number is KeyConfig's
                // cap, and this class cannot see it. "Never played" and "played before
                // keys existed" deserve the identical answer, so they take the identical
                // path rather than each inventing one.
                Keys = KeysAbsentMarker,
            };
        }

        // The fallback for a caller that supplied none. Deliberately grants NOTHING: this
        // class has no config reference and inventing a number here is exactly the
        // embedded-content the invariant forbids, so a caller who wants the authored
        // opening balance passes NewPlayer(...) in as the fallback -- GameSession does,
        // and it is the only place a wallet is ever seeded from a profile.
        private static PlayerProfile Defaults()
        {
            return NewPlayer(0);
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

            // v3's Lives refill used to sit here. It went out with the field in v6 --
            // there is nothing to restore, and a removal is the one schema change that
            // needs no branch, since JsonUtility drops a key with no matching field.

            if (profile.Version < 7)
            {
                // -1, not the cap: this class cannot see KeyConfig and must not learn what
                // a key is worth. The marker travels to KeyManager.ApplyPersisted, which
                // turns it into a full bar. 0 would be catastrophic here -- it is a real
                // count meaning "locked out", so an existing player would launch unable to
                // start a day and with nothing to tell them why.
                //
                // LastKeyRegenUtcTicks is deliberately left at its deserialized 0: that
                // reads as "no anchor", and the load path starts the clock at load time.
                profile.Keys = KeysAbsentMarker;
            }

            if (profile.Version < 5)
            {
                // Read AFTER CurrentDayIndex has been deserialized, which it has -- this
                // runs on a fully parsed profile. Marking everything up to today as seen is
                // the only answer that neither replays old celebrations nor swallows a
                // genuinely new one: a prop opening tomorrow is still strictly after this.
                profile.LastCelebratedDayIndex = profile.CurrentDayIndex;
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

        // Throws the save away, so the next Load reports "no profile found" and every
        // caller starts a player over from nothing (decisions.md D-026). Deleting the
        // FILE rather than writing a fresh profile over it is what makes this exact:
        // a "reset" that saved defaults would have to know every field, which is the
        // payload knowledge this class exists without -- and a field forgotten there
        // would leave a scrap of the old player behind.
        //
        // Returns whether the caller can now treat the player as new. An already-absent
        // file is success, not failure: the player wanted a clean slate and has one.
        // A failed delete returns false rather than throwing, because the one caller is
        // a UI button and the honest outcome there is "nothing was reset" rather than an
        // exception on the click.
        public bool Delete()
        {
            try
            {
                if (File.Exists(filePath)) File.Delete(filePath);
                return true;
            }
            catch (Exception e)
            {
                Debug.LogError($"Failed to delete player profile at {filePath}: {e.Message}");
                return false;
            }
        }
    }
}
