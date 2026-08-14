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
            foreach (var entry in boardTimeline)
            {
                if (entry.TriggerStepIndex != stepIndex) continue;
                if (entry.Item == null) continue; // not-yet-authored entry (e.g. a freshly added row) -- nothing meaningful to spawn

                var item = new BoardItem(entry.Item, entry.Modifications);
                if (entry.UseExactCell && board.TryPlaceItem(item, entry.X, entry.Y))
                {
                    continue;
                }

                board.RequestSpawn(item);
            }
        }
    }
}
