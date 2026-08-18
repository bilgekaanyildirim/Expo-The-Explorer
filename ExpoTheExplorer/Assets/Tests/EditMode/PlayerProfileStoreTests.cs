using System;
using System.IO;
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
    // build, and nothing else.
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

        // The reason Load accepts a RANGE rather than only CurrentVersion: the day
        // a v2 field is added, this exact case is a real player's saved wallet, and
        // refusing it would delete their money.
        [Test]
        public void Load_WhenFileIsAnOlderButVersionedSchema_StillReadsTheFieldsItHas()
        {
            File.WriteAllText(testFilePath, "{\"Version\":1,\"SoftMoney\":500,\"Gems\":9}");
            var store = new PlayerProfileStore(testFilePath);

            var profile = store.Load();

            Assert.AreEqual(500, profile.SoftMoney);
            Assert.AreEqual(9, profile.Gems);
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
