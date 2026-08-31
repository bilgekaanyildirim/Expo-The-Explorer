using System;
using System.IO;
using System.Text.RegularExpressions;
using ExpoTheExplorer.Systems.ProgressionSystem;
using ExpoTheExplorer.Systems.TelemetrySystem;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace ExpoTheExplorer.Tests.EditMode
{
    // Four groups, and the last one is the reason this system exists at all.
    //
    // The FILE cases mirror PlayerProfileStoreTests (missing / corrupt / version
    // range / round-trip), because the two stores deliberately behave the same way
    // -- with one documented divergence these tests pin down: Load returns NULL
    // here, where the profile store returns defaults. "No identity" has no correct
    // default; only a minted one, and minting is a write.
    //
    // The MINTING cases guard the contract the whole design rests on: an
    // installation id is written the moment it is invented, and never again.
    //
    // The ROTATION cases are "hand the phone to the next tester": a new playerId, a
    // kept installationId, an incremented ordinal.
    //
    // The BOUNDARY case is the one that would actually cost real playtest data if it
    // regressed: wiping the player's save must not touch the telemetry identity.
    // That is .claude/telemetry-plan.md §4's requirement stated as a test, and it is
    // the reason the identity is a second file instead of two more PlayerProfile
    // fields.
    public class TelemetryIdentityTests
    {
        private string identityPath;
        private string profilePath;

        private static readonly DateTime FixedNow = new DateTime(2026, 8, 31, 12, 0, 0, DateTimeKind.Utc);

        [SetUp]
        public void SetUp()
        {
            var stamp = Guid.NewGuid();
            identityPath = Path.Combine(Application.temporaryCachePath, $"telemetry_identity_test_{stamp}.json");
            profilePath = Path.Combine(Application.temporaryCachePath, $"player_profile_test_{stamp}.json");
        }

        [TearDown]
        public void TearDown()
        {
            if (File.Exists(identityPath)) File.Delete(identityPath);
            if (File.Exists(profilePath)) File.Delete(profilePath);
        }

        private TelemetryIdentityStore NewStore() =>
            new TelemetryIdentityStore(identityPath, () => FixedNow);

        // ---- file behaviour ----------------------------------------------------

        [Test]
        public void Load_WhenFileDoesNotExist_ReturnsNull_RatherThanADefaultIdentity()
        {
            var store = NewStore();

            // The documented divergence from PlayerProfileStore, and the whole reason
            // LoadOrCreate exists as a separate method: a defaulted installation id
            // would be re-rolled on every launch by any caller that forgot to save.
            Assert.IsNull(store.Load());
        }

        [Test]
        public void Load_WhenFileIsCorruptJson_ReturnsNull_DoesNotThrow()
        {
            File.WriteAllText(identityPath, "{ not valid json");
            var store = NewStore();

            LogAssert.Expect(LogType.Error, new Regex(@"\[Telemetry\] Failed to load identity"));

            TelemetryIdentity identity = null;
            Assert.DoesNotThrow(() => identity = store.Load());
            Assert.IsNull(identity);
        }

        [Test]
        public void Load_WhenFileCarriesNoVersion_IsRefused()
        {
            // Version 0 is indistinguishable from "nobody ever stamped this", so the
            // ids beside it could mean anything -- the same rule the save file follows.
            File.WriteAllText(identityPath, "{\"Version\":0,\"InstallationId\":\"I_DEAD\",\"PlayerId\":\"P_BEEF\"}");

            Assert.IsNull(NewStore().Load());
        }

        [Test]
        public void Load_WhenFileIsFromANewerBuild_IsRefused()
        {
            var future = TelemetryIdentityStore.CurrentVersion + 1;
            File.WriteAllText(
                identityPath,
                $"{{\"Version\":{future},\"InstallationId\":\"I_DEAD\",\"PlayerId\":\"P_BEEF\"}}");

            Assert.IsNull(NewStore().Load());
        }

        [Test]
        public void Load_WhenInstallationIdIsMissing_IsRefused()
        {
            // A structurally valid file with an empty id is worse than no file: it
            // would hand every run an empty installation column that groups them all
            // together.
            File.WriteAllText(identityPath, "{\"Version\":1,\"InstallationId\":\"\",\"PlayerId\":\"P_BEEF\"}");

            Assert.IsNull(NewStore().Load());
        }

        [Test]
        public void SaveThenLoad_RoundTripsEveryField()
        {
            var store = NewStore();
            var written = store.NewInstallation();

            store.Save(written);
            var read = store.Load();

            Assert.IsNotNull(read);
            Assert.AreEqual(written.InstallationId, read.InstallationId);
            Assert.AreEqual(written.PlayerId, read.PlayerId);
            Assert.AreEqual(written.PlayerOrdinal, read.PlayerOrdinal);
            Assert.AreEqual(written.PlayerCreatedAtUtcTicks, read.PlayerCreatedAtUtcTicks);
        }

        [Test]
        public void Save_StampsTheCurrentVersion_EvenWhenTheCallerLeftItZero()
        {
            var store = NewStore();
            var identity = store.NewInstallation();
            identity.Version = 0;

            store.Save(identity);

            Assert.AreEqual(TelemetryIdentityStore.CurrentVersion, identity.Version);
            Assert.AreEqual(TelemetryIdentityStore.CurrentVersion, store.Load().Version);
        }

        // ---- minting -----------------------------------------------------------

        [Test]
        public void LoadOrCreate_OnAnEmptyDevice_WritesTheIdentityImmediately()
        {
            var store = NewStore();

            var created = store.LoadOrCreate();

            // The write is the assertion. An identity returned but not persisted is
            // re-minted on the next launch, and every run this device reports would
            // carry a different installation id.
            Assert.IsTrue(File.Exists(identityPath));
            Assert.AreEqual(created.InstallationId, store.Load().InstallationId);
        }

        [Test]
        public void LoadOrCreate_CalledAgain_KeepsTheSameInstallation()
        {
            var first = NewStore().LoadOrCreate();
            var second = NewStore().LoadOrCreate();

            Assert.AreEqual(first.InstallationId, second.InstallationId);
            Assert.AreEqual(first.PlayerId, second.PlayerId);
            Assert.AreEqual(1, second.PlayerOrdinal);
        }

        [Test]
        public void LoadOrCreate_OnAnUnreadableFile_StartsAFreshInstallation()
        {
            File.WriteAllText(identityPath, "{ not valid json");
            LogAssert.Expect(LogType.Error, new Regex(@"\[Telemetry\] Failed to load identity"));

            var created = NewStore().LoadOrCreate();

            // Losing the link to this device's past runs is the honest outcome: a file
            // we cannot parse cannot tell us who this device is, and inventing a link
            // to unknown runs would be worse than admitting it is gone.
            Assert.IsNotNull(created.InstallationId);
            Assert.AreEqual(1, created.PlayerOrdinal);
        }

        [Test]
        public void NewInstallation_StampsTheInjectedClock_NotTheWallClock()
        {
            // The clock is a seam for the reason KeyManager's is (ExpoTheExplorer/CLAUDE.md):
            // anything this project timestamps stays testable.
            var created = NewStore().NewInstallation();

            Assert.AreEqual(FixedNow.Ticks, created.PlayerCreatedAtUtcTicks);
        }

        [Test]
        public void MintedIds_CarryTheirPrefixes_AndDoNotRepeat()
        {
            var store = NewStore();

            var a = store.NewInstallation();
            var b = store.NewInstallation();

            Assert.IsTrue(a.InstallationId.StartsWith("I_"));
            Assert.IsTrue(a.PlayerId.StartsWith("P_"));
            Assert.AreNotEqual(a.InstallationId, b.InstallationId);
            Assert.AreNotEqual(a.PlayerId, b.PlayerId);
        }

        // ---- rotation ----------------------------------------------------------

        [Test]
        public void NewPlayer_KeepsTheInstallation_AndRotatesThePlayer()
        {
            var store = NewStore();
            var first = store.LoadOrCreate();

            var second = store.NewPlayer(first);

            // The kept half is what lets a report say two testers shared a phone; the
            // rotated half is what stops their results being averaged together.
            Assert.AreEqual(first.InstallationId, second.InstallationId);
            Assert.AreNotEqual(first.PlayerId, second.PlayerId);
            Assert.AreEqual(2, second.PlayerOrdinal);
        }

        [Test]
        public void NewPlayer_WithATesterId_UsesItVerbatim()
        {
            var store = NewStore();
            var first = store.LoadOrCreate();

            var second = store.NewPlayer(first, "T004");

            Assert.AreEqual("T004", second.PlayerId);
            Assert.AreEqual(first.InstallationId, second.InstallationId);
        }

        [Test]
        public void NewPlayer_RepeatedTwice_KeepsCountingOrdinals()
        {
            var store = NewStore();
            var first = store.LoadOrCreate();

            var second = store.NewPlayer(first);
            var third = store.NewPlayer(second);

            Assert.AreEqual(3, third.PlayerOrdinal);
            Assert.AreEqual(first.InstallationId, third.InstallationId);
        }

        [Test]
        public void NewPlayer_WithNoCurrentIdentity_StartsAFreshInstallation_RatherThanThrowing()
        {
            // Reachable only by pressing the debug button before anything read the
            // file. Starting cleanly beats refusing on a debug path.
            var created = NewStore().NewPlayer(null, "T001");

            Assert.AreEqual("T001", created.PlayerId);
            Assert.IsTrue(created.InstallationId.StartsWith("I_"));
            Assert.AreEqual(1, created.PlayerOrdinal);
        }

        // ---- tester id sanitising ---------------------------------------------

        [Test]
        public void TrySanitizeTesterId_AcceptsPlainIds_AndTrimsSurroundingWhitespace()
        {
            Assert.IsTrue(TelemetryIds.TrySanitizeTesterId("T001", out var plain));
            Assert.AreEqual("T001", plain);

            Assert.IsTrue(TelemetryIds.TrySanitizeTesterId("  T002  ", out var padded));
            Assert.AreEqual("T002", padded);

            Assert.IsTrue(TelemetryIds.TrySanitizeTesterId("tester_01-a", out var punctuated));
            Assert.AreEqual("tester_01-a", punctuated);
        }

        [Test]
        public void TrySanitizeTesterId_RefusesRatherThanRepairing()
        {
            // Refusing is the point: a tester who typed "T 001" and silently got
            // "T001" would believe they had entered the first, and the note beside the
            // phone would disagree with the report from then on.
            Assert.IsFalse(TelemetryIds.TrySanitizeTesterId("T 001", out _));
            Assert.IsFalse(TelemetryIds.TrySanitizeTesterId("T\"001", out _));
            Assert.IsFalse(TelemetryIds.TrySanitizeTesterId("T\n001", out _));
            Assert.IsFalse(TelemetryIds.TrySanitizeTesterId(new string('T', 33), out _));
            Assert.IsFalse(TelemetryIds.TrySanitizeTesterId("   ", out _));
            Assert.IsFalse(TelemetryIds.TrySanitizeTesterId("", out _));
            Assert.IsFalse(TelemetryIds.TrySanitizeTesterId(null, out _));
        }

        // ---- the boundary between the two save files ---------------------------

        [Test]
        public void DeletingThePlayerProfile_LeavesTheTelemetryIdentityUntouched()
        {
            // .claude/telemetry-plan.md §4, as a test. This is the guarantee the whole
            // two-file design buys: a tester's local progress can be wiped as often as
            // the dev workflow wants, and the analytics side keeps knowing which
            // device -- and which tester -- the previous runs came from.
            //
            // It would fail the moment someone "simplifies" this into two extra
            // PlayerProfile fields, which is exactly the change this test exists to
            // stop, because nothing else in the project would notice.
            var identityStore = new TelemetryIdentityStore(identityPath, () => FixedNow);
            var before = identityStore.LoadOrCreate();

            var profileStore = new PlayerProfileStore(profilePath);
            profileStore.Save(PlayerProfileStore.NewPlayer(500));
            Assert.IsTrue(File.Exists(profilePath));

            Assert.IsTrue(profileStore.Delete());

            Assert.IsFalse(File.Exists(profilePath), "The profile is the file that is supposed to go.");
            Assert.IsTrue(File.Exists(identityPath), "The telemetry identity must survive a save wipe.");

            var after = identityStore.Load();
            Assert.AreEqual(before.InstallationId, after.InstallationId);
            Assert.AreEqual(before.PlayerId, after.PlayerId);
        }

        [Test]
        public void RotatingThePlayer_LeavesThePlayerProfileAlone()
        {
            // The mirror of the case above, and the other half of the ownership rule:
            // this store never touches the profile. The debug command that resets both
            // calls both, in order, so each file keeps exactly one writer.
            var profileStore = new PlayerProfileStore(profilePath);
            profileStore.Save(PlayerProfileStore.NewPlayer(500));

            var identityStore = new TelemetryIdentityStore(identityPath, () => FixedNow);
            var first = identityStore.LoadOrCreate();
            identityStore.Save(identityStore.NewPlayer(first));

            Assert.IsTrue(File.Exists(profilePath));
            Assert.AreEqual(500, profileStore.Load().SoftMoney);
        }
    }
}
