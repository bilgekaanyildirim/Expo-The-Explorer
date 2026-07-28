using System.Collections.Generic;
using ExpoTheExplorer.Data;

namespace ExpoTheExplorer.Core
{
    public enum TicketState
    {
        Active,
        Delivered,
        Cancelled
    }

    // A ticket's item count (main + side + drink) is what the tray counter tracks;
    // modifications are validated separately and are not part of that tally
    // (GDD Section 3 / CLAUDE.md Tray / Validation Mechanic).
    public class Ticket
    {
        public string CustomerName { get; }
        public PatienceType PatienceType { get; }
        public IReadOnlyList<FoodItemConfig> RequiredItems { get; }
        public IReadOnlyList<Modification> Modifications { get; }
        public float TimeLimitSeconds { get; }

        public float RemainingSeconds { get; set; }
        public TicketState State { get; set; } = TicketState.Active;

        public Ticket(
            string customerName,
            PatienceType patienceType,
            IReadOnlyList<FoodItemConfig> requiredItems,
            IReadOnlyList<Modification> modifications,
            float timeLimitSeconds)
        {
            CustomerName = customerName;
            PatienceType = patienceType;
            RequiredItems = requiredItems;
            Modifications = modifications;
            TimeLimitSeconds = timeLimitSeconds;
            RemainingSeconds = timeLimitSeconds;
        }
    }
}
