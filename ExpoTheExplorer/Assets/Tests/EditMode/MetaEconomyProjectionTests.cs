using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using ExpoTheExplorer.Core;
using ExpoTheExplorer.Data;
using ExpoTheExplorer.Editor;
using ExpoTheExplorer.Systems.DaySystem;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace ExpoTheExplorer.Tests.EditMode
{
    // The arithmetic behind the Meta Editor's economy panel. Everything here is about
    // whether a number an author will set prices against is the number the game actually
    // pays -- so the tests are written against payouts and rounding, not against the GUI.
    //
    // The tip rate is 0.05 (the shipped value) and the prices are chosen so no product ever
    // lands on a .5: Mathf.RoundToInt rounds halves to even, and a test that depends on
    // which way a half went would be testing float arithmetic rather than the rule.
    public class MetaEconomyProjectionTests
    {
        // UnityEngine.Object spelled out: this file needs `using System` for Guid, which
        // makes a bare `Object` ambiguous against System.Object.
        private readonly List<UnityEngine.Object> spawned = new();
        private string tempFolder;

        [SetUp]
        public void SetUp()
        {
            tempFolder = Path.Combine(Path.GetTempPath(), "MetaEconomyProjectionTests_" + Guid.NewGuid());
        }

        [TearDown]
        public void TearDown()
        {
            foreach (var asset in spawned) UnityEngine.Object.DestroyImmediate(asset);
            spawned.Clear();

            if (Directory.Exists(tempFolder)) Directory.Delete(tempFolder, recursive: true);
        }

        // --- fixtures ---------------------------------------------------------------

        private FoodItemConfig Food(string id, int basePrice)
        {
            var food = ScriptableObject.CreateInstance<FoodItemConfig>();
            spawned.Add(food);

            var serialized = new SerializedObject(food);
            serialized.FindProperty("id").stringValue = id;
            serialized.FindProperty("basePrice").intValue = basePrice;
            serialized.ApplyModifiedPropertiesWithoutUndo();

            return food;
        }

        private FoodCatalog Catalog(params FoodItemConfig[] items)
        {
            var catalog = ScriptableObject.CreateInstance<FoodCatalog>();
            spawned.Add(catalog);

            typeof(FoodCatalog)
                .GetField("items", BindingFlags.Instance | BindingFlags.NonPublic)
                .SetValue(catalog, items.ToList());

            return catalog;
        }

        private EconomyConfig Economy(float tipRateCritical = 0.05f)
        {
            var config = ScriptableObject.CreateInstance<EconomyConfig>();
            spawned.Add(config);

            var serialized = new SerializedObject(config);
            serialized.FindProperty("tipRateCritical").floatValue = tipRateCritical;
            serialized.ApplyModifiedPropertiesWithoutUndo();

            return config;
        }

        private GameConfig Game(int startingSoftMoney)
        {
            var config = ScriptableObject.CreateInstance<GameConfig>();
            spawned.Add(config);

            var serialized = new SerializedObject(config);
            serialized.FindProperty("startingSoftMoney").intValue = startingSoftMoney;
            serialized.ApplyModifiedPropertiesWithoutUndo();

            return config;
        }

        private void SaveDay(int dayIndex, params string[] mainItemIds)
        {
            DayFileIO.Save(tempFolder, new DayJson
            {
                runtime = new DayRuntimeJson
                {
                    dayIndex = dayIndex,
                    ticketsRequiredForDay = mainItemIds.Length,
                    ticketSequence = mainItemIds
                        .Select(id => new TicketEntryJson { mainItemId = id, sideItemId = "", drinkItemId = "" })
                        .ToArray(),
                    boardTimeline = Array.Empty<BoardSpawnEntryJson>(),
                },
                editorMeta = new DayEditorMetaJson(),
            });
        }

        private MetaEconomyProjection Build(FoodCatalog foodCatalog, int startingSoftMoney = 0, float tipRate = 0.05f) =>
            MetaEconomyProjection.Build(tempFolder, foodCatalog, Economy(tipRate), Game(startingSoftMoney));

        // --- the payout floor -------------------------------------------------------

        [Test]
        public void CleanFloor_IsEveryTicketAtTheCriticalTip()
        {
            // 100 -> 105, 40 -> 42. Neither product is a half, so the rounding rule is not
            // what is under test here; the formula is.
            SaveDay(0, "burger", "fries");

            var projection = Build(Catalog(Food("burger", 100), Food("fries", 40)));

            Assert.AreEqual(147, projection.Days[0].CleanFloor);
        }

        [Test]
        public void CleanFloor_RoundsPerDelivery_NotOnceOverTheDay()
        {
            // 26 x 1.05 = 27.3 -> 27 each, so the day pays 54. Summing first and rounding
            // once would give round(52 x 1.05) = round(54.6) = 55 -- which is a coin the
            // player's wallet never sees, because DayLifecycleManager rounds each delivery.
            SaveDay(0, "snack", "snack");

            var projection = Build(Catalog(Food("snack", 26)));

            Assert.AreEqual(54, projection.Days[0].CleanFloor);
        }

        [Test]
        public void OrderValue_IsSummedBasePrices_UntouchedByTheTip()
        {
            SaveDay(0, "burger", "fries");

            var projection = Build(Catalog(Food("burger", 100), Food("fries", 40)));

            Assert.AreEqual(140, projection.Days[0].OrderValue);
        }

        // --- the loss model ---------------------------------------------------------

        [Test]
        public void FloorWith_DropsThePriciestTickets_NotTheFirstOnes()
        {
            // Authored cheap-first on purpose: a projection that dropped by sequence
            // position would answer 105 + 42 = 147 - 42 = 105 here. The worst case is that
            // the BURGER expired, which leaves 42.
            SaveDay(0, "fries", "burger");

            var projection = Build(Catalog(Food("burger", 100), Food("fries", 40)));

            Assert.AreEqual(42, projection.Days[0].FloorWith(1));
        }

        [Test]
        public void FloorWith_MoreLossesThanTickets_IsZeroRatherThanNegative()
        {
            SaveDay(0, "burger");

            var projection = Build(Catalog(Food("burger", 100)));

            Assert.AreEqual(0, projection.Days[0].FloorWith(5));
        }

        [Test]
        public void MaxAbsorbableLosses_IsEveryLifeButTheLast()
        {
            // Losing the last life ends the day in the Continue popup rather than in a
            // completion, so it is not a loss this projection may assume for free.
            Assert.AreEqual(GameState.DefaultStartingLives - 1, MetaEconomyProjection.MaxAbsorbableLosses);
        }

        // --- accumulation across days -----------------------------------------------

        [Test]
        public void MinimumBeforeDay_ExcludesThatDayAndIncludesTheGrant()
        {
            SaveDay(0, "burger"); // 105
            SaveDay(1, "burger"); // 105
            SaveDay(2, "burger");

            var projection = Build(Catalog(Food("burger", 100)), startingSoftMoney: 1000);

            // A prop authored at Day 2 appears BEFORE the third day is played, so its budget
            // is the grant plus days 0 and 1 -- never day 2's own income.
            Assert.AreEqual(1210, projection.MinimumBeforeDay(2, 0));
        }

        [Test]
        public void MinimumBeforeDay_DayZero_IsTheGrantAlone()
        {
            SaveDay(0, "burger");

            var projection = Build(Catalog(Food("burger", 100)), startingSoftMoney: 1000);

            Assert.AreEqual(1000, projection.MinimumBeforeDay(0, 0));
        }

        [Test]
        public void MinimumBeforeDay_AppliesTheLossModelToEveryDay()
        {
            SaveDay(0, "fries", "burger");
            SaveDay(1, "fries", "burger");

            var projection = Build(Catalog(Food("burger", 100), Food("fries", 40)), startingSoftMoney: 1000);

            // Each day loses its burger and keeps its fries: 1000 + 42 + 42.
            Assert.AreEqual(1084, projection.MinimumBeforeDay(2, 1));
        }

        [Test]
        public void FloorBetween_IsInclusiveAtBothEnds()
        {
            SaveDay(0, "burger");
            SaveDay(1, "burger");
            SaveDay(2, "burger");

            var projection = Build(Catalog(Food("burger", 100)), startingSoftMoney: 1000);

            Assert.AreEqual(210, projection.FloorBetween(1, 2, 0));
        }

        // --- content problems -------------------------------------------------------

        [Test]
        public void UnresolvableItem_ContributesNothingAndIsCounted()
        {
            SaveDay(0, "ghost");

            var projection = Build(Catalog(Food("burger", 100)));

            Assert.AreEqual(0, projection.Days[0].CleanFloor);
            Assert.AreEqual(1, projection.UnresolvedItems);
        }

        [Test]
        public void ZeroPricedItem_IsCountedSoTheTotalCanBeDistrusted()
        {
            SaveDay(0, "garnish");

            var projection = Build(Catalog(Food("garnish", 0)));

            Assert.AreEqual(1, projection.UnpricedItems);
        }

        [Test]
        public void NoDayFiles_ReportsAProblemRatherThanAZeroFloor()
        {
            var projection = Build(Catalog(Food("burger", 100)));

            Assert.IsTrue(projection.IsEmpty);
            Assert.IsNotNull(projection.Problem, "An empty catalog must say so -- a silent 0 reads as 'the days pay nothing'.");
        }

        [Test]
        public void EmptyTicketSequence_IsZeroRatherThanAThrow()
        {
            SaveDay(0);

            var projection = Build(Catalog(Food("burger", 100)));

            Assert.AreEqual(0, projection.Days[0].TicketCount);
            Assert.AreEqual(0, projection.Days[0].CleanFloor);
        }

        // --- what the props cost ----------------------------------------------------

        [Test]
        public void LocationCost_SumsPurchasePricesAndExcludesDayUnlocks()
        {
            var location = new MetaLocation("Meta1", 0, new[]
            {
                new MetaItemDefinition("stand", MetaUnlockKind.Purchase, price: 500),
                new MetaItemDefinition("fountain", MetaUnlockKind.Purchase, price: 250),
                new MetaItemDefinition("banner", MetaUnlockKind.DayUnlock, unlockAtDayIndex: 4),
            });

            var cost = MetaLocationCost.Of(location);

            Assert.AreEqual(750, cost.PurchaseTotal);
            Assert.AreEqual(2, cost.PurchaseCount);
            Assert.AreEqual(1, cost.DayUnlockCount);
        }

        [Test]
        public void LocationCost_CountsFreePurchaseProps_SinceZeroIsInvisibleInATotal()
        {
            var location = new MetaLocation("Meta1", 0, new[]
            {
                new MetaItemDefinition("stand", MetaUnlockKind.Purchase, price: 500),
                new MetaItemDefinition("oops", MetaUnlockKind.Purchase, price: 0),
            });

            Assert.AreEqual(1, MetaLocationCost.Of(location).FreeCount);
        }

        [Test]
        public void CatalogCost_ThroughIndex_IsCumulativeNotJustThatLocation()
        {
            var catalog = MetaCatalog(
                new MetaLocation("Meta1", 0, new[] { new MetaItemDefinition("a", MetaUnlockKind.Purchase, price: 100) }),
                new MetaLocation("Meta2", 5, new[] { new MetaItemDefinition("b", MetaUnlockKind.Purchase, price: 200) }),
                new MetaLocation("Meta3", 9, new[] { new MetaItemDefinition("c", MetaUnlockKind.Purchase, price: 400) }));

            Assert.AreEqual(300, MetaLocationCost.Of(catalog, throughIndex: 1).PurchaseTotal);
            Assert.AreEqual(700, MetaLocationCost.Of(catalog).PurchaseTotal);
        }

        private MetaCatalog MetaCatalog(params MetaLocation[] locations)
        {
            var catalog = ScriptableObject.CreateInstance<MetaCatalog>();
            spawned.Add(catalog);

            typeof(MetaCatalog)
                .GetField("locations", BindingFlags.Instance | BindingFlags.NonPublic)
                .SetValue(catalog, locations.ToList());

            return catalog;
        }
    }
}
