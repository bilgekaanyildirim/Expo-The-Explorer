using System;
using System.Collections.Generic;
using System.Linq;
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
        [SerializeField] private EconomyConfig economyConfig;
        [SerializeField] private LivesConfig livesConfig;

        // No boardDistributionConfig field on purpose: board-distribution balancing is
        // authored per Day and arrives with the Day (decisions.md D-004). The shared asset
        // is now Day-Editor-only -- a seed for new Days. Wiring it here again would put a
        // second authority back on the runtime path.

        // Exposed for the ticket card's timer bar: the two ratios that decide
        // where it changes colour are the SAME ones that decide the tip tier, and
        // they live on EconomyConfig because they decide money (CLAUDE.md — Tip
        // Tiers). TicketCardsView reads them through here rather than keeping its
        // own copy next to the bar's colours, so the colour on screen and the tip
        // actually paid can never disagree about where a tier starts.
        public EconomyConfig EconomyConfig => economyConfig;

        public GameState State { get; private set; }
        public TicketSlotManager TicketSlotManager { get; private set; }
        public TrayManager TrayManager { get; private set; }
        public LivesManager LivesManager { get; private set; }
        public DayLifecycleManager DayLifecycleManager { get; private set; }

        private TicketFactory ticketFactory;
        private EconomyCalculator economyCalculator;
        private IReadOnlyList<DayDefinition> dayCatalog;
        private DayTicketSequenceProvider dayTicketSequenceProvider;

        // (Re)constructed alongside dayTicketSequenceProvider (see
        // RefreshDayTicketSequenceProvider) rather than once for the whole session --
        // its leakedTickets dedup state must not survive into a retried/new Day, or a
        // ticket that already leaked once in the PREVIOUS attempt would wrongly stay
        // "already leaked" (never leak again) in this one. Since D-004 there is a second
        // reason: each Day carries its own balancing, so a distributor built for the
        // previous Day would keep applying that Day's numbers.
        private BoardDistributor boardDistributor;

        // The single writer of SoftMoney/Gems (economy-plan.md Adım 1). It also
        // owns the day-start snapshot this class used to keep in a field of its
        // own -- GameState's balance setters are internal to ProgressionSystem
        // now, so a `State.SoftMoney = x` here would not compile.
        private Wallet wallet;

        // Position in dayCatalog, not a Day's JSON dayIndex (that only decides
        // sort order) -- null until a Day catalog exists (PR-7), so every
        // consumer falls back to the pre-Day-system GameConfig behavior.
        private DayDefinition CurrentDay =>
            DayCatalogNavigator.GetDayAt(dayCatalog, State.CurrentDayIndex);

        private void Awake()
        {
            EnsurePhysics2DRaycaster();

            State = new GameState(gameConfig);

            // Nothing is loaded from disk: the only thing that ever persisted was
            // Xp/Level, and that system is gone. PlayerProfileStore is still there
            // as the save boundary, unwired, waiting for the first piece of state
            // that actually needs to survive a session (CurrentDayIndex, then the
            // wallet -- see .claude/economy-plan.md).
            //
            // Constructed straight after State because its own constructor takes
            // the day-start snapshot, and because LivesManager below cannot charge
            // for a Continue without it.
            wallet = new Wallet(State);

            ticketFactory = new TicketFactory(ticketGenerationConfig);
            LivesManager = new LivesManager(State, livesConfig, wallet);
            DayLifecycleManager = new DayLifecycleManager(State);
            TicketSlotManager = new TicketSlotManager(State, CreateNextTicket, HandleLifeLoss);
            TrayManager = new TrayManager(State, slotIndex => TicketSlotManager.DeliverTicket(slotIndex), HandleLifeLoss);
            economyCalculator = new EconomyCalculator(economyConfig);
            dayCatalog = DayCatalogParser.ParseAll(new DayJsonSource().LoadAll(), foodCatalog);
            RefreshDayTicketSequenceProvider();

            // Subscribe before the initial fill so the first 3 tickets trigger
            // board playback too, not just later deliveries/cancellations.
            State.TicketAssigned.Subscribe(OnTicketAssigned);
            State.TicketDelivered.Subscribe(OnTicketDelivered);
            State.DayRetried.Subscribe(OnDayRetried);
            ApplyDayStartBoardPreSeed();
            TicketSlotManager.FillEmptySlots();
        }

        private void OnDestroy()
        {
            State.TicketAssigned.Unsubscribe(OnTicketAssigned);
            State.TicketDelivered.Unsubscribe(OnTicketDelivered);
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

        // Production is order-triggered (GDD Section 4), not a continuous poll --
        // every time a slot's ticket changes (a new arrival, OR a slot going empty
        // once the Day's authored sequence is exhausted), BoardDistributor
        // re-evaluates live what the board needs (required-pool top-up, then
        // noise-pool leaking) from the CURRENT active + upcoming ticket state.
        // Deliberately unconditional on assignment.Ticket being non-null: with
        // GuaranteedTicketCount covering only a few tickets per round, a later
        // slot-emptied event is what finally makes an earlier, not-yet-covered
        // active ticket "earliest" and eligible -- skipping this on a null
        // assignment was a real bug (found via playtest): the last tickets of a
        // finite Day could reach the end of the sequence without ever getting a
        // qualifying round, since no further arrivals remained to trigger one.
        // That slot's tray (if a timeout left it holding orphaned items) is
        // cleared back onto the board either way. No fallback: an authored Day
        // is required (PR-7).
        private void OnTicketAssigned((int SlotIndex, Ticket Ticket) assignment)
        {
            if (CurrentDay == null)
            {
                throw new InvalidOperationException("No Day loaded -- board playback requires an authored Day (PR-7).");
            }

            var activeTickets = State.TicketSlots.Where(t => t != null).ToList();
            var lookaheadCount = Math.Max(GameState.TicketSlotCount, CurrentDay.TicketRuntime.UpcomingQueueSize);
            var upcomingTickets = dayTicketSequenceProvider.PeekUpcoming(lookaheadCount);
            boardDistributor.OnOrderPlaced(activeTickets, upcomingTickets);

            TrayManager.OnTicketAssigned(assignment.SlotIndex);
        }

        // Banks the Economy Module's payout (GDD Section 9 — Order Value + the
        // tier's tip) the instant a ticket is delivered. TicketDelivered fires
        // with the ticket that just left, still holding its final
        // RemainingSeconds, so the tier is read from that same instance -- which
        // is the only moment the delivered ticket's remaining time still exists.
        private void OnTicketDelivered((int SlotIndex, Ticket Ticket) delivery)
        {
            var payout = economyCalculator.CalculatePayout(delivery.Ticket);
            wallet.EarnSoftMoney(Mathf.RoundToInt(payout.Total));
            DayLifecycleManager.RecordDelivery(payout);
        }

        // Both life-loss paths (TicketSlotManager's timeout, TrayManager's wrong
        // delivery) share this single delegate so the Day Complete popup's
        // "Orders failed" count catches either cause -- there's no other place
        // both funnel through.
        private void HandleLifeLoss()
        {
            LivesManager.LoseLife();
            DayLifecycleManager.RecordFailure();
        }

        // The day ended in failure and is being replayed, so this attempt's
        // earnings are taken back and its spending is not (economy-plan.md Adım 2
        // -- "a day attempt is atomic"). Without this the day's income survived a
        // failed day, which made repeatedly losing a day a way to farm money.
        //
        // Wired to DayRetried rather than called from RetryDay directly because
        // the event already exists for exactly this and RetryDay is also reachable
        // from UI. Note a paid Continue never publishes DayRetried, so continuing
        // correctly keeps what the day has earned so far -- only abandoning the
        // attempt gives it up.
        private void OnDayRetried(int _)
        {
            wallet.RevertToDayStart();
        }

        // Free alternative to the paid Continue flow (GameOverPopupView) --
        // abandons the current day attempt and restarts it at the same
        // difficulty (difficulty scale-down on retry is a still-open GDD
        // question, CLAUDE.md Section 4, deliberately not addressed here) --
        // a retry always replays the Day itself. Order matters for the
        // first three calls: Board.Clear() -> ApplyDayStartBoardPreSeed() ->
        // TrayManager.DiscardAllForNewDay() -> TicketSlotManager.
        // ResetSlotsForNewDay() -- the pre-seed must land on the freshly
        // cleared board before slots start refilling and cascading into
        // OnTicketAssigned's own board playback, or a different order
        // reintroduces stale items. LivesManager/DayLifecycleManager are
        // independent of those and of each other.
        public void RetryDay()
        {
            var ticketsBeforeRetry = State.TicketsDeliveredToday;
            RefreshDayTicketSequenceProvider();

            State.Board.Clear();
            ApplyDayStartBoardPreSeed();
            TrayManager.DiscardAllForNewDay();
            TicketSlotManager.ResetSlotsForNewDay();
            LivesManager.RetryDay();
            DayLifecycleManager.ResetForNewDay();

            State.DayRetried.Publish(ticketsBeforeRetry);
        }

        // Voluntary redo of a day that already succeeded (Day Complete
        // popup's Retry button, for a better star score) -- distinct from
        // RetryDay, which is the free life-loss-failure path. Rolls SoftMoney
        // back to the Wallet's day-start snapshot, so replaying for stars can't
        // stack income on top of what the day already paid out. Mirrors
        // RetryDay's reset order otherwise, including a full Lives refill.
        public void RetryCompletedDay()
        {
            wallet.RevertToDayStart();
            RefreshDayTicketSequenceProvider();

            State.Board.Clear();
            ApplyDayStartBoardPreSeed();
            TrayManager.DiscardAllForNewDay();
            TicketSlotManager.ResetSlotsForNewDay();
            LivesManager.RetryDay();
            DayLifecycleManager.ResetForNewDay();
        }

        // Free-win path: the day's goal was hit (GameManager.OnDayCompleted
        // already paused ticket production). Mirrors RetryDay's reset order
        // but deliberately skips LivesManager -- Lives are NOT reset on a
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
            wallet.CaptureDayStart();
            RefreshDayTicketSequenceProvider();

            State.Board.Clear();
            ApplyDayStartBoardPreSeed();
            TrayManager.DiscardAllForNewDay();
            TicketSlotManager.ResetSlotsForNewDay();
            DayLifecycleManager.ResetForNewDay();

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
        // reset to 0, rather than resetting a single long-lived instance. Also
        // (re)constructs boardDistributor for the same reason -- see its field
        // comment for why a stale instance can't carry over.
        private void RefreshDayTicketSequenceProvider()
        {
            dayTicketSequenceProvider = CurrentDay != null
                ? new DayTicketSequenceProvider(CurrentDay.TicketSequence, CurrentDay.TicketRuntime, ticketFactory)
                : null;
            boardDistributor = CurrentDay != null
                ? new BoardDistributor(State, CurrentDay.BoardDistribution)
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
