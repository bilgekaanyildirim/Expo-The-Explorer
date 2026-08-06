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
        private readonly PlayerProfileStore profileStore;
        private PlayerProfile lastCommittedProfile;

        // Calculation-only usage (existing tests) -- never touches persistence.
        // Commit/Discard are only ever called on an instance built with the
        // constructor below (GameManager always uses that one).
        public LevelManager(GameState state, LevelProgressionConfig config)
            : this(state, config, null, new PlayerProfile())
        {
        }

        public LevelManager(GameState state, LevelProgressionConfig config, PlayerProfileStore profileStore, PlayerProfile initialProfile)
        {
            this.state = state;
            this.config = config;
            this.profileStore = profileStore;
            lastCommittedProfile = initialProfile;
        }

        // Wired to GameState.DayCompleted (GameManager) -- whatever Xp/Level the
        // player is currently sitting on becomes permanent and is written to
        // disk. A later Discard rolls back to THIS point, not the profile that
        // was loaded at session start.
        public void CommitProgress()
        {
            lastCommittedProfile = new PlayerProfile { Xp = state.Xp, Level = state.Level };
            profileStore.Save(lastCommittedProfile);
        }

        // Wired to GameState.DayRetried (GameManager) -- discards whatever Xp/
        // Level was gained during the abandoned attempt, rolling back to the
        // last commit (or the session's initial profile, if nothing has been
        // committed yet). Pure in-memory rollback -- never touches disk.
        public void DiscardToLastCommitted()
        {
            state.Xp = lastCommittedProfile.Xp;
            state.Level = lastCommittedProfile.Level;
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
