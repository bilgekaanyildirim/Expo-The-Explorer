using ExpoTheExplorer.Core;
using ExpoTheExplorer.Data;
using ExpoTheExplorer.Systems.BoardDistribution;
using ExpoTheExplorer.Systems.TicketSystem;
using UnityEngine;

namespace ExpoTheExplorer.Bootstrap
{
    // Thin entry point: builds the central GameState from config and exposes it,
    // then drives the ticket lifecycle and board distribution. Systems/UI bind to
    // State/TicketSlotManager rather than this class growing gameplay logic itself.
    public class GameManager : MonoBehaviour
    {
        [SerializeField] private GameConfig gameConfig;
        [SerializeField] private TicketGenerationConfig ticketGenerationConfig;
        [SerializeField] private FoodCatalog foodCatalog;
        [SerializeField] private BoardDistributionConfig boardDistributionConfig;

        public GameState State { get; private set; }
        public TicketSlotManager TicketSlotManager { get; private set; }

        private TicketFactory ticketFactory;
        private BoardDistributor boardDistributor;

        private void Awake()
        {
            State = new GameState(gameConfig);
            ticketFactory = new TicketFactory(ticketGenerationConfig);
            TicketSlotManager = new TicketSlotManager(State, CreateNextTicket, ticketGenerationConfig.UpcomingQueueSize);
            TicketSlotManager.FillEmptySlots();
            boardDistributor = new BoardDistributor(State, boardDistributionConfig);
        }

        private void Update()
        {
            TicketSlotManager.Tick(Time.deltaTime);
            boardDistributor.Tick(Time.deltaTime, State.TicketSlots, TicketSlotManager.UpcomingTickets);
        }

        private Ticket CreateNextTicket()
        {
            var patienceType = ticketFactory.PickRandomPatienceType();
            var customerName = ticketFactory.PickRandomCustomerName();
            return ticketFactory.Create(foodCatalog.Items, customerName, patienceType);
        }
    }
}
