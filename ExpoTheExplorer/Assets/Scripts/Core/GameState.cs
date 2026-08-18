using ExpoTheExplorer.Data;

namespace ExpoTheExplorer.Core
{
    // Central runtime state for a session. UI reads this reactively; it holds no
    // Unity/MonoBehaviour dependency so it can be constructed and asserted on in
    // EditMode tests without a scene (CLAUDE.md Section 5 — Central Game State).
    public class GameState
    {
        public const int TicketSlotCount = 3;

        public Ticket[] TicketSlots { get; } = new Ticket[TicketSlotCount];

        private int lives;
        private int maxLives;
        private int softMoney;
        private int gems;
        private int currentDayIndex;

        // Setters publish on every actual change (LivesManager for lives, Wallet
        // for the two balances) so HUD views (LivesView, SoftMoneyView, GemsView)
        // can bind via LivesChanged/SoftMoneyChanged/GemsChanged instead of polling
        // GameState in Update.
        public int Lives
        {
            get => lives;
            set
            {
                if (lives == value) return;
                lives = value;
                LivesChanged.Publish(lives);
            }
        }

        // The "out of" half of the X/Y lives HUD. Deliberately NOT derived from
        // DefaultStartingLives at read time: MaxLives is set explicitly by whoever
        // grants a full refill (GameState's constructor at day start,
        // LivesManager's refill on a paid Continue or a retry) and otherwise just
        // holds, so a future "continue refills to a different amount than the day
        // started with" needs no change here. No such knob exists today --
        // LivesConfig carries only the two Continue prices, and every refill goes
        // to MaxLives.
        public int MaxLives
        {
            get => maxLives;
            set
            {
                if (maxLives == value) return;
                maxLives = value;
                MaxLivesChanged.Publish(maxLives);
            }
        }

        // Read by anyone, written by ONE thing: the setters are `internal` and
        // Core/AssemblyInfo.cs makes them visible only to ProgressionSystem,
        // whose Wallet is the single writer (root CLAUDE.md invariant, enforced
        // by the compiler since economy-plan.md Adım 1). Bootstrap, LivesSystem
        // and UI all live in other assemblies, so an accidental
        // `State.SoftMoney = x` there is a build error, not a second authority.
        // The value and its change event stay here; only the rules for changing
        // it moved out.
        public int SoftMoney
        {
            get => softMoney;
            internal set
            {
                if (softMoney == value) return;
                softMoney = value;
                SoftMoneyChanged.Publish(softMoney);
            }
        }

        public int Gems
        {
            get => gems;
            internal set
            {
                if (gems == value) return;
                gems = value;
                GemsChanged.Publish(gems);
            }
        }

        public EventBus<int> LivesChanged { get; } = new();
        public EventBus<int> MaxLivesChanged { get; } = new();
        public EventBus<int> SoftMoneyChanged { get; } = new();
        public EventBus<int> GemsChanged { get; } = new();

        // Set by LivesManager.LoseLife the instant Lives hits 0, cleared again by
        // LivesManager.TryContinue (GDD Section 6 — day ends, Gems refill lives
        // to continue). GameManager reads this to pause TicketSlotManager.Tick
        // so a frozen countdown can't keep depleting Lives further while the
        // player is looking at whatever "continue?" UI reacts to LivesDepleted.
        public bool IsAwaitingContinue { get; set; }

        // Carries the slot index alongside the ticket — WorldTrayView needs it
        // to tell whether a just-resolved batch on ITS OWN slot was a delivery
        // (plays the delivery-success lift/fade) as opposed to a wrong-order
        // scatter (TrayManager.TryAddItem calls this synchronously before
        // returning, so a subscriber's flag is already set by the time the
        // caller checks it).
        public EventBus<(int SlotIndex, Ticket Ticket)> TicketDelivered { get; } = new();
        public EventBus<Ticket> TicketCancelled { get; } = new();

        // Fires with the slot index + NEW ticket whenever one is assigned into a
        // slot (initial fill, or right after a deliver/cancel refills it) —
        // unlike TicketDelivered/TicketCancelled, which carry the ticket that
        // just LEFT. BoardDistributor listens to spawn that order's required
        // items; TrayManager listens to clear/scatter that slot's tray.
        public EventBus<(int SlotIndex, Ticket Ticket)> TicketAssigned { get; } = new();

        // Brackets exactly TrayManager.ScatterBackToBoard's own RequestSpawn
        // calls for one slot (a wrong order OR a timeout — never a
        // delivery, which never calls it) — deliberately narrower than
        // TicketAssigned, which also fires for the *next* ticket's
        // required-item spawn on a successful delivery (same synchronous
        // call, unrelated cause). A view that only cares about "did this
        // slot's leftover tray items just get sent back to the board"
        // should use these, not TicketAssigned.
        public EventBus<int> TraySlotScatterBegin { get; } = new();
        public EventBus<int> TraySlotScatterEnd { get; } = new();

        // Fires exactly once, the moment Lives crosses from >0 to 0 (never on
        // every subsequent life-loss attempt afterward) — payload is the
        // player's current Gems, so a "continue?" UI can immediately show
        // whether they can afford it without needing its own GameState poll.
        public EventBus<int> LivesDepleted { get; } = new();

        // Plain int, no event -- unlike Lives/SoftMoney/Gems, nothing BINDS to
        // this, so there is no view to keep in sync. It is read plenty though:
        // DayLifecycleManager exposes it as OrdersDeliveredCount, TicketSlotManager
        // sends it as the DayCompleted payload, and GameManager snapshots it before
        // a retry. Its single writer is DayLifecycleManager (RecordDelivery
        // increments, ResetForNewDay zeroes) -- add a second one and the day's
        // delivery count and its receipt can disagree.
        public int TicketsDeliveredToday { get; set; }

        // Which authored Day (position in GameManager's resolved Day catalog,
        // not the JSON dayIndex used only for sort order) the player is
        // currently on. Defaults to 0 and does NOT persist: the wallet is the
        // only thing saved between sessions (economy-plan.md Adım 4), so every
        // launch starts at the first Day. Whether it should resume where the
        // player left off is still an open design question (DaySystem_Roadmap Q4);
        // the profile schema is versioned, so adding the field later reads as 0 on
        // existing saves -- which is exactly "start at Day 0".
        public int CurrentDayIndex
        {
            get => currentDayIndex;
            set
            {
                if (currentDayIndex == value) return;
                currentDayIndex = value;
                CurrentDayIndexChanged.Publish(currentDayIndex);
            }
        }

        public EventBus<int> CurrentDayIndexChanged { get; } = new();

        // Fires exactly once per day, the moment TicketSlotManager empties the
        // last active slot with the Day's authored ticket sequence exhausted
        // (GDD Section 11 -- Daily Goal Mode). The goal is the Day's own
        // sequence length, not a global count. Payload is the final delivered
        // count that triggered it.
        public EventBus<int> DayCompleted { get; } = new();

        // Fires when the player abandons the current day attempt via the free
        // Retry action (GameManager.RetryDay) rather than paying to continue.
        // Payload is TicketsDeliveredToday as it stood right before the reset.
        public EventBus<int> DayRetried { get; } = new();

        public BoardGrid Board { get; }

        // GDD Section 6 fixes the starting life count at 3; it is a locked design
        // rule rather than a balancing knob, so it lives here instead of in a
        // config asset. The wallet seeds below are plain zero for the same
        // reason -- a fresh player owns nothing, and nothing is persisted across
        // sessions today, so every run starts from these values.
        public const int DefaultStartingLives = 3;

        public GameState(GameConfig config)
        {
            Lives = DefaultStartingLives;
            MaxLives = DefaultStartingLives;
            SoftMoney = 0;
            Gems = 0;
            Board = new BoardGrid(config);
        }
    }
}
