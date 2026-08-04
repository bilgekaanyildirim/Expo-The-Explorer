using System.Collections.Generic;
using ExpoTheExplorer.Core;
using ExpoTheExplorer.Data;

namespace ExpoTheExplorer.Systems.EconomySystem
{
    public enum SpeedTier
    {
        Lightning,
        Fast,
        Standard
    }

    // Full breakdown of one delivery's tip calculation (GDD Section 9), not just
    // the final number — lets UI show "Fast x1.5" or similar without
    // recomputing the formula itself.
    public readonly struct DeliveryTipResult
    {
        public float BaseTip { get; }
        public SpeedTier SpeedTier { get; }
        public float SpeedMultiplier { get; }
        public float PatienceDecayCoefficient { get; }
        public float TotalTip { get; }

        public DeliveryTipResult(float baseTip, SpeedTier speedTier, float speedMultiplier, float patienceDecayCoefficient)
        {
            BaseTip = baseTip;
            SpeedTier = speedTier;
            SpeedMultiplier = speedMultiplier;
            PatienceDecayCoefficient = patienceDecayCoefficient;
            TotalTip = baseTip * speedMultiplier * patienceDecayCoefficient;
        }
    }

    // Tip/speed/patience calculations (GDD Sections 8-9 / CLAUDE.md Section 5 —
    // Economy Module). Plain C#, no MonoBehaviour dependency, so it's
    // unit-testable in isolation and balancing only ever means tuning
    // EconomyConfig, never touching this class.
    public class EconomyCalculator
    {
        private readonly EconomyConfig config;

        public EconomyCalculator(EconomyConfig config)
        {
            this.config = config;
        }

        public DeliveryTipResult CalculateTip(Ticket ticket)
        {
            var itemCount = ticket.RequiredItems.Count;
            var elapsedSeconds = System.Math.Max(0f, ticket.TimeLimitSeconds - ticket.RemainingSeconds);

            var baseTip = config.BaseTipPerItem * itemCount;
            var speedTier = ResolveSpeedTier(elapsedSeconds, itemCount);
            var speedMultiplier = ResolveSpeedMultiplier(speedTier);
            var decayCoefficient = ResolvePatienceDecayCoefficient(ticket.PatienceType, elapsedSeconds, itemCount);

            return new DeliveryTipResult(baseTip, speedTier, speedMultiplier, decayCoefficient);
        }

        private SpeedTier ResolveSpeedTier(float elapsedSeconds, int itemCount)
        {
            if (elapsedSeconds <= config.LightningSecondsPerItem * itemCount) return SpeedTier.Lightning;
            if (elapsedSeconds <= config.FastSecondsPerItem * itemCount) return SpeedTier.Fast;
            return SpeedTier.Standard;
        }

        private float ResolveSpeedMultiplier(SpeedTier tier)
        {
            return tier switch
            {
                SpeedTier.Lightning => config.LightningMultiplier,
                SpeedTier.Fast => config.FastMultiplier,
                _ => config.StandardMultiplier,
            };
        }

        // Walks the matching patience type's steps in authored (ascending
        // threshold) order, keeping the last one already crossed — this is what
        // makes the curve hold flat between thresholds instead of interpolating
        // (GDD Section 8: stepped, not linear). Elapsed time before every step's
        // threshold means no decay yet (coefficient 1).
        private float ResolvePatienceDecayCoefficient(PatienceType patienceType, float elapsedSeconds, int itemCount)
        {
            var coefficient = 1f;

            foreach (var step in ResolveDecaySteps(patienceType))
            {
                if (elapsedSeconds >= step.SecondsPerItemThreshold * itemCount)
                {
                    coefficient = step.DecayCoefficient;
                }
            }

            return coefficient;
        }

        private IReadOnlyList<PatienceDecayStep> ResolveDecaySteps(PatienceType patienceType)
        {
            return patienceType switch
            {
                PatienceType.Impatient => config.ImpatientDecaySteps,
                PatienceType.Patient => config.PatientDecaySteps,
                _ => config.NormalDecaySteps,
            };
        }
    }
}
