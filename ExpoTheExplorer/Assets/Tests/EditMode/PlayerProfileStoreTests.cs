using System;
using System.IO;
using ExpoTheExplorer.Systems.ProgressionSystem;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace ExpoTheExplorer.Tests.EditMode
{
    // Two groups. The file-behaviour cases (missing / empty / corrupt / round-trip
    // / directory creation) predate the profile carrying anything, and their
    // fallback assertions check reference identity -- that the caller's own
    // instance comes back rather than a silently substituted default. The version
    // cases came with Adım 4, when the profile started carrying a real wallet, and
    // they pin down which files are readable: versioned ones from 1 up to this
    // build, and nothing else. v2 (decisions.md D-012) added CurrentDayIndex, which
    // turned the "older but versioned schema" case from a hypothetical into the
    // actual migration path a v1 player takes. v3 (decisions.md D-014) added Lives and
    // brought the first REAL upgrade with it -- absent-reads-as-0 is not survivable for
    // that field -- and v6 (decisions.md D-064) took both back out again, which is why
    // the cases below assert what a v3 file's OTHER fields still do rather than what its
    // lives become: a removal is proven by the old key being ignored and by the write
    // side never emitting it again. v4 (decisions.md D-020) added
    // OwnedMetaItemIds and went back to a SAFE default, so its cases guard something
    // different: not a value being lost, but a reference arriving null. That is why one
    // of them uses a CURRENT-version payload -- UpgradeToCurrent returns early there, so
    // only a normalization outside it catches the case.
    public class PlayerProfileStoreTests
    {
        private string testFilePath;

        [SetUp]
        public void SetUp()
        {
            testFilePath = Path.Combine(Application.temporaryCachePath, $"player_profile_test_{Guid.NewGuid()}.json");
        }

        [TearDown]
        public void TearDown()
        {
            if (File.Exists(testFilePath))
            {
                File.Delete(testFilePath);
            }
        }

        [Test]
        public void Load_WhenFileDoesNotExist_ReturnsDefaultProfile()
        {
            var store = new PlayerProfileStore(testFilePath);

            var profile = store.Load();

            Assert.IsNotNull(profile);
        }

        [Test]
        public void Load_WhenFileIsCorruptedJson_ReturnsDefaultProfile_DoesNotThrow()
        {
            File.WriteAllText(testFilePath, "{ not valid json");
            var store = new PlayerProfileStore(testFilePath);

            LogAssert.Expect(LogType.Error, new System.Text.RegularExpressions.Regex("Failed to load player profile"));
            PlayerProfile profile = null;
            Assert.DoesNotThrow(() => profile = store.Load());

            Assert.IsNotNull(profile);
        }

        [Test]
        public void Load_WhenFileIsEmpty_ReturnsDefaultProfile()
        {
            File.WriteAllText(testFilePath, string.Empty);
            var store = new PlayerProfileStore(testFilePath);

            var profile = store.Load();

            Assert.IsNotNull(profile);
        }

        [Test]
        public void Load_WhenFileDoesNotExist_WithFallback_ReturnsFallback()
        {
            var store = new PlayerProfileStore(testFilePath);
            var fallback = new PlayerProfile();

            var profile = store.Load(fallback);

            Assert.AreSame(fallback, profile);
        }

        [Test]
        public void Load_WhenFileIsEmpty_WithFallback_ReturnsFallback()
        {
            File.WriteAllText(testFilePath, string.Empty);
            var store = new PlayerProfileStore(testFilePath);
            var fallback = new PlayerProfile();

            var profile = store.Load(fallback);

            Assert.AreSame(fallback, profile);
        }

        [Test]
        public void Load_WhenFileIsCorruptedJson_WithFallback_ReturnsFallbackNotASubstitutedDefault()
        {
            File.WriteAllText(testFilePath, "{ not valid json");
            var store = new PlayerProfileStore(testFilePath);
            var fallback = new PlayerProfile();

            LogAssert.Expect(LogType.Error, new System.Text.RegularExpressions.Regex("Failed to load player profile"));
            var profile = store.Load(fallback);

            Assert.AreSame(fallback, profile);
        }

        [Test]
        public void Save_ThenLoad_RoundTripsWithoutFallingBack()
        {
            var store = new PlayerProfileStore(testFilePath);
            store.Save(new PlayerProfile());

            Assert.IsTrue(File.Exists(testFilePath));

            var fallback = new PlayerProfile();
            var reloaded = new PlayerProfileStore(testFilePath).Load(fallback);

            // A real file was read, so the fallback must NOT be what comes back.
            Assert.IsNotNull(reloaded);
            Assert.AreNotSame(fallback, reloaded);
        }

        [Test]
        public void Save_WhenCalledTwice_OverwritesRatherThanAppending()
        {
            var store = new PlayerProfileStore(testFilePath);

            // Held on to deliberately: Save stamps Version onto the instance it is
            // given, so this is what the file should contain -- comparing against a
            // fresh PlayerProfile() would compare against an unstamped v0.
            var saved = new PlayerProfile();
            store.Save(saved);
            store.Save(new PlayerProfile());

            Assert.AreEqual(JsonUtility.ToJson(saved), File.ReadAllText(testFilePath));
        }

        [Test]
        public void Save_StampsTheCurrentSchemaVersion_EvenWhenTheCallerLeftItZero()
        {
            var store = new PlayerProfileStore(testFilePath);
            var profile = new PlayerProfile { SoftMoney = 120, Gems = 3 };

            store.Save(profile);

            Assert.AreEqual(PlayerProfileStore.CurrentVersion, profile.Version);
            Assert.AreEqual(PlayerProfileStore.CurrentVersion, store.Load().Version);
        }

        [Test]
        public void SaveThenLoad_PreservesTheWallet()
        {
            var store = new PlayerProfileStore(testFilePath);
            store.Save(new PlayerProfile { SoftMoney = 1234, Gems = 7 });

            var reloaded = new PlayerProfileStore(testFilePath).Load();

            Assert.AreEqual(1234, reloaded.SoftMoney);
            Assert.AreEqual(7, reloaded.Gems);
        }

        // The v2 field (decisions.md D-012). It is what makes the main screen's
        // "Play" open the day the player left off on rather than always Day 0, so a
        // silent round-trip failure here would look like progress being lost.
        [Test]
        public void SaveThenLoad_PreservesTheCurrentDayIndex()
        {
            var store = new PlayerProfileStore(testFilePath);
            store.Save(new PlayerProfile { SoftMoney = 10, Gems = 1, CurrentDayIndex = 4 });

            var reloaded = new PlayerProfileStore(testFilePath).Load();

            Assert.AreEqual(4, reloaded.CurrentDayIndex);
        }

        // The whole reason the invariant demands a version field: without one,
        // "an old file whose numbers mean something else" and "a player who
        // genuinely has 0" are the same bytes. An unversioned file is refused
        // rather than half-read.
        [Test]
        public void Load_WhenFileCarriesNoVersion_IgnoresItsValues_AndFallsBack()
        {
            File.WriteAllText(testFilePath, "{\"SoftMoney\":9999,\"Gems\":42}");
            var store = new PlayerProfileStore(testFilePath);

            LogAssert.Expect(LogType.Warning, new System.Text.RegularExpressions.Regex("carries no schema version"));
            var profile = store.Load();

            Assert.AreEqual(0, profile.SoftMoney);
            Assert.AreEqual(0, profile.Gems);
        }

        // A file from a newer build may reinterpret fields this one would misread,
        // so it is left alone rather than partially trusted.
        [Test]
        public void Load_WhenFileVersionIsNewerThanThisBuild_FallsBack()
        {
            File.WriteAllText(testFilePath, "{\"Version\":9001,\"SoftMoney\":9999,\"Gems\":42}");
            var store = new PlayerProfileStore(testFilePath);

            LogAssert.Expect(LogType.Warning, new System.Text.RegularExpressions.Regex("newer than this build understands"));
            var profile = store.Load();

            Assert.AreEqual(0, profile.SoftMoney);
        }

        // The reason Load accepts a RANGE rather than only CurrentVersion, and no
        // longer a hypothetical: v2 added CurrentDayIndex (decisions.md D-012), so
        // the file below is exactly what a player who has been playing since v1 has
        // on disk. Refusing it would delete their money. The missing field reading
        // as 0 is also the whole migration -- 0 means "the first Day", which is
        // precisely where a v1 player was always sent on launch, so there is
        // nothing to upgrade.
        [Test]
        public void Load_WhenFileIsAnOlderButVersionedSchema_StillReadsTheFieldsItHas()
        {
            File.WriteAllText(testFilePath, "{\"Version\":1,\"SoftMoney\":500,\"Gems\":9}");
            var store = new PlayerProfileStore(testFilePath);

            var profile = store.Load();

            Assert.AreEqual(500, profile.SoftMoney);
            Assert.AreEqual(9, profile.Gems);
            Assert.AreEqual(0, profile.CurrentDayIndex, "a v1 file has no day index; it must read as Day 0");
        }

        // The oldest still-readable payload, written as a LITERAL rather than a
        // round-tripped object, because the thing under test is what an already-shipped
        // file means. Everything a v2 save carried must survive every upgrade since.
        //
        // It used to assert the v3 lives refill as well; that field and its upgrade left
        // in v6 (decisions.md D-064), so what is left is the half that actually protects
        // a real player -- their money and their place in the game.
        [Test]
        public void Load_WhenFileIsV2_KeepsEveryFieldItCarried()
        {
            File.WriteAllText(testFilePath, "{\"Version\":2,\"SoftMoney\":319,\"Gems\":4,\"CurrentDayIndex\":1}");
            var store = new PlayerProfileStore(testFilePath);

            var profile = store.Load();

            Assert.AreEqual(319, profile.SoftMoney, "a v2 file's money must survive the upgrade untouched");
            Assert.AreEqual(4, profile.Gems);
            Assert.AreEqual(1, profile.CurrentDayIndex);
        }

        // The case that actually protects a shipped save file: a real v3 player has money
        // and a place in the game, and no schema change since may touch either.
        //
        // The payload deliberately still carries "Lives":2 even though no field answers
        // to that name any more. That is the point of keeping it: v6 removed the field
        // rather than migrating it, and this is what proves the removal is SAFE -- an
        // unmatched JSON key must be dropped quietly by JsonUtility, not throw and not
        // take the rest of the object down with it. Delete that key from the payload and
        // the test stops testing the thing that could actually break a v3-v5 player.
        [Test]
        public void Load_WhenFileIsV3_KeepsEveryFieldAndDropsTheRetiredLivesKey()
        {
            File.WriteAllText(
                testFilePath,
                "{\"Version\":3,\"SoftMoney\":1250,\"Gems\":8,\"CurrentDayIndex\":5,\"Lives\":2}");
            var store = new PlayerProfileStore(testFilePath);

            var profile = store.Load();

            Assert.AreEqual(1250, profile.SoftMoney, "a v3 file's money must survive every later upgrade untouched");
            Assert.AreEqual(8, profile.Gems);
            Assert.AreEqual(5, profile.CurrentDayIndex);
            Assert.IsNotNull(profile.OwnedMetaItemIds, "an absent list must arrive empty, never null");
            Assert.IsEmpty(profile.OwnedMetaItemIds, "owning nothing is what an older save means");
        }

        [Test]
        public void SaveThenLoad_PreservesOwnedMetaItems()
        {
            var store = new PlayerProfileStore(testFilePath);
            store.Save(new PlayerProfile
            {
                OwnedMetaItemIds = { "Meta1.HotdogStand", "Meta1.Square", "Meta2.Fountain" }
            });

            var reloaded = new PlayerProfileStore(testFilePath).Load();

            Assert.AreEqual(
                new[] { "Meta1.HotdogStand", "Meta1.Square", "Meta2.Fountain" },
                reloaded.OwnedMetaItemIds);
        }

        // Keys are qualified by location on purpose, so the same decor id bought in two
        // restaurants is two entries rather than one. If this ever collapses to one, a
        // player who buys a fountain in Meta1 gets Meta2's for free.
        [Test]
        public void SaveThenLoad_SameItemIdInTwoLocations_StaysTwoEntries()
        {
            var store = new PlayerProfileStore(testFilePath);
            store.Save(new PlayerProfile { OwnedMetaItemIds = { "Meta1.Fountain", "Meta2.Fountain" } });

            var reloaded = new PlayerProfileStore(testFilePath).Load();

            Assert.AreEqual(2, reloaded.OwnedMetaItemIds.Count);
            Assert.Contains("Meta1.Fountain", reloaded.OwnedMetaItemIds);
            Assert.Contains("Meta2.Fountain", reloaded.OwnedMetaItemIds);
        }

        // A hand-edited or half-written CURRENT-version file, which UpgradeToCurrent
        // returns early on -- the exact gap the normalization sits outside that method to
        // close. Without it this reaches the caller as a NullReferenceException.
        [Test]
        public void Load_WhenCurrentVersionFileHasAnExplicitNullList_StillArrivesEmpty()
        {
            File.WriteAllText(
                testFilePath,
                "{\"Version\":4,\"SoftMoney\":5,\"Gems\":0,\"CurrentDayIndex\":0,\"Lives\":3,\"OwnedMetaItemIds\":null}");

            var profile = new PlayerProfileStore(testFilePath).Load();

            Assert.IsNotNull(profile.OwnedMetaItemIds);
            Assert.IsEmpty(profile.OwnedMetaItemIds);
            Assert.AreEqual(5, profile.SoftMoney, "normalizing the list must not disturb anything else");
        }

        // The fallback is an object some caller built, not something this class
        // deserialized, so its list can be null just as easily -- which is why Normalize
        // wraps the fallback returns too and not only the parsed one.
        [Test]
        public void Load_WithFallbackCarryingANullList_StillArrivesEmpty()
        {
            var fallback = new PlayerProfile { SoftMoney = 42, OwnedMetaItemIds = null };

            var profile = new PlayerProfileStore(testFilePath).Load(fallback);

            Assert.AreSame(fallback, profile, "the caller's own instance must come back");
            Assert.IsNotNull(profile.OwnedMetaItemIds);
            Assert.IsEmpty(profile.OwnedMetaItemIds);
        }

        [Test]
        public void Load_WhenFileDoesNotExist_OwnsNothingRatherThanNull()
        {
            var profile = new PlayerProfileStore(testFilePath).Load();

            Assert.IsNotNull(profile.OwnedMetaItemIds);
            Assert.IsEmpty(profile.OwnedMetaItemIds);
        }

        // Replaces Load_WhenFileIsCurrentVersion_DoesNotOverwriteLives, which guarded the
        // v3 upgrade against refilling a real player's saved lives on every launch. That
        // upgrade and that field left in v6 (decisions.md D-064), so the guard has nothing
        // to guard -- but the REMOVAL deserves a test of its own, from the write side.
        //
        // Asserted against the file's raw text rather than a reloaded object, because a
        // reloaded object cannot tell the difference: with no field to deserialize into, a
        // stray "Lives" key would read back as absent either way. The file is the only
        // place the difference is visible, and a Lives key reappearing there would mean
        // lives had quietly become persistent again.
        [Test]
        public void Save_WritesNoLivesKey()
        {
            var store = new PlayerProfileStore(testFilePath);
            store.Save(new PlayerProfile { SoftMoney = 10, Gems = 1, CurrentDayIndex = 2 });

            var json = File.ReadAllText(testFilePath);

            StringAssert.DoesNotContain("Lives", json, "lives left the save file in v6; writing them again would restore the behaviour D-064 removed");
            StringAssert.Contains("\"SoftMoney\":10", json, "the rest of the profile must still be written");
        }

        // v7's migration (decisions.md D-065), and the one that differs in kind from every
        // one before it: the correct value is NOT KNOWN HERE. It is KeyConfig's cap, and
        // this class has no config reference by design, so the upgrade writes a MARKER and
        // KeyManager resolves it. What must never happen is a plain 0 -- that is a real,
        // reachable count meaning "locked out", so an existing player would launch unable
        // to start a day with nothing on screen explaining why.
        [Test]
        public void Load_WhenFileIsOlderThanV7_MarksKeysAsAbsentRatherThanZero()
        {
            File.WriteAllText(
                testFilePath,
                "{\"Version\":6,\"SoftMoney\":500,\"Gems\":3,\"CurrentDayIndex\":7,\"LastCelebratedDayIndex\":7}");

            var profile = new PlayerProfileStore(testFilePath).Load();

            Assert.AreEqual(PlayerProfileStore.KeysAbsentMarker, profile.Keys);
            Assert.AreNotEqual(0, profile.Keys, "0 is a real count meaning locked out; it cannot double as 'absent'");

            // And the migration touches nothing the old file already carried.
            Assert.AreEqual(500, profile.SoftMoney);
            Assert.AreEqual(3, profile.Gems);
            Assert.AreEqual(7, profile.CurrentDayIndex);
        }

        // The anchor deliberately gets NO migration branch: 0 means "no timestamp", which
        // the load path reads as "start the clock now". That absence is what keeps
        // System.DateTime out of this class entirely.
        [Test]
        public void Load_WhenFileIsOlderThanV7_LeavesTheKeyAnchorAtZero()
        {
            File.WriteAllText(testFilePath, "{\"Version\":6,\"SoftMoney\":10}");

            var profile = new PlayerProfileStore(testFilePath).Load();

            Assert.AreEqual(0, profile.LastKeyRegenUtcTicks);
        }

        // The guard on the other side, the same shape as the Lives and celebration-marker
        // pairs: on a CURRENT-version file the count is DATA. Without this, an upgrade that
        // ran unconditionally would refill every player's keys on every launch -- infinite
        // keys for anyone willing to relaunch the game.
        [Test]
        public void Load_WhenFileIsCurrentVersion_DoesNotOverwriteKeys()
        {
            var store = new PlayerProfileStore(testFilePath);
            store.Save(new PlayerProfile { Keys = 0, LastKeyRegenUtcTicks = 123456789L });

            var reloaded = new PlayerProfileStore(testFilePath).Load();

            Assert.AreEqual(0, reloaded.Keys, "a saved zero is a player who spent their keys, not an absent field");
            Assert.AreEqual(123456789L, reloaded.LastKeyRegenUtcTicks);
        }

        // v5's migration, and the reason it exists at all (decisions.md D-041). Zero is NOT a
        // safe default for LastCelebratedDayIndex: read as 0, an existing save claims every
        // Day-unlocked prop the player got days ago is still owed a celebration, and the meta
        // screen would greet them with a queue of fanfares for things they already have.
        [Test]
        public void Load_WhenFileIsOlderThanV5_MarksEverythingUpToTodayAsAlreadySeen()
        {
            File.WriteAllText(
                testFilePath,
                "{\"Version\":4,\"SoftMoney\":500,\"Gems\":3,\"CurrentDayIndex\":7,\"Lives\":2}");

            var profile = new PlayerProfileStore(testFilePath).Load();

            Assert.AreEqual(7, profile.LastCelebratedDayIndex);

            // And nothing the old file DID carry is disturbed -- a migration that fixed one
            // field by resetting others would be worse than the bug it fixes.
            Assert.AreEqual(500, profile.SoftMoney);
            Assert.AreEqual(7, profile.CurrentDayIndex);
        }

        // The opposite guard, the same shape as the Lives one above: on a CURRENT-version
        // file the marker is data. Without this, an upgrade running unconditionally would
        // push the marker to today on every launch and silently swallow a real unlock.
        [Test]
        public void Load_WhenFileIsCurrentVersion_DoesNotOverwriteTheCelebrationMarker()
        {
            var store = new PlayerProfileStore(testFilePath);
            store.Save(new PlayerProfile { CurrentDayIndex = 9, LastCelebratedDayIndex = 4 });

            var reloaded = new PlayerProfileStore(testFilePath).Load();

            Assert.AreEqual(4, reloaded.LastCelebratedDayIndex);
        }

        // A brand-new player has no file at all, and every field they get is legitimately
        // zero: no money, first Day. Lives used to be the one exception here -- 0 lives
        // would have made the profile unplayable, so the no-file default filled them in --
        // and that exception went out with the field in v6 (decisions.md D-064). A fresh
        // GameState opens at a full bar, so nothing about a full bar is this file's job.
        [Test]
        public void Load_WhenFileDoesNotExist_DefaultsToNoMoneyAndTheFirstDay()
        {
            var profile = new PlayerProfileStore(testFilePath).Load();

            Assert.AreEqual(0, profile.SoftMoney);
            Assert.AreEqual(0, profile.CurrentDayIndex);
        }

        [Test]
        public void Save_CreatesParentDirectory_WhenMissing()
        {
            var nestedPath = Path.Combine(Application.temporaryCachePath, $"profile_test_dir_{Guid.NewGuid()}", "player_profile.json");
            var store = new PlayerProfileStore(nestedPath);

            Assert.DoesNotThrow(() => store.Save(new PlayerProfile()));

            Assert.IsTrue(File.Exists(nestedPath));

            File.Delete(nestedPath);
            Directory.Delete(Path.GetDirectoryName(nestedPath));
        }

        // --- the new-player grant (decisions.md D-026) ------------------------------------

        // The opening balance is authored content, so it arrives as an argument. What this
        // pins down is that it does not cost the fresh profile anything else: first Day,
        // no gems, nothing owned -- the same profile as before, with money in it. It also
        // asserted full lives until v6 took them out of the profile (decisions.md D-064).
        [Test]
        public void NewPlayer_CarriesTheGrantAndNothingElse()
        {
            var profile = PlayerProfileStore.NewPlayer(1000);

            Assert.AreEqual(1000, profile.SoftMoney);
            Assert.AreEqual(0, profile.Gems);
            Assert.AreEqual(0, profile.CurrentDayIndex);
            Assert.IsEmpty(profile.OwnedMetaItemIds);

            // Keys are the one field a fresh profile does NOT get as a zero: "never played"
            // and "played before keys existed" deserve the identical answer -- a full bar --
            // and it is the same marker that says so, resolved where the cap is known.
            Assert.AreEqual(PlayerProfileStore.KeysAbsentMarker, profile.Keys);
        }

        // A hand-edited config should read as broke, never as debt -- the same clamp
        // Wallet.ApplyPersistedBalances makes, applied one step earlier.
        [Test]
        public void NewPlayer_ClampsANegativeGrantToZero()
        {
            Assert.AreEqual(0, PlayerProfileStore.NewPlayer(-500).SoftMoney);
        }

        // The grant reaches a player through the FALLBACK, so it must lose to a real save:
        // topping an existing player up on every launch is the failure this asserts against.
        [Test]
        public void Load_WithANewPlayerFallback_PrefersTheSavedFile()
        {
            var store = new PlayerProfileStore(testFilePath);
            store.Save(new PlayerProfile { SoftMoney = 12 });

            var loaded = store.Load(PlayerProfileStore.NewPlayer(1000));

            Assert.AreEqual(12, loaded.SoftMoney);
        }

        [Test]
        public void Load_WithANewPlayerFallback_AndNoFile_GrantsTheMoney()
        {
            var loaded = new PlayerProfileStore(testFilePath).Load(PlayerProfileStore.NewPlayer(1000));

            Assert.AreEqual(1000, loaded.SoftMoney);
        }

        // --- Delete (decisions.md D-026) --------------------------------------------------

        [Test]
        public void Delete_RemovesTheFile_AndTheNextLoadIsANewPlayer()
        {
            var store = new PlayerProfileStore(testFilePath);
            store.Save(new PlayerProfile { SoftMoney = 900, CurrentDayIndex = 4, OwnedMetaItemIds = { "Meta1.Square" } });

            Assert.IsTrue(store.Delete());
            Assert.IsFalse(File.Exists(testFilePath), "the reset must leave no file behind for the next load to find");

            // Every field, not just the money: "start over" means the day index and the
            // owned props go too, and a partial reset would be the quiet failure here.
            var reloaded = store.Load(PlayerProfileStore.NewPlayer(1000));

            Assert.AreEqual(1000, reloaded.SoftMoney);
            Assert.AreEqual(0, reloaded.CurrentDayIndex);
            Assert.IsEmpty(reloaded.OwnedMetaItemIds);
        }

        // The player wanted a clean slate and already has one; reporting failure would make
        // the caller refuse to reload the screen for no reason.
        [Test]
        public void Delete_WhenThereIsNoFile_ReportsSuccess()
        {
            Assert.IsTrue(new PlayerProfileStore(testFilePath).Delete());
        }
    }
}
