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
using ExpoTheExplorer.Systems.MetaSystem;
using ExpoTheExplorer.Session;
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
    // SessionHost rather than MonoBehaviour since 5b (decisions.md D-022): the shared HUD
    // binds to "whichever component provides this scene's session", and the day scene's
    // provider is this class. Changing the base class does not change the component's type
    // identity, so nothing serialized in SampleScene is disturbed.
    public class GameManager : SessionHost
    {
        [SerializeField] private GameConfig gameConfig;
        [SerializeField] private TicketGenerationConfig ticketGenerationConfig;
        [SerializeField] private FoodCatalog foodCatalog;
        [SerializeField] private EconomyConfig economyConfig;
        [SerializeField] private LivesConfig livesConfig;

        // The four numbers behind the day's star rating (decisions.md D-060). Required,
        // unlike metaCatalog below: without it every completed day scores 0 stars and pays
        // no gems, so Awake says so as an ERROR rather than a note.
        [Tooltip("Star thresholds and the two failure penalties. A completed day scores 0 stars while this is empty.")]
        [SerializeField] private StarScoreConfig starScoreConfig;

        // OPTIONAL, and read for exactly one question: will the day the player is about to
        // enter open a Day-unlocked prop? If it does, "Next Day" sends them to the main
        // screen so the celebration (decisions.md D-041) cannot be skipped.
        //
        // Optional rather than required because a day scene must not be broken by a missing
        // reference for what is, from the day's point of view, a routing nicety. But NOT
        // silent: Awake says so once, because an empty field and a catalog with no upcoming
        // unlocks look identical on screen -- the same reason HudWalletSource logs which mode
        // it resolved to.
        [Tooltip("Only used to decide whether finishing this day should return the player to the main screen, so a newly unlocked prop is actually seen. Leave it empty and that routing simply does not happen.")]
        [SerializeField] private MetaCatalog metaCatalog;

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

        // Everything a PLAYER owns rather than a day: state, wallet, lives, the Day
        // catalog, the day index and the owned meta props. Shared with the main screen
        // (decisions.md D-021), which is why it is a class and not more fields here.
        public override GameSession Session => session;

        private GameSession session;

        // Forwarded, not re-stored. Keeping the property NAMES identical is what let this
        // extraction happen without rewiring a single view in the scene -- roughly a dozen
        // components read GameManager.State, and a rename would have touched all of them
        // for no gain.
        public GameState State => Session?.State;
        public LivesManager LivesManager => Session?.LivesManager;

        public TicketSlotManager TicketSlotManager { get; private set; }
        public TrayManager TrayManager { get; private set; }
        public DayLifecycleManager DayLifecycleManager { get; private set; }

        private TicketFactory ticketFactory;
        private EconomyCalculator economyCalculator;
        private DayTicketSequenceProvider dayTicketSequenceProvider;

        // (Re)constructed alongside dayTicketSequenceProvider (see
        // RefreshDayTicketSequenceProvider) rather than once for the whole session --
        // its leakedTickets dedup state must not survive into a retried/new Day, or a
        // ticket that already leaked once in the PREVIOUS attempt would wrongly stay
        // "already leaked" (never leak again) in this one. Since D-004 there is a second
        // reason: each Day carries its own balancing, so a distributor built for the
        // previous Day would keep applying that Day's numbers.
        private BoardDistributor boardDistributor;

        // Still the single writer of SoftMoney/Gems (economy-plan.md Adım 1); it just
        // lives on the session now, because the main screen needs the same one.
        private Wallet wallet => Session.Wallet;

        // No PlayerProfileStore field any more: GameSession composes what is written and
        // owns the only Save (decisions.md D-021). The four call sites below still own the
        // "a day attempt is atomic" contract -- WHEN to save is a game-flow decision and
        // stays here; WHAT gets written is a schema decision and does not.

        // Position in dayCatalog, not a Day's JSON dayIndex (that only decides
        // sort order) -- null until a Day catalog exists (PR-7), so every
        // consumer falls back to the pre-Day-system GameConfig behavior.
        private DayDefinition CurrentDay => Session.CurrentDay;

        // The star score's denominator: every ticket this Day will hand out, its own time
        // limit summed (decisions.md D-060). Asked of the Day rather than tracked here, so
        // it cannot drift from the limits TicketEntryFactory actually gives the tickets.
        // Zero with no Day loaded, which reads as "scored nothing" downstream.
        private float CurrentDayTicketSeconds => CurrentDay == null ? 0f : CurrentDay.TotalTicketSeconds;

        private void Awake()
        {
            EnsurePhysics2DRaycaster();

            // The whole session half of this method in one line (decisions.md D-021):
            // state, wallet + persisted balances, lives + persisted lives, the Day catalog
            // and the clamped day index, with the four ordering rules between them kept in
            // one place instead of duplicated per screen.
            //
            // A missing, corrupt or unversioned file loads as 0/0 (see PlayerProfileStore),
            // which is also what a brand-new player gets.
            session = new GameSession(gameConfig, livesConfig, foodCatalog);

            if (metaCatalog == null)
            {
                Debug.Log(
                    $"{nameof(GameManager)}: no MetaCatalog wired, so finishing a day will never route to the " +
                    "main screen for a prop unlock. Drag the catalog in if you want unlocks to be shown.", this);
            }

            if (starScoreConfig == null)
            {
                Debug.LogError(
                    $"{nameof(GameManager)}: no {nameof(StarScoreConfig)} wired, so every completed day will score " +
                    "0 stars and pay no gems. Drag Assets/Data/StarScoreConfig.asset into the Star Score Config slot.",
                    this);
            }

            ticketFactory = new TicketFactory(ticketGenerationConfig);
            DayLifecycleManager = new DayLifecycleManager(State, starScoreConfig);

            // The two life-loss paths pass DIFFERENT methods here, and that is the only
            // place the difference is decided (decisions.md D-060): a timeout and a wrong
            // delivery cost the star score different amounts, and neither system should
            // have to know that. TicketSlotManager and TrayManager keep taking a plain
            // Action and stay unaware there are now two of them.
            TicketSlotManager = new TicketSlotManager(State, CreateNextTicket, HandleTicketTimeout);
            TrayManager = new TrayManager(State, slotIndex => TicketSlotManager.DeliverTicket(slotIndex), HandleWrongDelivery);
            economyCalculator = new EconomyCalculator(economyConfig);

            // The catalog parse and the day-index clamp moved into GameSession, which is
            // where their ordering rule lives now. Both still happen BEFORE this line, so
            // the provider and distributor are still built off a resolved CurrentDay.
            RefreshDayTicketSequenceProvider();

            // The day's star budget, handed over on the way in exactly as it is on the
            // three retry/advance paths -- so day one of a session is scored by the same
            // line as every day after it. Nothing else here needs resetting (the state is
            // brand new), which is why this is the only day-start call.
            DayLifecycleManager.ResetForNewDay(CurrentDayTicketSeconds);

            // Subscribe before the initial fill so the first 3 tickets trigger
            // board playback too, not just later deliveries/cancellations.
            State.TicketAssigned.Subscribe(OnTicketAssigned);
            State.TicketDelivered.Subscribe(OnTicketDelivered);
            State.DayRetried.Subscribe(OnDayRetried);
            State.DayCompleted.Subscribe(OnDayCompleted);
            ApplyDayStartBoardPreSeed();
            TicketSlotManager.FillEmptySlots();
        }

        private void OnDestroy()
        {
            State.TicketAssigned.Unsubscribe(OnTicketAssigned);
            State.TicketDelivered.Unsubscribe(OnTicketDelivered);
            State.DayRetried.Unsubscribe(OnDayRetried);
            State.DayCompleted.Unsubscribe(OnDayCompleted);
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

        // RECORDS the Economy Module's payout (GDD Section 9 — Order Value + the
        // tier's tip) the instant a ticket is delivered, and deliberately does not
        // pay it. TicketDelivered fires with the ticket that just left, still holding
        // its final RemainingSeconds, so the tier is read from that same instance --
        // which is the only moment the delivered ticket's remaining time still exists.
        //
        // The wallet call that used to sit here is gone (D-057): nothing reaches the
        // player's balance until the day is COMPLETED, and the receipt this line feeds
        // is what the day then owes. DayLifecycleManager.RecordDelivery already rounds
        // once per delivery, so its running Total is to the coin the same number the
        // per-delivery EarnSoftMoney calls used to add up to -- moving the payment did
        // not move the amount.
        private void OnTicketDelivered((int SlotIndex, Ticket Ticket) delivery)
        {
            var payout = economyCalculator.CalculatePayout(delivery.Ticket);
            DayLifecycleManager.RecordDelivery(payout);
        }

        // Both life-loss paths (TicketSlotManager's timeout, TrayManager's wrong
        // delivery) funnel through the same method below so the Day Complete popup's
        // "Orders failed" count catches either cause -- there's no other place
        // both funnel through.
        //
        // They now arrive through two named entry points instead of one shared delegate
        // (decisions.md D-060), because the star score charges them different amounts. The
        // split lives HERE and only here: this class is the one that hands each system its
        // delegate, so no system had to learn what kind of failure it causes, and neither
        // can report the wrong kind.
        private void HandleTicketTimeout() => HandleLifeLoss(DayFailureCause.Timeout);

        private void HandleWrongDelivery() => HandleLifeLoss(DayFailureCause.WrongDelivery);

        private void HandleLifeLoss(DayFailureCause cause)
        {
            LivesManager.LoseLife();
            DayLifecycleManager.RecordFailure(cause);
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

        // The day's earnings stop being provisional the instant its goal is
        // reached, so this is the only place a wallet reaches disk on the winning
        // path (economy-plan.md Adım 4). Nothing is written on a failed day
        // precisely because nothing about it is permanent -- which is also why
        // quitting mid-day loses that day's income rather than banking it.
        private void OnDayCompleted(int _)
        {
            pendingReward = new DayRewardPurse(
                DayLifecycleManager.Total,
                DayLifecycleManager.StarCount * gameConfig.GemsPerStar);

            // Still saves, and still saves NOTHING of the reward. What this write banks
            // is everything the day changed that is not money: lives lost, and the day
            // index. Dropping it to "the handover saves" would mean a player who force
            // quits on the popup relaunches with their pre-day lives, which is a worse
            // trade than the ~2s window the money now sits in.
            SaveProfile();
        }

        // What the completed day owes the player but has not handed over yet. Null
        // whenever no day is waiting to pay out, which is every moment except between
        // DayCompleted and the player leaving the finished day.
        //
        // The debt exists because the payout is now a PERFORMANCE (D-057): coins and gems
        // reach the balance as the Day Complete popup's reward flight lands each icon on
        // its HUD counter, so what the player sees arrive and what they actually own are
        // the same event rather than two events that have to be kept in step.
        private DayRewardPurse pendingReward;

        public int PendingRewardSoftMoney => pendingReward?.SoftMoneyRemaining ?? 0;
        public int PendingRewardGems => pendingReward?.GemsRemaining ?? 0;

        // The two hand-over steps the reward flight calls, one per icon that lands. Both
        // go through Wallet like every other balance change in the game -- the purse only
        // decides how much of the debt is allowed out, it never touches a balance itself.
        // Both are clamped by the purse, so a miscounted animation can pay out less than
        // the day earned but never more.
        public void ClaimRewardGems(int count)
        {
            if (pendingReward == null) return;

            wallet.EarnGems(pendingReward.TakeGems(count));
        }

        public void ClaimRewardSoftMoney(int amount)
        {
            if (pendingReward == null) return;

            wallet.EarnSoftMoney(pendingReward.TakeSoftMoney(amount));
        }

        // Called by the reward flight when its last icon has landed. Separate from
        // CommitPendingReward only in that it also writes the file: the exits below
        // already save for their own reasons, and this one has no other reason to.
        public void CompleteRewardHandover()
        {
            if (CommitPendingReward()) SaveProfile();
        }

        // The safety net under the whole animation, and the reason a broken or unwired
        // reward flight cannot cost the player money: every exit from a finished day runs
        // this first, so whatever the flight did not hand over is paid in full before the
        // player can go anywhere. Returns whether anything was actually credited, so the
        // callers that already save do not gain a second write for nothing.
        private bool CommitPendingReward()
        {
            if (pendingReward == null) return false;

            var softMoney = pendingReward.TakeAllSoftMoney();
            var gems = pendingReward.TakeAllGems();
            pendingReward = null;

            wallet.EarnSoftMoney(softMoney);
            wallet.EarnGems(gems);

            return softMoney > 0 || gems > 0;
        }

        // The opposite of committing, for the one exit where the day's result is being
        // thrown away rather than collected: a voluntary redo. The debt is voided instead
        // of paid, which is what stops a finished day being replayed for its reward over
        // and over. Anything the flight already handed over is a real balance by then and
        // is taken back by RevertToDayStart, not by this.
        private void DiscardPendingReward()
        {
            pendingReward = null;
        }

        // Builds the profile from GameState's public getters rather than asking
        // the wallet for its numbers: the wallet owns the RULES for changing
        // balances, GameState holds the values.
        // WHEN to save is game flow and stays here; WHAT gets written is schema and moved
        // to GameSession.Save (decisions.md D-021). The four callers below are unchanged.
        private void SaveProfile() => Session.Save();

        // Day Complete popup's "Go Back". The day is finished, so what the main
        // screen should offer next is the NEXT Day -- that index is persisted here
        // and the scene is then dropped.
        //
        // Deliberately NOT AdvanceToNextDay: that method also clears the board,
        // discards trays and refills slots, which would (a) be thrown away
        // microseconds later as the scene unloads and (b) cascade TicketAssigned
        // through views that are already tearing down. Only the index needs to
        // survive; everything else is rebuilt by Awake in the fresh day scene.
        //
        // No wallet work: this day already completed, so OnDayCompleted banked its
        // earnings, and the next day's baseline is re-taken by ApplyPersistedBalances
        // when the day scene next loads.
        // What "Next Day" actually asks for (decisions.md D-042). Returns true when the
        // player stays in THIS scene on a fresh day, false when the scene is being dropped
        // for the main screen -- the caller only needs to know whether it still has a popup
        // to hide.
        //
        // The whole decision lives here rather than in the popup. That view already asked one
        // question and branched on it; a second question would have made it the place where
        // flow is decided, and flow is this class's job.
        //
        // Two reasons to leave for the main screen, and they resolve in this order:
        //   1. The next day opens a Day-unlocked prop. The player is sent to see it, because
        //      a celebration nobody is present for is not a celebration.
        //   2. There is no next day. The pre-existing fallback, unchanged.
        // Both hand off to ReturnToMainScreenFromCompletedDay, which already persists the
        // next index and loads the scene -- and which deliberately does NOT go through
        // AdvanceToNextDay, since clearing the board and refilling slots microseconds before
        // the scene unloads is either wasted or an event cascade through tearing-down views.
        // That reasoning applies to case 1 exactly as it did to case 2.
        public bool TryContinueIntoNextDay()
        {
            if (NextDayOpensAProp())
            {
                ReturnToMainScreenFromCompletedDay();
                return false;
            }

            if (AdvanceToNextDay()) return true;

            ReturnToMainScreenFromCompletedDay();
            return false;
        }

        // Asks about the location the meta screen will OPEN on, via the catalog overload, not
        // about some location of its own choosing. That is what makes "marched back to the
        // menu and shown nothing" unrepresentable: the celebration only starts from Start,
        // and at that moment the viewed location is the newest unlocked one -- the same one
        // this resolves.
        private bool NextDayOpensAProp()
        {
            if (metaCatalog == null) return false;

            var nextIndex = State.CurrentDayIndex + 1;

            // No next day means nothing opens on it either; the caller's second reason then
            // handles the exit. Checked here so this method never reports an unlock for a day
            // the player cannot reach.
            if (Session.DayCatalog == null || nextIndex >= Session.DayCatalog.Count) return false;

            return MetaResolver.DayUnlocksBetween(
                metaCatalog, Session.OwnedMetaItemIds, State.CurrentDayIndex, nextIndex).Count > 0;
        }

        public void ReturnToMainScreenFromCompletedDay()
        {
            // Before anything else: the day is over and the player is leaving with it,
            // so whatever the reward flight had not handed over yet is paid now. The
            // SaveProfile at the bottom then writes it, which is why this needs no save
            // of its own.
            CommitPendingReward();

            var nextIndex = State.CurrentDayIndex + 1;
            if (Session.DayCatalog != null && nextIndex < Session.DayCatalog.Count)
            {
                State.CurrentDayIndex = nextIndex;
            }

            SaveProfile();
            SceneFlow.LoadMainScreen();
        }

        // Game Over popup's "Main Menu": the player walks out of an attempt they
        // failed. Settled exactly the way the free Retry settles it --
        // RevertToDayStart, so the attempt's earnings are taken back and its
        // spending is not -- and the day index is deliberately left alone, so the
        // main screen still offers this same Day.
        //
        // Unlike a retry this REACHES DISK, which is the one place D-012 changes an
        // older rule ("a failed day never writes"). It has to: there is no later
        // DayCompleted to correct the figure, and without the write a Continue
        // bought with Gems during an attempt the player then abandons would be
        // silently refunded by walking out -- making paid Continues free for anyone
        // who ends up leaving. The write can only ever record money already spent,
        // never money earned, so it cannot bank a failed day's income.
        public void ReturnToMainScreenAbandoningDay()
        {
            // Defensive rather than load-bearing: this path is only reachable from the
            // Game Over popup, i.e. from a day that never completed, so there is no debt
            // to void. It is here so that "a failed day pays nothing" stays true by
            // construction rather than by the reader tracing which events can overlap.
            DiscardPendingReward();

            wallet.RevertToDayStart();

            // Refills lives BEFORE the save, and this line is load-bearing since
            // D-014 made lives persistent. This path is only reachable from the Game
            // Over popup, i.e. with Lives at 0 -- persisting that would hand the
            // player a save file they cannot play out of: 0 lives on launch, dead
            // before the first ticket, forever. Abandoning an attempt resets it the
            // same way the free Retry does, so the next attempt starts full either
            // way; the only difference is where the player goes next.
            LivesManager.RetryDay();

            SaveProfile();
            SceneFlow.LoadMainScreen();
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
            DayLifecycleManager.ResetForNewDay(CurrentDayTicketSeconds);

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
            // VOIDED, not paid: this attempt's result is being thrown away, so the debt
            // goes with it. Whatever the reward flight already handed over is a real
            // balance by now, and the revert below is what takes that part back -- the
            // two together are why replaying a finished day cannot farm the reward.
            DiscardPendingReward();

            wallet.RevertToDayStart();

            RefreshDayTicketSequenceProvider();

            State.Board.Clear();
            ApplyDayStartBoardPreSeed();
            TrayManager.DiscardAllForNewDay();
            TicketSlotManager.ResetSlotsForNewDay();
            LivesManager.RetryDay();
            DayLifecycleManager.ResetForNewDay(CurrentDayTicketSeconds);

            // Writes the reverted balance back to disk, unlike the failed-day
            // retry path: this day already COMPLETED, so OnDayCompleted has
            // already banked the higher figure and the file would keep paying it
            // out on the next launch if we left it alone.
            //
            // Deliberately LAST since D-014, where it used to be first: lives are
            // persisted now, and RetryDay above refills them, so saving before that
            // line would write the pre-refill count and hand the player back a
            // half-empty life bar if they quit mid-redo.
            SaveProfile();
        }

        // Free-win path: the day's goal was hit (GameManager.OnDayCompleted
        // already paused ticket production). Mirrors RetryDay's reset order
        // but deliberately skips LivesManager -- Lives are NOT reset on a
        // successful advance, only a failed retry pays that cost (CLAUDE.md
        // Section 3) -- and never publishes DayRetried.
        public bool AdvanceToNextDay()
        {
            var nextIndex = State.CurrentDayIndex + 1;
            if (Session.DayCatalog == null || nextIndex >= Session.DayCatalog.Count)
            {
                // Last authored Day. The caller decides what to show --
                // DayCompletePopupView sends the player to the main screen rather
                // than leaving them on a finished day with the popup gone.
                return false;
            }

            // Ordering is load-bearing twice over: the reward is paid BEFORE
            // CaptureDayStart, so the day the player is advancing into takes a baseline
            // that already includes what they just earned -- snapshot first and their
            // first retry of the new day would revert the reward away.
            CommitPendingReward();

            State.CurrentDayIndex = nextIndex;
            wallet.CaptureDayStart();

            // Persists the new index, and now also whatever the line above just paid
            // out. Without this write,
            // quitting during the day the player just advanced INTO would relaunch
            // them onto the day they had already beaten.
            SaveProfile();

            RefreshDayTicketSequenceProvider();

            State.Board.Clear();
            ApplyDayStartBoardPreSeed();
            TrayManager.DiscardAllForNewDay();
            TicketSlotManager.ResetSlotsForNewDay();
            DayLifecycleManager.ResetForNewDay(CurrentDayTicketSeconds);

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
