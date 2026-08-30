using System;
using System.Collections.Generic;
using System.Linq;
using ExpoTheExplorer.Core;
using ExpoTheExplorer.Systems.BoardDistribution;
using ExpoTheExplorer.Systems.DayLifecycle;
using ExpoTheExplorer.Systems.DaySystem;
using ExpoTheExplorer.Systems.EconomySystem;
using ExpoTheExplorer.Systems.TicketSystem;
using ExpoTheExplorer.Systems.TraySystem;

namespace ExpoTheExplorer.Simulation
{
    public sealed class DaySimulation
    {
        private readonly DayDefinition day;
        private readonly DayTicketSequenceProvider provider;
        private readonly BoardDistributor distributor;
        private readonly TicketSlotManager slotManager;
        private readonly TrayManager trayManager;
        private readonly EconomyCalculator economy;
        private readonly DayLifecycleManager lifecycle;
        private readonly SimulationMetrics metrics = new();
        private readonly IPlayerPolicy policy;
        private readonly List<IReadOnlyList<BoardItem>> trayContents = new(GameState.TicketSlotCount);

        private int openingAssignmentsWithoutDistribution;

        public DaySimulation(DayDefinition day, SimulationOptions options)
        {
            this.day = day ?? throw new ArgumentNullException(nameof(day));

            var masterRandom = new Random(options.Seed);
            var ticketFactory = new TicketFactory(options.TicketGenerationConfig, new Random(masterRandom.Next()));

            State = new GameState(options.GameConfig);
            provider = new DayTicketSequenceProvider(day.TicketSequence, day.TicketRuntime, ticketFactory);
            distributor = new BoardDistributor(State, day.BoardDistribution, new Random(masterRandom.Next()));
            economy = new EconomyCalculator(options.EconomyConfig);
            lifecycle = new DayLifecycleManager(State, options.StarScoreConfig);
            lifecycle.ResetForNewDay(day.TotalTicketSeconds);

            slotManager = new TicketSlotManager(State, NextTicketOrNull, HandleTicketTimeout);
            trayManager = new TrayManager(State, slotManager.DeliverTicket, HandleWrongDelivery, new Random(masterRandom.Next()));
            policy = new AutoCollectPolicy(trayManager, options.ActionDelaySeconds);

            for (var i = 0; i < GameState.TicketSlotCount; i++)
            {
                trayContents.Add(trayManager.GetContents(i));
            }

            State.TicketAssigned.Subscribe(OnTicketAssigned);
            State.TicketDelivered.Subscribe(OnTicketDelivered);

            DayBoardTimelinePlayer.ApplyForStep(State.Board, day.BoardTimeline, -1);
            openingAssignmentsWithoutDistribution = DayBoardTimelinePlayer.HasEntriesForStep(day.BoardTimeline, -1)
                ? GameState.TicketSlotCount
                : 0;

            slotManager.FillEmptySlots();
            metrics.SampleBoard(State.Board);
        }

        public GameState State { get; }
        public bool IsComplete => slotManager.IsDayComplete;
        public int LivesLost { get; private set; }
        public float VirtualTime { get; private set; }

        public bool Tick(float deltaSeconds)
        {
            if (slotManager.IsDayComplete) return false;

            policy.Tick(new SimulationView(State, trayContents, VirtualTime), deltaSeconds);
            slotManager.Tick(deltaSeconds);
            VirtualTime += deltaSeconds;
            metrics.SampleBoard(State.Board);

            return true;
        }

        public SimulationResult ToResult(int seed, SimulationEndReason endReason, string message = null)
        {
            return new SimulationResult
            {
                Seed = seed,
                EndReason = endReason,
                Message = message,
                DurationSeconds = VirtualTime,
                TicketsRequired = day.TicketsRequiredForDay,
                TicketsDelivered = State.TicketsDeliveredToday,
                LivesLost = LivesLost,
                Timeouts = lifecycle.TimeoutCount,
                WrongDeliveries = lifecycle.WrongDeliveryCount,
                OrdersValue = lifecycle.OrdersDeliveredValue,
                TipsValue = lifecycle.TipsValue,
                Revenue = lifecycle.Total,
                SavedSeconds = lifecycle.SavedSeconds,
                TotalTicketSeconds = lifecycle.TotalTicketSeconds,
                AverageRemainingTimeRatio = metrics.AverageRemainingTimeRatio,
                StarScore = lifecycle.StarScore,
                StarCount = lifecycle.StarCount,
                AverageBoardOccupancy = metrics.AverageBoardOccupancy,
                PeakBoardOccupancy = metrics.PeakBoardOccupancy,
                BoardCellCount = State.Board.CellCount,
                BoardFullSamples = metrics.BoardFullSamples,
                PeakPendingSpawns = metrics.PeakPendingSpawns,
                ZeroCompletableRounds = metrics.ZeroCompletableRounds,
                OrderPlacedRounds = metrics.OrderPlacedRounds,
                TipTiers = metrics.TipTiers,
            };
        }

        private Ticket NextTicketOrNull() => provider.HasNext ? provider.NextTicket() : null;

        private void OnTicketAssigned((int SlotIndex, Ticket Ticket) assignment)
        {
            if (openingAssignmentsWithoutDistribution > 0)
            {
                openingAssignmentsWithoutDistribution--;
            }
            else
            {
                var activeTickets = State.TicketSlots.Where(t => t != null).ToList();
                var lookaheadCount = Math.Max(GameState.TicketSlotCount, day.TicketRuntime.UpcomingQueueSize);
                var upcomingTickets = provider.PeekUpcoming(lookaheadCount);
                distributor.OnOrderPlaced(activeTickets, upcomingTickets);
                metrics.RecordOrderPlaced(State);
            }

            trayManager.OnTicketAssigned(assignment.SlotIndex);
        }

        private void OnTicketDelivered((int SlotIndex, Ticket Ticket) delivery)
        {
            var payout = economy.CalculatePayout(delivery.Ticket);
            metrics.RecordDelivery(delivery.Ticket, payout);
            lifecycle.RecordDelivery(payout);
        }

        private void HandleTicketTimeout(int slotIndex) => HandleLifeLoss(DayFailureCause.Timeout);

        private void HandleWrongDelivery(int slotIndex) => HandleLifeLoss(DayFailureCause.WrongDelivery);

        private void HandleLifeLoss(DayFailureCause cause)
        {
            LivesLost++;
            State.Lives = Math.Max(0, State.Lives - 1);
            lifecycle.RecordFailure(cause);
        }
    }
}
