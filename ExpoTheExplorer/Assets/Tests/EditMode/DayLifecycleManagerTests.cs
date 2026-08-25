using System.Collections.Generic;
using ExpoTheExplorer.Core;
using ExpoTheExplorer.Data;
using ExpoTheExplorer.Systems.DayLifecycle;
using ExpoTheExplorer.Systems.EconomySystem;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace ExpoTheExplorer.Tests.EditMode
{
    public class DayLifecycleManagerTests
    {
        // A tip-free delivery: Total == OrderValue == 10, so tests that only care
        // about the delivery COUNT don't have to reason about the tip split. It also
        // carries no remaining seconds, so it scores nothing -- the star tests below
        // build their own payouts with the timing they want to test.
        private static readonly DeliveryPayoutResult SamplePayout =
            new DeliveryPayoutResult(orderValue: 10, tier: TipTier.Critical, tipRate: 0f);

        // A day whose tickets add up to 100 seconds, so "seconds saved" and "percent of
        // the day's clock" are the same number and every expectation below reads directly.
        private const float DayBudgetSeconds = 100f;

        private readonly List<Object> spawnedAssets = new();

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
                Object.DestroyImmediate(asset);
            }
            spawnedAssets.Clear();
        }

        // Thresholds 0.5/0.25 rather than the shipped 0.55/0.3, for the reason
        // EconomySystemTests gives about its own ratios: a boundary test has to land on a
        // number floats represent exactly, or it stops testing the rule and starts testing
        // float arithmetic. The penalties keep their shipped values, which are exact.
        private StarScoreConfig CreateStarScoreConfig(
            float threeStarScore = 0.5f,
            float twoStarScore = 0.25f,
            float wrongDeliveryPenalty = 0.2f,
            float timeoutPenalty = 0.05f)
        {
            var config = ScriptableObject.CreateInstance<StarScoreConfig>();
            spawnedAssets.Add(config);

            var serialized = new SerializedObject(config);
            serialized.FindProperty("threeStarScore").floatValue = threeStarScore;
            serialized.FindProperty("twoStarScore").floatValue = twoStarScore;
            serialized.FindProperty("wrongDeliveryPenalty").floatValue = wrongDeliveryPenalty;
            serialized.FindProperty("timeoutPenalty").floatValue = timeoutPenalty;
            serialized.ApplyModifiedPropertiesWithoutUndo();

            return config;
        }

        // A day already begun: budget handed over the way GameManager hands it over on
        // every entry into a day.
        private DayLifecycleManager NewDay(StarScoreConfig config, float budgetSeconds = DayBudgetSeconds)
        {
            var manager = new DayLifecycleManager(new GameState(gameConfig), config);
            manager.ResetForNewDay(budgetSeconds);
            return manager;
        }

        private static DeliveryPayoutResult DeliveryWithSeconds(float remainingSeconds) =>
            new DeliveryPayoutResult(orderValue: 10, tier: TipTier.Full, tipRate: 0f, remainingSeconds: remainingSeconds);

        // The whole star rule on a clean day, in one table (decisions.md D-060): the score
        // is the fraction of the day's ticket clock the player handed back, and both
        // thresholds are inclusive lower bounds. The last row is the completion floor --
        // a day finished at a crawl is still a day finished.
        [TestCase(60f, 3)]
        [TestCase(50f, 3)]
        [TestCase(40f, 2)]
        [TestCase(25f, 2)]
        [TestCase(10f, 1)]
        [TestCase(0f, 1)]
        public void StarCount_ComesFromTheSecondsHandedBack(float savedSeconds, int expectedStars)
        {
            var manager = NewDay(CreateStarScoreConfig());

            manager.RecordDelivery(DeliveryWithSeconds(savedSeconds));

            Assert.AreEqual(expectedStars, manager.StarCount);
        }

        // The asymmetry that D-060 is built on, as a pair: the SAME 60-second day loses a
        // star to a wrong delivery and keeps all three through a timeout. A timeout has
        // already forfeited its ticket's own share of the clock, so its explicit penalty is
        // the remainder of the bill rather than the bill.
        [Test]
        public void StarCount_WrongDeliveryCostsAStar_WhereATimeoutIsAbsorbed()
        {
            var wrongDeliveryDay = NewDay(CreateStarScoreConfig());
            wrongDeliveryDay.RecordDelivery(DeliveryWithSeconds(60f));
            wrongDeliveryDay.RecordFailure(DayFailureCause.WrongDelivery);

            var timeoutDay = NewDay(CreateStarScoreConfig());
            timeoutDay.RecordDelivery(DeliveryWithSeconds(60f));
            timeoutDay.RecordFailure(DayFailureCause.Timeout);

            Assert.AreEqual(0.4f, wrongDeliveryDay.StarScore, 0.0001f);
            Assert.AreEqual(2, wrongDeliveryDay.StarCount);
            Assert.AreEqual(0.55f, timeoutDay.StarScore, 0.0001f);
            Assert.AreEqual(3, timeoutDay.StarCount);
        }

        // Lives start at 3, so 3+ failures in one day is only reachable by paying to
        // continue -- which refills lives but deliberately leaves this counter alone, since
        // it is still the same attempt. Those runs finish with nothing to show HOWEVER fast
        // they were, which is why the check sits ahead of the score.
        [Test]
        public void StarCount_AfterThreeFailures_IsZeroEvenOnAPerfectClock()
        {
            var manager = NewDay(CreateStarScoreConfig());
            manager.RecordDelivery(DeliveryWithSeconds(DayBudgetSeconds));
            manager.RecordFailure(DayFailureCause.Timeout);
            manager.RecordFailure(DayFailureCause.Timeout);
            manager.RecordFailure(DayFailureCause.WrongDelivery);

            Assert.AreEqual(1f, manager.TimeEfficiency, 0.0001f, "The clock was perfect.");
            Assert.AreEqual(0, manager.StarCount, "A paid Continue still costs the day its stars.");
        }

        // Penalties are allowed to exceed the time score -- that is how a sloppy fast run
        // lands on the completion floor -- but the score itself never goes negative, or the
        // thresholds would stop being read on the scale they are authored on.
        [Test]
        public void StarScore_NeverGoesNegative()
        {
            var manager = NewDay(CreateStarScoreConfig());
            manager.RecordDelivery(DeliveryWithSeconds(10f));
            manager.RecordFailure(DayFailureCause.WrongDelivery);
            manager.RecordFailure(DayFailureCause.WrongDelivery);

            Assert.AreEqual(0.4f, manager.FailurePenalty, 0.0001f);
            Assert.AreEqual(0f, manager.StarScore, 0.0001f);
            Assert.AreEqual(1, manager.StarCount);
        }

        // Guards the divide: a day with no authored budget (no Day loaded) scores nothing
        // rather than dividing by zero, and the completion floor still pays its one star.
        [Test]
        public void TimeEfficiency_WithNoBudget_IsZero_AndTheDayStillEarnsItsFirstStar()
        {
            var manager = NewDay(CreateStarScoreConfig(), budgetSeconds: 0f);
            manager.RecordDelivery(DeliveryWithSeconds(45f));

            Assert.AreEqual(0f, manager.TimeEfficiency, 0.0001f);
            Assert.AreEqual(1, manager.StarCount);
        }

        // Deliveries that somehow hand back more than the day's whole clock (a budget
        // authored shorter than the tickets it hands out) clamp instead of scoring above 1.
        [Test]
        public void TimeEfficiency_ClampsAtOne()
        {
            var manager = NewDay(CreateStarScoreConfig(), budgetSeconds: 50f);
            manager.RecordDelivery(DeliveryWithSeconds(40f));
            manager.RecordDelivery(DeliveryWithSeconds(40f));

            Assert.AreEqual(1f, manager.TimeEfficiency, 0.0001f);
        }

        // An unwired StarScoreConfig is a wiring bug, and it reads as zero stars rather
        // than falling back to a second star rule living in code (which is what D-060
        // removed). GameManager.Awake is what says so out loud.
        [Test]
        public void StarCount_WithNoConfig_IsZero()
        {
            var manager = new DayLifecycleManager(new GameState(gameConfig), null);
            manager.ResetForNewDay(DayBudgetSeconds);
            manager.RecordDelivery(DeliveryWithSeconds(DayBudgetSeconds));

            Assert.AreEqual(0f, manager.FailurePenalty, 0.0001f);
            Assert.AreEqual(0, manager.StarCount);
        }

        // The bar is scaled so that FULL means three stars (decisions.md D-061), so a score
        // sitting exactly on the three-star threshold fills it completely and the two-star
        // notch lands proportionally along it -- 0.25/0.5 with these test thresholds.
        [TestCase(50f, 1f)]
        [TestCase(25f, 0.5f)]
        [TestCase(0f, 0f)]
        [TestCase(80f, 1f)]
        public void ScoreProgressToMaxStars_IsTheScoreOverTheThreeStarThreshold(float savedSeconds, float expected)
        {
            var manager = NewDay(CreateStarScoreConfig());
            manager.RecordDelivery(DeliveryWithSeconds(savedSeconds));

            Assert.AreEqual(expected, manager.ScoreProgressToMaxStars, 0.0001f);
            Assert.AreEqual(0.5f, manager.TwoStarProgressPosition, 0.0001f);
        }

        // Retuning the thresholds has to MOVE the notch, which is the whole reason the view
        // is told this number instead of holding its own copy: at 0.30/0.60 the second star
        // sits halfway, at 0.45/0.60 it sits three quarters along.
        [Test]
        public void TwoStarProgressPosition_FollowsTheAuthoredThresholds()
        {
            var half = NewDay(CreateStarScoreConfig(threeStarScore: 0.6f, twoStarScore: 0.3f));
            var threeQuarters = NewDay(CreateStarScoreConfig(threeStarScore: 0.6f, twoStarScore: 0.45f));

            Assert.AreEqual(0.5f, half.TwoStarProgressPosition, 0.0001f);
            Assert.AreEqual(0.75f, threeQuarters.TwoStarProgressPosition, 0.0001f);
        }

        // Guards the second divide. A three-star threshold of 0 means every score already
        // maxes the rating, so the bar reads full and the notch collapses to its left edge
        // rather than the view being handed a NaN to position a star with.
        [Test]
        public void ScoreProgress_WithAZeroThreeStarThreshold_ReadsFull_AndCollapsesTheNotch()
        {
            var manager = NewDay(CreateStarScoreConfig(threeStarScore: 0f, twoStarScore: 0f));

            Assert.AreEqual(1f, manager.ScoreProgressToMaxStars, 0.0001f);
            Assert.AreEqual(0f, manager.TwoStarProgressPosition, 0.0001f);
            Assert.AreEqual(3, manager.StarCount);
        }

        [Test]
        public void ScoreProgress_WithNoConfig_IsEmpty()
        {
            var manager = new DayLifecycleManager(new GameState(gameConfig), null);
            manager.ResetForNewDay(DayBudgetSeconds);
            manager.RecordDelivery(DeliveryWithSeconds(DayBudgetSeconds));

            Assert.AreEqual(0f, manager.ScoreProgressToMaxStars, 0.0001f);
            Assert.AreEqual(0f, manager.TwoStarProgressPosition, 0.0001f);
        }

        [Test]
        public void RecordDelivery_SumsTheSecondsHandedBack()
        {
            var manager = NewDay(CreateStarScoreConfig());

            manager.RecordDelivery(DeliveryWithSeconds(12.5f));
            manager.RecordDelivery(DeliveryWithSeconds(7.5f));

            Assert.AreEqual(20f, manager.SavedSeconds, 0.0001f);
            Assert.AreEqual(0.2f, manager.TimeEfficiency, 0.0001f);
        }

        // The two causes are counted apart (they cost different amounts) and together (the
        // receipt's "Orders failed" row is still one number).
        [Test]
        public void RecordFailure_CountsEachCauseApartAndTogether()
        {
            var manager = NewDay(CreateStarScoreConfig());

            manager.RecordFailure(DayFailureCause.WrongDelivery);
            manager.RecordFailure(DayFailureCause.Timeout);
            manager.RecordFailure(DayFailureCause.Timeout);

            Assert.AreEqual(1, manager.WrongDeliveryCount);
            Assert.AreEqual(2, manager.TimeoutCount);
            Assert.AreEqual(3, manager.OrdersFailedCount);
        }

        [Test]
        public void RecordDelivery_IncrementsCounter()
        {
            var state = new GameState(gameConfig);
            var manager = new DayLifecycleManager(state, CreateStarScoreConfig());

            manager.RecordDelivery(SamplePayout);
            manager.RecordDelivery(SamplePayout);

            Assert.AreEqual(2, state.TicketsDeliveredToday);
        }

        // Day completion is now TicketSlotManager's job (sequence exhaustion + all
        // slots empty, see its AssignTicket) -- a delivery-count goal here would
        // never fire once a single ticket was lost to a timeout instead of being
        // delivered (bug: fixed 2026-08).
        [Test]
        public void RecordDelivery_NeverPublishesDayCompleted()
        {
            var state = new GameState(gameConfig);
            var manager = new DayLifecycleManager(state, CreateStarScoreConfig());

            var published = false;
            state.DayCompleted.Subscribe(_ => published = true);

            manager.RecordDelivery(SamplePayout);
            manager.RecordDelivery(SamplePayout);
            manager.RecordDelivery(SamplePayout);

            Assert.IsFalse(published);
        }

        // Every day-start figure, including the ones the score is built from -- a retry
        // that kept last attempt's saved seconds would grade the redo on both runs.
        [Test]
        public void ResetForNewDay_ClearsEveryFigure_AndTakesTheNewDaysBudget()
        {
            var state = new GameState(gameConfig);
            var manager = new DayLifecycleManager(state, CreateStarScoreConfig());
            manager.ResetForNewDay(DayBudgetSeconds);
            manager.RecordDelivery(DeliveryWithSeconds(40f));
            manager.RecordDelivery(DeliveryWithSeconds(40f));
            manager.RecordFailure(DayFailureCause.WrongDelivery);

            manager.ResetForNewDay(200f);

            Assert.AreEqual(0, state.TicketsDeliveredToday);
            Assert.AreEqual(0, manager.Total);
            Assert.AreEqual(0, manager.OrdersFailedCount);
            Assert.AreEqual(0, manager.WrongDeliveryCount);
            Assert.AreEqual(0, manager.TimeoutCount);
            Assert.AreEqual(0f, manager.SavedSeconds, 0.0001f);
            Assert.AreEqual(200f, manager.TotalTicketSeconds, 0.0001f, "The new day is scored against ITS length.");
            Assert.AreEqual(0f, manager.StarScore, 0.0001f);
        }

        [Test]
        public void RecordDelivery_SplitsOrderValueAndTipIntoSeparateTotals()
        {
            var state = new GameState(gameConfig);
            var manager = new DayLifecycleManager(state, CreateStarScoreConfig());
            var payout = new DeliveryPayoutResult(orderValue: 20, tier: TipTier.Full, tipRate: 0.5f);

            manager.RecordDelivery(payout); // Tip = 10 -> Total = 30

            Assert.AreEqual(20, manager.OrdersDeliveredValue);
            Assert.AreEqual(10, manager.TipsValue);
            Assert.AreEqual(30, manager.Total);
        }

        // Under the old model the tip line was TotalTip - BaseTip, which went
        // NEGATIVE whenever a patience coefficient below 1 pulled the total under
        // its own base. The tip is now its own non-negative addend, so the receipt
        // cannot show a negative Tips row however late the delivery was.
        [Test]
        public void RecordDelivery_WorstTier_LeavesTipsValueAtZero_NeverNegative()
        {
            var state = new GameState(gameConfig);
            var manager = new DayLifecycleManager(state, CreateStarScoreConfig());

            manager.RecordDelivery(new DeliveryPayoutResult(orderValue: 25, tier: TipTier.Critical, tipRate: 0f));

            Assert.AreEqual(25, manager.OrdersDeliveredValue);
            Assert.AreEqual(0, manager.TipsValue);
            Assert.AreEqual(25, manager.Total);
        }

        [Test]
        public void RecordFailure_IncrementsOrdersFailedCount()
        {
            var state = new GameState(gameConfig);
            var manager = new DayLifecycleManager(state, CreateStarScoreConfig());

            manager.RecordFailure(DayFailureCause.Timeout);
            manager.RecordFailure(DayFailureCause.WrongDelivery);

            Assert.AreEqual(2, manager.OrdersFailedCount);
        }
    }
}
