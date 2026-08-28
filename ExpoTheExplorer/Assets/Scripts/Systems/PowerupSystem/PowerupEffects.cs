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
        // BY COUNT SINCE D-118 (2026-08-28, the user's decision), where it was by IDENTITY
        // until then. The old rule kept every burger on the board if any ticket wanted one,
        // and it argued that a player looking at a third burger says there are too many
        // rather than that it is unwanted. The user's answer is that "too many" is exactly
        // what this powerup should be clearing: a board holding five burgers when two are
        // wanted IS noise, and a sweep that leaves it has not cleared the board. So the set
        // became a BUDGET -- how many of each key the active tickets still need -- and every
        // copy past it goes.
        //
        // Summed ACROSS slots, per key. Three tickets each wanting a burger keep three: the
        // keys collapse, the counts must not, and a HashSet cannot say that at all.
        //
        // TRAY CONTENTS ARE NOW CONSULTED, which reverses the second half of the old note.
        // Its argument was that a wrong order scatters the whole tray back onto the board, so
        // a burger in a tray does not make a board burger unwanted -- true, and the reason it
        // is safe to consult them anyway: the scatter RETURNS those items, so a board cleared
        // to the outstanding count is restored by the very event that would need it. What the
        // old note got right and is preserved: reading the tray must not drag a TraySystem
        // reference into this assembly, which is why the contents arrive as plain BoardItems
        // from the caller, exactly as PlanAutoCollect already takes them.
        //
        // Auto-Collect and Noise Clear now share ONE definition of what is still owed
        // (OutstandingFor), rather than each holding its own idea of it.
        //
        // IT CANNOT BREAK THE GUARANTEED-TICKET RULE (GDD Section 4), and there are two
        // independent reasons. First, everything an active ticket requires is kept, and the
        // guarantee is precisely "at least one ticket stays completable". Second, as a
        // backstop, BoardDistributor caches nothing -- SpawnMissingRequiredItems recounts
        // the board every round and re-spawns whatever a guaranteed ticket is missing, so
        // even a mistake here would close on the next ticket assignment. What this DOES
        // remove is a pre-spawn for a guaranteed ticket still sitting in the upcoming queue;
        // that one is re-created the moment it reaches a slot.
        // THE DATA-ONLY PATH, still here and still the fallback. NoiseClearRunner exists to
        // drop the cleared items off the board instead of blinking them out (D-120), and it
        // uses PlanNoiseClear below; this stays for the scene that has no runner wired, and
        // it is what every test drives, because the RULE is the same either way and the
        // animation is not something a test can see.
        public static bool ClearUnneededItems(
            GameState state, IReadOnlyList<IReadOnlyList<BoardItem>> trayContents)
        {
            if (state?.Board == null) return false;

            // The budget is CONSUMED as the sweep walks the board, so a key with an allowance
            // of two keeps the first two copies it meets and drops the rest. Which two is not
            // specified: they are whichever the scan order reaches first, and inventing a
            // "nearest the trays" rule nobody asked for would be a rule nobody can check.
            var allowance = OutstandingAcrossSlots(state, trayContents);
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

                    // Kept and CHARGED to the budget, or removed once the budget is spent.
                    // Only a key the tickets actually want is ever in the dictionary, so an
                    // item nobody ordered misses on the lookup and goes -- the identity rule
                    // this replaced, still intact as the budget's zero case.
                    var key = new RequiredItemKey(item.Config, item.Modifications);
                    if (allowance.TryGetValue(key, out var remaining) && remaining > 0)
                    {
                        allowance[key] = remaining - 1;
                        continue;
                    }

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

        // GDD 5.2 #1's DECISION half -- the WHOLE press, decided before anything moves.
        // The moving is somewhere else entirely (AutoCollectRunner, in the UI assembly),
        // and the split is not tidiness: a tray's contents are drawn by reparenting the
        // dragged object, so the collect has to go through the ordinary drop path, which
        // lives in a MonoBehaviour that no EditMode test can reach. Keeping the CHOOSING
        // here is what makes the powerup's actual rules testable at all.
        //
        // A PLAN RATHER THAN A NEXT-MOVE, and that is the fix for both bugs the player
        // reported (D-110). The previous shape answered "what should this slot take next"
        // and was re-asked after every single move, against the LIVE board:
        //
        //   * It collected items that were never on the board when the button was pressed.
        //     BoardGrid.RemoveItem BACKFILLS the cell it empties from the pending-spawn
        //     queue, and a delivery cascades synchronously into the NEXT ticket's required
        //     items spawning. Re-scanning after every move meant slots 1 and 2 helped
        //     themselves to whatever slot 0's own delivery had just produced.
        //   * It left finishable tickets unfinished. Walking slot 0, then 1, then 2 and
        //     grabbing whatever each one happened to want is greedy and blind: with one
        //     burger on the board, slot 0 (burger + a cola that does not exist) took it and
        //     stranded it in a tray that could never resolve, while slot 1 -- which wanted
        //     only that burger -- got nothing.
        //
        // So the board is read ONCE, into a budget of (key -> cells), and every ticket is
        // paid out of that fixed budget:
        //
        //   Pass 1, slots in order: a ticket whose outstanding requirement can be paid IN
        //   FULL is reserved and completed. One that cannot is skipped with its items left
        //   in the pot for a later slot -- that is the whole of the second fix.
        //   Pass 2, slots in order, only the tickets pass 1 did not complete: whatever the
        //   remaining budget can still put in them.
        //
        // Greedy in slot order rather than an optimal assignment, which is the rule the
        // user asked for ("tickets in order") and can never complete FEWER tickets than the
        // old code did.
        //
        // IT CANNOT COST A LIFE, and that property is now structural rather than incidental.
        // The tray's batch check fires the moment the tray is FULL, so the plan either
        // fills a slot exactly (a correct order -> delivery) or deliberately leaves at
        // least one space free. See MaxMovesFor below.
        //
        // Coordinates stay valid for the whole run even though the board is mutated between
        // moves: removing an item only ever backfills the cell it emptied, never moves
        // another one. The caller re-checks the exact BoardItem instance at each cell
        // anyway, so a board that changed under the plan skips the move instead of taking
        // the wrong item.
        public static List<AutoCollectMove> PlanAutoCollect(
            GameState state, IReadOnlyList<IReadOnlyList<BoardItem>> trayContents)
        {
            var moves = new List<AutoCollectMove>();
            if (state?.Board == null) return moves;

            var budget = BuildBudget(state.Board);
            var outstanding = new Dictionary<RequiredItemKey, int>[GameState.TicketSlotCount];
            var maxMoves = new int[GameState.TicketSlotCount];
            var completed = new bool[GameState.TicketSlotCount];

            for (var slot = 0; slot < GameState.TicketSlotCount; slot++)
            {
                var tray = TrayFor(trayContents, slot);
                outstanding[slot] = OutstandingFor(state, slot, tray);
                maxMoves[slot] = MaxMovesFor(state, slot, tray);
            }

            // Pass 1 -- complete what can be completed, out of the shared budget.
            for (var slot = 0; slot < GameState.TicketSlotCount; slot++)
            {
                var owed = outstanding[slot];
                if (owed == null || TotalCount(owed) == 0) continue;

                // A tray still holding something its ticket does not want cannot be
                // completed at all: the junk occupies space the order needs, so filling the
                // rest would only reach the batch check with a wrong item in it. Such a
                // slot falls through to pass 2, which leaves a space free.
                if (TotalCount(owed) != maxMoves[slot]) continue;

                if (!CanPayInFull(owed, budget)) continue;

                Take(owed, budget, slot, TotalCount(owed), moves);
                completed[slot] = true;
            }

            // Pass 2 -- partial fills, on the tickets pass 1 could not finish, out of
            // whatever the budget still holds. Tickets that arrive DURING the run are
            // unreachable from here: this list is closed before the caller's first move.
            for (var slot = 0; slot < GameState.TicketSlotCount; slot++)
            {
                if (completed[slot]) continue;

                var owed = outstanding[slot];
                if (owed == null || TotalCount(owed) == 0) continue;

                // One space is deliberately left free. A pass-2 slot is by definition one
                // the budget cannot finish, so this only ever binds on a junk tray -- and
                // there it is the difference between helping and triggering the player's
                // own wrong-order life loss.
                var limit = maxMoves[slot] - 1;
                if (limit <= 0) continue;

                Take(owed, budget, slot, limit, moves);
            }

            return moves;
        }

        // Everything in this slot's tray that its own ticket does NOT want -- a mis-drop
        // the player has not resolved, or a surplus copy beyond the required count. The
        // caller sends these back to the board before the plan is built, which is what
        // makes such a tray completable again and puts the item back in play for whichever
        // ticket does want it (D-110).
        //
        // Returned as the item INSTANCES rather than as counts, because the caller has to
        // find each one's GameObject in the tray to send it home.
        //
        // A slot with no active ticket is left alone rather than emptied: TrayManager
        // .OnTicketAssigned already scatters an orphaned tray the instant its ticket
        // changes, and a second path doing the same job is how two writers start
        // disagreeing.
        public static List<BoardItem> UnwantedTrayItems(
            GameState state, int slotIndex, IReadOnlyList<BoardItem> trayContents)
        {
            var unwanted = new List<BoardItem>();

            if (state == null || trayContents == null || trayContents.Count == 0) return unwanted;
            if (slotIndex < 0 || slotIndex >= GameState.TicketSlotCount) return unwanted;

            var ticket = state.TicketSlots[slotIndex];
            if (ticket == null || ticket.State != TicketState.Active) return unwanted;

            var allowance = TicketRequirements.RequiredCounts(ticket);

            foreach (var item in trayContents)
            {
                var key = new RequiredItemKey(item.Config, item.Modifications);

                // Counts, not just identity: a ticket wanting one cola with two in the tray
                // wants the second one gone as much as it wants a wrong food gone.
                if (allowance.TryGetValue(key, out var remaining) && remaining > 0)
                {
                    allowance[key] = remaining - 1;
                    continue;
                }

                unwanted.Add(item);
            }

            return unwanted;
        }

        // The board as (key -> cells in scan order). Scan order decides only WHICH of
        // several identical items is taken, and they are identical by construction -- the
        // key is the (food + modification) combo that the tray's batch check compares on.
        private static Dictionary<RequiredItemKey, List<AutoCollectMove>> BuildBudget(BoardGrid board)
        {
            var budget = new Dictionary<RequiredItemKey, List<AutoCollectMove>>();

            for (var y = 0; y < board.Height; y++)
            {
                for (var x = 0; x < board.Width; x++)
                {
                    var item = board.ItemAt(x, y);
                    if (item == null) continue;

                    var key = new RequiredItemKey(item.Config, item.Modifications);
                    if (!budget.TryGetValue(key, out var cells))
                    {
                        cells = new List<AutoCollectMove>();
                        budget[key] = cells;
                    }

                    // Slot is filled in when the move is actually handed to a ticket; here
                    // this is just "an item of this key, at this cell".
                    cells.Add(new AutoCollectMove(-1, x, y, item));
                }
            }

            return budget;
        }

        // The ticket's required multiset MINUS what its tray already holds, so a slot that
        // is halfway filled by hand asks only for the rest. Counts matter here, unlike in
        // The same sweep as ClearUnneededItems, but it DECIDES instead of doing: which cells
        // would be cleared, and which item each one held when the decision was made. Pure, so
        // the rule stays testable without a scene, while NoiseClearRunner does the removing
        // and the falling (D-120) -- the split PlanAutoCollect and AutoCollectRunner already
        // made, for the same reason.
        //
        // IT CARRIES THE EXPECTED ITEM, and that is not bookkeeping. A plan is a CLOSED LIST
        // computed before the first removal, and every removal BACKFILLS its cell from the
        // pending-spawn queue -- so by the time the runner reaches the fifth entry, the
        // second entry's cell may hold something else entirely, possibly a required item.
        // The runner re-verifies each cell against this instance and skips it if the board
        // moved on. ClearUnneededItems avoids the whole problem by reading each cell fresh
        // immediately before removing it; a planner cannot, so it hands over what it saw.
        public static List<NoiseClearRemoval> PlanNoiseClear(
            GameState state, IReadOnlyList<IReadOnlyList<BoardItem>> trayContents)
        {
            var removals = new List<NoiseClearRemoval>();
            if (state?.Board == null) return removals;

            var allowance = OutstandingAcrossSlots(state, trayContents);
            var board = state.Board;

            for (var y = 0; y < board.Height; y++)
            {
                for (var x = 0; x < board.Width; x++)
                {
                    var item = board.ItemAt(x, y);
                    if (item == null) continue;

                    var key = new RequiredItemKey(item.Config, item.Modifications);
                    if (allowance.TryGetValue(key, out var remaining) && remaining > 0)
                    {
                        allowance[key] = remaining - 1;
                        continue;
                    }

                    removals.Add(new NoiseClearRemoval(x, y, item));
                }
            }

            return removals;
        }

        // Every active slot's outstanding requirement, added up per key -- the budget
        // ClearUnneededItems sweeps against.
        //
        // Summed rather than maxed, which is the whole difference from the HashSet it
        // replaced: three tickets each wanting one burger want THREE burgers between them,
        // and a rule that collapsed them to one would clear a board the player still needs.
        //
        // A slot with no active ticket contributes nothing -- OutstandingFor answers null for
        // that, deliberately distinct from an empty dictionary, and both mean "add nothing"
        // here. Non-positive entries are skipped so a fully-tray-satisfied requirement cannot
        // leave a zero in the budget that reads as an allowance.
        private static Dictionary<RequiredItemKey, int> OutstandingAcrossSlots(
            GameState state, IReadOnlyList<IReadOnlyList<BoardItem>> trayContents)
        {
            var total = new Dictionary<RequiredItemKey, int>();

            for (var slot = 0; slot < GameState.TicketSlotCount; slot++)
            {
                var owed = OutstandingFor(state, slot, TrayFor(trayContents, slot));
                if (owed == null) continue;

                foreach (var pair in owed)
                {
                    if (pair.Value <= 0) continue;

                    total[pair.Key] = total.TryGetValue(pair.Key, out var running)
                        ? running + pair.Value
                        : pair.Value;
                }
            }

            return total;
        }

        // ClearUnneededItems and PlanAutoCollect: a ticket wanting two colas with one already
        // in the tray still wants exactly one more.
        //
        // Null (not empty) for a slot with no active ticket -- the two are different
        // answers and the caller treats them the same only by accident otherwise.
        private static Dictionary<RequiredItemKey, int> OutstandingFor(
            GameState state, int slotIndex, IReadOnlyList<BoardItem> trayContents)
        {
            var ticket = state.TicketSlots[slotIndex];
            if (ticket == null || ticket.State != TicketState.Active) return null;

            var outstanding = TicketRequirements.RequiredCounts(ticket);
            if (trayContents == null) return outstanding;

            foreach (var item in trayContents)
            {
                var key = new RequiredItemKey(item.Config, item.Modifications);

                // Only decrements a key the ticket actually wants. An item in the tray that
                // the ticket does NOT want must not reduce a requirement -- it is going to
                // cost a life when the tray fills, and quietly treating it as progress
                // would make this powerup finish that mistake for them. The caller normally
                // removes those first (UnwantedTrayItems), so this is the case where one
                // could not be sent home -- the player is holding it mid-drag.
                if (outstanding.TryGetValue(key, out var count) && count > 0)
                {
                    outstanding[key] = count - 1;
                }
            }

            return outstanding;
        }

        // How many items may be added to this tray at all: TraySlot.IsFull compares the
        // tray's COUNT against the ticket's required count, so this is the free space, and
        // reaching it is what runs the batch check. On a clean tray it equals the
        // outstanding total; on a tray with junk in it, it is smaller -- which is exactly
        // how PlanAutoCollect recognises one.
        private static int MaxMovesFor(GameState state, int slotIndex, IReadOnlyList<BoardItem> trayContents)
        {
            var ticket = state.TicketSlots[slotIndex];
            if (ticket == null || ticket.State != TicketState.Active) return 0;

            var occupied = trayContents?.Count ?? 0;
            return ticket.RequiredItems.Count - occupied;
        }

        private static int TotalCount(Dictionary<RequiredItemKey, int> counts)
        {
            var total = 0;
            foreach (var count in counts.Values) total += count;
            return total;
        }

        private static bool CanPayInFull(
            Dictionary<RequiredItemKey, int> owed, Dictionary<RequiredItemKey, List<AutoCollectMove>> budget)
        {
            foreach (var entry in owed)
            {
                if (entry.Value <= 0) continue;
                if (!budget.TryGetValue(entry.Key, out var cells) || cells.Count < entry.Value) return false;
            }

            return true;
        }

        // Moves up to `limit` items from the budget into the plan for this slot, and
        // decrements both sides as it goes -- an item handed to one ticket is gone from the
        // pot for every later one, which is the entire point of planning over a budget.
        private static void Take(
            Dictionary<RequiredItemKey, int> owed,
            Dictionary<RequiredItemKey, List<AutoCollectMove>> budget,
            int slotIndex,
            int limit,
            List<AutoCollectMove> moves)
        {
            foreach (var key in new List<RequiredItemKey>(owed.Keys))
            {
                var wanted = owed[key];
                if (wanted <= 0) continue;
                if (!budget.TryGetValue(key, out var cells)) continue;

                while (wanted > 0 && cells.Count > 0 && limit > 0)
                {
                    var cell = cells[0];
                    cells.RemoveAt(0);
                    moves.Add(new AutoCollectMove(slotIndex, cell.X, cell.Y, cell.Item));
                    wanted--;
                    limit--;
                }

                owed[key] = wanted;
                if (limit <= 0) return;
            }
        }

        private static IReadOnlyList<BoardItem> TrayFor(
            IReadOnlyList<IReadOnlyList<BoardItem>> trayContents, int slotIndex) =>
            trayContents != null && slotIndex < trayContents.Count ? trayContents[slotIndex] : null;
    }

    // One step of an Auto-Collect plan: take THIS item, from THIS cell, into THIS slot's
    // tray. The item is carried alongside the coordinate rather than looked up again at
    // execution time, so the runner can tell "the cell I planned" from "whatever is sitting
    // in that cell now" -- a delivery mid-run backfills cells behind it, and a move whose
    // item has changed is skipped rather than taken blind.
    public readonly struct AutoCollectMove
    {
        public int SlotIndex { get; }
        public int X { get; }
        public int Y { get; }
        public BoardItem Item { get; }

        public AutoCollectMove(int slotIndex, int x, int y, BoardItem item)
        {
            SlotIndex = slotIndex;
            X = x;
            Y = y;
            Item = item;
        }
    }

    // One cell a Noise Clear plan intends to empty, carrying the item it held when the plan
    // was made -- the same shape AutoCollectMove has, and for the identical reason: every
    // removal backfills its cell from the pending-spawn queue, so a runner walking a closed
    // list must be able to tell "the item I planned to clear" from "whatever is in that cell
    // now", which can be a required item that has just landed.
    public readonly struct NoiseClearRemoval
    {
        public int X { get; }
        public int Y { get; }
        public BoardItem Item { get; }

        public NoiseClearRemoval(int x, int y, BoardItem item)
        {
            X = x;
            Y = y;
            Item = item;
        }
    }
}
