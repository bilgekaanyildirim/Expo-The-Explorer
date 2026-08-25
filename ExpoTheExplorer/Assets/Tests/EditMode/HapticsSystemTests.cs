using ExpoTheExplorer.Data;
using ExpoTheExplorer.Systems.HapticsSystem;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace ExpoTheExplorer.Tests.EditMode
{
    // Covers the one rule this system actually owns: which of the moments that land in
    // a single frame is the one the player feels. Nothing here touches Nice Vibrations
    // or a device -- playback lives in HapticsBinder precisely so this half stays
    // reachable from an EditMode run.
    public class HapticsSystemTests
    {
        private HapticConfig config;

        [SetUp]
        public void SetUp() => config = ScriptableObject.CreateInstance<HapticConfig>();

        [TearDown]
        public void TearDown() => Object.DestroyImmediate(config);

        // Replaces whatever Reset seeded, so a test states its own table and cannot be
        // broken by a retune of the shipped defaults.
        private void SetEntries(params (HapticMoment Moment, HapticPreset Preset, int Priority, bool Enabled)[] rows)
        {
            var serialized = new SerializedObject(config);
            var entries = serialized.FindProperty("entries");
            entries.arraySize = rows.Length;

            for (var i = 0; i < rows.Length; i++)
            {
                var entry = entries.GetArrayElementAtIndex(i);
                entry.FindPropertyRelative("moment").enumValueIndex = (int)rows[i].Moment;
                entry.FindPropertyRelative("preset").enumValueIndex = (int)rows[i].Preset;
                entry.FindPropertyRelative("priority").intValue = rows[i].Priority;
                entry.FindPropertyRelative("enabled").boolValue = rows[i].Enabled;
            }

            serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        [Test]
        public void AResolvedMoment_IsTakenOnce_AndTheQueueIsThenEmpty()
        {
            SetEntries((HapticMoment.ItemPickup, HapticPreset.LightImpact, 10, true));
            var service = new HapticsService(config);

            service.Request(HapticMoment.ItemPickup);

            Assert.IsTrue(service.TryTakePending(out var preset));
            Assert.AreEqual(HapticPreset.LightImpact, preset);
            Assert.IsFalse(service.TryTakePending(out _), "Draining the queue must leave it empty.");
        }

        [Test]
        public void NothingRequested_TakesNothing()
        {
            SetEntries((HapticMoment.ItemPickup, HapticPreset.LightImpact, 10, true));
            var service = new HapticsService(config);

            Assert.IsFalse(service.TryTakePending(out _));
        }

        // The cascade this whole class exists for: a drop that completes an order queues
        // ItemDroppedInTray and then OrderDelivered in the same frame. The player must
        // feel the delivery, not the drop that caused it.
        [Test]
        public void WithinOneFrame_TheHighestPriorityWins_WhicheverArrivedFirst()
        {
            SetEntries(
                (HapticMoment.ItemDroppedInTray, HapticPreset.MediumImpact, 20, true),
                (HapticMoment.OrderDelivered, HapticPreset.Success, 40, true));

            var lowFirst = new HapticsService(config);
            lowFirst.Request(HapticMoment.ItemDroppedInTray);
            lowFirst.Request(HapticMoment.OrderDelivered);
            Assert.IsTrue(lowFirst.TryTakePending(out var afterLowFirst));
            Assert.AreEqual(HapticPreset.Success, afterLowFirst);

            var highFirst = new HapticsService(config);
            highFirst.Request(HapticMoment.OrderDelivered);
            highFirst.Request(HapticMoment.ItemDroppedInTray);
            Assert.IsTrue(highFirst.TryTakePending(out var afterHighFirst));
            Assert.AreEqual(HapticPreset.Success, afterHighFirst,
                "Arrival order must not decide the winner -- a lower priority arriving second cannot displace it.");
        }

        // LivesManager publishes LivesChanged and then LivesDepleted for the last life,
        // so the losing-the-game buzz has to survive the losing-a-life one.
        [Test]
        public void TheLastLife_IsFeltAsGameOver_NotAsALifeLost()
        {
            SetEntries(
                (HapticMoment.LifeLost, HapticPreset.Failure, 60, true),
                (HapticMoment.GameOver, HapticPreset.HeavyImpact, 80, true));

            var service = new HapticsService(config);
            service.Request(HapticMoment.LifeLost);
            service.Request(HapticMoment.GameOver);

            Assert.IsTrue(service.TryTakePending(out var preset));
            Assert.AreEqual(HapticPreset.HeavyImpact, preset);
        }

        [Test]
        public void EqualPriority_GoesToWhoeverAskedFirst()
        {
            SetEntries(
                (HapticMoment.OrderDelivered, HapticPreset.Success, 50, true),
                (HapticMoment.StarSeated, HapticPreset.MediumImpact, 50, true));

            var service = new HapticsService(config);
            service.Request(HapticMoment.OrderDelivered);
            service.Request(HapticMoment.StarSeated);

            Assert.IsTrue(service.TryTakePending(out var preset));
            Assert.AreEqual(HapticPreset.Success, preset);
        }

        // Each frame is judged on its own: a heavy moment does not keep outranking the
        // next frame's lighter ones.
        [Test]
        public void APreviousFramesWinner_DoesNotOutrankTheNextFrame()
        {
            SetEntries(
                (HapticMoment.GameOver, HapticPreset.HeavyImpact, 80, true),
                (HapticMoment.ItemPickup, HapticPreset.LightImpact, 10, true));

            var service = new HapticsService(config);
            service.Request(HapticMoment.GameOver);
            service.TryTakePending(out _);

            service.Request(HapticMoment.ItemPickup);

            Assert.IsTrue(service.TryTakePending(out var preset));
            Assert.AreEqual(HapticPreset.LightImpact, preset);
        }

        [Test]
        public void ADisabledMoment_IsSilent_AndDoesNotBlockAnotherMoment()
        {
            SetEntries(
                (HapticMoment.ItemPickup, HapticPreset.HeavyImpact, 90, false),
                (HapticMoment.OrderDelivered, HapticPreset.Success, 40, true));

            var service = new HapticsService(config);
            service.Request(HapticMoment.ItemPickup);
            service.Request(HapticMoment.OrderDelivered);

            Assert.IsTrue(service.TryTakePending(out var preset));
            Assert.AreEqual(HapticPreset.Success, preset,
                "A silenced row must not reserve the frame with its priority.");
        }

        // None is the second way to silence a row, and it has to behave exactly like
        // unticking Enabled -- otherwise an author picking it from the dropdown would get
        // a row that blocks the frame while playing nothing.
        [Test]
        public void ANonePreset_IsSilent_LikeADisabledRow()
        {
            SetEntries(
                (HapticMoment.ItemPickup, HapticPreset.None, 90, true),
                (HapticMoment.OrderDelivered, HapticPreset.Success, 40, true));

            var service = new HapticsService(config);
            service.Request(HapticMoment.ItemPickup);
            service.Request(HapticMoment.OrderDelivered);

            Assert.IsTrue(service.TryTakePending(out var preset));
            Assert.AreEqual(HapticPreset.Success, preset);
        }

        [Test]
        public void AMomentMissingFromTheTable_IsSilent()
        {
            SetEntries((HapticMoment.OrderDelivered, HapticPreset.Success, 40, true));
            var service = new HapticsService(config);

            service.Request(HapticMoment.PropLanded);

            Assert.IsFalse(service.TryTakePending(out _));
        }

        // An unwired config field is a wiring mistake, and the answer to it is a player
        // who feels nothing rather than a frame that throws.
        [Test]
        public void WithNoConfigAtAll_RequestsAreDropped_NotThrown()
        {
            var service = new HapticsService(null);

            Assert.DoesNotThrow(() => service.Request(HapticMoment.GameOver));
            Assert.IsFalse(service.TryTakePending(out _));
        }

        // The shipped table is content and will be retuned, so this pins the one
        // property the CODE depends on rather than any particular preset: every moment
        // the enum offers has a row, because a missing one is silent and silence is very
        // hard to notice.
        [Test]
        public void TheDefaultTable_CoversEveryMoment()
        {
            var seeded = ScriptableObject.CreateInstance<HapticConfig>();

            try
            {
                foreach (HapticMoment moment in System.Enum.GetValues(typeof(HapticMoment)))
                {
                    Assert.IsTrue(seeded.TryResolve(moment, out _, out _),
                        $"{moment} has no row in the default table, so it would never be felt.");
                }
            }
            finally
            {
                Object.DestroyImmediate(seeded);
            }
        }
    }
}
