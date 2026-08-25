using System.Collections.Generic;
using ExpoTheExplorer.Core;

namespace ExpoTheExplorer.Systems.PowerupSystem
{
    // What the three powerups actually DO (GDD Section 5.2). Static and pure-ish: each
    // method takes the state it changes and returns whether it found anything to change.
    //
    // THAT BOOL IS THE PROJECT'S "no wasted press" RULE, not a status code. GDD 5.2 says a
    // powerup that had no work to do costs no charge, and PowerupManager.TryUse implements
    // that by only deducting when the effect returns true. So `false` here is not a
    // failure -- it is "there was nothing to reset", and the player keeps their charge.
    //
    // Deliberately NOT an interface or a strategy class. There are exactly three of these,
    // the set is fixed by design rather than by content, and each is a few lines over state
    // that already exists -- abstraction-level.md puts the burden of proof on the layer,
    // and there is nothing here to justify one. They are registered as plain Func<bool>
    // delegates by the day scene's composition root, which is also what keeps PowerupSystem
    // from needing a reference to the systems these touch.
    //
    // Nothing here knows about charges, prices or the HUD. It is handed state and it edits
    // it; whether the player was allowed to ask is settled before these are ever called.
    public static class PowerupEffects
    {
        // GDD 5.2 #2 -- pulls every active ticket's countdown back to its OWN authored
        // limit, not to a shared number. Two tickets with different patience types get
        // different amounts of time back, which is correct: the limit is per-ticket Day
        // content (TicketRuntimeSettings), and a flat "+30 seconds" would quietly rewrite
        // that balancing from a powerup.
        //
        // THIS IS A THIRD WRITER of Ticket.RemainingSeconds, and the boundary is worth
        // stating rather than leaving for the next reader to trip over. The other two are
        // Ticket's constructor (which sets the opening value) and TicketSlotManager.Tick
        // (which spends it). Both of those are the "time moves forward" rule; this is the
        // only place time moves BACK. The alternative -- routing this through
        // TicketSlotManager so the field keeps one writer -- was rejected because it would
        // teach the ticket lifecycle what a powerup is, and that class has no other reason
        // ever to know.
        //
        // A ticket already sitting at its limit is skipped rather than rewritten, which is
        // what makes a press with three fresh tickets on screen cost nothing. Partial
        // counts as work: if one of three has lost a second, the charge is spent and all
        // three end up full.
        public static bool ResetActiveTicketTimers(GameState state)
        {
            if (state == null) return false;

            var restoredAny = false;

            for (var i = 0; i < GameState.TicketSlotCount; i++)
            {
                var ticket = state.TicketSlots[i];

                // Delivered and cancelled tickets are skipped, not just null ones. A
                // resolved ticket can still be sitting in the array for the rest of the
                // synchronous cascade that resolved it, and refilling its clock would put
                // time back on an order nobody is waiting for.
                if (ticket == null || ticket.State != TicketState.Active) continue;

                if (ticket.RemainingSeconds >= ticket.TimeLimitSeconds) continue;

                ticket.RemainingSeconds = ticket.TimeLimitSeconds;
                restoredAny = true;
            }

            return restoredAny;
        }

        // GDD 5.2 #3 -- removes every board item no ACTIVE ticket wants. Instant and
        // permanent; there is no window and nothing fades. An earlier design had it dim the
        // noise for a few seconds and the user corrected it, which is also why this has no
        // UI half at all: BoardGrid.RemoveItem publishes CellChanged and BoardView hides the
        // cell on its own, so the whole effect lives in the data layer.
        //
        // BY IDENTITY, NOT BY COUNT. A ticket that wants one burger protects every burger
        // on the board, because a player looking at a third one says there are too many, not
        // that it is unwanted. TicketRequirements.ActiveTicketKeys returns a set for exactly
        // this reason.
        //
        // TRAY CONTENTS ARE NOT CONSULTED, and that is deliberate rather than an oversight:
        // a burger already sitting in a tray does not stop the board's burgers from being
        // wanted, because a wrong order scatters the whole tray straight back onto the
        // board. Reading the tray would also drag a TraySystem reference into this assembly
        // for a question it does not need to ask.
        //
        // IT CANNOT BREAK THE GUARANTEED-TICKET RULE (GDD Section 4), and there are two
        // independent reasons. First, everything an active ticket requires is kept, and the
        // guarantee is precisely "at least one ticket stays completable". Second, as a
        // backstop, BoardDistributor caches nothing -- SpawnMissingRequiredItems recounts
        // the board every round and re-spawns whatever a guaranteed ticket is missing, so
        // even a mistake here would close on the next ticket assignment. What this DOES
        // remove is a pre-spawn for a guaranteed ticket still sitting in the upcoming queue;
        // that one is re-created the moment it reaches a slot.
        public static bool ClearUnneededItems(GameState state)
        {
            if (state?.Board == null) return false;

            var wanted = TicketRequirements.ActiveTicketKeys(state);
            var board = state.Board;
            var removedAny = false;

            for (var y = 0; y < board.Height; y++)
            {
                for (var x = 0; x < board.Width; x++)
                {
                    // Read fresh, immediately before the removal, and that ordering is
                    // load-bearing: RemoveItem BACKFILLS the cell it just emptied from the
                    // board's pending-spawn queue, so a version of this that collected
                    // coordinates first and deleted afterwards would delete whatever landed
                    // in the meantime -- possibly a required item it had already approved.
                    var item = board.ItemAt(x, y);
                    if (item == null) continue;

                    if (wanted.Contains(new RequiredItemKey(item.Config, item.Modifications))) continue;

                    board.RemoveItem(x, y);
                    removedAny = true;

                    // Whatever the backfill put here is deliberately NOT re-examined. The
                    // player asked to clear what was ON the board; the queue is what the
                    // board still owes them, and letting stuck required items finally land
                    // is the useful half of this powerup rather than a leak in it.
                }
            }

            return removedAny;
        }

        // GDD 5.2 #1's DECISION half -- which board cell this slot should take next. The
        // moving is somewhere else entirely (AutoCollectRunner, in the UI assembly), and
        // the split is not tidiness: a tray's contents are drawn by reparenting the dragged
        // object, so the collect has to go through the ordinary drop path, which lives in a
        // MonoBehaviour that no EditMode test can reach. Keeping the CHOOSING here is what
        // makes the powerup's actual rules testable at all.
        //
        // "Outstanding" is the ticket's required multiset MINUS what its tray already
        // holds, so a slot that is halfway filled by hand asks only for the rest. Counts
        // matter here, unlike in ClearUnneededItems: a ticket wanting two colas with one
        // already in the tray still wants exactly one more.
        //
        // Returns the FIRST match in scan order rather than a nearest or prettiest one.
        // The caller re-asks after every move, so the order only decides which of several
        // identical items is taken -- and they are identical by construction, since the
        // match is on the (food + modification) key.
        //
        // It cannot produce a wrong order: only items the ticket is still SHORT of are ever
        // offered, so the tray's batch check always resolves to a delivery rather than a
        // life lost. That is a property of this method, which is why it is pinned by a test
        // rather than trusted.
        public static bool TryFindAutoCollectItem(
            GameState state, int slotIndex, IReadOnlyList<BoardItem> trayContents, out int x, out int y)
        {
            x = -1;
            y = -1;

            if (state?.Board == null) return false;
            if (slotIndex < 0 || slotIndex >= GameState.TicketSlotCount) return false;

            var ticket = state.TicketSlots[slotIndex];
            if (ticket == null || ticket.State != TicketState.Active) return false;

            var outstanding = TicketRequirements.RequiredCounts(ticket);

            if (trayContents != null)
            {
                foreach (var item in trayContents)
                {
                    var key = new RequiredItemKey(item.Config, item.Modifications);

                    // Only decrements a key the ticket actually wants. An item already in
                    // the tray that the ticket does NOT want (a mis-drop the player has not
                    // resolved yet) must not reduce a requirement -- it is going to cost a
                    // life when the tray fills, and quietly treating it as progress would
                    // make this powerup finish that mistake for them.
                    if (outstanding.TryGetValue(key, out var count) && count > 0)
                    {
                        outstanding[key] = count - 1;
                    }
                }
            }

            var board = state.Board;

            for (var scanY = 0; scanY < board.Height; scanY++)
            {
                for (var scanX = 0; scanX < board.Width; scanX++)
                {
                    var item = board.ItemAt(scanX, scanY);
                    if (item == null) continue;

                    var key = new RequiredItemKey(item.Config, item.Modifications);
                    if (!outstanding.TryGetValue(key, out var stillNeeded) || stillNeeded <= 0) continue;

                    x = scanX;
                    y = scanY;
                    return true;
                }
            }

            return false;
        }
    }
}
