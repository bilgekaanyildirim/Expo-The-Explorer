using System;
using System.Collections.Generic;
using ExpoTheExplorer.Data;

namespace ExpoTheExplorer.Core
{
    // The single answer to "what does this ticket actually want", as
    // (food + modification-combo) keys. It exists because that question is asked in
    // several places and the answer has ONE non-obvious rule in it -- modifications count
    // for the Main item and for nothing else -- which every caller was spelling out for
    // itself.
    //
    // That rule is not cosmetic: it decides whether a delivered tray is correct. A second
    // copy of it is the kind of duplicate that survives right up until one copy is fixed
    // and the other is not, and then a perfectly good order starts costing a life.
    //
    // Extracted in .claude/powerup-plan.md Adım 5 with three consumers: TraySlot.Matches
    // (which carried its own copy), the Noise Clear powerup, and Adım 6's auto-collect.
    //
    // KNOWN REMAINING DUPLICATE, recorded rather than quietly left: BoardDistributor spells
    // the same rule out twice more (SpawnMissingRequiredItems and LeakNoiseItems). It is
    // untouched here because that file is outside this task's manifest, not because it is
    // correct to leave -- moving it is a small task of its own.
    public static class TicketRequirements
    {
        // THE rule, in one place. Modifications belong to the Main dish only: TicketFactory
        // never attaches any to a Side or a Drink, and both the board spawner and the drag
        // pickup preserve whatever a BoardItem was actually created with, so comparing an
        // empty modification list for those categories is correct rather than a shortcut.
        public static RequiredItemKey KeyFor(Ticket ticket, FoodItemConfig food)
        {
            var modifications = food.Category == FoodCategory.Main
                ? ticket.Modifications
                : Array.Empty<Modification>();

            return new RequiredItemKey(food, modifications);
        }

        // The multiset a correct tray for this ticket would hold. A DICTIONARY rather than
        // a set because count matters to the caller that validates a delivery: a ticket
        // asking for two colas is not satisfied by one.
        public static Dictionary<RequiredItemKey, int> RequiredCounts(Ticket ticket)
        {
            var counts = new Dictionary<RequiredItemKey, int>();
            if (ticket == null) return counts;

            foreach (var food in ticket.RequiredItems)
            {
                var key = KeyFor(ticket, food);
                counts.TryGetValue(key, out var count);
                counts[key] = count + 1;
            }

            return counts;
        }

        // Every key any ACTIVE ticket wants, across all three slots. A SET, not counts, and
        // that is the whole design of the Noise Clear powerup in one type choice: the rule
        // is "does any ticket in hand want one of these at all", not "how many". A player
        // looking at a third burger says there are too many, not that it is unwanted --
        // so surplus copies of a wanted food survive the clear.
        //
        // Resolved and returned rather than exposed as a live view: the caller iterates the
        // board while removing from it, and a lazily-evaluated query over the same state
        // would be answering a different question with every cell.
        public static HashSet<RequiredItemKey> ActiveTicketKeys(GameState state)
        {
            var keys = new HashSet<RequiredItemKey>();
            if (state == null) return keys;

            for (var i = 0; i < GameState.TicketSlotCount; i++)
            {
                var ticket = state.TicketSlots[i];

                // Delivered and cancelled tickets are excluded, not just null slots. A
                // resolved ticket can still be sitting in the array for the rest of the
                // synchronous cascade that resolved it, and treating its order as still
                // wanted would protect items nobody is waiting for.
                if (ticket == null || ticket.State != TicketState.Active) continue;

                foreach (var food in ticket.RequiredItems)
                {
                    keys.Add(KeyFor(ticket, food));
                }
            }

            return keys;
        }
    }
}
