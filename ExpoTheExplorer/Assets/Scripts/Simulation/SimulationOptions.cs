using ExpoTheExplorer.Data;

namespace ExpoTheExplorer.Simulation
{
    public class SimulationOptions
    {
        public int Seed { get; set; } = 1000;
        public float StepSeconds { get; set; } = 0.1f;
        public float ActionDelaySeconds { get; set; } = 0.5f;
        public float MaxVirtualSeconds { get; set; } = 600f;

        public GameConfig GameConfig { get; set; }
        public TicketGenerationConfig TicketGenerationConfig { get; set; }
        public EconomyConfig EconomyConfig { get; set; }
        public StarScoreConfig StarScoreConfig { get; set; }

        public SimulationOptions CloneForSeed(int seed)
        {
            return new SimulationOptions
            {
                Seed = seed,
                StepSeconds = StepSeconds,
                ActionDelaySeconds = ActionDelaySeconds,
                MaxVirtualSeconds = MaxVirtualSeconds,
                GameConfig = GameConfig,
                TicketGenerationConfig = TicketGenerationConfig,
                EconomyConfig = EconomyConfig,
                StarScoreConfig = StarScoreConfig,
            };
        }
    }
}
