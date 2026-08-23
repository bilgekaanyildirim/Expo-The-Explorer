using System.Collections.Generic;
using ExpoTheExplorer.Data;

namespace ExpoTheExplorer.Systems.MetaSystem
{
    // Why a purchase was refused, not just that it was. The shop has to say six different
    // things -- "you already have this", "buy the square first" and "you cannot afford it"
    // are three different screens for the player -- and none of them can be recovered from
    // a bool. A caller that only wants the yes/no compares against Ok.
    public enum MetaPurchaseVerdict
    {
        Ok,

        // Not a shop item at all: it appears on its authored day (a drinks fridge, a
        // fryer). Distinct from AlreadyOwned because its key never enters the save file,
        // so "already yours" would be a lie even once it is on screen.
        NotForSale,

        AlreadyOwned,

        // The location itself is not visitable yet. Unreachable through the UI, since the
        // shop only ever shows the location being viewed -- kept because a caller that
        // gets this wrong should get a verdict rather than a silent sale.
        LocationLocked,

        // Needs an area expansion that is not owned: the paved square gates the props
        // standing on it.
        AreaLocked,

        NotEnoughMoney
    }

    // The purchase RULES, and nothing else: no wallet, no save file, no UI. It answers
    // "would this sale be legal", and the screen's composition root is what then spends
    // through Wallet and records the key through the profile writer.
    //
    // That split is the reason this assembly references nothing but Data (decisions.md
    // D-015's MetaSystem -> ProgressionSystem arrow turned out to be unnecessary and was
    // dropped): the money and the owned-item list already have single writers in
    // ProgressionSystem, and a rules class that reached for them would either duplicate
    // that ownership or become untestable. Passing the balance in costs one parameter.
    public static class MetaPurchase
    {
        public static MetaPurchaseVerdict Evaluate(
            MetaLocation location,
            MetaItemDefinition item,
            ISet<string> ownedKeys,
            int currentDayIndex,
            int softMoney)
        {
            if (location == null || item == null) return MetaPurchaseVerdict.NotForSale;

            // Order matters, and it runs from the most fundamental reason to the most
            // incidental. A Day-unlocked prop is not for sale no matter how much money is
            // in the wallet, and reporting "you cannot afford it" for something that was
            // never purchasable would send a player off to earn money for nothing.
            if (item.Unlock != MetaUnlockKind.Purchase) return MetaPurchaseVerdict.NotForSale;

            if (!MetaResolver.IsLocationUnlocked(location, currentDayIndex))
            {
                return MetaPurchaseVerdict.LocationLocked;
            }

            if (MetaResolver.IsOwned(location, item, ownedKeys)) return MetaPurchaseVerdict.AlreadyOwned;

            // Before affordability on purpose: an area-gated prop's price is irrelevant
            // until the area is owned, and "not enough money" would point the player at
            // the wrong problem.
            if (!MetaResolver.IsAreaSatisfied(location, item, ownedKeys)) return MetaPurchaseVerdict.AreaLocked;

            if (softMoney < item.Price) return MetaPurchaseVerdict.NotEnoughMoney;

            return MetaPurchaseVerdict.Ok;
        }

        public static bool CanBuy(
            MetaLocation location,
            MetaItemDefinition item,
            ISet<string> ownedKeys,
            int currentDayIndex,
            int softMoney) =>
            Evaluate(location, item, ownedKeys, currentDayIndex, softMoney) == MetaPurchaseVerdict.Ok;

        // Everything the shop should list for a location, IN THE ORDER it should list them.
        // Both halves of that are rules rather than presentation, which is why they live
        // here with the tests and not in the view.
        //
        // What drops out: the already-owned (nothing left to do with them), anything that
        // was never for sale (Day-unlocked props), and -- since the user's instruction on
        // 2026-08-21 -- anything whose AREA is still locked. That last one reverses half of
        // the plan's MS2, where the recommendation had been to show area-gated props so the
        // player could see what was still to come. The cost of hiding them is real and worth
        // naming: nothing on screen now tells the player that buying the Square opens more
        // props. That is a "coming soon" affordance for a later step, not a reason to keep
        // showing rows the user does not want.
        //
        // What STAYS: props that are simply unaffordable. Seeing what a thing costs is what
        // makes it worth saving for, so the shop dims those rows rather than hiding them.
        //
        // ORDER: affordable first, then by ascending price. The player's next possible
        // purchase should be under their thumb, and the cheapest of those is the one they
        // are most likely to take.
        public static List<MetaItemDefinition> ShopItems(
            MetaLocation location, ISet<string> ownedKeys, int currentDayIndex, int softMoney)
        {
            var offers = new List<MetaItemDefinition>();
            if (location?.Items == null) return offers;

            if (!MetaResolver.IsLocationUnlocked(location, currentDayIndex)) return offers;

            foreach (var item in location.Items)
            {
                if (item == null || item.Unlock != MetaUnlockKind.Purchase) continue;
                if (MetaResolver.IsOwned(location, item, ownedKeys)) continue;
                if (!MetaResolver.IsAreaSatisfied(location, item, ownedKeys)) continue;

                offers.Add(item);
            }

            SortForDisplay(offers, softMoney);
            return offers;
        }

        // Insertion sort, and NOT List.Sort, because List.Sort is not stable: two props at
        // the same price would be free to swap places on every rebuild, and a list that
        // reorders itself between one open and the next reads as a bug that only happens
        // sometimes. Ties therefore keep the order the catalog was authored in.
        // MetaResolver.ActiveItems made the same call for the same reason, and its ordering
        // is pinned by a test as this one is.
        private static void SortForDisplay(List<MetaItemDefinition> offers, int softMoney)
        {
            for (var i = 1; i < offers.Count; i++)
            {
                var candidate = offers[i];
                var j = i - 1;

                while (j >= 0 && SortsBefore(candidate, offers[j], softMoney))
                {
                    offers[j + 1] = offers[j];
                    j--;
                }

                offers[j + 1] = candidate;
            }
        }

        // Strictly before -- equal keys must answer false, or the insertion sort above stops
        // being stable and the tie-keeps-authored-order promise breaks.
        private static bool SortsBefore(MetaItemDefinition a, MetaItemDefinition b, int softMoney)
        {
            var affordableA = a.Price <= softMoney;
            var affordableB = b.Price <= softMoney;
            if (affordableA != affordableB) return affordableA;

            return a.Price < b.Price;
        }
    }
}
