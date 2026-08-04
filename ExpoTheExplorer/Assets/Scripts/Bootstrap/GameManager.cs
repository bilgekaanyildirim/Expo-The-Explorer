using ExpoTheExplorer.Core;
using ExpoTheExplorer.Data;
using ExpoTheExplorer.Systems.BoardDistribution;
using ExpoTheExplorer.Systems.EconomySystem;
using ExpoTheExplorer.Systems.LivesSystem;
using ExpoTheExplorer.Systems.TicketSystem;
using ExpoTheExplorer.Systems.TraySystem;
using UnityEngine;
using UnityEngine.EventSystems;

namespace ExpoTheExplorer.Bootstrap
{
    // Thin entry point: builds the central GameState from config and exposes it,
    // then drives the ticket lifecycle, board distribution, and tray delivery.
    // Systems/UI bind to State/TicketSlotManager/TrayManager rather than this
    // class growing gameplay logic itself.
    public class GameManager : MonoBehaviour
    {
        [SerializeField] private GameConfig gameConfig;
        [SerializeField] private TicketGenerationConfig ticketGenerationConfig;
        [SerializeField] private FoodCatalog foodCatalog;
        [SerializeField] private BoardDistributionConfig boardDistributionConfig;
        [SerializeField] private EconomyConfig economyConfig;
        [SerializeField] private LivesConfig livesConfig;

        public GameState State { get; private set; }
        public TicketSlotManager TicketSlotManager { get; private set; }
        public TrayManager TrayManager { get; private set; }
        public LivesManager LivesManager { get; private set; }

        private TicketFactory ticketFactory;
        private BoardDistributor boardDistributor;
        private EconomyCalculator economyCalculator;

        private void Awake()
        {
            EnsurePhysics2DRaycaster();

            State = new GameState(gameConfig);
            ticketFactory = new TicketFactory(ticketGenerationConfig);
            LivesManager = new LivesManager(State, livesConfig);
            TicketSlotManager = new TicketSlotManager(State, CreateNextTicket, LivesManager.LoseLife, ticketGenerationConfig.UpcomingQueueSize);
            boardDistributor = new BoardDistributor(State, boardDistributionConfig);
            TrayManager = new TrayManager(State, slotIndex => TicketSlotManager.DeliverTicket(slotIndex), LivesManager.LoseLife);
            economyCalculator = new EconomyCalculator(economyConfig);

            // Subscribe before the initial fill so the first 3 tickets trigger
            // board distribution too, not just later deliveries/cancellations.
            State.TicketAssigned.Subscribe(OnTicketAssigned);
            State.TicketDelivered.Subscribe(OnTicketDelivered);
            TicketSlotManager.FillEmptySlots();
        }

        private void OnDestroy()
        {
            State.TicketAssigned.Unsubscribe(OnTicketAssigned);
            State.TicketDelivered.Unsubscribe(OnTicketDelivered);
        }

        // Paused while awaiting Continue (GDD Section 6 — Lives depleted, day
        // over) so a frozen ticket countdown can't keep cancelling tickets and
        // requesting further life loss from LivesManager, which is already a
        // no-op at 0 Lives but would otherwise mask the pause with silent
        // no-ops instead of actually holding time still.
        private void Update()
        {
            if (State.IsAwaitingContinue) return;

            TicketSlotManager.Tick(Time.deltaTime);
        }

        // Production is order-triggered (GDD Section 4), not a continuous poll —
        // every time a new ticket enters a slot, the board gets a chance to spawn
        // its required items and leak a noise item from the upcoming queue, and
        // that slot's tray (if a timeout left it holding orphaned items) is
        // cleared back onto the board.
        private void OnTicketAssigned((int SlotIndex, Ticket Ticket) assignment)
        {
            boardDistributor.OnOrderPlaced(State.TicketSlots, TicketSlotManager.UpcomingTickets);
            TrayManager.OnTicketAssigned(assignment.SlotIndex);
        }

        // Applies the Economy Module's tip formula (GDD Section 9) to SoftMoney
        // the instant a ticket is delivered — TicketDelivered fires with the
        // ticket that just left, still holding its final RemainingSeconds, so
        // elapsed delivery time is read from that same instance.
        private void OnTicketDelivered((int SlotIndex, Ticket Ticket) delivery)
        {
            var tip = economyCalculator.CalculateTip(delivery.Ticket).TotalTip;
            State.SoftMoney += Mathf.RoundToInt(tip);
        }

        private Ticket CreateNextTicket()
        {
            var patienceType = ticketFactory.PickRandomPatienceType();
            var customerName = ticketFactory.PickRandomCustomerName();
            return ticketFactory.Create(foodCatalog.Items, customerName, patienceType);
        }

        // Lets the same EventSystem that already drives the UGUI Canvas
        // (GraphicRaycaster) also raycast the world-space board item and tray
        // colliders, so drag handlers and drop targets interoperate via the
        // standard pointerDrag mechanism. Added in code rather than hand-edited
        // into the scene file — no manual Editor step needed on Main Camera.
        private void EnsurePhysics2DRaycaster()
        {
            var cam = Camera.main;
            if (cam != null && cam.GetComponent<Physics2DRaycaster>() == null)
            {
                cam.gameObject.AddComponent<Physics2DRaycaster>();
            }
        }
    }
}
