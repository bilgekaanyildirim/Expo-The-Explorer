using System;
using System.IO;
using ExpoTheExplorer.Systems.ProgressionSystem;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace ExpoTheExplorer.Tests.EditMode
{
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

            Assert.AreEqual(0, profile.Xp);
            Assert.AreEqual(0, profile.Level);
        }

        [Test]
        public void Load_WhenFileIsCorruptedJson_ReturnsDefaultProfile_DoesNotThrow()
        {
            File.WriteAllText(testFilePath, "{ not valid json");
            var store = new PlayerProfileStore(testFilePath);

            LogAssert.Expect(LogType.Error, new System.Text.RegularExpressions.Regex("Failed to load player profile"));
            PlayerProfile profile = null;
            Assert.DoesNotThrow(() => profile = store.Load());

            Assert.AreEqual(0, profile.Xp);
            Assert.AreEqual(0, profile.Level);
        }

        [Test]
        public void Load_WhenFileIsEmpty_ReturnsDefaultProfile()
        {
            File.WriteAllText(testFilePath, string.Empty);
            var store = new PlayerProfileStore(testFilePath);

            var profile = store.Load();

            Assert.AreEqual(0, profile.Xp);
            Assert.AreEqual(0, profile.Level);
        }

        [Test]
        public void Save_ThenLoad_RoundTripsXpAndLevel()
        {
            var store = new PlayerProfileStore(testFilePath);
            store.Save(new PlayerProfile { Xp = 450, Level = 3 });

            var reloaded = new PlayerProfileStore(testFilePath).Load();

            Assert.AreEqual(450, reloaded.Xp);
            Assert.AreEqual(3, reloaded.Level);
        }

        [Test]
        public void Save_WhenCalledTwice_LatestValuesWin()
        {
            var store = new PlayerProfileStore(testFilePath);
            store.Save(new PlayerProfile { Xp = 100, Level = 1 });
            store.Save(new PlayerProfile { Xp = 999, Level = 9 });

            var reloaded = store.Load();

            Assert.AreEqual(999, reloaded.Xp);
            Assert.AreEqual(9, reloaded.Level);
        }

        [Test]
        public void Save_CreatesParentDirectory_WhenMissing()
        {
            var nestedPath = Path.Combine(Application.temporaryCachePath, $"profile_test_dir_{Guid.NewGuid()}", "player_profile.json");
            var store = new PlayerProfileStore(nestedPath);

            Assert.DoesNotThrow(() => store.Save(new PlayerProfile { Xp = 10, Level = 1 }));

            Assert.IsTrue(File.Exists(nestedPath));

            File.Delete(nestedPath);
            Directory.Delete(Path.GetDirectoryName(nestedPath));
        }
    }
}
