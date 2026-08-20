using System.Collections.Generic;
using ExpoTheExplorer.Data;

namespace ExpoTheExplorer.Systems.MetaSystem
{
    // Answers "what is on the expo grounds right now" from the catalog plus two facts
    // about the player: which day they are on, and which props they own.
    //
    // Everything arrives as a PARAMETER -- no asset loading, no GameState, no save file.
    // That is what leaves this assembly referencing nothing but Data, and it is why the
    // rules can be pinned down by tests before any UI exists to show them. The counterpart
    // is a rule for callers: this class never learns anything on its own, so a caller that
    // forgets to pass the real day index gets a confidently wrong answer rather than a
    // null reference.
    //
    // Two things are deliberately DERIVED here rather than stored anywhere (decisions.md
    // D-015/D-017): whether a location is unlocked, and whether a Day-unlocked prop is
    // open. Both fall out of comparing the player's day index against an authored one, so
    // there is no second copy to drift from the catalog.
    public static class MetaResolver
    {
        // Owned keys are the qualified "<location>.<item>" form (MetaCatalog.OwnershipKey),
        // which is what the save file carries -- local ids alone would collide across
        // locations.
        public static bool IsOwned(MetaLocation location, MetaItemDefinition item, ISet<string> ownedKeys)
        {
            if (location == null || item == null || ownedKeys == null) return false;
            if (string.IsNullOrWhiteSpace(location.Id) || string.IsNullOrWhiteSpace(item.Id)) return false;

            return ownedKeys.Contains(MetaCatalog.OwnershipKey(location.Id, item.Id));
        }

        public static bool IsLocationUnlocked(MetaLocation location, int currentDayIndex) =>
            location != null && currentDayIndex >= location.UnlockAtDayIndex;

        // In catalog order, so a caller rendering a switcher gets the same left-to-right
        // sequence the asset was authored in.
        //
        // The list overload beside the asset one is the same split MetaCatalogValidator
        // makes, for the same reason: taking the locations rather than the ScriptableObject
        // is what lets these rules be exercised without a Unity object graph, and the asset
        // overload is then a one-liner.
        public static List<MetaLocation> UnlockedLocations(MetaCatalog catalog, int currentDayIndex) =>
            UnlockedLocations(catalog?.Locations, currentDayIndex);

        public static List<MetaLocation> UnlockedLocations(
            IReadOnlyList<MetaLocation> locations, int currentDayIndex)
        {
            var unlocked = new List<MetaLocation>();
            if (locations == null) return unlocked;

            foreach (var location in locations)
            {
                if (IsLocationUnlocked(location, currentDayIndex)) unlocked.Add(location);
            }

            return unlocked;
        }

        // The location the screen should open on: the NEWEST unlocked one (K6-4), found by
        // walking back from the end rather than by taking the highest UnlockAtDayIndex --
        // catalog order is the authored order, and two locations may legitimately share an
        // unlock day. Returns -1 when nothing is unlocked, which the validator treats as a
        // content error (something must be reachable at day 0) rather than something this
        // class should paper over.
        public static int DefaultLocationIndex(MetaCatalog catalog, int currentDayIndex) =>
            DefaultLocationIndex(catalog?.Locations, currentDayIndex);

        public static int DefaultLocationIndex(IReadOnlyList<MetaLocation> locations, int currentDayIndex)
        {
            if (locations == null) return -1;

            for (var i = locations.Count - 1; i >= 0; i--)
            {
                if (IsLocationUnlocked(locations[i], currentDayIndex)) return i;
            }

            return -1;
        }

        // Whether the prop is drawn. Purchase props appear when bought; Day-unlocked props
        // when the player reaches their authored day. A Day-unlocked prop is NEVER "owned"
        // -- its key never enters the save file -- which is what keeps "not for sale" and
        // "already yours" from blurring into each other.
        public static bool IsActive(
            MetaLocation location, MetaItemDefinition item, ISet<string> ownedKeys, int currentDayIndex)
        {
            if (location == null || item == null) return false;

            // Area gating applies to ACTIVE, not just to purchasable, and that is a
            // deliberate choice rather than an oversight. Owning a prop whose area is not
            // owned should be unreachable -- you cannot buy it without the area -- but an
            // author can add requiresAreaId to a prop players already have, and then their
            // table would hang in mid-air over ungravelled grass. Tying visibility to the
            // area keeps the picture coherent whatever the catalog does next.
            if (!IsAreaSatisfied(location, item, ownedKeys)) return false;

            return item.Unlock switch
            {
                MetaUnlockKind.Purchase => IsOwned(location, item, ownedKeys),
                MetaUnlockKind.DayUnlock => currentDayIndex >= item.UnlockAtDayIndex,
                _ => false
            };
        }

        // True when the prop needs no area, or when the area it names is owned. An area id
        // that names nothing is treated as NOT satisfied: the validator reports it as an
        // error, and silently letting the prop through would hide a content bug behind
        // correct-looking behaviour.
        public static bool IsAreaSatisfied(MetaLocation location, MetaItemDefinition item, ISet<string> ownedKeys)
        {
            if (item == null) return false;
            if (string.IsNullOrWhiteSpace(item.RequiresAreaId)) return true;
            if (location?.Items == null) return false;

            foreach (var candidate in location.Items)
            {
                if (candidate == null || !candidate.UnlocksArea) continue;
                if (candidate.Id != item.RequiresAreaId) continue;

                return IsOwned(location, candidate, ownedKeys);
            }

            return false;
        }

        // Convenience for the view: every prop that should be drawn, already in draw order
        // (ascending SortOrder, ties by authored order so the result is stable). Computed
        // on demand rather than cached -- this runs when the screen opens or after a
        // purchase, not per frame, so the cost model puts it at event frequency.
        public static List<MetaItemDefinition> ActiveItems(
            MetaLocation location, ISet<string> ownedKeys, int currentDayIndex)
        {
            var active = new List<MetaItemDefinition>();
            if (location?.Items == null) return active;

            for (var i = 0; i < location.Items.Count; i++)
            {
                var item = location.Items[i];
                if (IsActive(location, item, ownedKeys, currentDayIndex)) active.Add(item);
            }

            // Insertion sort rather than List.Sort, because List.Sort is NOT stable: two
            // props sharing a SortOrder would swap places between calls, and the visible
            // symptom of that is overlapping props flickering over each other. Insertion
            // sort keeps authored order for ties, and n is a couple of dozen at most.
            for (var i = 1; i < active.Count; i++)
            {
                var current = active[i];
                var j = i - 1;
                while (j >= 0 && active[j].SortOrder > current.SortOrder)
                {
                    active[j + 1] = active[j];
                    j--;
                }
                active[j + 1] = current;
            }

            return active;
        }
    }
}
