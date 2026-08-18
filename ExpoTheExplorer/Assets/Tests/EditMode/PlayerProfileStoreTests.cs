using System;
using System.IO;
using ExpoTheExplorer.Systems.ProgressionSystem;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace ExpoTheExplorer.Tests.EditMode
{
    // PlayerProfile carries no fields while XP/Level is gone and nothing else has
    // been made persistent yet, so these tests assert the store's FILE behavior
    // (missing / empty / corrupt / round-trip / directory creation) rather than a
    // payload. The fallback cases assert reference identity -- that the caller's
    // own instance comes back, not a silently substituted default -- which is the
    // part that would actually break a future profile carrying real state.
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
            store.Save(new PlayerProfile());
            store.Save(new PlayerProfile());

            Assert.AreEqual(JsonUtility.ToJson(new PlayerProfile()), File.ReadAllText(testFilePath));
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
