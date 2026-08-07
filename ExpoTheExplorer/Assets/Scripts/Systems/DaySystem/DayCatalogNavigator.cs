using System.Collections.Generic;

namespace ExpoTheExplorer.Systems.DaySystem
{
    public static class DayCatalogNavigator
    {
        public static DayDefinition GetDayAt(IReadOnlyList<DayDefinition> catalog, int index)
        {
            if (catalog == null || index < 0 || index >= catalog.Count)
            {
                return null;
            }
            return catalog[index];
        }

        // A Day's own authored retry-difficulty variant applies for as long as
        // the caller is on a retry attempt of that Day (not just the first one) --
        // falls back to baseDay itself if it has no RetryVariant, or isn't
        // retrying at all.
        public static DayDefinition GetEffectiveDay(DayDefinition baseDay, bool isRetryAttempt)
        {
            if (!isRetryAttempt)
            {
                return baseDay;
            }
            return baseDay?.RetryVariant ?? baseDay;
        }
    }
}
