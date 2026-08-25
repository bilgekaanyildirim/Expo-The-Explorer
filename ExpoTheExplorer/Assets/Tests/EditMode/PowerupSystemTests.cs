using System;
using System.Collections.Generic;
using System.IO;
using ExpoTheExplorer.Core;
using ExpoTheExplorer.Data;
using ExpoTheExplorer.Session;
using ExpoTheExplorer.Systems.DaySystem;
using ExpoTheExplorer.Systems.PowerupSystem;
using ExpoTheExplorer.Systems.ProgressionSystem;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace ExpoTheExplorer.Tests.EditMode
{
    // The powerup STOCK rules (GDD Section 5.2, plan in .claude/powerup-plan.md Adım 1).
    // No effect exists yet -- the three of them arrive in Adım 4-6 -- so everything here
    // is about the resource itself: what a player opens with, what a press costs, what a
    // press that had nothing to do costs, what a Gem buys, and whether any of it survives
    // a save.
    //
    // Two of these suites guard failures that are silent rather than loud, which is why
    // they are worth the length:
    //
    //   * the -1 marker. Read as a plain 0, an existing player launches owning nothing and
    //     nothing on screen says why. This is the fourth field in this project's save
    //     schema where zero is a real value rather than an absence (Lives v3, Keys v7),
    //     and each of the earlier ones cost a bug before it got a test.
    //   * the wasted press. A powerup that takes a charge for doing nothing is not a crash;
    //     it is a player quietly being robbed, and only a test can tell the difference
    //     between "it worked" and "it took the charge and did nothing".
    public class PowerupSystemTests
    {
        private const int StartAuto = 1;
        private const int StartTime = 2;
        private const int StartNoiseClear = 3;

        private const int CostAuto = 30;
        private const int CostTime = 20;
        private const int CostNoiseClear = 15;

        private readonly List<UnityEngine.Object> spawned = new();
        private string testFilePath;

        private GameConfig gameConfig;
        private PowerupConfig powerupConfig;
        private GameState state;
        private Wallet wallet;

        [SetUp]
        public void SetUp()
        {
            testFilePath = Path.Combine(Application.temporaryCachePath, $"powerup_test_{Guid.NewGuid()}.json");

            gameConfig = CreateAsset<GameConfig>();
            powerupConfig = CreatePowerupConfig(
                autoCollect: (StartAuto, CostAuto, PerDay: 1),
                timeReset: (StartTime, CostTime, PerDay: 2),
                noiseClear: (StartNoiseClear, CostNoiseClear, PerDay: 0));

            state = new GameState(gameConfig);
            wallet = new Wallet(state);
        }

        [TearDown]
        public void TearDown()
        {
            if (File.Exists(testFilePath)) File.Delete(testFilePath);
            foreach (var asset in spawned) UnityEngine.Object.DestroyImmediate(asset);
            spawned.Clear();
        }

        private T CreateAsset<T>() where T : ScriptableObject
        {
            var asset = ScriptableObject.CreateInstance<T>();
            spawned.Add(asset);
            return asset;
        }

        // PowerupConfig's numbers are private [SerializeField]s inside a nested
        // [Serializable] block, so they are set through SerializedObject exactly the way
        // KeySystemTests sets KeyConfig's -- which means the property-path strings below
        // must stay in step with the field names, including the block prefix.
        private PowerupConfig CreatePowerupConfig(
            (int Start, int Cost, int PerDay) autoCollect,
            (int Start, int Cost, int PerDay) timeReset,
            (int Start, int Cost, int PerDay) noiseClear)
        {
            var config = CreateAsset<PowerupConfig>();
            var serialized = new SerializedObject(config);

            SetBlock(serialized, "autoCollect", autoCollect);
            SetBlock(serialized, "timeReset", timeReset);
            SetBlock(serialized, "noiseClear", noiseClear);

            serialized.ApplyModifiedPropertiesWithoutUndo();
            return config;

            static void SetBlock(SerializedObject serialized, string block, (int Start, int Cost, int PerDay) values)
            {
                serialized.FindProperty($"{block}.startingCharges").intValue = values.Start;
                serialized.FindProperty($"{block}.gemCost").intValue = values.Cost;
                serialized.FindProperty($"{block}.chargesPerDayCompleted").intValue = values.PerDay;
            }
        }

        private PowerupManager NewManager() => new(powerupConfig, wallet);

        private void GiveGems(int gems) => wallet.ApplyPersistedBalances(state.SoftMoney, gems);

        // --- the enum's numbering is load-bearing ----------------------------------------

        // PowerupManager indexes its charge and effect arrays by (int)type, so these three
        // values are not cosmetic: renumbering them would silently hand each powerup
        // another one's charges, and nothing in the game would look broken. The save file
        // is deliberately immune (PlayerProfile carries a named field per type), which is
        // precisely why nothing else would catch this.
        [Test]
        public void PowerupType_NumbersAreThePositionsTheManagerIndexesBy()
        {
            Assert.AreEqual(0, (int)PowerupType.AutoCollect);
            Assert.AreEqual(1, (int)PowerupType.TimeReset);
            Assert.AreEqual(2, (int)PowerupType.NoiseClear);
        }

        [Test]
        public void PowerupTypes_All_ListsEveryTypeOnceInEnumOrder()
        {
            CollectionAssert.AreEqual(
                new[] { PowerupType.AutoCollect, PowerupType.TimeReset, PowerupType.NoiseClear },
                PowerupTypes.All);
            Assert.AreEqual(3, PowerupTypes.Count);
        }

        // --- what a player opens with ----------------------------------------------------

        [Test]
        public void NewManager_OpensOnTheAuthoredStartingStock()
        {
            var manager = NewManager();

            Assert.AreEqual(StartAuto, manager.ChargesOf(PowerupType.AutoCollect));
            Assert.AreEqual(StartTime, manager.ChargesOf(PowerupType.TimeReset));
            Assert.AreEqual(StartNoiseClear, manager.ChargesOf(PowerupType.NoiseClear));
        }

        [Test]
        public void GemCostOf_ReadsTheAuthoredPricePerType()
        {
            var manager = NewManager();

            Assert.AreEqual(CostAuto, manager.GemCostOf(PowerupType.AutoCollect));
            Assert.AreEqual(CostTime, manager.GemCostOf(PowerupType.TimeReset));
            Assert.AreEqual(CostNoiseClear, manager.GemCostOf(PowerupType.NoiseClear));
        }

        // --- the -1 marker ---------------------------------------------------------------

        // The whole reason PlayerProfileStore writes -1 rather than 0. An older save (or a
        // brand-new player) has to resolve to the authored starting stock, and the store
        // cannot do it: the number lives on PowerupConfig, which a file boundary must never
        // reference.
        [Test]
        public void ApplyPersisted_NegativeMarker_ResolvesToTheAuthoredStartingStock()
        {
            var manager = NewManager();

            manager.ApplyPersisted(-1, -1, -1);

            Assert.AreEqual(StartAuto, manager.ChargesOf(PowerupType.AutoCollect));
            Assert.AreEqual(StartTime, manager.ChargesOf(PowerupType.TimeReset));
            Assert.AreEqual(StartNoiseClear, manager.ChargesOf(PowerupType.NoiseClear));
        }

        // ANY negative is treated as absent, not only the exact marker -- a half-written or
        // hand-edited file carrying -2 means the same thing and must not leave a player
        // owning nothing. The same deliberate asymmetry KeyManager documents.
        [Test]
        public void ApplyPersisted_AnyNegative_IsTreatedAsAbsent()
        {
            var manager = NewManager();

            manager.ApplyPersisted(-2, -99, int.MinValue);

            Assert.AreEqual(StartAuto, manager.ChargesOf(PowerupType.AutoCollect));
            Assert.AreEqual(StartTime, manager.ChargesOf(PowerupType.TimeReset));
            Assert.AreEqual(StartNoiseClear, manager.ChargesOf(PowerupType.NoiseClear));
        }

        // The other half of why the marker cannot be 0: zero is a REAL count that a player
        // reaches by spending, and it has to survive a save unchanged.
        [Test]
        public void ApplyPersisted_Zero_IsARealCountAndIsKept()
        {
            var manager = NewManager();

            manager.ApplyPersisted(0, 0, 0);

            Assert.AreEqual(0, manager.ChargesOf(PowerupType.AutoCollect));
            Assert.AreEqual(0, manager.ChargesOf(PowerupType.TimeReset));
            Assert.AreEqual(0, manager.ChargesOf(PowerupType.NoiseClear));
        }

        [Test]
        public void ApplyPersisted_SavedCounts_AreUsedAsIs()
        {
            var manager = NewManager();

            manager.ApplyPersisted(7, 4, 9);

            Assert.AreEqual(7, manager.ChargesOf(PowerupType.AutoCollect));
            Assert.AreEqual(4, manager.ChargesOf(PowerupType.TimeReset));
            Assert.AreEqual(9, manager.ChargesOf(PowerupType.NoiseClear));
        }

        // --- spending --------------------------------------------------------------------

        // The main screen builds the same manager so it can sell against the counts, and
        // registers nothing. This is the test that says a charge cannot be spent there --
        // the whole safety of the two-screen split rests on it.
        [Test]
        public void TryUse_WithNoEffectRegistered_RefusesAndSpendsNothing()
        {
            var manager = NewManager();

            Assert.IsFalse(manager.TryUse(PowerupType.TimeReset));
            Assert.AreEqual(StartTime, manager.ChargesOf(PowerupType.TimeReset));
        }

        [Test]
        public void CanUse_IsFalseWithoutAnEffect_EvenHoldingCharges()
        {
            var manager = NewManager();

            Assert.IsFalse(manager.CanUse(PowerupType.TimeReset), "no board in this scene, so nothing can perform it");

            manager.RegisterEffect(PowerupType.TimeReset, () => true);

            Assert.IsTrue(manager.CanUse(PowerupType.TimeReset));
        }

        [Test]
        public void TryUse_WhenTheEffectRuns_SpendsExactlyOneAndPublishesTheNewCount()
        {
            var manager = NewManager();
            manager.RegisterEffect(PowerupType.TimeReset, () => true);

            var published = new List<(PowerupType Type, int Charges)>();
            manager.ChargesChanged.Subscribe(published.Add);

            Assert.IsTrue(manager.TryUse(PowerupType.TimeReset));

            Assert.AreEqual(StartTime - 1, manager.ChargesOf(PowerupType.TimeReset));
            CollectionAssert.AreEqual(new[] { (PowerupType.TimeReset, StartTime - 1) }, published);
        }

        // GDD 5.2's common rule, and the reason RegisterEffect takes a Func<bool> rather
        // than an Action: all three powerups have reachable moments with no work available
        // (an empty board, no active tickets, a finished day), and taking a scarce resource
        // for one of them is the opposite of what this system promises.
        [Test]
        public void TryUse_WhenTheEffectReportsNothingToDo_SpendsNothing()
        {
            var manager = NewManager();
            var ran = false;
            manager.RegisterEffect(PowerupType.AutoCollect, () => { ran = true; return false; });

            var published = 0;
            manager.ChargesChanged.Subscribe(_ => published++);

            Assert.IsFalse(manager.TryUse(PowerupType.AutoCollect));

            Assert.IsTrue(ran, "the effect has to run before anyone can know there was nothing to do");
            Assert.AreEqual(StartAuto, manager.ChargesOf(PowerupType.AutoCollect));
            Assert.AreEqual(0, published, "a refused press must not blink the HUD counter");
        }

        [Test]
        public void TryUse_AtZeroCharges_NeverRunsTheEffect()
        {
            var manager = NewManager();
            var runs = 0;
            manager.RegisterEffect(PowerupType.AutoCollect, () => { runs++; return true; });

            for (var i = 0; i < StartAuto; i++) Assert.IsTrue(manager.TryUse(PowerupType.AutoCollect));

            Assert.IsFalse(manager.TryUse(PowerupType.AutoCollect));
            Assert.AreEqual(StartAuto, runs, "an empty stock must be refused before the effect is even attempted");
            Assert.AreEqual(0, manager.ChargesOf(PowerupType.AutoCollect));
        }

        [Test]
        public void TryUse_SpendsOnlyTheTypeThatWasPressed()
        {
            var manager = NewManager();
            manager.RegisterEffect(PowerupType.AutoCollect, () => true);

            Assert.IsTrue(manager.TryUse(PowerupType.AutoCollect));

            Assert.AreEqual(StartTime, manager.ChargesOf(PowerupType.TimeReset));
            Assert.AreEqual(StartNoiseClear, manager.ChargesOf(PowerupType.NoiseClear));
        }

        // The one re-entrancy case the ordering in TryUse has to survive. An effect cascades
        // synchronously -- auto-collect can fill a tray, deliver a ticket and complete the
        // day inside this very call -- so a day-completion grant can land in the middle of a
        // press. Both operations are +/-1 on the same field, so the net has to be exact.
        [Test]
        public void TryUse_WhenADayCompletionGrantLandsInsideTheEffect_NetsOutExactly()
        {
            var manager = NewManager();
            manager.RegisterEffect(PowerupType.AutoCollect, () =>
            {
                manager.GrantForDayCompleted();
                return true;
            });

            Assert.IsTrue(manager.TryUse(PowerupType.AutoCollect));

            // +1 granted (autoCollect's authored per-day amount), -1 spent.
            Assert.AreEqual(StartAuto, manager.ChargesOf(PowerupType.AutoCollect));
            Assert.AreEqual(StartTime + 2, manager.ChargesOf(PowerupType.TimeReset), "the grant still pays the other types");
        }

        [Test]
        public void RegisterEffect_Twice_ReplacesRatherThanStacks()
        {
            var manager = NewManager();
            var first = 0;
            var second = 0;
            manager.RegisterEffect(PowerupType.TimeReset, () => { first++; return true; });
            manager.RegisterEffect(PowerupType.TimeReset, () => { second++; return true; });

            Assert.IsTrue(manager.TryUse(PowerupType.TimeReset));

            Assert.AreEqual(0, first);
            Assert.AreEqual(1, second);
        }

        // --- buying ----------------------------------------------------------------------

        [Test]
        public void TryBuyWithGems_Affordable_AddsOneChargeAndChargesTheWallet()
        {
            GiveGems(100);
            var manager = NewManager();

            Assert.IsTrue(manager.TryBuyWithGems(PowerupType.AutoCollect));

            Assert.AreEqual(StartAuto + 1, manager.ChargesOf(PowerupType.AutoCollect));
            Assert.AreEqual(100 - CostAuto, state.Gems);
        }

        // The ordering guard: there must be no path that takes Gems without handing over a
        // charge, and none that hands one over for free. Wallet refuses the spend first, so
        // the count never moves.
        [Test]
        public void TryBuyWithGems_Unaffordable_ChangesNothingAtAll()
        {
            GiveGems(CostAuto - 1);
            var manager = NewManager();

            var published = 0;
            manager.ChargesChanged.Subscribe(_ => published++);

            Assert.IsFalse(manager.TryBuyWithGems(PowerupType.AutoCollect));

            Assert.AreEqual(StartAuto, manager.ChargesOf(PowerupType.AutoCollect));
            Assert.AreEqual(CostAuto - 1, state.Gems);
            Assert.AreEqual(0, published);
        }

        [Test]
        public void TryBuyWithGems_HasNoStockCeiling()
        {
            GiveGems(CostNoiseClear * 10);
            var manager = NewManager();

            for (var i = 0; i < 10; i++) Assert.IsTrue(manager.TryBuyWithGems(PowerupType.NoiseClear));

            Assert.AreEqual(StartNoiseClear + 10, manager.ChargesOf(PowerupType.NoiseClear));
        }

        // --- earning ---------------------------------------------------------------------

        [Test]
        public void GrantForDayCompleted_AddsTheAuthoredAmountPerType()
        {
            var manager = NewManager();

            manager.GrantForDayCompleted();

            Assert.AreEqual(StartAuto + 1, manager.ChargesOf(PowerupType.AutoCollect));
            Assert.AreEqual(StartTime + 2, manager.ChargesOf(PowerupType.TimeReset));
        }

        // 0 is how a designer turns this earn path off for one powerup, so it must be a
        // real no-op rather than a publish carrying an unchanged number.
        [Test]
        public void GrantForDayCompleted_WithZeroAuthored_GrantsNothingAndPublishesNothingForThatType()
        {
            var manager = NewManager();
            var published = new List<PowerupType>();
            manager.ChargesChanged.Subscribe(change => published.Add(change.Type));

            manager.GrantForDayCompleted();

            Assert.AreEqual(StartNoiseClear, manager.ChargesOf(PowerupType.NoiseClear));
            CollectionAssert.DoesNotContain(published, PowerupType.NoiseClear);
        }

        // --- the save file ---------------------------------------------------------------

        // A v7 file predates powerups entirely. Read naively it would carry three absent
        // ints as zeros and hand an existing player nothing; the upgrade writes markers
        // instead, and the manager turns those into the authored stock.
        [Test]
        public void Profile_FromBeforePowerupsExisted_OpensOnTheStartingStock()
        {
            File.WriteAllText(testFilePath, "{\"Version\":7,\"SoftMoney\":500,\"Gems\":3,\"CurrentDayIndex\":1,\"Keys\":2}");

            var profile = new PlayerProfileStore(testFilePath).Load();
            var manager = NewManager();
            manager.ApplyPersisted(profile.AutoCollectCharges, profile.TimeResetCharges, profile.NoiseClearCharges);

            Assert.AreEqual(StartAuto, manager.ChargesOf(PowerupType.AutoCollect));
            Assert.AreEqual(StartTime, manager.ChargesOf(PowerupType.TimeReset));
            Assert.AreEqual(StartNoiseClear, manager.ChargesOf(PowerupType.NoiseClear));
            Assert.AreEqual(500, profile.SoftMoney, "the rest of the old save must survive the upgrade untouched");
        }

        // The v8 file is the awkward one: powerups existed, so two of its three counts are
        // real and must survive, but the third was written under the old KEY name
        // (BoardClarityCharges) and JsonUtility will not find it. Read naively that field
        // comes back 0 -- a real count meaning "you have none" -- and a player quietly
        // loses a stock they may have paid Gems for. The v9 branch turns exactly that one
        // field into a marker and leaves the other two alone.
        [Test]
        public void Profile_FromTheRenamedVersion_ResolvesOnlyTheRenamedFieldAndKeepsTheRest()
        {
            File.WriteAllText(
                testFilePath,
                "{\"Version\":8,\"SoftMoney\":0,\"Gems\":0,\"CurrentDayIndex\":0,\"Keys\":5," +
                "\"AutoCollectCharges\":4,\"TimeResetCharges\":6,\"BoardClarityCharges\":9}");

            var profile = new PlayerProfileStore(testFilePath).Load();
            var manager = NewManager();
            manager.ApplyPersisted(profile.AutoCollectCharges, profile.TimeResetCharges, profile.NoiseClearCharges);

            Assert.AreEqual(4, manager.ChargesOf(PowerupType.AutoCollect), "an intact key keeps its real count");
            Assert.AreEqual(6, manager.ChargesOf(PowerupType.TimeReset), "an intact key keeps its real count");
            Assert.AreEqual(
                StartNoiseClear,
                manager.ChargesOf(PowerupType.NoiseClear),
                "the renamed field is unreadable, so it must resolve to the authored stock rather than to zero");
        }

        // The complement: once a file IS current its own counts are the truth, including a
        // deliberate zero. Upgrading a current file would silently refill a spent stock.
        [Test]
        public void Profile_AlreadyCurrent_KeepsItsOwnCountsIncludingZero()
        {
            File.WriteAllText(
                testFilePath,
                "{\"Version\":9,\"SoftMoney\":0,\"Gems\":0,\"CurrentDayIndex\":0,\"Keys\":5," +
                "\"AutoCollectCharges\":0,\"TimeResetCharges\":6,\"NoiseClearCharges\":0}");

            var profile = new PlayerProfileStore(testFilePath).Load();
            var manager = NewManager();
            manager.ApplyPersisted(profile.AutoCollectCharges, profile.TimeResetCharges, profile.NoiseClearCharges);

            Assert.AreEqual(0, manager.ChargesOf(PowerupType.AutoCollect));
            Assert.AreEqual(6, manager.ChargesOf(PowerupType.TimeReset));
            Assert.AreEqual(0, manager.ChargesOf(PowerupType.NoiseClear));
        }

        // --- through the session ---------------------------------------------------------

        private GameSession NewSession(PowerupConfig config, PlayerProfileStore store) =>
            new(
                gameConfig,
                CreateAsset<LivesConfig>(),
                CreateAsset<KeyConfig>(),
                foodCatalog: null,
                powerupConfig: config,
                profileStore: store,
                dayCatalog: new List<DayDefinition>());

        // GameSession composes what reaches the file, so a field omitted there does not
        // fail loudly -- it writes a zero over charges a player paid Gems for. This is the
        // same reason GameSessionTests pins the wallet and the day index.
        [Test]
        public void Session_SaveThenLoad_RoundTripsEveryCharge()
        {
            var store = new PlayerProfileStore(testFilePath);
            var session = NewSession(powerupConfig, store);

            session.PowerupManager.ApplyPersisted(4, 5, 6);
            session.Save();

            var reloaded = NewSession(powerupConfig, new PlayerProfileStore(testFilePath));

            Assert.AreEqual(4, reloaded.PowerupManager.ChargesOf(PowerupType.AutoCollect));
            Assert.AreEqual(5, reloaded.PowerupManager.ChargesOf(PowerupType.TimeReset));
            Assert.AreEqual(6, reloaded.PowerupManager.ChargesOf(PowerupType.NoiseClear));
        }

        [Test]
        public void Session_WithNoPowerupConfig_HasNoManager()
        {
            var session = NewSession(config: null, new PlayerProfileStore(testFilePath));

            Assert.IsNull(session.PowerupManager, "failing open means no stock, not a broken scene");
        }

        // The reason GameSession keeps the loaded counts in three fields. A forgotten
        // Inspector drag must not be the thing that wipes a stock the player bought -- a
        // session that cannot MANAGE the charges still has to write them back unharmed.
        [Test]
        public void Session_WithNoPowerupConfig_WritesBackTheChargesItLoaded()
        {
            File.WriteAllText(
                testFilePath,
                "{\"Version\":9,\"SoftMoney\":0,\"Gems\":0,\"CurrentDayIndex\":0,\"Keys\":5," +
                "\"AutoCollectCharges\":8,\"TimeResetCharges\":9,\"NoiseClearCharges\":10}");

            NewSession(config: null, new PlayerProfileStore(testFilePath)).Save();

            var profile = new PlayerProfileStore(testFilePath).Load();

            Assert.AreEqual(8, profile.AutoCollectCharges);
            Assert.AreEqual(9, profile.TimeResetCharges);
            Assert.AreEqual(10, profile.NoiseClearCharges);
        }
    }
}
