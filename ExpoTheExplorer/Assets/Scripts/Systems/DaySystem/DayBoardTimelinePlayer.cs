using System.Collections.Generic;
using ExpoTheExplorer.Core;

namespace ExpoTheExplorer.Systems.DaySystem
{
    // Deterministic playback only -- never takes a Random, so it never behaves
    // differently between runs of the same authored Day (Q1: no runtime
    // randomness, only DayContentGenerator uses randomness, only at Editor
    // "Generate" time).
    public static class DayBoardTimelinePlayer
    {
        public static void ApplyForStep(BoardGrid board, IReadOnlyList<ResolvedBoardSpawnEntry> boardTimeline, long stepIndex)
        {
            if (boardTimeline == null) return;

            foreach (var entry in boardTimeline)
            {
                if (!PlaysOnStep(entry, stepIndex)) continue;

                var item = new BoardItem(entry.Item, entry.Modifications);
                if (entry.UseExactCell && board.TryPlaceItem(item, entry.X, entry.Y))
                {
                    continue;
                }

                board.RequestSpawn(item);
            }
        }

        // Does this Day put anything on the board for that step? Asked by GameManager about
        // Day Start (-1), where the answer decides whether the opening ticket fill
        // distributes at all -- a Day that authored its own opening board opens with
        // EXACTLY that board and nothing spawned on top of it.
        //
        // It lives HERE, next to the playback it must agree with, rather than as a LINQ
        // line at the call site: "which entries count" has a rule in it (the not-yet-
        // authored null Item is skipped), and a second copy of that rule could answer yes
        // for a timeline the player then places nothing from -- an opening with the
        // distribution suppressed and an empty board, which is the one outcome this whole
        // feature exists to prevent.
        public static bool HasEntriesForStep(IReadOnlyList<ResolvedBoardSpawnEntry> boardTimeline, long stepIndex)
        {
            if (boardTimeline == null) return false;

            foreach (var entry in boardTimeline)
            {
                if (PlaysOnStep(entry, stepIndex)) return true;
            }

            return false;
        }

        // The single filter both methods above run on. A null ENTRY is tolerated for the
        // same reason a null Item is: the authoring side builds this list row by row, and a
        // half-filled row is an editing state rather than a broken Day.
        private static bool PlaysOnStep(ResolvedBoardSpawnEntry entry, long stepIndex)
        {
            if (entry == null) return false;
            if (entry.TriggerStepIndex != stepIndex) return false;

            // Not-yet-authored entry (e.g. a freshly added row) -- nothing meaningful to spawn.
            return entry.Item != null;
        }
    }
}
