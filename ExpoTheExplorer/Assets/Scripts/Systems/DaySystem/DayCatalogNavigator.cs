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
    }
}
