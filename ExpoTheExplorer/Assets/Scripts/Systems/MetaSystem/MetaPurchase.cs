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

        // Everything the shop should list for a location: props that are for sale and not
        // yet owned, whatever the reason they cannot be bought right now. An unaffordable
        // or area-gated prop STAYS in the list on purpose -- seeing what is still to come,
        // and what it costs, is most of what a shop is for. Only the already-owned drop
        // out, because there is nothing left to do with them.
        public static List<MetaItemDefinition> ShopItems(
            MetaLocation location, ISet<string> ownedKeys, int currentDayIndex)
        {
            var offers = new List<MetaItemDefinition>();
            if (location?.Items == null) return offers;

            if (!MetaResolver.IsLocationUnlocked(location, currentDayIndex)) return offers;

            foreach (var item in location.Items)
            {
                if (item == null || item.Unlock != MetaUnlockKind.Purchase) continue;
                if (MetaResolver.IsOwned(location, item, ownedKeys)) continue;

                offers.Add(item);
            }

            return offers;
        }
    }
}
