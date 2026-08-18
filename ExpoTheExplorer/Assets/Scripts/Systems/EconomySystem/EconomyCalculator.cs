using ExpoTheExplorer.Core;
using ExpoTheExplorer.Data;

namespace ExpoTheExplorer.Systems.EconomySystem
{
    // Which of the three tip rates a delivery earned. This is the SAME division
    // the ticket card's timer bar paints itself with (green/orange/red), by
    // construction: both read EconomyConfig's WarningRatio/CriticalRatio, so the
    // colour the player sees while a ticket runs is the tier they get paid.
    // Replaces the old SpeedTier (Lightning/Fast/Standard), whose thresholds were
    // seconds-per-item and therefore invisible on screen (GDD v1.1).
    public enum TipTier
    {
        Full,
        Warning,
        Critical
    }

    // Full breakdown of one delivery's payout, not just the final number — lets
    // UI show "tip 15%" without recomputing the formula (the same reason the old
    // DeliveryTipResult carried SpeedTier).
    //
    // OrderValue is an int because food prices are ints and SoftMoney is an int:
    // the guaranteed half of the payout never passes through float, so only the
    // tip can carry a rounding remainder, and it is rounded exactly once by
    // whoever banks it (GameManager).
    public readonly struct DeliveryPayoutResult
    {
        public int OrderValue { get; }
        public TipTier Tier { get; }
        public float TipRate { get; }
        public float Tip { get; }
        public float Total { get; }

        public DeliveryPayoutResult(int orderValue, TipTier tier, float tipRate)
        {
            OrderValue = orderValue;
            Tier = tier;
            TipRate = tipRate;
            Tip = orderValue * tipRate;
            Total = orderValue + Tip;
        }
    }

    // The delivery payout formula (GDD Section 9 / CLAUDE.md Section 5 — Economy
    // Module). Plain C#, no MonoBehaviour dependency, so it's unit-testable in
    // isolation and balancing only ever means tuning EconomyConfig, never
    // touching this class.
    //
    //   Order Value = the summed base prices of the ticket's required items
    //   Tip         = Order Value x the tier's rate
    //   Total       = Order Value + Tip
    //
    // Order Value is paid in full on every successful delivery and is never
    // scaled by anything. Item count does not enter the formula at all -- a
    // bigger order is worth more because its items cost more, and it reaches a
    // lower tier only because gathering it genuinely takes longer.
    public class EconomyCalculator
    {
        private readonly EconomyConfig config;

        public EconomyCalculator(EconomyConfig config)
        {
            this.config = config;
        }

        public DeliveryPayoutResult CalculatePayout(Ticket ticket)
        {
            var orderValue = ResolveOrderValue(ticket);
            var tier = ResolveTipTier(ResolveRemainingRatio(ticket));

            return new DeliveryPayoutResult(orderValue, tier, ResolveTipRate(tier));
        }

        // A null entry is skipped rather than throwing: a Day authored against a
        // food asset that was later deleted already survives everywhere else in
        // the pipeline (DayCatalogParser drops unresolvable ids), so a delivery
        // must not be the one place that hard-crashes over it. An unpriced item
        // contributes 0, which is a content bug surfaced by validation, not
        // something to paper over here.
        private static int ResolveOrderValue(Ticket ticket)
        {
            var total = 0;

            foreach (var item in ticket.RequiredItems)
            {
                if (item == null) continue;
                total += item.BasePrice;
            }

            return total;
        }

        // Fraction of the ticket's OWN limit still on the clock. A ratio, not a
        // second count, is what makes the tiers scale per patience type for free
        // (a 45s Impatient ticket drops a tier every 15s, a 150s Patient one
        // every 50s). A non-positive limit counts as fully elapsed rather than
        // dividing by zero -- an unauthored limit should read as "no time left",
        // not as a free Full-tier tip.
        private static float ResolveRemainingRatio(Ticket ticket)
        {
            if (ticket.TimeLimitSeconds <= 0f) return 0f;

            var ratio = ticket.RemainingSeconds / ticket.TimeLimitSeconds;
            if (ratio < 0f) return 0f;
            return ratio > 1f ? 1f : ratio;
        }

        // Critical is tested first, and both bounds are inclusive (<=), matching
        // TicketCardsView.TimerFillColorFor exactly -- authored so that a ratio
        // sitting precisely on a threshold can never land in a different bucket
        // than the colour the bar is showing at that same instant. It also means
        // a CriticalRatio authored above WarningRatio makes the warning tier
        // unreachable instead of throwing.
        private TipTier ResolveTipTier(float remainingRatio)
        {
            if (remainingRatio <= config.CriticalRatio) return TipTier.Critical;
            if (remainingRatio <= config.WarningRatio) return TipTier.Warning;
            return TipTier.Full;
        }

        private float ResolveTipRate(TipTier tier)
        {
            return tier switch
            {
                TipTier.Critical => config.TipRateCritical,
                TipTier.Warning => config.TipRateWarning,
                _ => config.TipRateFull,
            };
        }
    }
}
