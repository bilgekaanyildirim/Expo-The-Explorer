using System;
using ExpoTheExplorer.Core;
using ExpoTheExplorer.Data;
using ExpoTheExplorer.Systems.KeySystem;
using ExpoTheExplorer.Systems.ProgressionSystem;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace ExpoTheExplorer.Tests.EditMode
{
    // The key economy's rules are all TIME rules, and time is the one thing a
    // play-test cannot fast-forward -- which is why KeyManager takes its clock as a
    // Func and why this file exists. Every case below moves `now` by hand.
    //
    // The cases are grouped by the question they answer: does time pay out, does a
    // full bar stop banking it, does the remainder survive, can the clock be gamed,
    // does a purchase ever take money without giving keys, and does a save round-trip
    // through ApplyPersisted mean what the store thinks it means.
    public class KeySystemTests
    {
        // A fixed instant rather than DateTime.UtcNow: a test whose result depends on
        // when it runs is a test that fails at midnight.
        private static readonly DateTime Start = new(2026, 8, 25, 12, 0, 0, DateTimeKind.Utc);

        private const int Cap = 5;
        private const int RegenMinutes = 30;
        private const int RefillCost = 40;

        private GameConfig gameConfig;
        private KeyConfig keyConfig;
        private GameState state;
        private Wallet wallet;
        private DateTime now;

        [SetUp]
        public void SetUp()
        {
            gameConfig = ScriptableObject.CreateInstance<GameConfig>();
            keyConfig = CreateKeyConfig(Cap, RegenMinutes, RefillCost);
            state = new GameState(gameConfig);
            wallet = new Wallet(state);
            now = Start;
        }

        [TearDown]
        public void TearDown()
        {
            UnityEngine.Object.DestroyImmediate(gameConfig);
            UnityEngine.Object.DestroyImmediate(keyConfig);
        }

        // The config's fields are private [SerializeField]s, so they are set the same
        // way LivesSystemTests sets its own -- through SerializedObject, which means
        // the property-name strings here must stay in step with the field names.
        private static KeyConfig CreateKeyConfig(int maxKeys, int regenMinutes, int refillGemCost)
        {
            var config = ScriptableObject.CreateInstance<KeyConfig>();
            var serialized = new SerializedObject(config);
            serialized.FindProperty("maxKeys").intValue = maxKeys;
            serialized.FindProperty("regenMinutes").intValue = regenMinutes;
            serialized.FindProperty("refillGemCost").intValue = refillGemCost;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            return config;
        }

        // The clock is read through a closure over `now`, so advancing time is a plain
        // assignment and every manager built in a test shares one timeline.
        private KeyManager NewManager() => new(keyConfig, wallet, () => now);

        private void Advance(TimeSpan by) => now += by;

        private static TimeSpan Intervals(double count) => TimeSpan.FromMinutes(RegenMinutes * count);

        // Drains the bar to `target` without leaning on regen, so a test can set up a
        // starting count without asserting anything about time on the way there.
        private static void SpendDownTo(KeyManager manager, int target)
        {
            while (manager.Keys > target) Assert.IsTrue(manager.TrySpendKey(), "setup spend must succeed");
        }

        // --- opening state ----------------------------------------------------------

        // Keys are permission to play, so a brand-new player gets all of them. Anything
        // else ships someone a game they have to wait to start.
        [Test]
        public void NewPlayer_StartsFull()
        {
            var manager = NewManager();

            Assert.AreEqual(Cap, manager.Keys);
            Assert.AreEqual(Cap, manager.MaxKeys);
            Assert.IsTrue(manager.IsFull);
            Assert.IsTrue(manager.HasKey);
        }

        // --- regen ------------------------------------------------------------------

        [Test]
        public void Refresh_BeforeAFullInterval_GrantsNothing()
        {
            var manager = NewManager();
            SpendDownTo(manager, 0);

            Advance(Intervals(0.99));
            manager.Refresh();

            Assert.AreEqual(0, manager.Keys, "a key is earned on the whole interval, not proportionally");
            Assert.IsFalse(manager.HasKey);
        }

        [Test]
        public void Refresh_AfterOneInterval_GrantsExactlyOne()
        {
            var manager = NewManager();
            SpendDownTo(manager, 0);

            Advance(Intervals(1));
            manager.Refresh();

            Assert.AreEqual(1, manager.Keys);
        }

        [Test]
        public void Refresh_AfterSeveralIntervals_GrantsThatMany()
        {
            var manager = NewManager();
            SpendDownTo(manager, 0);

            Advance(Intervals(3));
            manager.Refresh();

            Assert.AreEqual(3, manager.Keys);
        }

        // The case a naive implementation gets wrong: advancing the anchor to `now`
        // instead of by whole intervals silently robs the player of up to a full
        // interval every time anything happens to call Refresh.
        [Test]
        public void Refresh_CarriesTheUnfinishedRemainder()
        {
            var manager = NewManager();
            SpendDownTo(manager, 0);

            Advance(Intervals(1.5));
            manager.Refresh();
            Assert.AreEqual(1, manager.Keys, "one whole interval elapsed, plus half of the next");

            // Only half an interval more -- which completes the one already in flight.
            Advance(Intervals(0.5));
            manager.Refresh();

            Assert.AreEqual(2, manager.Keys, "the half interval already banked must count toward the next key");
        }

        [Test]
        public void Refresh_NeverGrantsPastTheCap()
        {
            var manager = NewManager();
            SpendDownTo(manager, 0);

            Advance(Intervals(100));
            manager.Refresh();

            Assert.AreEqual(Cap, manager.Keys);
        }

        // THE case that makes a full bar mean something. Sitting at the cap for hours
        // must not bank those hours: spending a key afterwards has to start a fresh
        // wait, not hand the key straight back.
        [Test]
        public void Refresh_WhileFull_DoesNotBankTime()
        {
            var manager = NewManager();

            Advance(TimeSpan.FromHours(10));
            manager.Refresh();
            Assert.AreEqual(Cap, manager.Keys, "still full, nothing to grant");

            Assert.IsTrue(manager.TrySpendKey());
            Assert.AreEqual(Cap - 1, manager.Keys);

            // A single second later the banked hours would have paid this back instantly.
            Advance(TimeSpan.FromSeconds(1));
            manager.Refresh();

            Assert.AreEqual(Cap - 1, manager.Keys, "the 10 idle hours must not refund the key that was just spent");
        }

        // The same rule, but with no Refresh call in between -- the anchor is only ever
        // dragged forward from inside Refresh, so this proves the drag also happens on
        // the Refresh that TrySpendKey does for itself. Correctness must not depend on
        // anyone having ticked in the meantime.
        [Test]
        public void SpendingAfterALongIdleFullPeriod_StartsAFreshWait_WithNoTicksInBetween()
        {
            var manager = NewManager();

            Advance(TimeSpan.FromDays(3));
            Assert.IsTrue(manager.TrySpendKey());

            Advance(Intervals(0.9));
            manager.Refresh();
            Assert.AreEqual(Cap - 1, manager.Keys, "the wait started when the key was spent, not three days ago");

            Advance(Intervals(0.1));
            manager.Refresh();
            Assert.AreEqual(Cap, manager.Keys);
        }

        // --- clock tampering ---------------------------------------------------------

        // Setting the device clock back must not pay out, and must not punish either:
        // refusing to move the anchor would leave the player owed nothing until real
        // time caught up, which for a clock set months back is effectively forever.
        [Test]
        public void Refresh_WhenTheClockGoesBackwards_GrantsNothing_AndDoesNotStall()
        {
            var manager = NewManager();
            SpendDownTo(manager, 0);

            now -= TimeSpan.FromDays(30);
            manager.Refresh();
            Assert.AreEqual(0, manager.Keys, "moving the clock back must not mint keys");

            // And from the new "now", a normal interval still works.
            Advance(Intervals(1));
            manager.Refresh();

            Assert.AreEqual(1, manager.Keys, "the player must not be stalled until real time catches up");
        }

        // --- spending ----------------------------------------------------------------

        [Test]
        public void TrySpendKey_AtZero_ReturnsFalse_AndStaysAtZero()
        {
            var manager = NewManager();
            SpendDownTo(manager, 0);

            Assert.IsFalse(manager.TrySpendKey());
            Assert.AreEqual(0, manager.Keys, "a key count must never go negative");
        }

        [Test]
        public void TrySpendKey_PublishesTheNewCount()
        {
            var manager = NewManager();
            var published = new System.Collections.Generic.List<int>();
            manager.KeysChanged.Subscribe(published.Add);

            manager.TrySpendKey();

            Assert.AreEqual(new[] { Cap - 1 }, published);
        }

        [Test]
        public void TrySpendKey_AtZero_PublishesNothing()
        {
            var manager = NewManager();
            SpendDownTo(manager, 0);

            var published = 0;
            manager.KeysChanged.Subscribe(_ => published++);

            manager.TrySpendKey();

            Assert.AreEqual(0, published, "a refused spend is not a change");
        }

        // --- the Gem refill ----------------------------------------------------------

        [Test]
        public void TryRefillWithGems_WithEnoughGems_FillsToTheCapAndCharges()
        {
            state.Gems = 100;
            var manager = NewManager();
            SpendDownTo(manager, 0);

            Assert.IsTrue(manager.TryRefillWithGems());

            Assert.AreEqual(Cap, manager.Keys);
            Assert.AreEqual(100 - RefillCost, state.Gems);
        }

        // Fills TO the cap rather than adding a fixed amount, so the cap is never
        // exceeded and there is one rule instead of two.
        [Test]
        public void TryRefillWithGems_FromAPartialBar_StopsAtTheCap()
        {
            state.Gems = 100;
            var manager = NewManager();
            SpendDownTo(manager, Cap - 2);

            Assert.IsTrue(manager.TryRefillWithGems());

            Assert.AreEqual(Cap, manager.Keys, "buying must never overfill");
        }

        // The guard that protects the player's Gems: no path spends without granting.
        [Test]
        public void TryRefillWithGems_WithoutEnoughGems_ChangesNothing()
        {
            state.Gems = RefillCost - 1;
            var manager = NewManager();
            SpendDownTo(manager, 0);

            Assert.IsFalse(manager.TryRefillWithGems());

            Assert.AreEqual(0, manager.Keys);
            Assert.AreEqual(RefillCost - 1, state.Gems, "a refused purchase must not take a single gem");
        }

        // The other half of that guard, and the one that would be theft: a player at
        // the cap has nothing to buy, so they must not be charged for it.
        [Test]
        public void TryRefillWithGems_WhenAlreadyFull_TakesNoGems()
        {
            state.Gems = 100;
            var manager = NewManager();

            Assert.IsFalse(manager.TryRefillWithGems());

            Assert.AreEqual(100, state.Gems);
            Assert.AreEqual(Cap, manager.Keys);
        }

        [Test]
        public void TryRefillWithGems_RestartsTheClock()
        {
            state.Gems = 100;
            var manager = NewManager();
            SpendDownTo(manager, 0);

            Advance(Intervals(0.9)); // Nearly a key's worth of progress, then buy.
            Assert.IsTrue(manager.TryRefillWithGems());

            Assert.IsTrue(manager.TrySpendKey());
            Advance(Intervals(0.2));
            manager.Refresh();

            Assert.AreEqual(Cap - 1, manager.Keys, "the pre-purchase progress must not survive a refill to full");
        }

        // --- what a save round-trip means --------------------------------------------

        // -1 is the marker PlayerProfileStore writes for a save that predates keys. It
        // exists because 0 is NOT a safe default -- 0 keys is a locked-out player --
        // and the store has no config with which to fill in the cap itself.
        [Test]
        public void ApplyPersisted_WithTheAbsentMarker_TreatsThePlayerAsFull()
        {
            var manager = NewManager();
            SpendDownTo(manager, 0);

            manager.ApplyPersisted(-1, 0);

            Assert.AreEqual(Cap, manager.Keys, "a save from before keys existed must not lock the player out");
        }

        // The whole reason an ANCHOR is persisted rather than a countdown: time passes
        // while the game is closed.
        [Test]
        public void ApplyPersisted_WithAnOldAnchor_PaysOutTheOfflineTime()
        {
            var manager = NewManager();
            var closedAt = now;

            Advance(Intervals(2));
            manager.ApplyPersisted(0, closedAt.Ticks);

            Assert.AreEqual(2, manager.Keys);
        }

        [Test]
        public void ApplyPersisted_WithOfflineTimePastTheCap_StopsAtTheCap()
        {
            var manager = NewManager();
            var closedAt = now;

            Advance(TimeSpan.FromDays(7));
            manager.ApplyPersisted(0, closedAt.Ticks);

            Assert.AreEqual(Cap, manager.Keys);
        }

        // A zero anchor means "this file has no timestamp" -- which is what lets
        // PlayerProfileStore stay free of System.DateTime: it never has to invent one.
        [Test]
        public void ApplyPersisted_WithNoAnchor_StartsTheClockNow()
        {
            var manager = NewManager();

            manager.ApplyPersisted(0, 0);
            Assert.AreEqual(0, manager.Keys);

            Advance(Intervals(0.99));
            manager.Refresh();
            Assert.AreEqual(0, manager.Keys, "the clock must start at load, not at the epoch");

            Advance(Intervals(0.01));
            manager.Refresh();
            Assert.AreEqual(1, manager.Keys);
        }

        // A corrupt or hand-edited file can carry anything, and `new DateTime(ticks)`
        // THROWS rather than clamping -- which would take down the whole session load
        // over a field with a perfectly good fallback.
        [Test]
        public void ApplyPersisted_WithNonsenseAnchor_FallsBackInsteadOfThrowing()
        {
            var manager = NewManager();

            Assert.DoesNotThrow(() => manager.ApplyPersisted(2, long.MaxValue));
            Assert.DoesNotThrow(() => manager.ApplyPersisted(2, -99));

            Assert.AreEqual(2, manager.Keys);
        }

        [Test]
        public void ApplyPersisted_ClampsASavedCountAboveTheCap()
        {
            var manager = NewManager();

            manager.ApplyPersisted(Cap + 10, now.Ticks);

            Assert.AreEqual(Cap, manager.Keys, "a hand-edited file must not draw more keys than the game admits exist");
        }

        // --- the countdown the popup renders ------------------------------------------

        [Test]
        public void SecondsUntilNextKey_WhenFull_IsZero()
        {
            var manager = NewManager();

            Assert.AreEqual(0, manager.SecondsUntilNextKey());
        }

        [Test]
        public void SecondsUntilNextKey_CountsDownTowardTheNextGrant()
        {
            var manager = NewManager();
            SpendDownTo(manager, 0);

            Assert.AreEqual(RegenMinutes * 60, manager.SecondsUntilNextKey());

            Advance(TimeSpan.FromMinutes(10));

            Assert.AreEqual((RegenMinutes - 10) * 60, manager.SecondsUntilNextKey());
        }

        // The persisted anchor must be the one the manager is actually counting from,
        // or a save/reload would move the goalposts.
        [Test]
        public void LastRegenUtcTicks_RoundTripsThroughApplyPersisted()
        {
            var manager = NewManager();
            SpendDownTo(manager, 0);
            Advance(Intervals(0.5));
            manager.Refresh();

            var saved = manager.LastRegenUtcTicks;
            var savedKeys = manager.Keys;

            var reloaded = NewManager();
            reloaded.ApplyPersisted(savedKeys, saved);

            Assert.AreEqual(manager.SecondsUntilNextKey(), reloaded.SecondsUntilNextKey(),
                "reloading must not restart the interval already in flight");
        }
    }
}
