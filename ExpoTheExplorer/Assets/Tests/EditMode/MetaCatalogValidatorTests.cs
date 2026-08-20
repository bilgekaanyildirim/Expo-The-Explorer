using System.Collections.Generic;
using System.Linq;
using ExpoTheExplorer.Data;
using NUnit.Framework;
using UnityEngine;

namespace ExpoTheExplorer.Tests.EditMode
{
    // Unlike EconomySystemTests these need no SerializedObject: MetaLocation,
    // MetaItemDefinition and MetaLocation carry public constructors precisely so the
    // schema can be exercised without going through an asset. Only sprites have to be
    // spawned, because "has a sprite at all" is one of the rules under test.
    public class MetaCatalogValidatorTests
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

        // A minimal item that breaks no rule, so each test can perturb exactly one thing.
        private MetaItemDefinition ValidPurchase(
            string id = "Fountain",
            int price = 100,
            string requiresAreaId = null,
            bool unlocksArea = false,
            Vector2 normalizedPosition = default) =>
            new(
                id,
                MetaUnlockKind.Purchase,
                price: price,
                sprite: CreateSprite(),
                requiresAreaId: requiresAreaId,
                unlocksArea: unlocksArea,
                normalizedPosition: normalizedPosition);

        private MetaLocation ValidLocation(
            string id = "Meta1",
            int unlockAtDayIndex = 0,
            params MetaItemDefinition[] items) =>
            new(id, unlockAtDayIndex, items.Length == 0 ? new[] { ValidPurchase() } : items, CreateSprite());

        private static MetaCatalogValidationResult Validate(params MetaLocation[] locations) =>
            MetaCatalogValidator.Validate(locations);

        private static void AssertClean(MetaCatalogValidationResult result)
        {
            Assert.IsTrue(result.IsValid, "Unexpected errors: " + string.Join(" | ", result.Errors));
            Assert.IsEmpty(result.Warnings, "Unexpected warnings: " + string.Join(" | ", result.Warnings));
        }

        private static void AssertErrorMentioning(MetaCatalogValidationResult result, params string[] fragments)
        {
            Assert.IsFalse(result.IsValid, "Expected an error, got none.");
            foreach (var fragment in fragments)
            {
                Assert.IsTrue(
                    result.Errors.Any(e => e.Contains(fragment)),
                    $"No error mentioned '{fragment}'. Errors: {string.Join(" | ", result.Errors)}");
            }
        }

        // --- shape of the catalog itself -------------------------------------------------

        [Test]
        public void Validate_NullCatalogAsset_IsAnErrorNotAnException()
        {
            var result = MetaCatalogValidator.Validate((MetaCatalog)null);

            Assert.IsFalse(result.IsValid);
        }

        [Test]
        public void Validate_NoLocations_IsInvalid()
        {
            var result = MetaCatalogValidator.Validate(new List<MetaLocation>());

            AssertErrorMentioning(result, "no locations");
        }

        [Test]
        public void Validate_MinimalWellFormedCatalog_IsCleanWithNoWarnings()
        {
            AssertClean(Validate(ValidLocation()));
        }

        [Test]
        public void Validate_LocationWithoutBackground_IsInvalid()
        {
            var location = new MetaLocation("Meta1", 0, new[] { ValidPurchase() }, backgroundSprite: null);

            AssertErrorMentioning(Validate(location), "background sprite");
        }

        [Test]
        public void Validate_DuplicateLocationId_IsInvalid()
        {
            var result = Validate(ValidLocation("Meta1"), ValidLocation("Meta1", 5));

            AssertErrorMentioning(result, "Duplicate location id");
        }

        [Test]
        public void Validate_BlankLocationId_IsInvalid()
        {
            var result = Validate(ValidLocation(""));

            AssertErrorMentioning(result, "has no id");
        }

        // A save with no file starts the player at Day 0, so something must be reachable
        // there or the meta screen opens empty on a brand-new game.
        [Test]
        public void Validate_NothingUnlockedAtDayZero_IsInvalid()
        {
            var result = Validate(ValidLocation("Meta1", 3));

            AssertErrorMentioning(result, "Day 0");
        }

        [Test]
        public void Validate_LocationsOutOfUnlockOrder_WarnsButStaysValid()
        {
            var result = Validate(ValidLocation("Meta1", 0), ValidLocation("Meta3", 12), ValidLocation("Meta2", 6));

            Assert.IsTrue(result.IsValid, string.Join(" | ", result.Errors));
            Assert.IsTrue(result.Warnings.Any(w => w.Contains("before the location listed above")));
        }

        [Test]
        public void Validate_LocationWithNoItems_WarnsButStaysValid()
        {
            var location = new MetaLocation("Meta1", 0, new List<MetaItemDefinition>(), CreateSprite());

            var result = Validate(location);

            Assert.IsTrue(result.IsValid, string.Join(" | ", result.Errors));
            Assert.IsTrue(result.Warnings.Any(w => w.Contains("no items")));
        }

        // --- items ----------------------------------------------------------------------

        [Test]
        public void Validate_ItemWithBlankId_IsInvalid()
        {
            var result = Validate(ValidLocation("Meta1", 0, ValidPurchase(id: "")));

            AssertErrorMentioning(result, "has no id");
        }

        [Test]
        public void Validate_DuplicateItemIdWithinOneLocation_IsInvalidAndNamesTheOwnershipKey()
        {
            var result = Validate(ValidLocation("Meta1", 0, ValidPurchase("Fountain"), ValidPurchase("Fountain")));

            AssertErrorMentioning(result, "Duplicate item id", "Meta1.Fountain");
        }

        // The whole point of per-location ids: the same decor may exist in two places.
        [Test]
        public void Validate_SameItemIdInTwoDifferentLocations_IsFine()
        {
            var result = Validate(
                ValidLocation("Meta1", 0, ValidPurchase("Fountain")),
                ValidLocation("Meta2", 12, ValidPurchase("Fountain")));

            AssertClean(result);
        }

        [Test]
        public void Validate_ItemWithNoSprite_IsInvalid()
        {
            var item = new MetaItemDefinition("Ghost", MetaUnlockKind.Purchase, price: 50);

            AssertErrorMentioning(Validate(ValidLocation("Meta1", 0, item)), "no sprite");
        }

        [Test]
        public void Validate_PurchasableItemPricedAtZero_IsInvalid()
        {
            var result = Validate(ValidLocation("Meta1", 0, ValidPurchase(price: 0)));

            AssertErrorMentioning(result, "price above zero");
        }

        [Test]
        public void Validate_PositionFarOutsideTheBackground_WarnsButStaysValid()
        {
            var result = Validate(ValidLocation("Meta1", 0, ValidPurchase(normalizedPosition: new Vector2(9f, 0.5f))));

            Assert.IsTrue(result.IsValid, string.Join(" | ", result.Errors));
            Assert.IsTrue(result.Warnings.Any(w => w.Contains("outside the background")));
        }

        // Slightly off the edge is how art that bleeds past a border is authored.
        [Test]
        public void Validate_PositionSlightlyOutsideTheBackground_DoesNotWarn()
        {
            var result = Validate(ValidLocation("Meta1", 0, ValidPurchase(normalizedPosition: new Vector2(1.1f, -0.05f))));

            AssertClean(result);
        }

        // --- Day-unlocked props ---------------------------------------------------------

        private MetaItemDefinition DayUnlocked(string id, int unlockAtDayIndex = 0, int price = 0) =>
            new(id, MetaUnlockKind.DayUnlock, price: price, unlockAtDayIndex: unlockAtDayIndex, sprite: CreateSprite());

        // Since D-017 an unlock is one authored number and Min(0) keeps it in range, so
        // there is nothing left to reject. Whether that number is the RIGHT day is a
        // judgement no code in Data can make -- this assembly cannot see the Day catalog.
        [Test]
        public void Validate_DayUnlockAtALaterDay_IsFine()
        {
            AssertClean(Validate(ValidLocation("Meta1", 0, DayUnlocked("DrinkFridge", unlockAtDayIndex: 4))));
        }

        // Day 0 is legitimate: "part of the scene from the very first day".
        [Test]
        public void Validate_DayUnlockAtDayZero_IsFine()
        {
            AssertClean(Validate(ValidLocation("Meta1", 0, DayUnlocked("Sign", unlockAtDayIndex: 0))));
        }

        [Test]
        public void Validate_DayUnlockCarryingAPrice_WarnsThatThePriceIsIgnored()
        {
            var result = Validate(ValidLocation("Meta1", 0, DayUnlocked("DrinkFridge", unlockAtDayIndex: 4, price: 40)));

            Assert.IsTrue(result.IsValid, string.Join(" | ", result.Errors));
            Assert.IsTrue(result.Warnings.Any(w => w.Contains("ignored")));
        }


        // --- area gating ----------------------------------------------------------------

        [Test]
        public void Validate_ItemRequiringARealAreaInItsOwnLocation_IsFine()
        {
            var result = Validate(ValidLocation(
                "Meta1", 0,
                ValidPurchase("Square", unlocksArea: true),
                ValidPurchase("Table1", requiresAreaId: "Square")));

            AssertClean(result);
        }

        [Test]
        public void Validate_ItemRequiringAnUnknownArea_IsInvalid()
        {
            var result = Validate(ValidLocation("Meta1", 0, ValidPurchase("Table1", requiresAreaId: "Nowhere")));

            AssertErrorMentioning(result, "no item in this location has that id");
        }

        // The id resolves, just not to an AREA -- distinct from "not found", because the
        // fix is ticking Unlocks Area rather than hunting a typo.
        [Test]
        public void Validate_ItemRequiringAnItemThatIsNotAnArea_IsInvalid()
        {
            var result = Validate(ValidLocation(
                "Meta1", 0,
                ValidPurchase("Fountain"),
                ValidPurchase("Table1", requiresAreaId: "Fountain")));

            AssertErrorMentioning(result, "Unlocks Area");
        }

        [Test]
        public void Validate_ItemRequiringItsOwnArea_IsInvalid()
        {
            var result = Validate(ValidLocation("Meta1", 0, ValidPurchase("Square", requiresAreaId: "Square", unlocksArea: true)));

            AssertErrorMentioning(result, "its own area");
        }

        // The multi-location trap this validator exists for: a gate that reaches across
        // locations resolves to a real id, so a plain "not found" would send the author
        // looking for a typo that is not there.
        [Test]
        public void Validate_AreaGateReachingIntoAnotherLocation_IsInvalidAndNamesThatLocation()
        {
            var result = Validate(
                ValidLocation("Meta1", 0, ValidPurchase("Square", unlocksArea: true)),
                ValidLocation("Meta2", 12, ValidPurchase("Table1", requiresAreaId: "Square")));

            AssertErrorMentioning(result, "belongs to location 'Meta1'", "self-contained");
        }

        [Test]
        public void Validate_AreaNothingRequires_WarnsButStaysValid()
        {
            var result = Validate(ValidLocation("Meta1", 0, ValidPurchase("Square", unlocksArea: true)));

            Assert.IsTrue(result.IsValid, string.Join(" | ", result.Errors));
            Assert.IsTrue(result.Warnings.Any(w => w.Contains("no item in this location requires it")));
        }

        // --- the ownership key ----------------------------------------------------------

        [Test]
        public void OwnershipKey_JoinsLocationAndItemWithADot()
        {
            Assert.AreEqual("Meta1.Fountain", MetaCatalog.OwnershipKey("Meta1", "Fountain"));
        }

        // --- a Meta1-shaped catalog -----------------------------------------------------

        // Meta1 as it will actually be authored: a ruin that disappears when bought, an
        // area expansion with a prop standing on it, plain decor, and the three
        // Day-unlocked props with their three different trigger shapes. Guards the
        // combination, which the single-rule tests above cannot.
        [Test]
        public void Validate_RealisticMeta1Shape_IsCleanWithNoWarnings()
        {
            // The stand is an ordinary bought prop now (D-016): the background carries the
            // ruin, and this draws over it. There is no "vanishes when bought" case left.
            var stand = ValidPurchase("HotdogStand", price: 250);

            var location = new MetaLocation(
                "Meta1",
                0,
                new[]
                {
                    stand,
                    ValidPurchase("Square", price: 900, unlocksArea: true),
                    ValidPurchase("Table1", price: 120, requiresAreaId: "Square"),
                    ValidPurchase("Fountain", price: 400),
                    DayUnlocked("Fryer", unlockAtDayIndex: 2),
                    DayUnlocked("DrinkFridge", unlockAtDayIndex: 4),
                    DayUnlocked("SaucesStand", unlockAtDayIndex: 7)
                },
                CreateSprite());

            AssertClean(Validate(location));
        }
    }
}
