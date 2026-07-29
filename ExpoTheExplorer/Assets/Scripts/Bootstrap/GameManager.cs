using ExpoTheExplorer.Core;
using ExpoTheExplorer.Data;
using ExpoTheExplorer.Systems.TicketSystem;
using UnityEngine;

namespace ExpoTheExplorer.Bootstrap
{
    // Thin entry point: builds the central GameState from config and exposes it,
    // then drives the ticket lifecycle. Systems/UI bind to State/TicketSlotManager
    // rather than this class growing gameplay logic itself.
    public class GameManager : MonoBehaviour
    {
        [SerializeField] private GameConfig gameConfig;
        [SerializeField] private TicketGenerationConfig ticketGenerationConfig;
        [SerializeField] private FoodCatalog foodCatalog;

        public GameState State { get; private set; }
        public TicketSlotManager TicketSlotManager { get; private set; }

        private TicketFactory ticketFactory;

        private void Awake()
        {
            State = new GameState(gameConfig);
            ticketFactory = new TicketFactory(ticketGenerationConfig);
            TicketSlotManager = new TicketSlotManager(State, CreateNextTicket);
            TicketSlotManager.FillEmptySlots();
        }

        private void Update()
        {
            TicketSlotManager.Tick(Time.deltaTime);
        }

        private Ticket CreateNextTicket()
        {
            var patienceType = ticketFactory.PickRandomPatienceType();
            var customerName = ticketFactory.PickRandomCustomerName();
            return ticketFactory.Create(foodCatalog.Items, customerName, patienceType);
        }
    }
}
