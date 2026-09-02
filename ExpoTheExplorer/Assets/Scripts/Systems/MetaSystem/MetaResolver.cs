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
    // The prop the player is waiting for, and how far along that wait is. A struct rather
    // than two out parameters because both callers -- the view and its tests -- read the
    // pair together, and "item plus progress" is one answer rather than two.
    public readonly struct MetaDayUnlockPreview
    {
        // Null means there is nothing coming: every Day-unlocked prop in this location is
        // already open, or none was ever authored.
        public readonly MetaItemDefinition Item;

        // 0 on the day the wait began, 1 on the day the prop opens. Clamped, so a caller can
        // hand it straight to a fill amount.
        public readonly float Progress;

        public MetaDayUnlockPreview(MetaItemDefinition item, float progress)
        {
            Item = item;
            Progress = progress;
        }
    }

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

        // Whether this location opened INSIDE the window the celebration is owed for -- the
        // location-sized answer to the question DayUnlocksBetween asks about props, and it uses
        // the same half-open window `(since, current]` and the same marker
        // (GameSession.LastCelebratedDayIndex), so the two cannot come to disagree about which
        // day boundary has already been paid out.
        //
        // Deliberately does NOT ask whether the location authors a message. HasUnlockPopup is
        // the opt-in and it lives on the data, exactly as it does for a prop; a resolver that
        // filtered on it would be a second place where "no message means no celebration" is
        // spelled, and the first one to change would be the one nobody remembered.
        public static bool OpenedBetween(MetaLocation location, int sinceDayIndex, int currentDayIndex)
        {
            if (location == null || currentDayIndex <= sinceDayIndex) return false;

            return location.UnlockAtDayIndex > sinceDayIndex && location.UnlockAtDayIndex <= currentDayIndex;
        }

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
            if (!IsAreaSatisfied(location, item, ownedKeys, currentDayIndex)) return false;

            return StandsOnItsOwnTerms(item, location, ownedKeys, currentDayIndex);
        }

        // Whether the prop has arrived by ITS OWN rule, with its area left out of the question:
        // a bought prop is here once bought, a Day-unlocked one once its day comes. Split out
        // of IsActive so the area walk below can ask it of an AREA prop without re-entering
        // IsActive and starting a recursion -- see IsAreaSatisfied for why that matters.
        private static bool StandsOnItsOwnTerms(
            MetaItemDefinition item, MetaLocation location, ISet<string> ownedKeys, int currentDayIndex) =>
            item.Unlock switch
            {
                MetaUnlockKind.Purchase => IsOwned(location, item, ownedKeys),
                MetaUnlockKind.DayUnlock => currentDayIndex >= item.UnlockAtDayIndex,
                _ => false
            };

        // True when the prop needs no area, or when every area up its chain is STANDING THERE.
        //
        // "Standing there", not "bought", and that distinction is the whole of decisions.md
        // D-155. This used to ask IsOwned of the area, which silently made a whole authoring
        // shape dead: an area prop that opens on a DAY can never be owned, because a
        // Day-unlocked prop's key never enters the save file. meta1 authors exactly that --
        // the fryer opens on Day 6 with unlocksArea ticked and the tent sits behind it -- and
        // the tent was therefore unbuyable forever, which is the 14/15 the user could not
        // clear. Every tool called the catalog valid; only this predicate disagreed. Asking
        // whether the area is PRESENT covers both kinds and changes nothing for a bought area,
        // where present and owned are the same thing.
        //
        // THE WHOLE CHAIN, not one link, and that is stricter than what it replaced. The tent
        // needs the fryer, and the fryer needs the building bought -- so a player who skipped
        // the building must not be sold a tent to stand next to a fryer that is not drawn.
        //
        // Walked ITERATIVELY. The recursive spelling -- ask IsActive of the area, which asks
        // this of ITS area -- reads better and is a stack overflow waiting for the first
        // catalog that authors a cycle (A behind B's area, B behind A's). MetaCatalogValidator
        // catches a prop requiring its OWN area and nothing longer, so the guard has to live
        // here. The step cap is the item count: a chain that never repeats a prop cannot be
        // longer than that, so exceeding it IS a cycle, and it costs a counter rather than the
        // HashSet a visited-set would allocate on every prop of every redraw.
        //
        // An area id that names nothing is still NOT satisfied: the validator reports it as an
        // error, and silently letting the prop through would hide a content bug behind
        // correct-looking behaviour.
        public static bool IsAreaSatisfied(
            MetaLocation location, MetaItemDefinition item, ISet<string> ownedKeys, int currentDayIndex)
        {
            if (item == null) return false;
            if (string.IsNullOrWhiteSpace(item.RequiresAreaId)) return true;
            if (location?.Items == null) return false;

            var requiredAreaId = item.RequiresAreaId;

            for (var step = 0; !string.IsNullOrWhiteSpace(requiredAreaId); step++)
            {
                if (step >= location.Items.Count) return false;

                var area = FindArea(location, requiredAreaId);
                if (area == null) return false;
                if (!StandsOnItsOwnTerms(area, location, ownedKeys, currentDayIndex)) return false;

                requiredAreaId = area.RequiresAreaId;
            }

            return true;
        }

        // The item that OPENS the named area. UnlocksArea is part of the match, not a check
        // afterwards: an id shared by a prop that does not open an area must not shadow the one
        // that does.
        private static MetaItemDefinition FindArea(MetaLocation location, string areaId)
        {
            foreach (var candidate in location.Items)
            {
                if (candidate == null || !candidate.UnlocksArea) continue;
                if (candidate.Id != areaId) continue;

                return candidate;
            }

            return null;
        }

        // The NEXT Day-unlocked prop and how far the wait has come. The view draws it as a
        // faint silhouette filling from the bottom, so the player can see what is coming and
        // roughly when (the user's request, 2026-08-21).
        //
        // Nothing new is authored for this. The wait STARTS at the most recent Day-unlock
        // milestone the player has already passed, and day 0 when there is none -- the
        // user's choice among three options, and the one that needs no second number per
        // prop. The practical consequence is that the pace varies: two unlocks eight days
        // apart fill slowly, two days apart fill fast. That is the catalog's rhythm showing
        // through rather than a defect.
        //
        // Only ONE prop is previewed -- the nearest by day, ties by authored order. Showing
        // every future prop would fill the grounds with translucent objects and blur the one
        // thing the player is actually close to.
        public static MetaDayUnlockPreview NextDayUnlock(
            MetaLocation location, ISet<string> ownedKeys, int currentDayIndex)
        {
            if (location?.Items == null) return default;

            MetaItemDefinition target = null;
            var previousMilestone = 0;

            foreach (var item in location.Items)
            {
                if (item == null || item.Unlock != MetaUnlockKind.DayUnlock) continue;

                // Area gating applies to the preview too, not only to IsActive. What holds
                // such a prop back is a PURCHASE, not time, and a bar creeping toward full
                // would promise "wait and it comes" when the honest answer is "buy the
                // square". A prop whose day has passed but whose area is unowned therefore
                // shows nothing at all, which is the same silence IsActive already gives it.
                if (!IsAreaSatisfied(location, item, ownedKeys, currentDayIndex)) continue;

                if (item.UnlockAtDayIndex <= currentDayIndex)
                {
                    // Already open: it is not what we are waiting for, but it may be where
                    // the current wait started.
                    if (item.UnlockAtDayIndex > previousMilestone) previousMilestone = item.UnlockAtDayIndex;
                    continue;
                }

                // Strictly less, so ties keep authored order -- the same stability rule
                // ActiveItems and MetaPurchase's ordering follow.
                if (target == null || item.UnlockAtDayIndex < target.UnlockAtDayIndex) target = item;
            }

            if (target == null) return default;

            // The span cannot be zero in practice: the target's day is strictly after today
            // and the milestone is at or before it, so this is at least 1. Guarded anyway,
            // because a catalog is data an author is part-way through and two props on one
            // day is the sort of thing that reaches here before the validator is read.
            var span = target.UnlockAtDayIndex - previousMilestone;
            var progress = span <= 0 ? 0f : (float)(currentDayIndex - previousMilestone) / span;

            return new MetaDayUnlockPreview(target, progress < 0f ? 0f : progress > 1f ? 1f : progress);
        }

        // Day-unlocked props that became active BETWEEN two days -- what the main screen owes
        // the player a celebration for (D-041). Exclusive of `sinceDayIndex` and inclusive of
        // `currentDayIndex`: the "since" day has already been accounted for, so a prop that
        // opened exactly then is not owed again. That is also what stops a brand new player,
        // whose marker sits at 0, from being shown a day-0 prop as though it had just
        // arrived.
        //
        // Returned in authored catalog order, because two props may share a day and the
        // screen shows them one after another -- so the order has to be something an author
        // controls rather than whatever the list happens to yield.
        // Catalog-level overload, for callers that hold the asset rather than one location --
        // the day scene asking "will tomorrow open something" (D-042). It resolves the SAME
        // location the meta screen will open on, which is what makes "sent back to the menu
        // but shown nothing" impossible: the celebration only ever starts from Start, and at
        // that moment the viewed location IS the newest unlocked one.
        //
        // NOTE the two overloads mean a bare `null` names neither -- the ambiguity that cost
        // a CS0121 on DefaultLocationIndex earlier. Cast at the call site if you ever need to
        // pass one.
        public static List<MetaItemDefinition> DayUnlocksBetween(
            MetaCatalog catalog, ISet<string> ownedKeys, int sinceDayIndex, int currentDayIndex)
        {
            var index = DefaultLocationIndex(catalog, currentDayIndex);
            return index < 0
                ? new List<MetaItemDefinition>()
                : DayUnlocksBetween(catalog.Locations[index], ownedKeys, sinceDayIndex, currentDayIndex);
        }

        public static List<MetaItemDefinition> DayUnlocksBetween(
            MetaLocation location, ISet<string> ownedKeys, int sinceDayIndex, int currentDayIndex)
        {
            var opened = new List<MetaItemDefinition>();
            if (location?.Items == null || currentDayIndex <= sinceDayIndex) return opened;

            foreach (var item in location.Items)
            {
                if (item == null || item.Unlock != MetaUnlockKind.DayUnlock) continue;
                if (item.UnlockAtDayIndex <= sinceDayIndex || item.UnlockAtDayIndex > currentDayIndex) continue;

                // The area gate again: a prop whose area is unowned is not on screen, so
                // there is nothing to zoom in on and nothing to celebrate. Buying the area
                // later makes it appear without a celebration, which is the honest outcome --
                // the purchase already was the moment.
                if (!IsAreaSatisfied(location, item, ownedKeys, currentDayIndex)) continue;

                opened.Add(item);
            }

            return opened;
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
