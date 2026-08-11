using System;
using System.Collections.Generic;
using System.IO;
using ExpoTheExplorer.Core;
using ExpoTheExplorer.Data;
using ExpoTheExplorer.Systems.ProgressionSystem;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace ExpoTheExplorer.Tests.EditMode
{
    public class ProgressionSystemTests
    {
        private readonly List<UnityEngine.Object> spawnedAssets = new();
        private readonly List<string> tempProfilePaths = new();
        private GameConfig gameConfig;

        [SetUp]
        public void SetUp()
        {
            gameConfig = ScriptableObject.CreateInstance<GameConfig>();
            spawnedAssets.Add(gameConfig);
        }

        [TearDown]
        public void TearDown()
        {
            foreach (var asset in spawnedAssets)
            {
                UnityEngine.Object.DestroyImmediate(asset);
            }
            spawnedAssets.Clear();

            foreach (var path in tempProfilePaths)
            {
                if (File.Exists(path)) File.Delete(path);
            }
            tempProfilePaths.Clear();
        }

        private PlayerProfileStore CreateTempProfileStore()
        {
            var path = Path.Combine(Application.temporaryCachePath, $"leveltest_profile_{Guid.NewGuid()}.json");
            tempProfilePaths.Add(path);
            return new PlayerProfileStore(path);
        }

        private LevelProgressionConfig CreateLevelProgressionConfig(
            float xpPerItem = 5f,
            float impatientXpMultiplier = 1f,
            float normalXpMultiplier = 1f,
            float patientXpMultiplier = 1f)
        {
            var config = ScriptableObject.CreateInstance<LevelProgressionConfig>();
            spawnedAssets.Add(config);

            var serialized = new SerializedObject(config);
            serialized.FindProperty("xpPerItem").floatValue = xpPerItem;
            serialized.FindProperty("impatientXpMultiplier").floatValue = impatientXpMultiplier;
            serialized.FindProperty("normalXpMultiplier").floatValue = normalXpMultiplier;
            serialized.FindProperty("patientXpMultiplier").floatValue = patientXpMultiplier;
            serialized.ApplyModifiedPropertiesWithoutUndo();

            return config;
        }

        private void SetXpToNextLevel(LevelProgressionConfig config, params int[] thresholds)
        {
            var serialized = new SerializedObject(config);
            var listProperty = serialized.FindProperty("xpToNextLevel");
            listProperty.arraySize = thresholds.Length;
            for (var i = 0; i < thresholds.Length; i++)
            {
                listProperty.GetArrayElementAtIndex(i).intValue = thresholds[i];
            }
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        private Ticket CreateTicket(int itemCount, PatienceType patienceType)
        {
            var requiredItems = new List<FoodItemConfig>(new FoodItemConfig[itemCount]);
            return new Ticket("Test Customer", patienceType, requiredItems, new List<Modification>(), 100f);
        }

        [Test]
        public void AddXp_BelowThreshold_IncreasesXp_DoesNotChangeLevel()
        {
            var config = CreateLevelProgressionConfig();
            SetXpToNextLevel(config, 100);
            var state = new GameState(gameConfig);
            var manager = new LevelManager(state, config);

            manager.AddXp(50);

            Assert.AreEqual(50, state.Xp);
            Assert.AreEqual(0, state.Level);
        }

        [Test]
        public void AddXp_ExactlyAtThreshold_LevelsUpAndResetsXpToZero()
        {
            var config = CreateLevelProgressionConfig();
            SetXpToNextLevel(config, 100);
            var state = new GameState(gameConfig);
            var manager = new LevelManager(state, config);

            manager.AddXp(100);

            Assert.AreEqual(0, state.Xp);
            Assert.AreEqual(1, state.Level);
        }

        [Test]
        public void AddXp_AboveThreshold_LevelsUpAndCarriesRemainder()
        {
            var config = CreateLevelProgressionConfig();
            SetXpToNextLevel(config, 100);
            var state = new GameState(gameConfig);
            var manager = new LevelManager(state, config);

            manager.AddXp(130);

            Assert.AreEqual(30, state.Xp);
            Assert.AreEqual(1, state.Level);
        }

        [Test]
        public void AddXp_LargeAmount_LevelsUpMultipleTimesInOneCall()
        {
            var config = CreateLevelProgressionConfig();
            SetXpToNextLevel(config, 100, 100);
            var state = new GameState(gameConfig);
            var manager = new LevelManager(state, config);

            manager.AddXp(250);

            Assert.AreEqual(2, state.Level);
            Assert.AreEqual(50, state.Xp);
        }

        [Test]
        public void AddXp_AtMaxAuthoredLevel_LevelStaysCapped_XpKeepsAccumulating()
        {
            var config = CreateLevelProgressionConfig();
            SetXpToNextLevel(config, 100);
            var state = new GameState(gameConfig);
            var manager = new LevelManager(state, config);

            manager.AddXp(100);
            manager.AddXp(500);

            Assert.AreEqual(1, state.Level);
            Assert.AreEqual(500, state.Xp);
        }

        [Test]
        public void AddXp_PublishesXpChanged_WithFinalValue()
        {
            var config = CreateLevelProgressionConfig();
            SetXpToNextLevel(config, 100);
            var state = new GameState(gameConfig);
            var manager = new LevelManager(state, config);

            int? lastPublished = null;
            state.XpChanged.Subscribe(xp => lastPublished = xp);

            manager.AddXp(50);

            Assert.AreEqual(50, lastPublished);
        }

        [Test]
        public void AddXp_WhenLevelingUpMultipleTimes_PublishesLevelChangedOncePerLevelGained()
        {
            var config = CreateLevelProgressionConfig();
            SetXpToNextLevel(config, 100, 100);
            var state = new GameState(gameConfig);
            var manager = new LevelManager(state, config);

            var publishedLevels = new List<int>();
            state.LevelChanged.Subscribe(level => publishedLevels.Add(level));

            manager.AddXp(250);

            Assert.AreEqual(new List<int> { 1, 2 }, publishedLevels);
        }

        [Test]
        public void CalculateXp_BaseXp_ScalesWithRequiredItemCount()
        {
            var config = CreateLevelProgressionConfig(xpPerItem: 5f);
            var manager = new LevelManager(new GameState(gameConfig), config);

            var oneItem = manager.CalculateXp(CreateTicket(1, PatienceType.Normal));
            var threeItems = manager.CalculateXp(CreateTicket(3, PatienceType.Normal));

            Assert.AreEqual(5f, oneItem.BaseXp, 0.0001f);
            Assert.AreEqual(15f, threeItems.BaseXp, 0.0001f);
        }

        [Test]
        public void CalculateXp_AppliesCorrectPatienceMultiplier()
        {
            var config = CreateLevelProgressionConfig(impatientXpMultiplier: 2f, normalXpMultiplier: 1f, patientXpMultiplier: 0.5f);
            var manager = new LevelManager(new GameState(gameConfig), config);

            var impatient = manager.CalculateXp(CreateTicket(1, PatienceType.Impatient));
            var normal = manager.CalculateXp(CreateTicket(1, PatienceType.Normal));
            var patient = manager.CalculateXp(CreateTicket(1, PatienceType.Patient));

            Assert.AreEqual(2f, impatient.PatienceMultiplier, 0.0001f);
            Assert.AreEqual(1f, normal.PatienceMultiplier, 0.0001f);
            Assert.AreEqual(0.5f, patient.PatienceMultiplier, 0.0001f);
        }

        [Test]
        public void CalculateXp_TotalXp_IsProductOfBaseXpAndPatienceMultiplier()
        {
            var config = CreateLevelProgressionConfig(xpPerItem: 5f, impatientXpMultiplier: 2f);
            var manager = new LevelManager(new GameState(gameConfig), config);

            var result = manager.CalculateXp(CreateTicket(3, PatienceType.Impatient));

            Assert.AreEqual(15f, result.BaseXp, 0.0001f);
            Assert.AreEqual(2f, result.PatienceMultiplier, 0.0001f);
            Assert.AreEqual(15f * 2f, result.TotalXp, 0.0001f);
        }

        [Test]
        public void CommitProgress_SavesCurrentXpAndLevel_ViaProfileStore()
        {
            var config = CreateLevelProgressionConfig();
            SetXpToNextLevel(config, 100);
            var state = new GameState(gameConfig);
            var path = Path.Combine(Application.temporaryCachePath, $"leveltest_profile_{Guid.NewGuid()}.json");
            tempProfilePaths.Add(path);
            var store = new PlayerProfileStore(path);
            var manager = new LevelManager(state, config, store, new PlayerProfile());

            manager.AddXp(130);
            manager.CommitProgress();

            var reloaded = new PlayerProfileStore(path).Load();

            Assert.AreEqual(30, reloaded.Xp);
            Assert.AreEqual(1, reloaded.Level);
        }

        [Test]
        public void DiscardToLastCommitted_RollsBackToInitialProfile_WhenNeverCommitted()
        {
            var config = CreateLevelProgressionConfig();
            SetXpToNextLevel(config, 100);
            var state = new GameState(gameConfig);
            var initialProfile = new PlayerProfile { Xp = 20, Level = 3 };
            var manager = new LevelManager(state, config, CreateTempProfileStore(), initialProfile);

            manager.AddXp(50);
            manager.DiscardToLastCommitted();

            Assert.AreEqual(20, state.Xp);
            Assert.AreEqual(3, state.Level);
        }

        [Test]
        public void CommitProgress_UpdatesBaseline_SoLaterDiscardRollsBackToNewestCommit()
        {
            var config = CreateLevelProgressionConfig();
            SetXpToNextLevel(config, 100, 100, 100);
            var state = new GameState(gameConfig);
            var manager = new LevelManager(state, config, CreateTempProfileStore(), new PlayerProfile());

            manager.AddXp(50);
            manager.CommitProgress();
            manager.AddXp(30);

            manager.DiscardToLastCommitted();

            Assert.AreEqual(50, state.Xp);
            Assert.AreEqual(0, state.Level);
        }

        [Test]
        public void GetXpProgressRatio_PartiallyThroughLevel_ReturnsCorrectFraction()
        {
            var config = CreateLevelProgressionConfig();
            SetXpToNextLevel(config, 100);
            var state = new GameState(gameConfig);
            var manager = new LevelManager(state, config);

            manager.AddXp(25);

            Assert.AreEqual(0.25f, manager.GetXpProgressRatio(), 0.0001f);
        }

        [Test]
        public void GetXpProgressRatio_AtZeroXp_ReturnsZero()
        {
            var config = CreateLevelProgressionConfig();
            SetXpToNextLevel(config, 100);
            var state = new GameState(gameConfig);
            var manager = new LevelManager(state, config);

            Assert.AreEqual(0f, manager.GetXpProgressRatio(), 0.0001f);
        }

        [Test]
        public void GetXpProgressRatio_AtMaxAuthoredLevel_ReturnsOne()
        {
            var config = CreateLevelProgressionConfig();
            SetXpToNextLevel(config, 100);
            var state = new GameState(gameConfig);
            var manager = new LevelManager(state, config);

            manager.AddXp(100);
            manager.AddXp(500);

            Assert.AreEqual(1f, manager.GetXpProgressRatio(), 0.0001f);
        }

        [Test]
        public void DiscardToLastCommitted_DoesNotWriteToProfileStore()
        {
            var config = CreateLevelProgressionConfig();
            SetXpToNextLevel(config, 100);
            var state = new GameState(gameConfig);
            var path = Path.Combine(Application.temporaryCachePath, $"leveltest_profile_{Guid.NewGuid()}.json");
            tempProfilePaths.Add(path);
            var store = new PlayerProfileStore(path);
            var manager = new LevelManager(state, config, store, new PlayerProfile());

            manager.AddXp(50);
            manager.DiscardToLastCommitted();

            Assert.IsFalse(File.Exists(path));
        }

        // Day Complete popup's post-success Retry: the day's own completion
        // already committed a higher Xp/Level to disk, so this needs to
        // actually rewrite that commit, not just roll back in memory like
        // DiscardToLastCommitted (which would be a no-op here).
        [Test]
        public void RevertToDayStart_RollsBackStateXpAndLevel_ToGivenSnapshot()
        {
            var config = CreateLevelProgressionConfig();
            SetXpToNextLevel(config, 100);
            var state = new GameState(gameConfig);
            var manager = new LevelManager(state, config, CreateTempProfileStore(), new PlayerProfile());

            manager.AddXp(130);
            manager.CommitProgress(); // day succeeds, Xp/Level committed
            manager.AddXp(20); // player keeps playing after the popup, hypothetically

            manager.RevertToDayStart(new PlayerProfile { Xp = 0, Level = 0 });

            Assert.AreEqual(0, state.Xp);
            Assert.AreEqual(0, state.Level);
        }

        [Test]
        public void RevertToDayStart_UnlikeDiscardToLastCommitted_OverwritesProfileStoreOnDisk()
        {
            var config = CreateLevelProgressionConfig();
            SetXpToNextLevel(config, 100);
            var state = new GameState(gameConfig);
            var path = Path.Combine(Application.temporaryCachePath, $"leveltest_profile_{Guid.NewGuid()}.json");
            tempProfilePaths.Add(path);
            var store = new PlayerProfileStore(path);
            var manager = new LevelManager(state, config, store, new PlayerProfile());

            manager.AddXp(130); // commits Xp=30, Level=1 to disk
            manager.CommitProgress();

            manager.RevertToDayStart(new PlayerProfile { Xp = 0, Level = 0 });

            var reloaded = new PlayerProfileStore(path).Load();
            Assert.AreEqual(0, reloaded.Xp);
            Assert.AreEqual(0, reloaded.Level);
        }

        // Confirms the exploit this method exists to close: without updating
        // lastCommittedProfile, a life-loss retry of the REPLAYED attempt
        // would discard back to the original (higher, already-paid-out)
        // commit instead of the day-start baseline.
        [Test]
        public void RevertToDayStart_UpdatesBaseline_SoLaterDiscardRollsBackToRevertedValue()
        {
            var config = CreateLevelProgressionConfig();
            SetXpToNextLevel(config, 100, 100, 100);
            var state = new GameState(gameConfig);
            var manager = new LevelManager(state, config, CreateTempProfileStore(), new PlayerProfile());

            manager.AddXp(150); // Xp=50, Level=1
            manager.CommitProgress();
            manager.RevertToDayStart(new PlayerProfile { Xp = 0, Level = 0 });

            manager.AddXp(40); // player retries, makes some progress, then fails
            manager.DiscardToLastCommitted();

            Assert.AreEqual(0, state.Xp);
            Assert.AreEqual(0, state.Level);
        }
    }
}
