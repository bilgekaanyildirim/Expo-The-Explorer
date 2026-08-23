using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using ExpoTheExplorer.Core;
using ExpoTheExplorer.Data;
using ExpoTheExplorer.Session;
using ExpoTheExplorer.Systems.DaySystem;
using ExpoTheExplorer.Systems.ProgressionSystem;
using NUnit.Framework;
using UnityEngine;

namespace ExpoTheExplorer.Tests.EditMode
{
    // The session half of what used to be GameManager.Awake, now reachable from a test for
    // the first time (decisions.md D-021). The day-index CLAMP is the point of this suite:
    // it has guarded a real failure since D-012 -- an index past the last authored Day
    // leaves CurrentDay null and throws on the first ticket -- and had no test at all
    // while it sat inside a MonoBehaviour in a predefined assembly.
    //
    // The Save cases matter for a different reason. GameSession composes the object that
    // reaches the file, so a field omitted there does not fail loudly: it silently writes
    // a zero over a real player's money, day or lives. "Save then load returns every
    // field" is the only thing standing between that and a shipped build.
    public class GameSessionTests
    {
        private string testFilePath;
        // Fully qualified because this file imports both System (for Guid) and
        // UnityEngine, which makes a bare `Object` ambiguous. MetaCatalogValidatorTests
        // uses the short form legitimately -- it has no System import, so nothing to
        // clash with.
        private readonly List<UnityEngine.Object> spawned = new();

        [SetUp]
        public void SetUp()
        {
            testFilePath = Path.Combine(Application.temporaryCachePath, $"session_test_{Guid.NewGuid()}.json");
        }

        [TearDown]
        public void TearDown()
        {
            if (File.Exists(testFilePath)) File.Delete(testFilePath);
            foreach (var asset in spawned) UnityEngine.Object.DestroyImmediate(asset);
            spawned.Clear();
        }

        private T CreateConfig<T>() where T : ScriptableObject
        {
            var asset = ScriptableObject.CreateInstance<T>();
            spawned.Add(asset);
            return asset;
        }

        private static List<DayDefinition> Catalog(int dayCount) =>
            Enumerable.Range(0, dayCount)
                .Select(i => new DayDefinition(i, 1, new List<ResolvedTicketEntry>(), new List<ResolvedBoardSpawnEntry>()))
                .ToList();

        // foodCatalog is null throughout: passing a dayCatalog means the real parse (which
        // would need Resources and a FoodCatalog asset) is never reached. That injection
        // point exists for exactly this.
        // Kept so the starting-grant cases can assert against the CONFIG's value rather
        // than against a literal: the number is authored in GameConfig.asset and a test
        // that repeats it would pass while the asset says something else.
        private GameConfig gameConfig;

        private GameSession CreateSession(int dayCount = 3, PlayerProfile saved = null)
        {
            var store = new PlayerProfileStore(testFilePath);
            if (saved != null) store.Save(saved);

            gameConfig = CreateConfig<GameConfig>();

            return new GameSession(
                gameConfig,
                CreateConfig<LivesConfig>(),
                foodCatalog: null,
                profileStore: store,
                dayCatalog: Catalog(dayCount));
        }

        // --- the day-index clamp ---------------------------------------------------------

        [Test]
        public void DayIndex_WithinTheCatalog_IsUsedAsIs()
        {
            var session = CreateSession(dayCount: 5, saved: new PlayerProfile { CurrentDayIndex = 3 });

            Assert.AreEqual(3, session.State.CurrentDayIndex);
        }

        // The failure this clamp exists for: a save from a build with more content, or one
        // written before a Day was deleted. Landing on the last Day is the safe outcome;
        // trusting the number leaves CurrentDay null and throws on the first ticket.
        [Test]
        public void DayIndex_PastTheLastDay_ClampsToTheLastOne()
        {
            var session = CreateSession(dayCount: 3, saved: new PlayerProfile { CurrentDayIndex = 99 });

            Assert.AreEqual(2, session.State.CurrentDayIndex);
            Assert.IsNotNull(session.CurrentDay, "a clamped index must resolve to a real Day");
        }

        [Test]
        public void DayIndex_Negative_ClampsToTheFirstDay()
        {
            var session = CreateSession(dayCount: 3, saved: new PlayerProfile { CurrentDayIndex = -7 });

            Assert.AreEqual(0, session.State.CurrentDayIndex);
        }

        [Test]
        public void DayIndex_WithAnEmptyCatalog_IsZero()
        {
            var session = CreateSession(dayCount: 0, saved: new PlayerProfile { CurrentDayIndex = 4 });

            Assert.AreEqual(0, session.State.CurrentDayIndex);
            Assert.IsNull(session.CurrentDay, "no catalog means no Day, which every consumer already falls back on");
        }

        // --- what the profile seeds ------------------------------------------------------

        [Test]
        public void Balances_ComeFromTheProfile_ThroughTheWallet()
        {
            var session = CreateSession(saved: new PlayerProfile { SoftMoney = 1250, Gems = 8 });

            Assert.AreEqual(1250, session.State.SoftMoney);
            Assert.AreEqual(8, session.State.Gems);
        }

        // ApplyPersistedBalances re-takes the day-start snapshot, which is what makes the
        // first retry of a session revert to the RESTORED balance instead of to zero. This
        // is ordering rule 4, and it is invisible until someone reorders the constructor.
        [Test]
        public void FirstRetryOfASession_RevertsToTheRestoredBalance_NotToZero()
        {
            var session = CreateSession(saved: new PlayerProfile { SoftMoney = 500, Gems = 2 });

            session.Wallet.EarnSoftMoney(300);
            session.Wallet.RevertToDayStart();

            Assert.AreEqual(500, session.State.SoftMoney);
        }

        [Test]
        public void Lives_ComeFromTheProfile()
        {
            var session = CreateSession(saved: new PlayerProfile { Lives = 1 });

            Assert.AreEqual(1, session.State.Lives);
        }

        [Test]
        public void OwnedMetaItems_ComeFromTheProfile()
        {
            var session = CreateSession(
                saved: new PlayerProfile { OwnedMetaItemIds = { "Meta1.Square", "Meta1.Table1" } });

            Assert.AreEqual(2, session.OwnedMetaItemIds.Count);
            Assert.IsTrue(session.OwnedMetaItemIds.Contains("Meta1.Square"));
        }

        // A player with no save is a NEW player, and a new player is given the opening
        // balance authored on GameConfig (decisions.md D-026). Asserted through the config
        // rather than against a literal, so re-authoring the asset cannot make this lie.
        [Test]
        public void WithNoSavedFile_StartsWithTheAuthoredGrantAndFullLives()
        {
            var session = CreateSession();

            Assert.AreEqual(gameConfig.StartingSoftMoney, session.State.SoftMoney);
            Assert.Greater(gameConfig.StartingSoftMoney, 0, "a zero grant would make this test prove nothing");
            Assert.AreEqual(0, session.State.Gems, "the grant is coins only");
            Assert.AreEqual(GameState.DefaultStartingLives, session.State.Lives);
            Assert.IsEmpty(session.OwnedMetaItemIds);
        }

        // The other half of the grant rule, and the one that protects a real player: it is
        // given ONCE, to someone with no readable save. A player who spent down to zero has
        // a save that says zero, and must stay at zero -- topping them up here would hand
        // out the grant on every launch.
        [Test]
        public void WithASavedFile_TheGrantIsNotHandedOutAgain()
        {
            var session = CreateSession(saved: new PlayerProfile { SoftMoney = 0, Lives = 2 });

            Assert.AreEqual(0, session.State.SoftMoney);
        }

        // The grant arrives before the day-start snapshot is taken, because it comes through
        // ApplyPersistedBalances like any restored balance. Without that a new player's first
        // failed day would revert their wallet to zero.
        [Test]
        public void TheGrantSurvivesARetry_LikeARestoredBalance()
        {
            var session = CreateSession();

            session.Wallet.EarnSoftMoney(70);
            session.Wallet.RevertToDayStart();

            Assert.AreEqual(gameConfig.StartingSoftMoney, session.State.SoftMoney);
        }

        // --- what Save writes ------------------------------------------------------------

        // The case that guards player data. A field forgotten in GameSession.Save does not
        // throw -- it writes a zero over something real -- so every field is asserted at a
        // DISTINCT value, because a bug that copies the wrong field would pass if two of
        // them happened to match.
        [Test]
        public void Save_ThenLoad_ReturnsEveryField()
        {
            var session = CreateSession(
                dayCount: 5,
                saved: new PlayerProfile
                {
                    SoftMoney = 1250,
                    Gems = 8,
                    CurrentDayIndex = 3,
                    Lives = 2,
                    OwnedMetaItemIds = { "Meta1.Square" }
                });

            session.Save();

            var reloaded = new PlayerProfileStore(testFilePath).Load();

            Assert.AreEqual(PlayerProfileStore.CurrentVersion, reloaded.Version);
            Assert.AreEqual(1250, reloaded.SoftMoney);
            Assert.AreEqual(8, reloaded.Gems);
            Assert.AreEqual(3, reloaded.CurrentDayIndex);
            Assert.AreEqual(2, reloaded.Lives);
            Assert.AreEqual(new[] { "Meta1.Square" }, reloaded.OwnedMetaItemIds);
        }

        // The set is live: this is how a purchase will commit, and Save has to pick it up
        // rather than writing whatever was loaded.
        [Test]
        public void Save_PicksUpMetaItemsAddedSinceLoad()
        {
            var session = CreateSession(saved: new PlayerProfile { OwnedMetaItemIds = { "Meta1.Square" } });

            session.OwnedMetaItemIds.Add("Meta1.Fountain");
            session.Save();

            var reloaded = new PlayerProfileStore(testFilePath).Load();

            Assert.AreEqual(2, reloaded.OwnedMetaItemIds.Count);
            Assert.Contains("Meta1.Fountain", reloaded.OwnedMetaItemIds);
        }

        [Test]
        public void Save_WritesLiveValues_NotTheOnesLoaded()
        {
            var session = CreateSession(saved: new PlayerProfile { SoftMoney = 100 });

            session.Wallet.EarnSoftMoney(50);
            session.Save();

            Assert.AreEqual(150, new PlayerProfileStore(testFilePath).Load().SoftMoney);
        }
    }
}
