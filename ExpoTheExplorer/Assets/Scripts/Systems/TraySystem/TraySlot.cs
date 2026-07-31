using System;
using System.Collections.Generic;
using ExpoTheExplorer.Core;
using ExpoTheExplorer.Data;

namespace ExpoTheExplorer.Systems.TraySystem
{
    // One ticket slot's tray contents (GDD Section 3/5) — the tray IS the drop
    // area itself, not a separate preview; items accumulate here until the
    // count matches the ticket's required item count, at which point TrayManager
    // runs a single batch check against Matches().
    public class TraySlot
    {
        private readonly List<BoardItem> items = new();

        public IReadOnlyList<BoardItem> Items => items;

        public void Add(BoardItem item) => items.Add(item);

        public void Clear() => items.Clear();

        public bool IsFull(Ticket ticket) => ticket != null && items.Count >= ticket.RequiredItems.Count;

        // Batch validation: does this tray's contents — as a multiset of
        // (food, modification-combo) — exactly match what the ticket requires?
        // Modifications only matter for the Main item; TicketFactory never
        // attaches any to Side/Drink, and BoardDistributor/drag pickup preserve
        // whatever a BoardItem was actually spawned with, so comparing full
        // Modifications lists here is correct for every category.
        public bool Matches(Ticket ticket)
        {
            if (ticket == null) return false;
            if (items.Count != ticket.RequiredItems.Count) return false;

            var requiredCounts = new Dictionary<RequiredItemKey, int>();
            foreach (var food in ticket.RequiredItems)
            {
                var mods = food.Category == FoodCategory.Main ? ticket.Modifications : Array.Empty<Modification>();
                var key = new RequiredItemKey(food, mods);
                requiredCounts.TryGetValue(key, out var count);
                requiredCounts[key] = count + 1;
            }

            var actualCounts = new Dictionary<RequiredItemKey, int>();
            foreach (var item in items)
            {
                var key = new RequiredItemKey(item.Config, item.Modifications);
                actualCounts.TryGetValue(key, out var count);
                actualCounts[key] = count + 1;
            }

            if (requiredCounts.Count != actualCounts.Count) return false;

            foreach (var (key, requiredCount) in requiredCounts)
            {
                if (!actualCounts.TryGetValue(key, out var actualCount) || actualCount != requiredCount)
                {
                    return false;
                }
            }

            return true;
        }
    }
}
