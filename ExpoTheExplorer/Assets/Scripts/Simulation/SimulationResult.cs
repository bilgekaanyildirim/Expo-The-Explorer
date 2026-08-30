namespace ExpoTheExplorer.Simulation
{
    public enum SimulationEndReason
    {
        Completed,
        LivesDepleted,
        MaxTimeReached,
        InvalidSetup
    }

    public readonly struct TipTierCounts
    {
        public int Full { get; }
        public int Warning { get; }
        public int Critical { get; }

        public TipTierCounts(int full, int warning, int critical)
        {
            Full = full;
            Warning = warning;
            Critical = critical;
        }
    }

    public class SimulationResult
    {
        public int Seed { get; set; }
        public SimulationEndReason EndReason { get; set; }
        public bool Completed => EndReason == SimulationEndReason.Completed;
        public string Message { get; set; }

        public float DurationSeconds { get; set; }
        public int TicketsRequired { get; set; }
        public int TicketsDelivered { get; set; }
        public int LivesLost { get; set; }
        public int Timeouts { get; set; }
        public int WrongDeliveries { get; set; }

        public int OrdersValue { get; set; }
        public int TipsValue { get; set; }
        public int Revenue { get; set; }

        public float SavedSeconds { get; set; }
        public float TotalTicketSeconds { get; set; }
        public float AverageRemainingTimeRatio { get; set; }
        public float StarScore { get; set; }
        public int StarCount { get; set; }

        public float AverageBoardOccupancy { get; set; }
        public int PeakBoardOccupancy { get; set; }
        public int BoardCellCount { get; set; }
        public int BoardFullSamples { get; set; }
        public int PeakPendingSpawns { get; set; }
        public int ZeroCompletableRounds { get; set; }
        public int OrderPlacedRounds { get; set; }

        public TipTierCounts TipTiers { get; set; }
    }
}
