using System;
using System.IO;
using ExpoTheExplorer.Core;
using ExpoTheExplorer.Systems.ProgressionSystem;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace ExpoTheExplorer.Tests.EditMode
{
    // Two groups. The file-behaviour cases (missing / empty / corrupt / round-trip
    // / directory creation) predate the profile carrying anything, and their
    // fallback assertions check reference identity -- that the caller's own
    // instance comes back rather than a silently substituted default. The version
    // cases came with Adım 4, when the profile started carrying a real wallet, and
    // they pin down which files are readable: versioned ones from 1 up to this
    // build, and nothing else. v2 (decisions.md D-012) added CurrentDayIndex, which
    // turned the "older but versioned schema" case from a hypothetical into the
    // actual migration path a v1 player takes. v3 (decisions.md D-014) added Lives and
    // brought the first REAL upgrade with it -- absent-reads-as-0 is not survivable for
    // that field -- so the v2-payload case below is the one that guards a shipped save
    // file against losing its money to a schema change.
    public class PlayerProfileStoreTests
    {
        private string testFilePath;

        [SetUp]
        public void SetUp()
        {
            testFilePath = Path.Combine(Application.temporaryCachePath, $"player_profile_test_{Guid.NewGuid()}.json");
        }

        [TearDown]
        public void TearDown()
        {
            if (File.Exists(testFilePath))
            {
                File.Delete(testFilePath);
            }
        }

        [Test]
        public void Load_WhenFileDoesNotExist_ReturnsDefaultProfile()
        {
            var store = new PlayerProfileStore(testFilePath);

            var profile = store.Load();

            Assert.IsNotNull(profile);
        }

        [Test]
        public void Load_WhenFileIsCorruptedJson_ReturnsDefaultProfile_DoesNotThrow()
        {
            File.WriteAllText(testFilePath, "{ not valid json");
            var store = new PlayerProfileStore(testFilePath);

            LogAssert.Expect(LogType.Error, new System.Text.RegularExpressions.Regex("Failed to load player profile"));
            PlayerProfile profile = null;
            Assert.DoesNotThrow(() => profile = store.Load());

            Assert.IsNotNull(profile);
        }

        [Test]
        public void Load_WhenFileIsEmpty_ReturnsDefaultProfile()
        {
            File.WriteAllText(testFilePath, string.Empty);
            var store = new PlayerProfileStore(testFilePath);

            var profile = store.Load();

            Assert.IsNotNull(profile);
        }

        [Test]
        public void Load_WhenFileDoesNotExist_WithFallback_ReturnsFallback()
        {
            var store = new PlayerProfileStore(testFilePath);
            var fallback = new PlayerProfile();

            var profile = store.Load(fallback);

            Assert.AreSame(fallback, profile);
        }

        [Test]
        public void Load_WhenFileIsEmpty_WithFallback_ReturnsFallback()
        {
            File.WriteAllText(testFilePath, string.Empty);
            var store = new PlayerProfileStore(testFilePath);
            var fallback = new PlayerProfile();

            var profile = store.Load(fallback);

            Assert.AreSame(fallback, profile);
        }

        [Test]
        public void Load_WhenFileIsCorruptedJson_WithFallback_ReturnsFallbackNotASubstitutedDefault()
        {
            File.WriteAllText(testFilePath, "{ not valid json");
            var store = new PlayerProfileStore(testFilePath);
            var fallback = new PlayerProfile();

            LogAssert.Expect(LogType.Error, new System.Text.RegularExpressions.Regex("Failed to load player profile"));
            var profile = store.Load(fallback);

            Assert.AreSame(fallback, profile);
        }

        [Test]
        public void Save_ThenLoad_RoundTripsWithoutFallingBack()
        {
            var store = new PlayerProfileStore(testFilePath);
            store.Save(new PlayerProfile());

            Assert.IsTrue(File.Exists(testFilePath));

            var fallback = new PlayerProfile();
            var reloaded = new PlayerProfileStore(testFilePath).Load(fallback);

            // A real file was read, so the fallback must NOT be what comes back.
            Assert.IsNotNull(reloaded);
            Assert.AreNotSame(fallback, reloaded);
        }

        [Test]
        public void Save_WhenCalledTwice_OverwritesRatherThanAppending()
        {
            var store = new PlayerProfileStore(testFilePath);

            // Held on to deliberately: Save stamps Version onto the instance it is
            // given, so this is what the file should contain -- comparing against a
            // fresh PlayerProfile() would compare against an unstamped v0.
            var saved = new PlayerProfile();
            store.Save(saved);
            store.Save(new PlayerProfile());

            Assert.AreEqual(JsonUtility.ToJson(saved), File.ReadAllText(testFilePath));
        }

        [Test]
        public void Save_StampsTheCurrentSchemaVersion_EvenWhenTheCallerLeftItZero()
        {
            var store = new PlayerProfileStore(testFilePath);
            var profile = new PlayerProfile { SoftMoney = 120, Gems = 3 };

            store.Save(profile);

            Assert.AreEqual(PlayerProfileStore.CurrentVersion, profile.Version);
            Assert.AreEqual(PlayerProfileStore.CurrentVersion, store.Load().Version);
        }

        [Test]
        public void SaveThenLoad_PreservesTheWallet()
        {
            var store = new PlayerProfileStore(testFilePath);
            store.Save(new PlayerProfile { SoftMoney = 1234, Gems = 7 });

            var reloaded = new PlayerProfileStore(testFilePath).Load();

            Assert.AreEqual(1234, reloaded.SoftMoney);
            Assert.AreEqual(7, reloaded.Gems);
        }

        // The v2 field (decisions.md D-012). It is what makes the main screen's
        // "Play" open the day the player left off on rather than always Day 0, so a
        // silent round-trip failure here would look like progress being lost.
        [Test]
        public void SaveThenLoad_PreservesTheCurrentDayIndex()
        {
            var store = new PlayerProfileStore(testFilePath);
            store.Save(new PlayerProfile { SoftMoney = 10, Gems = 1, CurrentDayIndex = 4 });

            var reloaded = new PlayerProfileStore(testFilePath).Load();

            Assert.AreEqual(4, reloaded.CurrentDayIndex);
        }

        // The whole reason the invariant demands a version field: without one,
        // "an old file whose numbers mean something else" and "a player who
        // genuinely has 0" are the same bytes. An unversioned file is refused
        // rather than half-read.
        [Test]
        public void Load_WhenFileCarriesNoVersion_IgnoresItsValues_AndFallsBack()
        {
            File.WriteAllText(testFilePath, "{\"SoftMoney\":9999,\"Gems\":42}");
            var store = new PlayerProfileStore(testFilePath);

            LogAssert.Expect(LogType.Warning, new System.Text.RegularExpressions.Regex("carries no schema version"));
            var profile = store.Load();

            Assert.AreEqual(0, profile.SoftMoney);
            Assert.AreEqual(0, profile.Gems);
        }

        // A file from a newer build may reinterpret fields this one would misread,
        // so it is left alone rather than partially trusted.
        [Test]
        public void Load_WhenFileVersionIsNewerThanThisBuild_FallsBack()
        {
            File.WriteAllText(testFilePath, "{\"Version\":9001,\"SoftMoney\":9999,\"Gems\":42}");
            var store = new PlayerProfileStore(testFilePath);

            LogAssert.Expect(LogType.Warning, new System.Text.RegularExpressions.Regex("newer than this build understands"));
            var profile = store.Load();

            Assert.AreEqual(0, profile.SoftMoney);
        }

        // The reason Load accepts a RANGE rather than only CurrentVersion, and no
        // longer a hypothetical: v2 added CurrentDayIndex (decisions.md D-012), so
        // the file below is exactly what a player who has been playing since v1 has
        // on disk. Refusing it would delete their money. The missing field reading
        // as 0 is also the whole migration -- 0 means "the first Day", which is
        // precisely where a v1 player was always sent on launch, so there is
        // nothing to upgrade.
        [Test]
        public void Load_WhenFileIsAnOlderButVersionedSchema_StillReadsTheFieldsItHas()
        {
            File.WriteAllText(testFilePath, "{\"Version\":1,\"SoftMoney\":500,\"Gems\":9}");
            var store = new PlayerProfileStore(testFilePath);

            var profile = store.Load();

            Assert.AreEqual(500, profile.SoftMoney);
            Assert.AreEqual(9, profile.Gems);
            Assert.AreEqual(0, profile.CurrentDayIndex, "a v1 file has no day index; it must read as Day 0");
        }

        // The v2 -> v3 migration, written against a LITERAL v2 payload rather than a
        // round-tripped object, because the thing under test is what an already-shipped
        // file means. This is the shape of a real save that existed on disk when v3
        // landed, day index and all: everything it already carried must survive
        // untouched, and only the field v3 ADDED may be filled in.
        [Test]
        public void Load_WhenFileIsV2_KeepsItsFieldsAndFillsLivesWithTheStartingCount()
        {
            File.WriteAllText(testFilePath, "{\"Version\":2,\"SoftMoney\":319,\"Gems\":4,\"CurrentDayIndex\":1}");
            var store = new PlayerProfileStore(testFilePath);

            var profile = store.Load();

            Assert.AreEqual(319, profile.SoftMoney, "a v2 file's money must survive the upgrade untouched");
            Assert.AreEqual(4, profile.Gems);
            Assert.AreEqual(1, profile.CurrentDayIndex);
            Assert.AreEqual(GameState.DefaultStartingLives, profile.Lives,
                "0 lives would be a player dead on arrival, so the upgrade must fill this in");
        }

        // The opposite guard: a genuine v3 file's Lives is data, not something to
        // overwrite. Without this, an upgrade that ran unconditionally would silently
        // refill every player's lives on every launch.
        [Test]
        public void Load_WhenFileIsCurrentVersion_DoesNotOverwriteLives()
        {
            var store = new PlayerProfileStore(testFilePath);
            store.Save(new PlayerProfile { SoftMoney = 10, Gems = 1, CurrentDayIndex = 2, Lives = 1 });

            var reloaded = new PlayerProfileStore(testFilePath).Load();

            Assert.AreEqual(1, reloaded.Lives);
        }

        // A brand-new player has no file at all, and 0 lives would make that profile
        // unplayable -- so the no-file default is full lives, unlike money and day
        // index which are legitimately zero.
        [Test]
        public void Load_WhenFileDoesNotExist_DefaultsToFullLivesButNoMoney()
        {
            var profile = new PlayerProfileStore(testFilePath).Load();

            Assert.AreEqual(GameState.DefaultStartingLives, profile.Lives);
            Assert.AreEqual(0, profile.SoftMoney);
            Assert.AreEqual(0, profile.CurrentDayIndex);
        }

        [Test]
        public void Save_CreatesParentDirectory_WhenMissing()
        {
            var nestedPath = Path.Combine(Application.temporaryCachePath, $"profile_test_dir_{Guid.NewGuid()}", "player_profile.json");
            var store = new PlayerProfileStore(nestedPath);

            Assert.DoesNotThrow(() => store.Save(new PlayerProfile()));

            Assert.IsTrue(File.Exists(nestedPath));

            File.Delete(nestedPath);
            Directory.Delete(Path.GetDirectoryName(nestedPath));
        }
    }
}
