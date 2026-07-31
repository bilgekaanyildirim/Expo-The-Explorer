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

        public int Lives { get; set; }
        public int SoftMoney { get; set; }
        public int Gems { get; set; }
        public int Xp { get; set; }
        public int Level { get; set; }

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

        public BoardGrid Board { get; }

        public GameState(GameConfig config)
        {
            Lives = config.StartingLives;
            SoftMoney = config.StartingSoftMoney;
            Gems = config.StartingGems;
            Board = new BoardGrid(config);
        }
    }
}
