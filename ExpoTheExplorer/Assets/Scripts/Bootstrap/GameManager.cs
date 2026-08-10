using System;
using System.Collections.Generic;
using System.IO;
using ExpoTheExplorer.Core;
using ExpoTheExplorer.Data;
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
        private EconomyCalculator economyCalculator;
        private DayLifecycleManager dayLifecycleManager;
        private IReadOnlyList<DayDefinition> dayCatalog;
        private DayTicketSequenceProvider dayTicketSequenceProvider;

        // Set by RetryDay, cleared by AdvanceToNextDay -- stays true across
        // repeated retries of the SAME Day, not just the first one, so
        // CurrentDay keeps resolving to that Day's RetryVariant (if it has
        // one) for as long as the player is retrying it.
        private bool isRetryAttempt;

        // Position in dayCatalog, not a Day's JSON dayIndex (that only decides
        // sort order) -- null until a Day catalog exists (PR-7), so every
        // consumer falls back to the pre-Day-system GameConfig behavior.
        private DayDefinition CurrentDay =>
            DayCatalogNavigator.GetEffectiveDay(DayCatalogNavigator.GetDayAt(dayCatalog, State.CurrentDayIndex), isRetryAttempt);

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
            TicketSlotManager = new TicketSlotManager(State, CreateNextTicket, LivesManager.LoseLife);
            TrayManager = new TrayManager(State, slotIndex => TicketSlotManager.DeliverTicket(slotIndex), LivesManager.LoseLife);
            economyCalculator = new EconomyCalculator(economyConfig);
            dayCatalog = DayCatalogParser.ParseAll(new DayJsonSource().LoadAll(), foodCatalog);
            dayLifecycleManager = new DayLifecycleManager(State);
            RefreshDayTicketSequenceProvider();
            LevelManager = new LevelManager(State, levelProgressionConfig, PlayerProfileStore, profile);

            // Subscribe before the initial fill so the first 3 tickets trigger
            // board playback too, not just later deliveries/cancellations.
            State.TicketAssigned.Subscribe(OnTicketAssigned);
            State.TicketDelivered.Subscribe(OnTicketDelivered);
            State.DayCompleted.Subscribe(OnDayCompleted);
            State.DayRetried.Subscribe(OnDayRetried);
            ApplyDayStartBoardPreSeed();
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
        // every time a new ticket enters a slot, the Day's authored board
        // timeline plays back whatever step corresponds to that exact ticket
        // (ArrivalSequence == its position in DayDefinition.TicketSequence),
        // and that slot's tray (if a timeout left it holding orphaned items)
        // is cleared back onto the board. No fallback: an authored Day is
        // required (PR-7).
        private void OnTicketAssigned((int SlotIndex, Ticket Ticket) assignment)
        {
            if (CurrentDay == null)
            {
                throw new InvalidOperationException("No Day loaded -- board playback requires an authored Day (PR-7).");
            }

            // Ticket is null once the Day's authored sequence is exhausted -- the slot is
            // just left empty (TicketSlotManager.AssignTicket), nothing to play back.
            if (assignment.Ticket != null)
            {
                DayBoardTimelinePlayer.ApplyForStep(State.Board, CurrentDay.BoardTimeline, assignment.Ticket.ArrivalSequence);
            }
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
        // correctly leaves earned XP untouched either way. TicketSlotManager
        // already set IsDayComplete itself before publishing this (see its
        // AssignTicket) -- nothing left to pause here.
        private void OnDayCompleted(int _)
        {
            LevelManager.CommitProgress();
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
        // question, CLAUDE.md Section 4, deliberately not addressed here) --
        // or, if the Day has its own authored RetryVariant, that instead
        // (isRetryAttempt flips CurrentDay over to it). Order matters for the
        // first three calls: Board.Clear() -> ApplyDayStartBoardPreSeed() ->
        // TrayManager.DiscardAllForNewDay() -> TicketSlotManager.
        // ResetSlotsForNewDay() -- the pre-seed must land on the freshly
        // cleared board before slots start refilling and cascading into
        // OnTicketAssigned's own board playback, or a different order
        // reintroduces stale items. LivesManager/dayLifecycleManager are
        // independent of those and of each other.
        public void RetryDay()
        {
            var ticketsBeforeRetry = State.TicketsDeliveredToday;
            isRetryAttempt = true;
            RefreshDayTicketSequenceProvider();

            State.Board.Clear();
            ApplyDayStartBoardPreSeed();
            TrayManager.DiscardAllForNewDay();
            TicketSlotManager.ResetSlotsForNewDay();
            LivesManager.RetryDay();
            dayLifecycleManager.ResetForNewDay();

            State.DayRetried.Publish(ticketsBeforeRetry);
        }

        // Free-win path: the day's goal was hit (GameManager.OnDayCompleted
        // already paused ticket production). Mirrors RetryDay's reset order
        // but deliberately skips LivesManager -- Lives/Xp are NOT reset on a
        // successful advance, only a failed retry pays that cost (CLAUDE.md
        // Section 3) -- and never publishes DayRetried.
        public bool AdvanceToNextDay()
        {
            var nextIndex = State.CurrentDayIndex + 1;
            if (dayCatalog == null || nextIndex >= dayCatalog.Count)
            {
                return false; // last authored Day -- PR-9 decides what the UI shows
            }

            State.CurrentDayIndex = nextIndex;
            isRetryAttempt = false;
            RefreshDayTicketSequenceProvider();

            State.Board.Clear();
            ApplyDayStartBoardPreSeed();
            TrayManager.DiscardAllForNewDay();
            TicketSlotManager.ResetSlotsForNewDay();
            dayLifecycleManager.ResetForNewDay();

            return true;
        }

        // No fallback: an authored Day is required (PR-7) -- until then this
        // throws instead of silently falling back to the old procedural
        // TicketFactory.Create path (removed; TicketFactory itself stays,
        // just for PickRandomCustomerName's cosmetic reuse inside
        // TicketEntryFactory).
        private Ticket CreateNextTicket()
        {
            if (dayTicketSequenceProvider == null)
            {
                throw new InvalidOperationException("No Day loaded -- ticket generation requires an authored Day (PR-7).");
            }

            // A Day's authored sequence is finite and WILL run out mid-day (see
            // TicketSlotManager.AssignTicket) -- null tells it to leave that slot empty
            // instead of trying (and failing) to draw one more.
            return dayTicketSequenceProvider.HasNext ? dayTicketSequenceProvider.NextTicket() : null;
        }

        // Re-pointed every time CurrentDay could have changed (Awake, RetryDay,
        // AdvanceToNextDay) -- a fresh provider per Day/retry-variant, cursor
        // reset to 0, rather than resetting a single long-lived instance.
        private void RefreshDayTicketSequenceProvider()
        {
            dayTicketSequenceProvider = CurrentDay != null
                ? new DayTicketSequenceProvider(CurrentDay.TicketSequence, ticketGenerationConfig, ticketFactory)
                : null;
        }

        // Applies the Day's triggerStepIndex == -1 board entries once, right
        // after the board is cleared and before any ticket gets assigned into
        // a slot -- everything after this point plays back per-ticket via
        // OnTicketAssigned instead.
        private void ApplyDayStartBoardPreSeed()
        {
            if (CurrentDay == null) return;
            DayBoardTimelinePlayer.ApplyForStep(State.Board, CurrentDay.BoardTimeline, -1);
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
