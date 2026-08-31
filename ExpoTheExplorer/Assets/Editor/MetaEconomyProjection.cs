using System.Collections.Generic;
using System.IO;
using System.Linq;
using ExpoTheExplorer.Core;
using ExpoTheExplorer.Data;
using ExpoTheExplorer.Systems.DaySystem;
using ExpoTheExplorer.Systems.EconomySystem;
using UnityEditor;
using UnityEngine;

namespace ExpoTheExplorer.Editor
{
    // What one authored Day is GUARANTEED to pay, and nothing above that.
    //
    // The guarantee rests on three facts read out of the runtime rather than assumed:
    //
    //  1. A day does NOT need every ticket delivered. TicketSlotManager ends the day when
    //     the authored sequence is exhausted AND every slot is empty, and a timeout reaches
    //     that same end -- Tick cancels the expired ticket and pulls the sequence's next
    //     entry into the freed slot. So a day finishes with expired tickets in it; the
    //     player is simply not paid for them.
    //  2. Only a TIMEOUT costs money. A wrong delivery spends a life and nothing else
    //     (GameManager.HandleWrongDelivery) -- the ticket stays standing and can still be
    //     delivered, for a smaller tip at worst. So the expired ticket is the only failure
    //     with a price tag, and it is the one modelled here.
    //  3. Order Value is paid in full on every delivery and is never scaled by anything
    //     (EconomyCalculator: "a late delivery loses tip, never the food's own price").
    //     The smallest tip a delivery can earn is the Critical rate.
    //
    // Which gives two floors, and both are worth an author's eye: the day with nothing lost,
    // and the day with as much lost as a player can absorb on their own lives.
    public readonly struct DayIncomeFloor
    {
        public int DayIndex { get; }

        // Every ticket's minimum payout, sorted HIGH to LOW. Sorted at construction because
        // the only question ever asked of it is "drop the n priciest", and answering that
        // from a sorted list is a slice rather than a re-sort per repaint.
        //
        // Per-ticket rather than a single total: a total cannot answer what a day looks like
        // when its two best orders expire, and that is the number this tool exists for.
        public IReadOnlyList<int> TicketPayouts { get; }

        // Summed base prices of every required item across the day -- the part of the
        // payout no play quality can move.
        public int OrderValue { get; }

        // Item ids the food catalog could not resolve. They contribute 0 here, the same as
        // at runtime (EconomyCalculator skips a null item), so a Day carrying one is costed
        // LOW rather than skipped -- and the count is surfaced so the number can be
        // distrusted rather than silently believed.
        public int UnresolvedItems { get; }

        // Items that resolved but are priced at 0. Also a content bug, and also one that
        // drags this floor down, so it is reported next to the unresolved ones.
        public int UnpricedItems { get; }

        public DayIncomeFloor(
            int dayIndex, IReadOnlyList<int> ticketPayouts, int orderValue, int unresolvedItems, int unpricedItems)
        {
            DayIndex = dayIndex;
            TicketPayouts = ticketPayouts ?? new List<int>();
            OrderValue = orderValue;
            UnresolvedItems = unresolvedItems;
            UnpricedItems = unpricedItems;
        }

        public int TicketCount => TicketPayouts.Count;

        // Every ticket delivered, every one of them at the Critical tip. Nothing can pay
        // less than this without a ticket being lost outright.
        public int CleanFloor => TicketPayouts.Sum();

        // The same day with `lostTickets` tickets expired. WORST case, not average: the
        // priciest orders are assumed to be the ones that ran out, which is why the list is
        // sorted descending and this skips from the front.
        //
        // Erring in that direction is deliberate for an authoring floor -- a player who
        // loses two cheap tickets earns MORE than the panel promised, and a price set
        // against this number is affordable for everyone rather than for the lucky.
        public int FloorWith(int lostTickets)
        {
            if (lostTickets <= 0) return CleanFloor;

            var total = 0;
            for (var i = lostTickets; i < TicketPayouts.Count; i++)
            {
                total += TicketPayouts[i];
            }

            return total;
        }
    }

    // What one location's props cost, split by what the split MEANS: only Purchase props
    // are ever paid for, so a location's price tag is their prices and nothing else.
    public readonly struct MetaLocationCost
    {
        public int PurchaseCount { get; }
        public int PurchaseTotal { get; }
        public int DayUnlockCount { get; }

        // Purchase props authored at price 0. Free is a content bug rather than a valid
        // default (the field's own tooltip says so), and it is invisible in a total.
        public int FreeCount { get; }

        public MetaLocationCost(int purchaseCount, int purchaseTotal, int dayUnlockCount, int freeCount)
        {
            PurchaseCount = purchaseCount;
            PurchaseTotal = purchaseTotal;
            DayUnlockCount = dayUnlockCount;
            FreeCount = freeCount;
        }

        public static MetaLocationCost Of(MetaLocation location)
        {
            if (location?.Items == null) return default;

            var purchaseCount = 0;
            var purchaseTotal = 0;
            var dayUnlockCount = 0;
            var freeCount = 0;

            foreach (var item in location.Items)
            {
                if (item == null) continue;

                if (item.Unlock != MetaUnlockKind.Purchase)
                {
                    dayUnlockCount++;
                    continue;
                }

                purchaseCount++;
                purchaseTotal += item.Price;
                if (item.Price <= 0) freeCount++;
            }

            return new MetaLocationCost(purchaseCount, purchaseTotal, dayUnlockCount, freeCount);
        }

        // Every location up to and including `throughIndex`, or the whole catalog when that
        // is -1. The cumulative form is the one the verdict needs: a player reaching the
        // third location has had to pay for the first two as well, so costing this location
        // alone against the whole game's income would flatter every price in the catalog.
        public static MetaLocationCost Of(MetaCatalog catalog, int throughIndex = -1)
        {
            if (catalog?.Locations == null) return default;

            var purchaseCount = 0;
            var purchaseTotal = 0;
            var dayUnlockCount = 0;
            var freeCount = 0;

            for (var i = 0; i < catalog.Locations.Count; i++)
            {
                if (throughIndex >= 0 && i > throughIndex) break;

                var cost = Of(catalog.Locations[i]);
                purchaseCount += cost.PurchaseCount;
                purchaseTotal += cost.PurchaseTotal;
                dayUnlockCount += cost.DayUnlockCount;
                freeCount += cost.FreeCount;
            }

            return new MetaLocationCost(purchaseCount, purchaseTotal, dayUnlockCount, freeCount);
        }
    }

    // The authored Day content read as a SPENDING BUDGET: how many coins the player is
    // certain to hold by any given Day, so a meta price can be set against a number instead
    // of against a feeling.
    //
    // Authoring-side only, and deliberately so. Nothing here is a second authority on
    // money: the payout arithmetic is EconomySystem's DeliveryPayoutResult, the rounding is
    // DayLifecycleManager's, the prices are the food assets' and the opening balance is
    // GameConfig's. This class does no economics of its own -- it only replays the ones
    // that already exist, over every Day file at once.
    //
    // What it deliberately does NOT model: spending. Powerups, refills and paid Continues
    // all come out of the same wallet, so the real balance sits at or below this line,
    // never above it. It is a ceiling on the guarantee, not a prediction.
    public class MetaEconomyProjection
    {
        private readonly List<DayIncomeFloor> days;

        public IReadOnlyList<DayIncomeFloor> Days => days;

        // GameConfig.StartingSoftMoney -- a new player's grant, which is part of the budget
        // for anything bought before the first Day is finished.
        public int StartingSoftMoney { get; }

        // Null when the projection is trustworthy; one sentence naming what is missing
        // otherwise. A missing config leaves an EMPTY projection rather than a wrong one:
        // reading a zero floor as "the days pay nothing" is the failure this avoids.
        public string Problem { get; }

        private MetaEconomyProjection(List<DayIncomeFloor> days, int startingSoftMoney, string problem)
        {
            this.days = days ?? new List<DayIncomeFloor>();
            StartingSoftMoney = startingSoftMoney;
            Problem = problem;
        }

        // The most tickets a player can lose in one day and still finish it on their own
        // lives: every life but the last, since losing the last one ends the day in the
        // Continue popup rather than in a completion.
        //
        // Read from GameState rather than typed as a 2, so a change to the starting lives
        // moves this with it. Losses beyond it are reachable only by PAYING for a Continue,
        // which is a gem cost this projection does not model -- hence a cap rather than a
        // free parameter.
        public static int MaxAbsorbableLosses => Mathf.Max(0, GameState.DefaultStartingLives - 1);

        public int DayCount => days.Count;
        public bool IsEmpty => days.Count == 0;

        public int UnresolvedItems => days.Sum(d => d.UnresolvedItems);
        public int UnpricedItems => days.Sum(d => d.UnpricedItems);

        // Coins guaranteed in hand at the MOMENT Day `dayIndex` begins -- the grant plus
        // every EARLIER day's floor, this day's own income excluded.
        //
        // That exclusive reading is the one the catalog's own fields ask for: both
        // MetaLocation.UnlockAtDayIndex and a Day-Unlock prop's are "the catalog position
        // of the Day this appears BEFORE", so the money that can pay for what appears there
        // is the money earned before that Day is played.
        public int MinimumBeforeDay(int dayIndex, int lostTicketsPerDay)
        {
            var total = StartingSoftMoney;

            foreach (var day in days)
            {
                if (day.DayIndex >= dayIndex) break;
                total += day.FloorWith(lostTicketsPerDay);
            }

            return total;
        }

        // Coins guaranteed in hand once Day `dayIndex` has been COMPLETED. The same number
        // MinimumBeforeDay would give for the next day, named for the question it answers.
        public int MinimumAfterDay(int dayIndex, int lostTicketsPerDay) =>
            MinimumBeforeDay(dayIndex + 1, lostTicketsPerDay);

        // The floor earned across a closed range of days, both ends included. Used for the
        // window between one location opening and the next.
        public int FloorBetween(int firstDayIndex, int lastDayIndex, int lostTicketsPerDay)
        {
            var total = 0;

            foreach (var day in days)
            {
                if (day.DayIndex < firstDayIndex) continue;
                if (day.DayIndex > lastDayIndex) break;
                total += day.FloorWith(lostTicketsPerDay);
            }

            return total;
        }

        public int TotalFloor(int lostTicketsPerDay) => days.Sum(d => d.FloorWith(lostTicketsPerDay));

        // The highest Day index on disk, or -1 with nothing authored. NOT DayCount - 1: the
        // files carry their own index, and a hole in the numbering (possible between a
        // hand-deleted file and the Day Editor's next renumber) would make a positional
        // answer point at the wrong Day.
        public int LastDayIndex => days.Count == 0 ? -1 : days[days.Count - 1].DayIndex;

        // ---- building ------------------------------------------------------------

        // Cached because the Meta window redraws its page on every repaint and this reads
        // every Day file off disk. Invalidated on window focus and by an explicit Refresh
        // button -- the same posture the Day Editor takes towards its own files: load once,
        // reload on a gesture, never poll.
        private static MetaEconomyProjection cached;

        public static MetaEconomyProjection Cached => cached ??= Build();

        public static void Invalidate() => cached = null;

        // Discovers the configs the same way MetaEditorWindow discovers its catalog: the
        // first asset of each type. A project with two of any of them is already ambiguous
        // everywhere else in the editor tooling.
        public static MetaEconomyProjection Build()
        {
            var foodCatalog = FindFirstAsset<FoodCatalog>();
            var economyConfig = FindFirstAsset<EconomyConfig>();
            var gameConfig = FindFirstAsset<GameConfig>();

            var missing = new List<string>();
            if (foodCatalog == null) missing.Add("FoodCatalog");
            if (economyConfig == null) missing.Add("EconomyConfig");
            if (gameConfig == null) missing.Add("GameConfig");

            if (missing.Count > 0)
            {
                return new MetaEconomyProjection(
                    null, 0, $"No {string.Join(" / ", missing)} asset found — the income floor cannot be computed.");
            }

            return Build(
                Path.Combine(Application.dataPath, "Resources", "Days"), foodCatalog, economyConfig, gameConfig);
        }

        // Takes the folder rather than finding it, for the reason DayFileIO does: a test can
        // point this at a temp directory full of hand-written Days.
        public static MetaEconomyProjection Build(
            string daysFolderPath, FoodCatalog foodCatalog, EconomyConfig economyConfig, GameConfig gameConfig)
        {
            var files = DayFileIO.LoadAll(daysFolderPath);
            var days = new List<DayIncomeFloor>(files.Count);

            foreach (var file in files)
            {
                days.Add(FloorFor(file.Json, foodCatalog, economyConfig));
            }

            var problem = files.Count == 0
                ? $"No Day files found in {daysFolderPath} — there is no income to project."
                : null;

            return new MetaEconomyProjection(days, gameConfig.StartingSoftMoney, problem);
        }

        // One Day's tickets, each costed at the worst tip a DELIVERED ticket can earn.
        private static DayIncomeFloor FloorFor(DayJson dayJson, FoodCatalog foodCatalog, EconomyConfig economyConfig)
        {
            var runtime = dayJson?.runtime;
            var sequence = runtime?.ticketSequence;
            var payouts = new List<int>();

            if (sequence == null || sequence.Length == 0)
            {
                return new DayIncomeFloor(runtime?.dayIndex ?? 0, payouts, 0, 0, 0);
            }

            var orderValueTotal = 0;
            var unresolved = 0;
            var unpriced = 0;

            foreach (var entry in sequence)
            {
                if (entry == null) continue;

                var orderValue = 0;
                AddItem(entry.mainItemId, foodCatalog, ref orderValue, ref unresolved, ref unpriced);
                AddItem(entry.sideItemId, foodCatalog, ref orderValue, ref unresolved, ref unpriced);
                AddItem(entry.drinkItemId, foodCatalog, ref orderValue, ref unresolved, ref unpriced);

                // The real payout struct, not a re-derived formula. "Order Value plus the
                // Critical tip" is a rule that lives in EconomySystem, and a second copy of
                // it here is exactly the drift this tool exists to detect.
                var payout = new DeliveryPayoutResult(orderValue, TipTier.Critical, economyConfig.TipRateCritical);

                orderValueTotal += orderValue;

                // Rounded per delivery, matching DayLifecycleManager.RecordDelivery. Summing
                // the floats and rounding once at the end would drift from what the player's
                // wallet actually receives, by up to half a coin per ticket.
                payouts.Add(Mathf.RoundToInt(payout.Total));
            }

            // Descending, so "the n priciest expired" is the first n entries. See
            // DayIncomeFloor.FloorWith for why that is the case being modelled.
            payouts.Sort((a, b) => b.CompareTo(a));

            return new DayIncomeFloor(runtime.dayIndex, payouts, orderValueTotal, unresolved, unpriced);
        }

        // An EMPTY id is a slot the ticket does not use (no side, no drink) and is not a
        // problem; a non-empty id the catalog cannot resolve is.
        private static void AddItem(
            string id, FoodCatalog foodCatalog, ref int orderValue, ref int unresolved, ref int unpriced)
        {
            if (string.IsNullOrEmpty(id)) return;

            var item = foodCatalog.GetById(id);
            if (item == null)
            {
                unresolved++;
                return;
            }

            if (item.BasePrice <= 0) unpriced++;
            orderValue += item.BasePrice;
        }

        private static T FindFirstAsset<T>() where T : Object
        {
            var guids = AssetDatabase.FindAssets($"t:{typeof(T).Name}");
            return guids.Length == 0 ? null : AssetDatabase.LoadAssetAtPath<T>(AssetDatabase.GUIDToAssetPath(guids[0]));
        }
    }

    // Drops the cache when anything it was computed FROM lands on disk: a Day file saved by
    // the Day Editor, or a config asset whose prices and tip rates feed the arithmetic.
    //
    // An import hook rather than an OnFocus override, deliberately. OdinMenuEditorWindow
    // owns the EditorWindow message methods, and Unity delivers each message to one member
    // only -- an OnFocus declared in the subclass would hide the base one and silently take
    // whatever it does with it. This is also simply more correct: the cache goes stale when
    // the FILES change, which is the event, and a Day saved while the Meta window already
    // has focus would never raise the other one.
    internal class MetaEconomyAssetWatcher : AssetPostprocessor
    {
        private static void OnPostprocessAllAssets(
            string[] imported, string[] deleted, string[] movedTo, string[] movedFrom)
        {
            if (Touches(imported) || Touches(deleted) || Touches(movedTo) || Touches(movedFrom))
            {
                MetaEconomyProjection.Invalidate();
            }
        }

        // Deliberately broad -- every `.asset` rather than the three the projection reads.
        // Narrowing it would mean naming those assets by path here, which is a second place
        // to keep in step with what Build() actually loads, and invalidation costs one
        // field write against a re-read that only happens on the next repaint.
        private static bool Touches(string[] paths)
        {
            foreach (var path in paths)
            {
                if (path == null) continue;
                if (path.EndsWith(".asset")) return true;
                if (path.Replace('\\', '/').Contains("/Resources/Days/")) return true;
            }

            return false;
        }
    }
}
