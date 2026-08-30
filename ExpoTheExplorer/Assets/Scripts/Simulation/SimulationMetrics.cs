using System.Collections.Generic;
using ExpoTheExplorer.Core;
using ExpoTheExplorer.Systems.EconomySystem;

namespace ExpoTheExplorer.Simulation
{
    public class SimulationMetrics
    {
        private float occupancySum;
        private int occupancySamples;
        private float remainingRatioSum;
        private int deliveredSamples;
        private int fullTierCount;
        private int warningTierCount;
        private int criticalTierCount;

        public int PeakBoardOccupancy { get; private set; }
        public int BoardFullSamples { get; private set; }
        public int PeakPendingSpawns { get; private set; }
        public int ZeroCompletableRounds { get; private set; }
        public int OrderPlacedRounds { get; private set; }

        public float AverageBoardOccupancy => occupancySamples == 0 ? 0f : occupancySum / occupancySamples;
        public float AverageRemainingTimeRatio => deliveredSamples == 0 ? 0f : remainingRatioSum / deliveredSamples;
        public TipTierCounts TipTiers => new(fullTierCount, warningTierCount, criticalTierCount);

        public void SampleBoard(BoardGrid board)
        {
            if (board == null) return;

            var occupied = board.OccupiedCellCount;
            occupancySum += occupied;
            occupancySamples++;

            if (occupied > PeakBoardOccupancy) PeakBoardOccupancy = occupied;
            if (board.IsFull) BoardFullSamples++;
            if (board.PendingSpawnCount > PeakPendingSpawns) PeakPendingSpawns = board.PendingSpawnCount;
        }

        public void RecordDelivery(Ticket ticket, DeliveryPayoutResult payout)
        {
            if (ticket != null && ticket.TimeLimitSeconds > 0f)
            {
                remainingRatioSum += payout.RemainingSeconds / ticket.TimeLimitSeconds;
            }

            deliveredSamples++;

            switch (payout.Tier)
            {
                case TipTier.Critical:
                    criticalTierCount++;
                    break;
                case TipTier.Warning:
                    warningTierCount++;
                    break;
                default:
                    fullTierCount++;
                    break;
            }
        }

        public void RecordOrderPlaced(GameState state)
        {
            OrderPlacedRounds++;
            if (!AnyActiveTicketCompletable(state)) ZeroCompletableRounds++;
        }

        private static bool AnyActiveTicketCompletable(GameState state)
        {
            if (state?.Board == null) return false;

            var boardCounts = CountBoard(state.Board);
            for (var i = 0; i < GameState.TicketSlotCount; i++)
            {
                var ticket = state.TicketSlots[i];
                if (ticket == null || ticket.State != TicketState.Active) continue;

                if (IsCompletableFrom(TicketRequirements.RequiredCounts(ticket), boardCounts))
                {
                    return true;
                }
            }

            return false;
        }

        private static Dictionary<RequiredItemKey, int> CountBoard(BoardGrid board)
        {
            var counts = new Dictionary<RequiredItemKey, int>();
            for (var x = 0; x < board.Width; x++)
            {
                for (var y = 0; y < board.Height; y++)
                {
                    var item = board.ItemAt(x, y);
                    if (item == null) continue;

                    var key = new RequiredItemKey(item.Config, item.Modifications);
                    counts.TryGetValue(key, out var count);
                    counts[key] = count + 1;
                }
            }

            return counts;
        }

        private static bool IsCompletableFrom(
            Dictionary<RequiredItemKey, int> required,
            Dictionary<RequiredItemKey, int> available)
        {
            foreach (var entry in required)
            {
                if (!available.TryGetValue(entry.Key, out var count) || count < entry.Value)
                {
                    return false;
                }
            }

            return true;
        }
    }
}
