using System;
using ExpoTheExplorer.Core;
using ExpoTheExplorer.Data;
using ExpoTheExplorer.Systems.ProgressionSystem;

namespace ExpoTheExplorer.Systems.KeySystem
{
    // The key economy (.claude/key-plan.md): keys gate PLAYING, refill themselves in
    // real time, and can be bought back with Gems. Distinct from Lives, which since
    // decisions.md D-064 are a per-day allowance that resets every morning -- neither
    // resource reads the other, and this class never touches a life.
    //
    // Plain C#, no MonoBehaviour, so the whole rule set is unit-testable in isolation
    // (CLAUDE.md Section 5) -- which matters more here than usual, because every rule
    // in this class is a TIME rule and time is the one thing a play-test cannot
    // fast-forward.
    //
    // SINGLE WRITER, enforced by the compiler rather than by comment. The count lives
    // in a private field here instead of on GameState, and that is a deliberate
    // departure from how Lives works: GameState.Lives has a public setter, so its
    // single-writer rule rests on everybody reading LivesManager's comment first.
    // There is no such hole here. The second reason is scope -- GameState is the
    // central state of a DAY, and a key outlives the day, the scene and the session.
    //
    // THE CLOCK IS INJECTED, and this is the project's first dependency on wall-clock
    // time (nothing else in Scripts/ reads DateTime at all). Keeping it behind one
    // Func means the offline-accrual, cap and clock-tampering cases below are all
    // reachable from a test; a class that called DateTime.UtcNow directly would have
    // none of them under test, which for a rule the player experiences as "when do I
    // get to play again" is not acceptable.
    public class KeyManager
    {
        private readonly KeyConfig config;
        private readonly Wallet wallet;
        private readonly Func<DateTime> utcNow;

        // The only two pieces of state, and the only two things ever persisted.
        // `anchorUtc` is the instant the CURRENT partial interval started -- not the
        // last time anything happened -- which is what lets a part-finished half hour
        // survive a Refresh, a save and a relaunch.
        private int keys;
        private DateTime anchorUtc;

        // The Wallet is taken rather than GameState, for the same reason LivesManager
        // takes one: Gems have exactly one writer (root invariant, and the setters are
        // internal to ProgressionSystem), so the 40-Gem refill ASKS rather than
        // assigns. New one-directional arrow KeySystem -> ProgressionSystem; nothing
        // in ProgressionSystem knows keys exist.
        public KeyManager(KeyConfig config, Wallet wallet, Func<DateTime> utcNow = null)
        {
            this.config = config;
            this.wallet = wallet;
            this.utcNow = utcNow ?? (() => DateTime.UtcNow);

            // A brand-new player is FULL. There is no other sensible opening: keys are
            // permission to play, and shipping someone a game they have to wait to
            // start would be absurd. A loaded profile overwrites both fields through
            // ApplyPersisted before anything reads them.
            keys = config.MaxKeys;
            anchorUtc = this.utcNow();
        }

        // Carries the new count. Subscribers re-render; nobody has to poll, and the
        // HUD gets the same treatment coins and gems already have.
        public EventBus<int> KeysChanged { get; } = new();

        // Deliberately NOT auto-refreshing on read. A getter with a side effect that
        // publishes an event is the kind of thing that fires a UI rebuild from inside
        // a render pass; callers that need the value to be current call Refresh first,
        // and every mutating method below already does.
        public int Keys => keys;

        public int MaxKeys => config.MaxKeys;

        public int RefillGemCost => config.RefillGemCost;

        // What gets persisted alongside the count (step 2 writes it). Exposed as ticks
        // rather than a DateTime so the save DTO stays a plain-old-data type that
        // JsonUtility can round-trip -- it cannot serialize a DateTime.
        public long LastRegenUtcTicks => anchorUtc.Ticks;

        public bool IsFull => keys >= config.MaxKeys;

        private TimeSpan RegenInterval => TimeSpan.FromMinutes(config.RegenMinutes);

        // Seeds from a loaded profile (step 2). -1 means "this save predates keys":
        // PlayerProfileStore writes that marker because 0 is NOT a safe default for a
        // key count -- 0 keys is a locked-out player, exactly the trap v3's Lives fell
        // into -- and the store cannot fill in the cap itself, having no config and no
        // business knowing what the number means. So the marker crosses the boundary
        // and is resolved HERE, where the cap lives.
        //
        // A zero or nonsensical anchor is treated as "no anchor yet" and starts the
        // clock now. That is what keeps System.DateTime out of the store as well: the
        // file never has to invent a timestamp, it just says it has none.
        public void ApplyPersisted(int savedKeys, long savedAnchorTicks)
        {
            keys = savedKeys < 0
                ? config.MaxKeys
                : Math.Clamp(savedKeys, 0, config.MaxKeys);

            anchorUtc = IsUsableTicks(savedAnchorTicks)
                ? new DateTime(savedAnchorTicks, DateTimeKind.Utc)
                : utcNow();

            // Pay out whatever accrued while the game was closed. This is the whole
            // point of persisting an anchor rather than a countdown.
            Refresh();
        }

        // Idempotent and cheap, so it can be called before every question without a
        // caller having to wonder whether it is due. That is the design rule that
        // matters most here: CORRECTNESS NEVER DEPENDS ON A TICK EXISTING. A per-second
        // Update somewhere may keep the display fresh, but if it is missing, or the
        // scene it lived in is gone, every answer below is still right.
        public void Refresh()
        {
            var now = utcNow();

            // The device clock moved BACKWARDS -- a manual change, a timezone-less
            // reboot, an NTP correction. Drag the anchor to now: no free keys, and no
            // punishment either. Refusing to move it would leave the player owed
            // nothing until real time caught back up to the old anchor, which for a
            // clock set months forward and back is effectively forever.
            if (now < anchorUtc)
            {
                anchorUtc = now;
                return;
            }

            // A full bar does not bank time. Dragging the anchor forward while full is
            // what makes that true, and it is why spending a key never hands out the
            // next one instantly no matter how long the player sat at the cap.
            if (keys >= config.MaxKeys)
            {
                anchorUtc = now;
                return;
            }

            var intervalTicks = RegenInterval.Ticks;
            if (intervalTicks <= 0) return; // KeyConfig's [Min(1)] should make this unreachable.

            var steps = (now - anchorUtc).Ticks / intervalTicks;
            if (steps <= 0) return;

            var granted = (int)Math.Min(steps, config.MaxKeys - keys);
            keys += granted;

            if (keys >= config.MaxKeys)
            {
                // Hit the cap partway through the elapsed time; the surplus is dropped
                // rather than banked, which is the same rule as sitting at the cap.
                anchorUtc = now;
            }
            else
            {
                // Advance by whole intervals ONLY, so the unfinished remainder of the
                // current one carries over. Setting the anchor to `now` here would
                // silently rob the player of up to one full interval every time
                // anything happened to call Refresh.
                anchorUtc = anchorUtc.AddTicks(steps * intervalTicks);
            }

            KeysChanged.Publish(keys);
        }

        // True when the player may start or retry a day. A question, not a purchase --
        // per the user's rule, starting a day is GATED by keys but does not cost one;
        // only leaving a LOST day does.
        public bool HasKey
        {
            get
            {
                Refresh();
                return keys > 0;
            }
        }

        // The charge for a lost day, taken when the player leaves it (retry or main
        // menu). Returns false at zero rather than throwing or clamping silently, so a
        // caller can tell "spent" from "could not" and offer the popup instead.
        //
        // Callers that must not be blocked -- leaving for the main menu, which would
        // otherwise be a softlock at zero keys -- ignore the result and simply proceed.
        public bool TrySpendKey()
        {
            Refresh();
            if (keys <= 0) return false;

            keys--;

            // No anchor handling here on purpose: if the bar was full, the Refresh
            // above already pulled the anchor to now, so the fresh interval starts from
            // this moment. If it was not full, an interval is already in flight and
            // restarting it would rob the player of the progress they had made.
            KeysChanged.Publish(keys);
            return true;
        }

        // Skips the wait for Gems. Fills to the CAP rather than adding a fixed amount
        // (the user's choice), so the cap is never exceeded and there is one rule
        // instead of two.
        public bool TryRefillWithGems()
        {
            Refresh();

            // Checked BEFORE the wallet is touched: a player at the cap has nothing to
            // buy, and charging them for it would be theft. This ordering is the whole
            // guard -- there is no path that spends without granting.
            if (keys >= config.MaxKeys) return false;

            if (!wallet.TrySpendGems(config.RefillGemCost)) return false;

            keys = config.MaxKeys;
            anchorUtc = utcNow(); // Full again: the clock stops.

            KeysChanged.Publish(keys);
            return true;
        }

        // For the popup's countdown. Rounded UP so the display never shows 0 while the
        // key is still a fraction of a second away -- a counter that sits on zero
        // without anything happening reads as broken.
        public int SecondsUntilNextKey()
        {
            Refresh();
            if (keys >= config.MaxKeys) return 0;

            var remaining = anchorUtc + RegenInterval - utcNow();
            return remaining <= TimeSpan.Zero ? 0 : (int)Math.Ceiling(remaining.TotalSeconds);
        }

        // A corrupt or hand-edited file can carry anything, and `new DateTime(ticks)`
        // THROWS on an out-of-range value rather than clamping -- which would take down
        // the whole session load over a field that has a perfectly good fallback.
        private static bool IsUsableTicks(long ticks) =>
            ticks > 0 && ticks <= DateTime.MaxValue.Ticks;
    }
}
