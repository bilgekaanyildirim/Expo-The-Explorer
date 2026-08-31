using System;
using System.IO;
using UnityEngine;

namespace ExpoTheExplorer.Systems.TelemetrySystem
{
    // Load/save boundary for TelemetryIdentity, and the SINGLE WRITER of
    // telemetry_identity.json. Deliberately shaped after PlayerProfileStore next
    // door -- same versioning rule, same "the file path is a persistence detail
    // decided here" constructor pair, same fall-back-rather-than-throw posture --
    // because two persistence boundaries that behave differently is one more thing
    // to remember than a project this size can afford.
    //
    // IT SHARES NOTHING WITH THAT CLASS AT RUNTIME, and that is the entire reason
    // this file exists (.claude/telemetry-plan.md §C.3). PlayerProfileStore.Delete()
    // throws the whole save away, which is what makes "reset the player" exact; this
    // file is outside that delete, so a save wipe cannot take the installation id
    // with it. The two stores must never learn about each other.
    //
    // THERE IS NO Delete() HERE, and its absence is load-bearing. Nothing in this
    // project may erase a telemetry identity: doing so would silently orphan every
    // run this device has already written to Firestore, which is precisely the
    // outcome the plan's §4 requirement ("resetting local save must NOT delete the
    // old player's data") exists to prevent. Rotating a player is StartNewPlayer's
    // job and it keeps the installation.
    public class TelemetryIdentityStore
    {
        // v1: Version + InstallationId + PlayerId + PlayerOrdinal +
        //     PlayerCreatedAtUtcTicks.
        //
        // The root CLAUDE.md invariant -- "save data carries a version number;
        // unversioned saves are never written" -- applies to this file too. It is a
        // small file with no migrations yet, which is exactly when a version number
        // is cheapest to add and easiest to forget.
        public const int CurrentVersion = 1;

        private const string FileName = "telemetry_identity.json";

        private readonly string filePath;
        private readonly Func<DateTime> utcNow;

        public TelemetryIdentityStore()
            : this(Path.Combine(Application.persistentDataPath, FileName))
        {
        }

        // Explicit path for tests, and an injected clock for the same reason
        // KeyManager takes one (ExpoTheExplorer/CLAUDE.md, Progression): this project
        // routes wall-clock time through a seam rather than reading DateTime.UtcNow
        // wherever it is needed, so that anything timestamped stays testable. This is
        // the second user of that rule and it takes the same shape rather than
        // inventing a new one.
        public TelemetryIdentityStore(string filePath, Func<DateTime> utcNow = null)
        {
            this.filePath = filePath;
            this.utcNow = utcNow ?? (() => DateTime.UtcNow);
        }

        public string FilePath => filePath;

        // Reports what is on disk, or NULL when there is nothing usable there.
        //
        // RETURNING NULL IS THE ONE PLACE THIS CLASS DIVERGES FROM
        // PlayerProfileStore, which never returns null, and the difference is not
        // stylistic. "No profile" has a correct answer -- a new player, with authored
        // starting money -- so that class can hand one back. "No identity" has no
        // correct answer at all: an installation id cannot be defaulted, only MINTED,
        // and minting is a write. Quietly returning a fresh identity from a method
        // called Load would mean every reader that forgot to save re-rolled the
        // installation on the next launch, and the id's whole contract is that it
        // never changes. So absence is reported honestly and LoadOrCreate is the only
        // path that mints.
        public TelemetryIdentity Load()
        {
            if (!File.Exists(filePath)) return null;

            try
            {
                var json = File.ReadAllText(filePath);
                var identity = JsonUtility.FromJson<TelemetryIdentity>(json);
                if (identity == null) return null;

                if (!IsVersionReadable(identity.Version)) return null;
                if (string.IsNullOrEmpty(identity.InstallationId)) return null;
                if (string.IsNullOrEmpty(identity.PlayerId)) return null;

                return identity;
            }
            catch (Exception e)
            {
                Debug.LogError($"[Telemetry] Failed to load identity at {filePath}: {e.Message}");
                return null;
            }
        }

        // The launch path: whatever is on disk, or a brand-new installation written
        // out before it is handed back.
        //
        // THE WRITE IS NOT OPTIONAL. An identity minted and returned without being
        // saved would be minted again on the very next launch, and every run this
        // device wrote would carry a different installation id -- the analytics side
        // would see a hundred single-run devices instead of one device with a hundred
        // runs, and nothing would look broken while it happened.
        //
        // A file that exists but cannot be read produces a NEW installation rather
        // than a repair. That is the honest outcome: a file we cannot parse cannot
        // tell us who this device is, and inventing a link to unknown past runs would
        // be worse than admitting the link is lost. Load has already logged why.
        public TelemetryIdentity LoadOrCreate()
        {
            var existing = Load();
            if (existing != null) return existing;

            var created = NewInstallation();
            Save(created);
            Debug.Log(
                $"[Telemetry] Minted a new installation: {created.InstallationId} / {created.PlayerId} " +
                $"(file: {filePath})");
            return created;
        }

        // A device nobody has played on yet: a fresh installation AND its first
        // playtester, together. The two are minted in one call because there is no
        // moment in this system's life when an installation exists without a player
        // -- somebody is always holding the phone.
        public TelemetryIdentity NewInstallation()
        {
            return new TelemetryIdentity
            {
                InstallationId = TelemetryIds.NewInstallationId(),
                PlayerId = TelemetryIds.NewPlayerId(),
                PlayerOrdinal = 1,
                PlayerCreatedAtUtcTicks = utcNow().Ticks,
            };
        }

        // Hands the same device to the next playtester. The ONE operation in this
        // project that changes a playerId.
        //
        // WHAT IT KEEPS IS THE WHOLE POINT: the installation id crosses unchanged, so
        // the report can still say these two testers shared a phone. What it does not
        // do is equally important -- it touches no Firestore document, so the
        // previous tester's runs stay exactly where they are (plan §4). This method
        // cannot delete telemetry; nothing here can.
        //
        // `testerId` null means "mint a random one". A caller that wants "T001" has
        // already validated it through TelemetryIds.TrySanitizeTesterId -- this method
        // does not re-check, because a second validation in a second place is how the
        // two eventually disagree about what a legal id is.
        //
        // A null `current` is treated as a fresh installation rather than a crash:
        // the only way to reach it is a debug button pressed before anything read the
        // file, and refusing there would be a worse answer than starting cleanly.
        public TelemetryIdentity NewPlayer(TelemetryIdentity current, string testerId = null)
        {
            if (current == null || string.IsNullOrEmpty(current.InstallationId))
            {
                var fresh = NewInstallation();
                if (!string.IsNullOrEmpty(testerId)) fresh.PlayerId = testerId;
                return fresh;
            }

            return new TelemetryIdentity
            {
                InstallationId = current.InstallationId,
                PlayerId = string.IsNullOrEmpty(testerId) ? TelemetryIds.NewPlayerId() : testerId,
                PlayerOrdinal = current.PlayerOrdinal + 1,
                PlayerCreatedAtUtcTicks = utcNow().Ticks,
            };
        }

        // Stamps CurrentVersion itself rather than trusting the caller, exactly as
        // PlayerProfileStore.Save does and for the same reason: an unversioned file
        // must not be able to reach disk however it was constructed.
        public void Save(TelemetryIdentity identity)
        {
            if (identity == null) return;

            identity.Version = CurrentVersion;

            try
            {
                var directory = Path.GetDirectoryName(filePath);
                if (!string.IsNullOrEmpty(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                File.WriteAllText(filePath, JsonUtility.ToJson(identity));
            }
            catch (Exception e)
            {
                // Logged, never thrown. A telemetry failure must not break anything
                // the player is doing (plan §L) -- and the caller here is usually a
                // debug button, where an exception on the click is the least useful
                // possible outcome.
                Debug.LogError($"[Telemetry] Failed to save identity at {filePath}: {e.Message}");
            }
        }

        // Same asymmetry PlayerProfileStore documents: an OLDER file is readable (it
        // simply lacks newer fields), version 0 is not (it means no version was ever
        // written, so the values beside it have unknown meaning), and a NEWER one is
        // not (a later build may have reinterpreted a field this one would misread).
        //
        // Refusing means "mint a new installation", which costs the link to this
        // device's past runs -- so it is worth a warning rather than a silent
        // fallback.
        private static bool IsVersionReadable(int version)
        {
            if (version >= 1 && version <= CurrentVersion) return true;

            Debug.LogWarning(
                version == 0
                    ? "[Telemetry] Identity file carries no schema version; starting a new installation."
                    : $"[Telemetry] Identity schema v{version} is newer than this build understands (v{CurrentVersion}); starting a new installation.");
            return false;
        }
    }
}
