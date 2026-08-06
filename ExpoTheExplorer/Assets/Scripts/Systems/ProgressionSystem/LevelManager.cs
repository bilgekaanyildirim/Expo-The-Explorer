using ExpoTheExplorer.Core;
using ExpoTheExplorer.Data;

namespace ExpoTheExplorer.Systems.ProgressionSystem
{
    public readonly struct DeliveryXpResult
    {
        public float BaseXp { get; }
        public float PatienceMultiplier { get; }
        public float TotalXp { get; }

        public DeliveryXpResult(float baseXp, float patienceMultiplier)
        {
            BaseXp = baseXp;
            PatienceMultiplier = patienceMultiplier;
            TotalXp = baseXp * patienceMultiplier;
        }
    }

    public class LevelManager
    {
        private readonly GameState state;
        private readonly LevelProgressionConfig config;

        public LevelManager(GameState state, LevelProgressionConfig config)
        {
            this.state = state;
            this.config = config;
        }

        // Xp += amount happens once; the loop only transfers Xp into Level while
        // there's a next authored level AND Xp already covers its threshold.
        // Once Level reaches config.XpToNextLevel.Count, the loop condition is
        // permanently false (Level never changes again), so every later AddXp
        // call just accumulates into Xp with no further subtraction -- this IS
        // the locked "max level caps, Xp keeps accumulating" behavior, no
        // special-casing needed. Bounds-check before index access in the &&
        // (order matters -- reversed, a capped player's next AddXp would throw).
        public void AddXp(int amount)
        {
            state.Xp += amount;
            while (state.Level < config.XpToNextLevel.Count && state.Xp >= config.XpToNextLevel[state.Level])
            {
                state.Xp -= config.XpToNextLevel[state.Level];
                state.Level++;
            }
        }

        public DeliveryXpResult CalculateXp(Ticket ticket)
        {
            var itemCount = ticket.RequiredItems.Count;
            var baseXp = config.XpPerItem * itemCount;
            var multiplier = ResolvePatienceMultiplier(ticket.PatienceType);
            return new DeliveryXpResult(baseXp, multiplier);
        }

        private float ResolvePatienceMultiplier(PatienceType patienceType)
        {
            return patienceType switch
            {
                PatienceType.Impatient => config.ImpatientXpMultiplier,
                PatienceType.Patient => config.PatientXpMultiplier,
                _ => config.NormalXpMultiplier,
            };
        }
    }
}
