using ExpoTheExplorer.Core;
using ExpoTheExplorer.Data;
using ExpoTheExplorer.Systems.BoardDistribution;
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

        public GameState State { get; private set; }
        public TicketSlotManager TicketSlotManager { get; private set; }
        public TrayManager TrayManager { get; private set; }

        private TicketFactory ticketFactory;
        private BoardDistributor boardDistributor;

        private void Awake()
        {
            EnsurePhysics2DRaycaster();

            State = new GameState(gameConfig);
            ticketFactory = new TicketFactory(ticketGenerationConfig);
            TicketSlotManager = new TicketSlotManager(State, CreateNextTicket, ticketGenerationConfig.UpcomingQueueSize);
            boardDistributor = new BoardDistributor(State, boardDistributionConfig);
            TrayManager = new TrayManager(State, slotIndex => TicketSlotManager.DeliverTicket(slotIndex));

            // Subscribe before the initial fill so the first 3 tickets trigger
            // board distribution too, not just later deliveries/cancellations.
            State.TicketAssigned.Subscribe(OnTicketAssigned);
            TicketSlotManager.FillEmptySlots();
        }

        private void OnDestroy()
        {
            State.TicketAssigned.Unsubscribe(OnTicketAssigned);
        }

        private void Update()
        {
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
