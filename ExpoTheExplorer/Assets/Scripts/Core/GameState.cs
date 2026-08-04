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

        // Setters publish on every actual change (LivesManager.LoseLife/TryContinue,
        // GameManager's tip payout) so HUD views (LivesView, SoftMoneyView) can bind
        // via LivesChanged/SoftMoneyChanged instead of polling GameState in Update.
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
        // GameConfig.StartingLives at read time -- LivesManager.TryContinue can
        // refill Lives to a different amount (LivesConfig.ContinueRefillAmount is
        // intentionally a separate knob), so MaxLives is instead set explicitly
        // by whoever grants a full refill (GameState's constructor for day start,
        // LivesManager.TryContinue for a paid continue) and otherwise just holds.
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

        public int SoftMoney
        {
            get => softMoney;
            set
            {
                if (softMoney == value) return;
                softMoney = value;
                SoftMoneyChanged.Publish(softMoney);
            }
        }

        public int Gems { get; set; }
        public int Xp { get; set; }
        public int Level { get; set; }

        public EventBus<int> LivesChanged { get; } = new();
        public EventBus<int> MaxLivesChanged { get; } = new();
        public EventBus<int> SoftMoneyChanged { get; } = new();

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

        public BoardGrid Board { get; }

        public GameState(GameConfig config)
        {
            Lives = config.StartingLives;
            MaxLives = config.StartingLives;
            SoftMoney = config.StartingSoftMoney;
            Gems = config.StartingGems;
            Board = new BoardGrid(config);
        }
    }
}
