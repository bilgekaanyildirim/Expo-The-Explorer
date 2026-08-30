using System;
using ExpoTheExplorer.Systems.DaySystem;

namespace ExpoTheExplorer.Simulation
{
    public static class SimulationRunner
    {
        public static SimulationResult RunOne(DayDefinition day, SimulationOptions options)
        {
            var setupError = ValidateSetup(day, options);
            if (!string.IsNullOrEmpty(setupError))
            {
                return new SimulationResult
                {
                    Seed = options?.Seed ?? 0,
                    EndReason = SimulationEndReason.InvalidSetup,
                    Message = setupError,
                    TicketsRequired = day?.TicketsRequiredForDay ?? 0,
                };
            }

            var simulation = new DaySimulation(day, options);
            var step = Math.Max(0.01f, options.StepSeconds);
            var maxSeconds = Math.Max(step, options.MaxVirtualSeconds);

            while (!simulation.IsComplete &&
                   simulation.LivesLost < ExpoTheExplorer.Core.GameState.DefaultStartingLives &&
                   simulation.VirtualTime < maxSeconds)
            {
                simulation.Tick(step);
            }

            if (simulation.IsComplete)
            {
                return simulation.ToResult(options.Seed, SimulationEndReason.Completed);
            }

            if (simulation.LivesLost >= ExpoTheExplorer.Core.GameState.DefaultStartingLives)
            {
                return simulation.ToResult(options.Seed, SimulationEndReason.LivesDepleted);
            }

            return simulation.ToResult(
                options.Seed,
                SimulationEndReason.MaxTimeReached,
                $"Reached the {maxSeconds:0.##}s simulation limit before the day resolved.");
        }

        private static string ValidateSetup(DayDefinition day, SimulationOptions options)
        {
            if (day == null) return "No day was provided.";
            if (options == null) return "No simulation options were provided.";
            if (options.GameConfig == null) return "Game Config is missing.";
            if (options.TicketGenerationConfig == null) return "Ticket Generation Config is missing.";
            if (options.EconomyConfig == null) return "Economy Config is missing.";
            if (options.StarScoreConfig == null) return "Star Score Config is missing.";
            if (day.BoardDistribution == null) return "Day has no Board Distribution settings.";
            if (day.TicketRuntime == null) return "Day has no Ticket Runtime settings.";
            if (day.TicketSequence == null) return "Day has no ticket sequence.";
            return null;
        }
    }
}
