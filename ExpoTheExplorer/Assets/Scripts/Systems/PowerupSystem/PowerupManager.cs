using System;
using ExpoTheExplorer.Core;
using ExpoTheExplorer.Data;
using ExpoTheExplorer.Systems.ProgressionSystem;

namespace ExpoTheExplorer.Systems.PowerupSystem
{
    // The powerup STOCK (GDD Section 5.2, plan in .claude/powerup-plan.md): how many
    // charges of each powerup the player holds, how they buy more (Gems, on the main
    // screen only), and what spending one costs.
    //
    // THERE IS NO DAY-COMPLETION EARN PATH ANY MORE (2026-08-28, the user's decision).
    // GrantForDayCompleted lived here and paid a per-type authored amount every time a day
    // was won -- but that amount had been 0 on all three powerups since the asset was first
    // tuned, so the path never actually paid anyone anything. What remains: the starting
    // stock, the Gem purchase, and EnsureAtLeast, which is the tutorial's teaching guarantee
    // rather than a way to earn.
    //
    // It deliberately does NOT know what any powerup DOES. The three effects are handed
    // in as delegates by whoever can actually perform them, which in this project is the
    // day scene's composition root and nothing else -- the same shape TrayManager and
    // BoardDistributor already use to stay out of each other's assemblies. That is not
    // only tidiness: it is what makes the two-screen split safe. The main screen builds
    // this manager too (it needs the counts to sell against), registers no effects, and
    // therefore physically cannot spend a charge.
    //
    // Plain C#, no MonoBehaviour, so every rule below is unit-testable with no scene.
    //
    // SINGLE WRITER, enforced by the compiler rather than by comment: the counts live in
    // a private array here and not on GameState. That is the same departure KeyManager
    // made from LivesManager and for the same two reasons -- GameState.Lives has a public
    // setter, so its single-writer rule rests on everyone reading a comment first, and
    // GameState is the central state of a DAY while a charge outlives the day, the scene
    // and the session.
    public class PowerupManager
    {
        private readonly PowerupConfig config;
        private readonly Wallet wallet;

        // Indexed by (int)PowerupType. The enum's numbering is load-bearing for exactly
        // this, which is why PowerupType spells its values out and PowerupSystemTests
        // pins them -- a renumbering would silently swap which powerup holds whose
        // charges, and nothing else in the game would notice.
        private readonly int[] charges = new int[PowerupTypes.Count];

        // Null until the day scene registers one. A null entry is not an error: it is
        // how the main screen (and any scene with no board) says "nothing here can
        // perform this", and TryUse turns it into a refusal that costs nothing.
        private readonly Func<bool>[] effects = new Func<bool>[PowerupTypes.Count];

        // The Wallet is taken rather than GameState, for the reason KeyManager takes one:
        // Gems have exactly one writer (root invariant, and the setters are internal to
        // ProgressionSystem), so a purchase ASKS rather than assigns. One-directional
        // arrow PowerupSystem -> ProgressionSystem; nothing there knows powerups exist.
        public PowerupManager(PowerupConfig config, Wallet wallet)
        {
            this.config = config;
            this.wallet = wallet;

            // A brand-new player opens on the authored starting stock. A loaded profile
            // overwrites this through ApplyPersisted before anything reads it -- and an
            // older save takes the identical path, because "never played" and "played
            // before powerups existed" deserve the same answer.
            foreach (var type in PowerupTypes.All)
            {
                charges[(int)type] = config.For(type).StartingCharges;
            }
        }

        // Carries the type and its NEW count, so a subscriber can re-render one row
        // without asking about the other two. Same shape as KeyManager.KeysChanged.
        public EventBus<(PowerupType Type, int Charges)> ChargesChanged { get; } = new();

        public int ChargesOf(PowerupType type) => charges[(int)type];

        public int GemCostOf(PowerupType type) => config.For(type).GemCost;

        // LOCKED UNTIL TAUGHT, forwarded rather than decided. The rule lives on
        // PowerupSettings beside the schedule it reads; these two exist because this class is
        // the façade the views already hold -- PowerupShopView reads even the PRICE through
        // GemCostOf rather than carrying its own PowerupConfig reference, and a second pointer
        // to the same asset dragged into a scene is invisible until one of them is wrong.
        //
        // The DAY is a parameter and not something this class fetches: a charge outlives the
        // day, so a stock keeper that knew which day it was would be knowing something outside
        // its own scope. Each screen passes its own.
        //
        // Nothing here touches the counts. A lock is permission, not stock: a locked powerup's
        // charges are real, untouched, and usable the day it unlocks.
        public bool IsUnlocked(PowerupType type, int dayIndex) => config.For(type).IsUnlockedOnDay(dayIndex);

        public string LockLabelFor(PowerupType type) => config.LockLabelFor(config.For(type));

        // The seam the day scene fills in and the main screen leaves empty. A Func<bool>
        // rather than an Action because the RETURN VALUE is a rule, not a detail: false
        // means "there was nothing to do", and GDD 5.2's common rule is that such a press
        // costs no charge. All three powerups have reachable moments with no work
        // available -- an empty board, no active tickets, a finished day -- and taking a
        // scarce resource for nothing is the opposite of what this system promises.
        //
        // Registering twice replaces rather than stacks: there is one board per scene, so
        // two effects for one type would mean two scenes are live at once, which this
        // project's Single-mode loads make impossible.
        public void RegisterEffect(PowerupType type, Func<bool> effect)
        {
            effects[(int)type] = effect;
        }

        // Seeds from a loaded profile. -1 means "this save predates powerups":
        // PlayerProfileStore writes that marker because 0 is NOT a safe default for a
        // charge count -- 0 is a real, reachable value meaning "you have none" -- and the
        // store cannot fill in the authored starting stock itself, having no config and
        // no business knowing what a charge means. The marker crosses the boundary and is
        // resolved HERE, where the config lives. Identical in shape to keys, and for the
        // identical reason.
        //
        // Deliberately publishes nothing: this runs inside GameSession's constructor,
        // before any view exists to hear it.
        public void ApplyPersisted(int autoCollectCharges, int timeResetCharges, int noiseClearCharges)
        {
            Apply(PowerupType.AutoCollect, autoCollectCharges);
            Apply(PowerupType.TimeReset, timeResetCharges);
            Apply(PowerupType.NoiseClear, noiseClearCharges);

            void Apply(PowerupType type, int saved)
            {
                charges[(int)type] = saved < 0
                    ? config.For(type).StartingCharges
                    : saved;
            }
        }

        // True when the player holds a charge AND something is registered that could
        // spend it. The HUD reads this to dim a button; it is deliberately not a
        // guarantee that a press will succeed, because whether there is any work to do
        // is only knowable by running the effect (is the item the ticket needs actually
        // on the board?). Promising more than that would need the HUD to poll the board
        // every frame for three separate answers.
        public bool CanUse(PowerupType type) => charges[(int)type] > 0 && effects[(int)type] != null;

        // Spends one charge and runs the powerup. Returns false -- WITHOUT spending --
        // when the player has none, when nothing is registered to perform it, or when the
        // effect reports it had no work to do (GDD 5.2's common rule).
        //
        // The deduction happens AFTER the effect, and that ordering is the one subtle
        // thing here. The alternative -- deduct first, put back on failure -- would have
        // to publish a count and then publish it again a moment later, which a HUD
        // renders as a blink on every wasted press. Running first costs one re-entrancy
        // consideration instead: an effect cascades synchronously (auto-collect can fill
        // a tray, deliver a ticket and complete the day inside this call), so another
        // write to the same count -- the tutorial's EnsureAtLeast, a debug grant -- can land
        // in the middle of it. That is harmless, because both operations are +/-1 on the same
        // field and neither reads a stale copy: the other write publishes its count, then
        // this publishes the spent one.
        public bool TryUse(PowerupType type)
        {
            var index = (int)type;
            if (charges[index] <= 0) return false;

            var effect = effects[index];
            if (effect == null || !effect()) return false;

            charges[index]--;
            ChargesChanged.Publish((type, charges[index]));
            return true;
        }

        // The Gem purchase (GDD 5.2 earn path #2). Callable from anywhere, but by design
        // only ONE caller exists -- the main screen's powerup shop. The day scene never
        // sells, because opening a store mid-day suspends the very time pressure the
        // powerup exists to relieve.
        //
        // There is no stock ceiling: nothing in the design caps how many charges a player
        // may hoard, and inventing one here would be a balancing number with no author.
        public bool TryBuyWithGems(PowerupType type)
        {
            if (!wallet.TrySpendGems(config.For(type).GemCost)) return false;

            var index = (int)type;
            charges[index]++;
            ChargesChanged.Publish((type, charges[index]));
            return true;
        }

#if UNITY_EDITOR || DEVELOPMENT_BUILD
        // The debug menu's powerup cheat (decisions.md D-092). A negative amount removes,
        // so one method covers "give me ten" and "take them away and let me see the button
        // dim" without a second entry point.
        //
        // IT IS NOT ApplyPersisted, and that distinction cost a bug to find rather than to
        // fix: ApplyPersisted assigns the three counts and publishes NOTHING, because at
        // load time no view is subscribed yet and there is nothing to tell. Reusing it here
        // would set the numbers correctly and leave the HUD showing the old ones until some
        // unrelated event happened to repaint -- the cheat would look broken while working
        // perfectly. Publishing is the whole difference, so this is its own method.
        //
        // Floors at zero rather than going negative: a negative charge count would make
        // CanUse false and TryUse false in the same way an empty stock does, but it would
        // also need N presses of "give" before the first one had any visible effect.
        public void DebugGrant(PowerupType type, int amount)
        {
            var index = (int)type;
            charges[index] = Math.Max(0, charges[index] + amount);
            ChargesChanged.Publish((type, charges[index]));
        }
#endif

        // The tutorial's earn path, and the ONLY one that is a floor rather than an
        // addition. It exists because the tutorial makes the player PRESS a powerup: a
        // forced press against an empty stock opens the shop (D-105) instead of teaching
        // anything, and the step could never complete.
        //
        // "Top up to" rather than "grant N" is the whole design, not a detail. ArmTutorial
        // runs on every day-start path -- first load, both retries, and the advance to the
        // next Day -- so an ADDING grant would make replaying the introduction Day a charge
        // farm. Raising a floor is idempotent: replay it as often as you like and the second
        // time does nothing. It also leaves a well-stocked player alone, which an addition
        // would not: someone holding six charges does not need two more to be taught.
        //
        // Returns whether anything changed, so a caller can log a grant without having to
        // read the count before and after.
        public bool EnsureAtLeast(PowerupType type, int minimumCharges)
        {
            if (minimumCharges <= 0) return false;

            var index = (int)type;
            if (charges[index] >= minimumCharges) return false;

            charges[index] = minimumCharges;
            ChargesChanged.Publish((type, charges[index]));
            return true;
        }
    }
}
