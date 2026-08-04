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

        // Creation order across the whole ticket lifecycle (active slots AND
        // the upcoming lookahead queue), used by BoardDistributor to pick which
        // tickets' required items are guaranteed on the board (GDD Section 4).
        // Slot index can't serve this purpose since slots get reused on
        // delivery/cancellation.
        public long ArrivalSequence { get; }

        public Ticket(
            string customerName,
            PatienceType patienceType,
            IReadOnlyList<FoodItemConfig> requiredItems,
            IReadOnlyList<Modification> modifications,
            float timeLimitSeconds,
            long arrivalSequence = 0)
        {
            CustomerName = customerName;
            PatienceType = patienceType;
            RequiredItems = requiredItems;
            Modifications = modifications;
            TimeLimitSeconds = timeLimitSeconds;
            RemainingSeconds = timeLimitSeconds;
            ArrivalSequence = arrivalSequence;
        }
    }
}
