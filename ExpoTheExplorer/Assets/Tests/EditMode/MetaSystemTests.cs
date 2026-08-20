using System.Collections.Generic;
using System.Linq;
using ExpoTheExplorer.Data;
using ExpoTheExplorer.Systems.MetaSystem;
using NUnit.Framework;
using UnityEngine;

namespace ExpoTheExplorer.Tests.EditMode
{
    // The rules the meta screen will run on, pinned before any UI exists to show them.
    // Nothing here touches Unity beyond spawning sprites, because MetaResolver and
    // MetaPurchase take every fact as a parameter -- which is the whole reason they can be
    // tested at all.
    public class MetaSystemTests
    {
        private readonly List<Object> spawned = new();

        [TearDown]
        public void TearDown()
        {
            foreach (var asset in spawned) Object.DestroyImmediate(asset);
            spawned.Clear();
        }

        private Sprite CreateSprite()
        {
            var texture = new Texture2D(2, 2);
            spawned.Add(texture);
            var sprite = Sprite.Create(texture, new Rect(0, 0, 2, 2), new Vector2(0.5f, 0.5f));
            spawned.Add(sprite);
            return sprite;
        }

        private MetaItemDefinition Purchase(
            string id, int price = 100, string requiresAreaId = null, bool unlocksArea = false, int sortOrder = 0) =>
            new(id, MetaUnlockKind.Purchase, price: price, sprite: CreateSprite(),
                requiresAreaId: requiresAreaId, unlocksArea: unlocksArea, sortOrder: sortOrder);

        private MetaItemDefinition DayUnlocked(string id, int unlockAtDayIndex, int sortOrder = 0) =>
            new(id, MetaUnlockKind.DayUnlock, unlockAtDayIndex: unlockAtDayIndex,
                sprite: CreateSprite(), sortOrder: sortOrder);

        private MetaLocation Location(string id, int unlockAtDayIndex, params MetaItemDefinition[] items) =>
            new(id, unlockAtDayIndex, items, CreateSprite());

        // Seeded by reflection rather than through SerializedObject the way
        // EconomySystemTests does. The difference is what is being set: EconomyConfig's
        // knobs are floats, which SerializedObject writes in one line each, while this is a
        // LIST of plain serializable objects -- filling it that way would mean walking
        // every field of every MetaLocation and MetaItemDefinition by name, which is more
        // fragile than the reflection it replaces and would need updating on every schema
        // change. The alternative, a public setter, would exist purely for tests.
        private MetaCatalog Catalog(params MetaLocation[] locations)
        {
            var catalog = ScriptableObject.CreateInstance<MetaCatalog>();
            spawned.Add(catalog);

            typeof(MetaCatalog)
                .GetField("locations", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)
                .SetValue(catalog, locations.ToList());

            return catalog;
        }

        private static ISet<string> Owned(params string[] keys) => new HashSet<string>(keys);

        // --- ownership keys --------------------------------------------------------------

        [Test]
        public void IsOwned_MatchesOnTheQualifiedKey_NotTheLocalId()
        {
            var fountain = Purchase("Fountain");
            var location = Location("Meta1", 0, fountain);

            Assert.IsTrue(MetaResolver.IsOwned(location, fountain, Owned("Meta1.Fountain")));
            Assert.IsFalse(MetaResolver.IsOwned(location, fountain, Owned("Fountain")));
        }

        // The point of qualifying keys: the same decor id in two locations is two purchases.
        [Test]
        public void IsOwned_SameIdInAnotherLocation_IsNotOwnedHere()
        {
            var fountain = Purchase("Fountain");
            var meta2 = Location("Meta2", 12, fountain);

            Assert.IsFalse(MetaResolver.IsOwned(meta2, fountain, Owned("Meta1.Fountain")));
        }

        // --- location unlocking ----------------------------------------------------------

        [Test]
        public void IsLocationUnlocked_OpensOnItsOwnDayAndStaysOpen()
        {
            var location = Location("Meta2", 12, Purchase("Fountain"));

            Assert.IsFalse(MetaResolver.IsLocationUnlocked(location, 11));
            Assert.IsTrue(MetaResolver.IsLocationUnlocked(location, 12));
            Assert.IsTrue(MetaResolver.IsLocationUnlocked(location, 40));
        }

        [Test]
        public void UnlockedLocations_KeepsCatalogOrderAndDropsLockedOnes()
        {
            var catalog = Catalog(
                Location("Meta1", 0, Purchase("A")),
                Location("Meta2", 6, Purchase("B")),
                Location("Meta3", 12, Purchase("C")));

            var unlocked = MetaResolver.UnlockedLocations(catalog, 6);

            Assert.AreEqual(new[] { "Meta1", "Meta2" }, unlocked.Select(l => l.Id).ToArray());
        }

        [Test]
        public void DefaultLocationIndex_IsTheNewestUnlockedOne()
        {
            var catalog = Catalog(
                Location("Meta1", 0, Purchase("A")),
                Location("Meta2", 6, Purchase("B")),
                Location("Meta3", 12, Purchase("C")));

            Assert.AreEqual(0, MetaResolver.DefaultLocationIndex(catalog, 0));
            Assert.AreEqual(1, MetaResolver.DefaultLocationIndex(catalog, 6));
            Assert.AreEqual(2, MetaResolver.DefaultLocationIndex(catalog, 99));
        }

        // Walked back from the END, not picked by highest unlock day: catalog order is the
        // authored order, and two locations may legitimately share an unlock day.
        [Test]
        public void DefaultLocationIndex_TwoLocationsSharingADay_PicksTheLaterOne()
        {
            var catalog = Catalog(
                Location("Meta1", 0, Purchase("A")),
                Location("Meta2", 0, Purchase("B")));

            Assert.AreEqual(1, MetaResolver.DefaultLocationIndex(catalog, 0));
        }

        [Test]
        public void DefaultLocationIndex_NothingUnlocked_IsMinusOne()
        {
            var catalog = Catalog(Location("Meta1", 5, Purchase("A")));

            Assert.AreEqual(-1, MetaResolver.DefaultLocationIndex(catalog, 0));
        }

        // --- what is on screen -----------------------------------------------------------

        [Test]
        public void IsActive_PurchaseProp_OnlyOnceOwned()
        {
            var fountain = Purchase("Fountain");
            var location = Location("Meta1", 0, fountain);

            Assert.IsFalse(MetaResolver.IsActive(location, fountain, Owned(), 0));
            Assert.IsTrue(MetaResolver.IsActive(location, fountain, Owned("Meta1.Fountain"), 0));
        }

        [Test]
        public void IsActive_DayUnlockedProp_AppearsOnItsDayWithoutBeingOwned()
        {
            var fridge = DayUnlocked("DrinkFridge", 4);
            var location = Location("Meta1", 0, fridge);

            Assert.IsFalse(MetaResolver.IsActive(location, fridge, Owned(), 3));
            Assert.IsTrue(MetaResolver.IsActive(location, fridge, Owned(), 4));
            Assert.IsFalse(MetaResolver.IsOwned(location, fridge, Owned()));
        }

        [Test]
        public void IsActive_AreaGatedProp_NeedsTheAreaOwnedToo()
        {
            var square = Purchase("Square", unlocksArea: true);
            var table = Purchase("Table1", requiresAreaId: "Square");
            var location = Location("Meta1", 0, square, table);

            // Owning the table but not the square: deliberately NOT active, so a
            // requiresAreaId added to a prop players already have cannot leave it hanging
            // over ungravelled grass.
            Assert.IsFalse(MetaResolver.IsActive(location, table, Owned("Meta1.Table1"), 0));
            Assert.IsTrue(MetaResolver.IsActive(location, table, Owned("Meta1.Table1", "Meta1.Square"), 0));
        }

        // An area id naming nothing is a content bug the validator reports; treating it as
        // unsatisfied keeps the bug visible instead of hiding it behind a working prop.
        [Test]
        public void IsAreaSatisfied_AreaIdNamingNothing_IsNotSatisfied()
        {
            var table = Purchase("Table1", requiresAreaId: "Nowhere");
            var location = Location("Meta1", 0, table);

            Assert.IsFalse(MetaResolver.IsAreaSatisfied(location, table, Owned("Meta1.Nowhere")));
        }

        [Test]
        public void IsAreaSatisfied_AreaIdNamingANonAreaItem_IsNotSatisfied()
        {
            var fountain = Purchase("Fountain");
            var table = Purchase("Table1", requiresAreaId: "Fountain");
            var location = Location("Meta1", 0, fountain, table);

            Assert.IsFalse(MetaResolver.IsAreaSatisfied(location, table, Owned("Meta1.Fountain")));
        }

        [Test]
        public void ActiveItems_ReturnsDrawOrderAndOmitsInactive()
        {
            var back = Purchase("Back", sortOrder: 1);
            var front = Purchase("Front", sortOrder: 9);
            var unowned = Purchase("Unowned", sortOrder: 5);
            var location = Location("Meta1", 0, front, back, unowned);

            var active = MetaResolver.ActiveItems(location, Owned("Meta1.Back", "Meta1.Front"), 0);

            Assert.AreEqual(new[] { "Back", "Front" }, active.Select(i => i.Id).ToArray());
        }

        // List.Sort is not stable, so equal SortOrders would swap between calls and
        // overlapping props would flicker. Authored order has to survive a tie.
        [Test]
        public void ActiveItems_EqualSortOrders_KeepAuthoredOrderStably()
        {
            var a = Purchase("A", sortOrder: 3);
            var b = Purchase("B", sortOrder: 3);
            var c = Purchase("C", sortOrder: 3);
            var location = Location("Meta1", 0, a, b, c);
            var owned = Owned("Meta1.A", "Meta1.B", "Meta1.C");

            var first = MetaResolver.ActiveItems(location, owned, 0).Select(i => i.Id).ToArray();
            var second = MetaResolver.ActiveItems(location, owned, 0).Select(i => i.Id).ToArray();

            Assert.AreEqual(new[] { "A", "B", "C" }, first);
            Assert.AreEqual(first, second);
        }

        // --- can I buy it, and why not ---------------------------------------------------

        [Test]
        public void Evaluate_AffordablePurchasableProp_IsOk()
        {
            var fountain = Purchase("Fountain", price: 100);
            var location = Location("Meta1", 0, fountain);

            Assert.AreEqual(
                MetaPurchaseVerdict.Ok,
                MetaPurchase.Evaluate(location, fountain, Owned(), 0, softMoney: 100));
        }

        [Test]
        public void Evaluate_OneCoinShort_IsNotEnoughMoney()
        {
            var fountain = Purchase("Fountain", price: 100);
            var location = Location("Meta1", 0, fountain);

            Assert.AreEqual(
                MetaPurchaseVerdict.NotEnoughMoney,
                MetaPurchase.Evaluate(location, fountain, Owned(), 0, softMoney: 99));
        }

        [Test]
        public void Evaluate_DayUnlockedProp_IsNotForSaleEvenWhenRich()
        {
            var fridge = DayUnlocked("DrinkFridge", 4);
            var location = Location("Meta1", 0, fridge);

            Assert.AreEqual(
                MetaPurchaseVerdict.NotForSale,
                MetaPurchase.Evaluate(location, fridge, Owned(), 4, softMoney: 99999));
        }

        [Test]
        public void Evaluate_AlreadyOwned_SaysSo()
        {
            var fountain = Purchase("Fountain", price: 100);
            var location = Location("Meta1", 0, fountain);

            Assert.AreEqual(
                MetaPurchaseVerdict.AlreadyOwned,
                MetaPurchase.Evaluate(location, fountain, Owned("Meta1.Fountain"), 0, softMoney: 100));
        }

        [Test]
        public void Evaluate_LockedLocation_SaysSoRatherThanSelling()
        {
            var fountain = Purchase("Fountain", price: 100);
            var location = Location("Meta2", 12, fountain);

            Assert.AreEqual(
                MetaPurchaseVerdict.LocationLocked,
                MetaPurchase.Evaluate(location, fountain, Owned(), 0, softMoney: 100));
        }

        // Area before affordability: an area-gated prop's price is irrelevant until the
        // area is owned, and "not enough money" would point at the wrong problem.
        [Test]
        public void Evaluate_AreaLockedAndUnaffordable_ReportsTheAreaFirst()
        {
            var square = Purchase("Square", price: 900, unlocksArea: true);
            var table = Purchase("Table1", price: 100, requiresAreaId: "Square");
            var location = Location("Meta1", 0, square, table);

            Assert.AreEqual(
                MetaPurchaseVerdict.AreaLocked,
                MetaPurchase.Evaluate(location, table, Owned(), 0, softMoney: 0));
        }

        [Test]
        public void Evaluate_AreaOwned_LetsTheGatedPropThrough()
        {
            var square = Purchase("Square", price: 900, unlocksArea: true);
            var table = Purchase("Table1", price: 100, requiresAreaId: "Square");
            var location = Location("Meta1", 0, square, table);

            Assert.AreEqual(
                MetaPurchaseVerdict.Ok,
                MetaPurchase.Evaluate(location, table, Owned("Meta1.Square"), 0, softMoney: 100));
        }

        // --- the shop's list -------------------------------------------------------------

        [Test]
        public void ShopItems_KeepsUnaffordableAndGatedProps_DropsOwnedAndDayUnlocked()
        {
            var square = Purchase("Square", price: 900, unlocksArea: true);
            var table = Purchase("Table1", price: 100, requiresAreaId: "Square");
            var fountain = Purchase("Fountain", price: 400);
            var fridge = DayUnlocked("DrinkFridge", 4);
            var location = Location("Meta1", 0, square, table, fountain, fridge);

            var offers = MetaPurchase.ShopItems(location, Owned("Meta1.Fountain"), 0);

            // Square and Table1 stay: seeing what is still to come, and what it costs, is
            // most of what a shop is for. Fountain is owned and the fridge was never for
            // sale, so neither has anything left to offer.
            Assert.AreEqual(new[] { "Square", "Table1" }, offers.Select(i => i.Id).ToArray());
        }

        [Test]
        public void ShopItems_LockedLocation_OffersNothing()
        {
            var location = Location("Meta2", 12, Purchase("Fountain", price: 400));

            Assert.IsEmpty(MetaPurchase.ShopItems(location, Owned(), 0));
        }

        // --- degenerate input ------------------------------------------------------------

        // Every entry point takes data straight from an asset an author is mid-way through
        // filling in, so nulls are a normal state rather than a programming error.
        [Test]
        public void NullsAreAnsweredNotThrown()
        {
            Assert.IsFalse(MetaResolver.IsActive(null, null, Owned(), 0));
            Assert.IsFalse(MetaResolver.IsOwned(null, null, null));
            Assert.IsFalse(MetaResolver.IsLocationUnlocked(null, 0));
            // Cast required: both take a nullable first parameter (the asset overload and
            // the location-list one), so a bare null names neither.
            Assert.AreEqual(-1, MetaResolver.DefaultLocationIndex((MetaCatalog)null, 0));
            Assert.IsEmpty(MetaResolver.UnlockedLocations((MetaCatalog)null, 0));
            Assert.AreEqual(-1, MetaResolver.DefaultLocationIndex((IReadOnlyList<MetaLocation>)null, 0));
            Assert.IsEmpty(MetaResolver.UnlockedLocations((IReadOnlyList<MetaLocation>)null, 0));
            Assert.IsEmpty(MetaResolver.ActiveItems(null, Owned(), 0));
            Assert.IsEmpty(MetaPurchase.ShopItems(null, Owned(), 0));
            Assert.AreEqual(MetaPurchaseVerdict.NotForSale, MetaPurchase.Evaluate(null, null, Owned(), 0, 0));
        }
    }
}
