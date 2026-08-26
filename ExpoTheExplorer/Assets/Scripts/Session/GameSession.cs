using System;
using System.Collections.Generic;
using ExpoTheExplorer.Core;
using ExpoTheExplorer.Data;
using ExpoTheExplorer.Systems.DaySystem;
using ExpoTheExplorer.Systems.KeySystem;
using ExpoTheExplorer.Systems.LivesSystem;
using ExpoTheExplorer.Systems.PowerupSystem;
using ExpoTheExplorer.Systems.ProgressionSystem;

namespace ExpoTheExplorer.Session
{
    // Everything that belongs to a PLAYER rather than to a day being played: the game
    // state, the wallet, lives, the Day catalog, which Day they are on, and which meta
    // props they own. Constructed identically by both screens -- the day scene's
    // GameManager and (from Adım 5b) the main screen's own root.
    //
    // It exists because those two screens need the same construction steps and the steps
    // carry FIVE ordering rules that must not be duplicated:
    //
    //   1. the wallet is built before ApplyPersistedBalances, because GameState's balance
    //      setters are internal to ProgressionSystem and nothing else can seed them;
    //   2. LivesManager is built before anything that could cost a life;
    //   3. the Day catalog is parsed before the day index is clamped, because the clamp
    //      needs its Count;
    //   4. ApplyPersistedBalances re-takes the day-start snapshot, so the first retry of
    //      a session reverts to the RESTORED balance rather than to zero;
    //   5. KeyManager is built after the wallet, because the Gem refill is charged
    //      through it, and its ApplyPersisted follows immediately -- that call is what
    //      turns the profile's -1 marker into a full bar AND pays out the keys earned
    //      while the game was closed (decisions.md D-065);
    //   6. PowerupManager is built after the wallet for the identical reason -- the Gem
    //      purchase is charged through it -- and its ApplyPersisted follows immediately,
    //      resolving the same style of -1 marker into the authored starting stock
    //      (GDD 5.2, .claude/powerup-plan.md Adım 1).
    //
    // A second class re-implementing those five is exactly the failure the single-writer
    // invariant exists to prevent, which is why the alternative -- a separate controller
    // for the main screen -- was rejected (decisions.md D-021).
    //
    // It is a plain class, not a MonoBehaviour, and it looks up NOTHING: every config
    // arrives as a constructor argument. That is what lets a test build one with no
    // scene, no assets and no Unity object graph -- and it is why the day-index clamp is
    // testable here for the first time, a gap D-012 recorded and could not close while
    // this code sat inside GameManager.
    //
    // What it deliberately does NOT hold: the day RUNTIME. Ticket slots, the tray, board
    // distribution, the economy calculator, the ticket-sequence provider and the per-frame
    // tick all stay in GameManager, because they are the part of a session that must not
    // exist while the player is on a menu -- a running day behind the main screen would
    // time tickets out and cost lives.
    public class GameSession
    {
        private readonly PlayerProfileStore profileStore;
        private readonly Wallet wallet;

        public GameState State { get; }
        public LivesManager LivesManager { get; }

        // Handed out rather than wrapped, exactly like Wallet below: KeyManager is already
        // the compiler-enforced single writer of the key count, so re-exposing its methods
        // here would add a second surface to keep in step for no gain.
        public KeyManager KeyManager { get; }

        // The powerup stock, shared by both screens: the day scene spends charges, the
        // main screen sells them (GDD 5.2). Handed out rather than wrapped, exactly like
        // KeyManager and Wallet above -- it is already the single writer of the counts.
        //
        // **NULL when no PowerupConfig was supplied**, and that is deliberate rather than
        // an oversight. The project's rule for a forgotten Inspector drag is that it FAILS
        // OPEN (see NoKeysPopupView's note in CLAUDE.md): a missing config costs the
        // player their powerups, never their ability to play the day. The two scene roots
        // are the ones that say so out loud; this class stays silent because it also runs
        // inside EditMode tests, where a Debug.LogError is a test failure.
        public PowerupManager PowerupManager { get; }

        // What the profile said the three stocks were, kept ONLY for the null-manager case
        // above. Without it, saving a session whose PowerupConfig was never wired would
        // write zeros over a real player's purchased charges -- destroying data this
        // session was never able to manage in the first place. With a manager present
        // these are never read.
        private readonly int savedAutoCollectCharges;
        private readonly int savedTimeResetCharges;
        private readonly int savedNoiseClearCharges;

        // The parsed Day catalog. Exposed read-only because GameManager needs its Count
        // and its entries to build a Day; nothing outside may replace it.
        public IReadOnlyList<DayDefinition> DayCatalog { get; }

        // Meta props the player has bought, as the qualified "<location>.<item>" keys
        // MetaCatalog.OwnershipKey composes. A mutable set on purpose: this is the live
        // inventory a purchase adds to, and Save writes it back.
        //
        // This is the single writer the schema step (decisions.md D-020) deliberately left
        // out -- there was nothing to write it then, because the rules take the set as a
        // parameter (D-019) and no purchase could be committed yet.
        public ISet<string> OwnedMetaItemIds { get; }

        // How far the meta screen has congratulated the player (profile v5, decisions.md
        // D-041). Settable, unlike OwnedMetaItemIds which is a mutable set behind a
        // read-only property, because this is one number rather than a collection.
        //
        // Its SINGLE WRITER is the thing that plays the celebration -- MetaGroundsView --
        // and that is the whole reason it is not written anywhere else: "has this been
        // shown" is only knowable by whatever showed it. The wallet and the day index keep
        // GameManager as their writer; this datum simply has a different one, which the
        // root invariant allows and the map records.
        public int LastCelebratedDayIndex { get; set; }

        // Null until a Day catalog exists, so every consumer falls back the same way it
        // did when this lived on GameManager.
        public DayDefinition CurrentDay => DayCatalogNavigator.GetDayAt(DayCatalog, State.CurrentDayIndex);

        public GameSession(
            GameConfig gameConfig,
            LivesConfig livesConfig,
            KeyConfig keyConfig,
            FoodCatalog foodCatalog,
            // Optional so the existing callers and tests that pass everything after
            // foodCatalog by NAME keep compiling, and because a day without powerups is
            // still a playable day (see the PowerupManager property). Both scene roots
            // treat it as required and say so; only a test may legitimately omit it.
            PowerupConfig powerupConfig = null,
            PlayerProfileStore profileStore = null,
            IReadOnlyList<DayDefinition> dayCatalog = null,
            Func<DateTime> utcNow = null)
        {
            this.profileStore = profileStore ?? new PlayerProfileStore();

            State = new GameState(gameConfig);
            wallet = new Wallet(State);

            // The fallback carries the authored opening balance (decisions.md D-026), so
            // "a player with no readable save" and "a player with 1000 coins to spend"
            // are the same fact rather than two. It reaches the wallet through the
            // ordinary ApplyPersistedBalances call below -- no new write path, so Wallet
            // stays the single writer of both balances.
            //
            // It is the fallback for an UNREADABLE file too (corrupt, or written by a
            // newer build). That is deliberate: this build cannot tell what those numbers
            // mean, so it treats the player as new, and a new player gets the grant. The
            // alternative -- a corrupt file dropping someone to zero coins -- is worse and
            // is not what the version check exists to do.
            var profile = this.profileStore.Load(PlayerProfileStore.NewPlayer(gameConfig.StartingSoftMoney));

            // Order 1 and 4: through the wallet, never by assignment, and it re-snapshots
            // the day start as a side effect that a retry depends on.
            wallet.ApplyPersistedBalances(profile.SoftMoney, profile.Gems);

            // Order 2: before anything that can lose a life. Nothing here can, but
            // GameManager's slot fill runs moments later and does.
            //
            // Nothing seeds Lives from the profile since D-064: lives are a per-day
            // resource again, and `new GameState(config)` above already opened at a full
            // bar. A line here restoring a saved count is precisely what "her gün 3 canla
            // başlasın" removes -- and there is no saved count left to read.
            LivesManager = new LivesManager(State, livesConfig, wallet);

            // Order 5 (decisions.md D-065): AFTER the wallet, because the 40-Gem refill is
            // charged through it, and paired with its ApplyPersisted the way the wallet and
            // lives loads already are. This is the fifth rule that makes this class worth
            // having as one place -- a second constructor re-deriving them is exactly what
            // the single-writer invariant exists to prevent.
            //
            // ApplyPersisted does two jobs in one call: it resolves the profile's -1 marker
            // (an older save, or a player who has never played) into a full bar, and it pays
            // out whatever accrued while the game was CLOSED, which is the entire reason an
            // anchor is persisted instead of a countdown.
            KeyManager = new KeyManager(keyConfig, wallet, utcNow);
            KeyManager.ApplyPersisted(profile.Keys, profile.LastKeyRegenUtcTicks);

            // Order 6 (GDD 5.2): AFTER the wallet, because the Gem purchase is charged
            // through it, and paired with its ApplyPersisted the way the wallet, lives and
            // keys already are. Same -1-marker resolution as keys -- 0 is a real charge
            // count, so an older save cannot use it to mean "absent", and the authored
            // starting stock lives on the config this line is the first to see.
            //
            // Nothing is registered as an EFFECT here. That happens in the day scene's
            // root and nowhere else, which is what makes a charge unspendable on the main
            // screen even though the counts are fully available there to be sold against.
            savedAutoCollectCharges = profile.AutoCollectCharges;
            savedTimeResetCharges = profile.TimeResetCharges;
            savedNoiseClearCharges = profile.NoiseClearCharges;

            if (powerupConfig != null)
            {
                PowerupManager = new PowerupManager(powerupConfig, wallet);
                PowerupManager.ApplyPersisted(
                    profile.AutoCollectCharges,
                    profile.TimeResetCharges,
                    profile.NoiseClearCharges);
            }

            // Injectable so a test can supply a catalog without Resources or a FoodCatalog
            // asset; production passes null and gets the real parse.
            DayCatalog = dayCatalog ?? DayCatalogParser.ParseAll(new DayJsonSource().LoadAll(), foodCatalog);

            // Order 3: after the parse, because the clamp reads DayCatalog.Count. Routed
            // through GoToDay rather than assigning, so that method is the ONLY statement in
            // the project that writes this field (D-092) -- a rule that is worth nothing if
            // the class declaring it exempts its own constructor.
            GoToDay(profile.CurrentDayIndex);

            OwnedMetaItemIds = new HashSet<string>(profile.OwnedMetaItemIds);
            LastCelebratedDayIndex = profile.LastCelebratedDayIndex;
        }

        // The wallet is handed out rather than wrapped: it is already the compiler-enforced
        // single writer of both balances (decisions.md D-010), so re-exposing its methods
        // here would add a second surface to keep in step for no gain.
        public Wallet Wallet => wallet;

        // A persisted index is a claim about a catalog that may have changed since it was
        // written -- a Day can be deleted, or the file can come from a build with more
        // content -- so it is clamped rather than trusted. Landing on the last authored
        // Day is the safe failure: the alternative is CurrentDay resolving to null and
        // ticket creation throwing on the first slot fill.
        //
        // Internal rather than private only so the test suite can reach it; it is the one
        // piece of this class that had no test at all while it lived in GameManager.
        internal int ResolveStartingDayIndex(int persistedIndex) => ClampToCatalog(persistedIndex);

        // THE SINGLE WRITER of GameState.CurrentDayIndex (decisions.md D-092).
        //
        // It did not used to be. The number was assigned in three places -- twice in
        // GameManager (the day advance, and the completed-day exit to the main screen) and
        // once in the constructor above -- while fingerprint.md claimed GameManager was its
        // single writer. That claim was simply false, and it had gone unnoticed because the
        // three sites never ran at the same time. D-092 needed a FOURTH caller from the main
        // screen, where GameManager does not exist at all, which is the point at which a
        // split ownership stops being a documentation error and becomes a real one.
        //
        // Deliberately does NOT save. Every existing caller already decides that for itself
        // and two of them save several other things in the same breath (the advance writes
        // the freshly-paid reward alongside the new index); a save in here would double-write
        // on those paths and, worse, make the write order of a multi-field save depend on
        // which field happened to be set last. A writer sets the value; persisting it is the
        // caller's sentence.
        //
        // Clamped rather than trusted for the reason the constructor's load is: an index past
        // the last authored Day resolves CurrentDay to null and throws on the first ticket.
        // Same clamp, one implementation, so a hand-typed debug index and a restored save
        // cannot disagree about what "Day 40" means.
        public void GoToDay(int index)
        {
            State.CurrentDayIndex = ClampToCatalog(index);
        }

        private int ClampToCatalog(int index)
        {
            if (DayCatalog == null || DayCatalog.Count == 0) return 0;
            if (index <= 0) return 0;

            return Math.Min(index, DayCatalog.Count - 1);
        }

        // The single place that decides WHAT reaches the file. PlayerProfileStore stays the
        // file boundary (ProgressionSystem's job per blueprint); this is the composer, and
        // there is deliberately no third class between them: a separate saver would need a
        // copy of every field this object already holds, and a field forgotten in that copy
        // does not fail loudly -- it silently zeroes a real player's data.
        //
        // Every caller goes through here. GameManager's four save points (day completed,
        // completed-day retry, day advance, and abandoning a failed attempt) are call
        // sites, not writers.
        public void Save()
        {
            profileStore.Save(new PlayerProfile
            {
                SoftMoney = State.SoftMoney,
                Gems = State.Gems,
                CurrentDayIndex = State.CurrentDayIndex,

                // Both halves of the key state, and both are required: the count alone
                // would restart the current regen interval on every launch, handing the
                // player a fresh 30-minute wait each time they quit near the end of one.
                Keys = KeyManager.Keys,
                LastKeyRegenUtcTicks = KeyManager.LastRegenUtcTicks,

                // The three powerup stocks (v8). When no config was wired there is no
                // manager to ask, so what was LOADED is written straight back rather than
                // zeroed -- a forgotten Inspector drag must not cost a player charges they
                // paid Gems for. That also means a marker (-1) read from an older file is
                // written back as a marker, so the next launch still resolves it properly.
                AutoCollectCharges = PowerupManager?.ChargesOf(PowerupType.AutoCollect) ?? savedAutoCollectCharges,
                TimeResetCharges = PowerupManager?.ChargesOf(PowerupType.TimeReset) ?? savedTimeResetCharges,
                NoiseClearCharges = PowerupManager?.ChargesOf(PowerupType.NoiseClear) ?? savedNoiseClearCharges,

                OwnedMetaItemIds = new List<string>(OwnedMetaItemIds),
                LastCelebratedDayIndex = LastCelebratedDayIndex,
            });
        }
    }
}
