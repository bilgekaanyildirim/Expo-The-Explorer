using System.Collections.Generic;
using System.IO;
using ExpoTheExplorer.Core;
using ExpoTheExplorer.Data;
using ExpoTheExplorer.Systems.BoardDistribution;
using ExpoTheExplorer.Systems.DayLifecycle;
using ExpoTheExplorer.Systems.DaySystem;
using ExpoTheExplorer.Systems.EconomySystem;
using ExpoTheExplorer.Systems.LivesSystem;
using ExpoTheExplorer.Systems.ProgressionSystem;
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
        [SerializeField] private LevelProgressionConfig levelProgressionConfig;

        public GameState State { get; private set; }
        public TicketSlotManager TicketSlotManager { get; private set; }
        public TrayManager TrayManager { get; private set; }
        public LivesManager LivesManager { get; private set; }
        public PlayerProfileStore PlayerProfileStore { get; private set; }
        public LevelManager LevelManager { get; private set; }

        private TicketFactory ticketFactory;
        private BoardDistributor boardDistributor;
        private EconomyCalculator economyCalculator;
        private DayLifecycleManager dayLifecycleManager;
        private IReadOnlyList<DayDefinition> dayCatalog;

        // Position in dayCatalog, not a Day's JSON dayIndex (that only decides
        // sort order) -- null until a Day catalog exists (PR-7), so every
        // consumer falls back to the pre-Day-system GameConfig behavior.
        private DayDefinition CurrentDay => DayCatalogNavigator.GetDayAt(dayCatalog, State.CurrentDayIndex);

        private void Awake()
        {
            EnsurePhysics2DRaycaster();

            State = new GameState(gameConfig);

            // Loads whatever was last committed to disk, falling back to this
            // session's config-seeded Xp/Level (not hardcoded 0/0) when there's no
            // save yet -- so GameConfig.StartingXp/StartingLevel round-trips
            // correctly even once a designer sets StartingLevel to a nonzero value.
            // Nothing calls Save() yet -- the real commit trigger (day completed
            // successfully) doesn't exist in the game yet and is wired up in a
            // later PR.
            PlayerProfileStore = new PlayerProfileStore(Path.Combine(Application.persistentDataPath, "player_profile.json"));
            var profile = PlayerProfileStore.Load(new PlayerProfile { Xp = State.Xp, Level = State.Level });
            State.Xp = profile.Xp;
            State.Level = profile.Level;

            ticketFactory = new TicketFactory(ticketGenerationConfig);
            LivesManager = new LivesManager(State, livesConfig);
            TicketSlotManager = new TicketSlotManager(State, CreateNextTicket, LivesManager.LoseLife, ticketGenerationConfig.UpcomingQueueSize);
            boardDistributor = new BoardDistributor(State, boardDistributionConfig);
            TrayManager = new TrayManager(State, slotIndex => TicketSlotManager.DeliverTicket(slotIndex), LivesManager.LoseLife);
            economyCalculator = new EconomyCalculator(economyConfig);
            dayCatalog = DayCatalogParser.ParseAll(new DayJsonSource().LoadAll(), foodCatalog);
            dayLifecycleManager = new DayLifecycleManager(State, () => CurrentDay?.TicketsRequiredForDay ?? gameConfig.TicketsRequiredPerDay);
            LevelManager = new LevelManager(State, levelProgressionConfig, PlayerProfileStore, profile);

            // Subscribe before the initial fill so the first 3 tickets trigger
            // board distribution too, not just later deliveries/cancellations.
            State.TicketAssigned.Subscribe(OnTicketAssigned);
            State.TicketDelivered.Subscribe(OnTicketDelivered);
            State.DayCompleted.Subscribe(OnDayCompleted);
            State.DayRetried.Subscribe(OnDayRetried);
            TicketSlotManager.FillEmptySlots();
        }

        private void OnDestroy()
        {
            State.TicketAssigned.Unsubscribe(OnTicketAssigned);
            State.TicketDelivered.Unsubscribe(OnTicketDelivered);
            State.DayCompleted.Unsubscribe(OnDayCompleted);
            State.DayRetried.Unsubscribe(OnDayRetried);
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
            dayLifecycleManager.RecordDelivery();

            var xpResult = LevelManager.CalculateXp(delivery.Ticket);
            LevelManager.AddXp(Mathf.RoundToInt(xpResult.TotalXp));
        }

        // The day's Xp/Level gains become permanent the instant the daily goal
        // is reached (GDD Section 10/11) -- Continue never fires DayCompleted
        // or DayRetried, only a real Retry does, so currency-continue
        // correctly leaves earned XP untouched either way.
        private void OnDayCompleted(int _)
        {
            LevelManager.CommitProgress();
            TicketSlotManager.PauseForDayComplete();
        }

        // Wipes this attempt's Xp/Level gains back to the last commit (CLAUDE.md
        // Section 3 -- Progression/Lives System: retry discards the day's XP).
        private void OnDayRetried(int _)
        {
            LevelManager.DiscardToLastCommitted();
        }

        // Free alternative to the paid Continue flow (GameOverPopupView) --
        // abandons the current day attempt and restarts it at the same
        // difficulty (difficulty scale-down on retry is a still-open GDD
        // question, CLAUDE.md Section 4, deliberately not addressed here).
        // Order matters for the first three calls: Board.Clear() ->
        // TrayManager.DiscardAllForNewDay() -> TicketSlotManager.
        // ResetSlotsForNewDay() are coupled through OnTicketAssigned's
        // existing cascade into boardDistributor/TrayManager above, and
        // running them in a different order reintroduces stale items onto
        // the board. LivesManager/dayLifecycleManager are independent of
        // those three and of each other.
        public void RetryDay()
        {
            var ticketsBeforeRetry = State.TicketsDeliveredToday;

            State.Board.Clear();
            TrayManager.DiscardAllForNewDay();
            TicketSlotManager.ResetSlotsForNewDay();
            LivesManager.RetryDay();
            dayLifecycleManager.ResetForNewDay();

            State.DayRetried.Publish(ticketsBeforeRetry);
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
