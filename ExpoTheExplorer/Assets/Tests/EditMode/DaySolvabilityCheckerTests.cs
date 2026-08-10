using System.Collections.Generic;
using ExpoTheExplorer.Core;
using ExpoTheExplorer.Data;
using ExpoTheExplorer.Systems.DaySystem;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace ExpoTheExplorer.Tests.EditMode
{
    public class DaySolvabilityCheckerTests
    {
        private readonly List<UnityEngine.Object> spawned = new();

        private FoodItemConfig main;
        private FoodItemConfig side;
        private ModificationConfig extraCheese;

        [SetUp]
        public void SetUp()
        {
            main = CreateFoodItem("main");
            side = CreateFoodItem("side");
            extraCheese = CreateModification("extra_cheese");
        }

        [TearDown]
        public void TearDown()
        {
            foreach (var o in spawned) UnityEngine.Object.DestroyImmediate(o);
            spawned.Clear();
        }

        private ResolvedTicketEntry CreateEntry(FoodItemConfig mainItem, FoodItemConfig sideItem = null, params Modification[] mods)
        {
            return new ResolvedTicketEntry(mainItem, sideItem, null, mods, PatienceType.Normal, null, 0f);
        }

        private static ResolvedBoardSpawnEntry CreateSpawn(int triggerStepIndex, FoodItemConfig item, params Modification[] mods)
        {
            return new ResolvedBoardSpawnEntry(triggerStepIndex, item, mods, useExactCell: true, x: 0, y: 0);
        }

        [Test]
        public void FindShortfalls_SufficientSupply_ReturnsEmpty()
        {
            var ticketSequence = new List<ResolvedTicketEntry> { CreateEntry(main), CreateEntry(main), CreateEntry(main) };
            var boardTimeline = new List<ResolvedBoardSpawnEntry> { CreateSpawn(0, main), CreateSpawn(1, main), CreateSpawn(2, main) };

            var shortfalls = DaySolvabilityChecker.FindShortfalls(ticketSequence, boardTimeline);

            CollectionAssert.IsEmpty(shortfalls);
        }

        // Mirrors the exact class of bug found in the field: 5 tickets need a plain (no
        // modification) side item, but only 3 units of it are ever spawned across the whole
        // Day. The first 3 demanders (in arrival order) are covered by whatever's already on
        // the board; the last 2 (ranked 3rd and 4th among same-key demanders) never get their
        // own copy since nothing replenishes a fungible item once "presentCount" already
        // satisfies the per-call guarantee check.
        [Test]
        public void FindShortfalls_FungibleUndersupply_FlagsLaterDemandersOnly()
        {
            var ticketSequence = new List<ResolvedTicketEntry>();
            for (var i = 0; i < 5; i++)
            {
                ticketSequence.Add(CreateEntry(main, side));
            }

            var boardTimeline = new List<ResolvedBoardSpawnEntry>
            {
                CreateSpawn(0, main), CreateSpawn(0, side),
                CreateSpawn(1, main), CreateSpawn(1, side),
                CreateSpawn(2, main), CreateSpawn(2, side),
                CreateSpawn(3, main),
                CreateSpawn(4, main),
            };

            var shortfalls = DaySolvabilityChecker.FindShortfalls(ticketSequence, boardTimeline);

            var flaggedTicketIndices = new List<int>();
            foreach (var shortfall in shortfalls)
            {
                Assert.AreSame(side, shortfall.MissingKey.Food);
                flaggedTicketIndices.Add(shortfall.TicketIndex);
            }
            flaggedTicketIndices.Sort();
            CollectionAssert.AreEqual(new[] { 3, 4 }, flaggedTicketIndices);
        }

        [Test]
        public void FindShortfalls_SupplyExactlyAtDeadline_StillCounts()
        {
            var ticketSequence = new List<ResolvedTicketEntry> { CreateEntry(main) };
            var boardTimeline = new List<ResolvedBoardSpawnEntry> { CreateSpawn(0, main) }; // deadline for ticket 0 (N=1) is step 0

            var shortfalls = DaySolvabilityChecker.FindShortfalls(ticketSequence, boardTimeline);

            CollectionAssert.IsEmpty(shortfalls);
        }

        [Test]
        public void FindShortfalls_ModifiedMainItemCollision_AlsoDetected()
        {
            var cheesyMod = new Modification(extraCheese, isAddition: true);
            var ticketSequence = new List<ResolvedTicketEntry> { CreateEntry(main, null, cheesyMod), CreateEntry(main, null, cheesyMod) };
            var boardTimeline = new List<ResolvedBoardSpawnEntry> { CreateSpawn(0, main, cheesyMod) }; // only one copy ever spawned

            var shortfalls = DaySolvabilityChecker.FindShortfalls(ticketSequence, boardTimeline);

            Assert.AreEqual(1, shortfalls.Count);
            Assert.AreEqual(1, shortfalls[0].TicketIndex);
            Assert.AreSame(main, shortfalls[0].MissingKey.Food);
        }

        private FoodItemConfig CreateFoodItem(string id)
        {
            var item = ScriptableObject.CreateInstance<FoodItemConfig>();
            spawned.Add(item);

            var serialized = new SerializedObject(item);
            serialized.FindProperty("id").stringValue = id;
            serialized.ApplyModifiedPropertiesWithoutUndo();

            return item;
        }

        private ModificationConfig CreateModification(string id)
        {
            var mod = ScriptableObject.CreateInstance<ModificationConfig>();
            spawned.Add(mod);

            var serialized = new SerializedObject(mod);
            serialized.FindProperty("id").stringValue = id;
            serialized.ApplyModifiedPropertiesWithoutUndo();

            return mod;
        }
    }
}
