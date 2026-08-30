using System.Collections.Generic;
using ExpoTheExplorer.Data;
using UnityEngine;

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

        // The face on the card, drawn once beside the name and fixed for this
        // ticket's whole life. Flavour only -- nothing reads it but the card's
        // photo frame -- but it lives HERE rather than in the view for the same
        // reason CustomerName does: TicketCardView rebuilds its content every
        // time the slot's ticket reference changes, so a portrait picked view-side
        // would be a second writer of this customer's identity and could hand the
        // same ticket a different face on a rebuild.
        //
        // Independent of the name, not paired with it: names.json holds 313
        // entries and Art/Characters holds 28 files, so there is no id to line
        // them up on (decisions.md D-139).
        //
        // Null is legal and means "no portraits authored yet" -- the frame just
        // stays empty. It is the LAST parameter, and optional, so the authoring
        // path (TicketFactory.Create) and the test suite construct a ticket
        // exactly as they did before this field existed.
        public Sprite CustomerPortrait { get; }

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
            long arrivalSequence = 0,
            Sprite customerPortrait = null)
        {
            CustomerName = customerName;
            CustomerPortrait = customerPortrait;
            PatienceType = patienceType;
            RequiredItems = requiredItems;
            Modifications = modifications;
            TimeLimitSeconds = timeLimitSeconds;
            RemainingSeconds = timeLimitSeconds;
            ArrivalSequence = arrivalSequence;
        }
    }
}
