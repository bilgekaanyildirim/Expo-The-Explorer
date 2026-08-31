using System.Collections.Generic;
using ExpoTheExplorer.Data;
using Sirenix.Utilities.Editor;
using UnityEditor;
using UnityEngine;

namespace ExpoTheExplorer.Editor
{
    // The Meta Editor's economy panel: what this location costs, set against what the
    // authored Days are guaranteed to pay by the time it opens.
    //
    // Split from MetaEconomyProjection because the split is along the testable line -- the
    // arithmetic is a pure function of assets on disk and has unit tests; this file is
    // IMGUI and cannot have any. Nothing here computes money; every number on screen comes
    // from the projection or from MetaLocationCost.
    internal static class MetaEconomyGUI
    {
        // Assumptions and view state, kept in EditorPrefs rather than in a field: they
        // survive domain reloads, and the loss count in particular is a balancing stance an
        // author takes once and expects to still be there after the next recompile.
        private const string LostTicketsKey = "ExpoTheExplorer.MetaEditor.LostTicketsPerDay";
        private const string ShowPanelKey = "ExpoTheExplorer.MetaEditor.ShowEconomy";
        private const string ShowDayTableKey = "ExpoTheExplorer.MetaEditor.ShowDayTable";
        private const string ShowUnlockTableKey = "ExpoTheExplorer.MetaEditor.ShowUnlockTable";

        private static int LostTickets
        {
            get => Mathf.Clamp(
                EditorPrefs.GetInt(LostTicketsKey, MetaEconomyProjection.MaxAbsorbableLosses),
                0,
                MetaEconomyProjection.MaxAbsorbableLosses);
            set => EditorPrefs.SetInt(LostTicketsKey, value);
        }

        private static bool GetFlag(string key, bool fallback) => EditorPrefs.GetBool(key, fallback);
        private static void SetFlag(string key, bool value) => EditorPrefs.SetBool(key, value);

        internal static void Draw(MetaCatalog catalog, int locationIndex)
        {
            if (catalog?.Locations == null || locationIndex < 0 || locationIndex >= catalog.Locations.Count) return;

            var location = catalog.Locations[locationIndex];
            if (location == null) return;

            var projection = MetaEconomyProjection.Cached;
            var lost = LostTickets;

            var cost = MetaLocationCost.Of(location);
            var cumulative = MetaLocationCost.Of(catalog, locationIndex);
            var windowEndDay = WindowEndDay(catalog, locationIndex, projection);
            var budgetAtClose = projection.MinimumBeforeDay(windowEndDay + 1, lost);
            var surplus = budgetAtClose - cumulative.PurchaseTotal;

            var expanded = SirenixEditorGUI.Foldout(
                GetFlag(ShowPanelKey, true), Headline(cost.PurchaseTotal, budgetAtClose, surplus));
            SetFlag(ShowPanelKey, expanded);
            if (!expanded) return;

            EditorGUI.indentLevel++;

            DrawAssumptions(projection);
            DrawBudget(projection, location, locationIndex, catalog, windowEndDay, lost);
            DrawCost(location, cost, cumulative, locationIndex);
            DrawVerdict(surplus, budgetAtClose, cumulative.PurchaseTotal, windowEndDay, projection);
            DrawDayUnlockTable(projection, location, lost);
            DrawDayTable(projection, lost);

            EditorGUI.indentLevel--;
        }

        // The one line an author reads without expanding anything: this location's price,
        // the money that is certain to exist by the time it stops being the newest one, and
        // the gap between the budget and everything bought up to here.
        private static string Headline(int locationCost, int budget, int surplus)
        {
            var sign = surplus >= 0 ? "+" : "−";
            return $"Economy — this location {Coins(locationCost)} · budget {Coins(budget)} · {sign}{Coins(Mathf.Abs(surplus))}";
        }

        private static void DrawAssumptions(MetaEconomyProjection projection)
        {
            EditorGUILayout.BeginHorizontal();

            var max = MetaEconomyProjection.MaxAbsorbableLosses;
            var value = EditorGUILayout.IntSlider(
                new GUIContent(
                    "Tickets lost per day",
                    "How many of each day's tickets are assumed to time out. Only a TIMEOUT costs money: a wrong "
                    + "delivery spends a life and leaves the ticket standing, so it can still be delivered.\n\n"
                    + $"Capped at {max} because that is every life but the last — losing the last one ends the day "
                    + "in the Continue popup instead of a completion, and a paid Continue is a gem cost this "
                    + "projection does not model.\n\n"
                    + "Worst case is assumed: the PRICIEST tickets are the ones lost."),
                LostTickets, 0, max);

            if (value != LostTickets) LostTickets = value;

            if (GUILayout.Button(
                    new GUIContent("Refresh", "Re-read every Day file. Do this after editing Days, food prices or EconomyConfig."),
                    EditorStyles.miniButton, GUILayout.Width(58)))
            {
                MetaEconomyProjection.Invalidate();
            }

            EditorGUILayout.EndHorizontal();

            if (!string.IsNullOrEmpty(projection.Problem))
            {
                EditorGUILayout.HelpBox(projection.Problem, MessageType.Warning);
                return;
            }

            if (projection.UnresolvedItems > 0 || projection.UnpricedItems > 0)
            {
                EditorGUILayout.HelpBox(
                    $"{projection.UnresolvedItems} unresolvable food id(s) and {projection.UnpricedItems} item(s) "
                    + "priced at 0 across the Day files. Both contribute nothing to an order, exactly as they would "
                    + "at runtime, so every figure below is lower than the finished content will pay.",
                    MessageType.Warning);
            }
        }

        private static void DrawBudget(
            MetaEconomyProjection projection,
            MetaLocation location,
            int locationIndex,
            MetaCatalog catalog,
            int windowEndDay,
            int lost)
        {
            SirenixEditorGUI.BeginBox("Guaranteed coins");

            var openDay = location.UnlockAtDayIndex;
            var atOpen = projection.MinimumBeforeDay(openDay, lost);
            var earnedBeforeOpen = atOpen - projection.StartingSoftMoney;

            Row(
                $"When this opens (Day {openDay})",
                Coins(atOpen),
                $"The {Coins(projection.StartingSoftMoney)} new-player grant plus {Coins(earnedBeforeOpen)} earned "
                + $"over days 0–{openDay - 1}. A location's Unlock At Day Index is the Day it appears BEFORE, so "
                + "this is the money in hand at that moment.");

            var nextLocation = NextLocation(catalog, locationIndex);
            var closeLabel = nextLocation == null
                ? $"By the last authored Day ({projection.LastDayIndex})"
                : $"When '{Label(nextLocation)}' opens (Day {nextLocation.UnlockAtDayIndex})";

            Row(
                closeLabel,
                Coins(projection.MinimumBeforeDay(windowEndDay + 1, lost)),
                nextLocation == null
                    ? "Everything the authored catalog can pay, with nothing left to play."
                    : "The end of this location's window: after this the player has somewhere newer to spend on.");

            Row(
                $"Earned during days {location.UnlockAtDayIndex}–{windowEndDay}",
                Coins(projection.FloorBetween(location.UnlockAtDayIndex, windowEndDay, lost)),
                "This location's own income window, the grant and everything earlier excluded.");

            EditorGUILayout.Space(2f);

            var clean = projection.MinimumBeforeDay(windowEndDay + 1, 0);
            Row(
                "…if nothing is ever lost",
                Coins(clean),
                "The same figure with 0 tickets lost per day — the optimistic bound. A player still tipping at the "
                + "Critical rate on every single delivery, which is itself the worst tip in the game.");

            SirenixEditorGUI.EndBox();
        }

        private static void DrawCost(
            MetaLocation location, MetaLocationCost cost, MetaLocationCost cumulative, int locationIndex)
        {
            SirenixEditorGUI.BeginBox("What it costs");

            Row(
                $"This location ({cost.PurchaseCount} for sale)",
                Coins(cost.PurchaseTotal),
                "Every Purchase prop's price, summed. Day-Unlock props are not for sale and are excluded.");

            if (cost.DayUnlockCount > 0)
            {
                Row(
                    $"…plus {cost.DayUnlockCount} Day-Unlock prop(s)",
                    "free",
                    "Not for sale — they appear at an authored Day and cost the player nothing.");
            }

            if (locationIndex > 0)
            {
                Row(
                    "Earlier locations",
                    Coins(cumulative.PurchaseTotal - cost.PurchaseTotal),
                    "Already on the player's bill by the time they get here.");

                Row(
                    "Everything up to here",
                    Coins(cumulative.PurchaseTotal),
                    "What the verdict is measured against: a player reaching this location has had to pay for the "
                    + "earlier ones too.");
            }

            if (cost.FreeCount > 0)
            {
                EditorGUILayout.HelpBox(
                    $"{cost.FreeCount} Purchase prop(s) here are priced at 0, so they are free and invisible in the "
                    + "total above. Price them, or make them Day Unlocks.",
                    MessageType.Warning);
            }

            SirenixEditorGUI.EndBox();
        }

        private static void DrawVerdict(
            int surplus, int budget, int cumulativeCost, int windowEndDay, MetaEconomyProjection projection)
        {
            if (projection.IsEmpty) return;

            var window = windowEndDay >= projection.LastDayIndex
                ? "by the last authored Day"
                : $"by Day {windowEndDay + 1}";

            if (surplus >= 0)
            {
                EditorGUILayout.HelpBox(
                    $"Affordable. {window} the player is guaranteed {Coins(budget)}, and everything up to here "
                    + $"costs {Coins(cumulativeCost)} — {Coins(surplus)} spare.\n\n"
                    + "Spending is not deducted: powerups, refills and paid Continues come out of the same wallet, "
                    + "so the real balance sits at or below this.",
                    MessageType.Info);
                return;
            }

            EditorGUILayout.HelpBox(
                $"Short by {Coins(-surplus)}. {window} the player is guaranteed {Coins(budget)}, but everything up "
                + $"to here costs {Coins(cumulativeCost)} — so this location cannot be finished on guaranteed income "
                + "alone.\n\n"
                + "That is not automatically wrong: it means the player has to play better than the floor, or leave "
                + "something unbought. It IS wrong if the props here are meant to be gates rather than choices.",
                MessageType.Warning);
        }

        private static void DrawDayUnlockTable(MetaEconomyProjection projection, MetaLocation location, int lost)
        {
            var unlocks = new List<MetaItemDefinition>();
            if (location.Items != null)
            {
                foreach (var item in location.Items)
                {
                    if (item != null && item.Unlock == MetaUnlockKind.DayUnlock) unlocks.Add(item);
                }
            }

            if (unlocks.Count == 0) return;

            var expanded = SirenixEditorGUI.Foldout(
                GetFlag(ShowUnlockTableKey, false), $"Day-Unlock props ({unlocks.Count})");
            SetFlag(ShowUnlockTableKey, expanded);
            if (!expanded) return;

            unlocks.Sort((a, b) => a.UnlockAtDayIndex.CompareTo(b.UnlockAtDayIndex));

            EditorGUI.indentLevel++;
            Header("prop", "day", "coins in hand");

            foreach (var item in unlocks)
            {
                TableRow(
                    string.IsNullOrWhiteSpace(item.Id) ? "<no id>" : item.Id,
                    item.UnlockAtDayIndex.ToString(),
                    Coins(projection.MinimumBeforeDay(item.UnlockAtDayIndex, lost)));
            }

            EditorGUILayout.LabelField(
                "What the player is guaranteed to hold when each one appears — the budget it competes with.",
                EditorStyles.wordWrappedMiniLabel);
            EditorGUI.indentLevel--;
        }

        private static void DrawDayTable(MetaEconomyProjection projection, int lost)
        {
            if (projection.IsEmpty) return;

            var expanded = SirenixEditorGUI.Foldout(
                GetFlag(ShowDayTableKey, false), $"Per-day floor ({projection.DayCount} days)");
            SetFlag(ShowDayTableKey, expanded);
            if (!expanded) return;

            EditorGUI.indentLevel++;
            Header("day", "tickets", $"−{lost} lost", "clean", "running");

            var running = projection.StartingSoftMoney;
            foreach (var day in projection.Days)
            {
                var floor = day.FloorWith(lost);
                running += floor;

                TableRow(
                    $"Day {day.DayIndex}",
                    day.TicketCount.ToString(),
                    Coins(floor),
                    Coins(day.CleanFloor),
                    Coins(running));
            }

            EditorGUILayout.LabelField(
                $"'running' starts at the {Coins(projection.StartingSoftMoney)} new-player grant and adds each day's "
                + "floor as it is completed.",
                EditorStyles.wordWrappedMiniLabel);
            EditorGUI.indentLevel--;
        }

        // ---- small drawing helpers -----------------------------------------------

        private static void Row(string label, string value, string tooltip)
        {
            EditorGUILayout.LabelField(new GUIContent(label, tooltip), new GUIContent(value, tooltip));
        }

        private static void Header(params string[] columns)
        {
            EditorGUILayout.BeginHorizontal();
            for (var i = 0; i < columns.Length; i++)
            {
                GUILayout.Label(columns[i], EditorStyles.miniBoldLabel, ColumnWidth(i, columns.Length));
            }
            EditorGUILayout.EndHorizontal();
        }

        private static void TableRow(params string[] cells)
        {
            EditorGUILayout.BeginHorizontal();
            for (var i = 0; i < cells.Length; i++)
            {
                GUILayout.Label(cells[i], EditorStyles.miniLabel, ColumnWidth(i, cells.Length));
            }
            EditorGUILayout.EndHorizontal();
        }

        // The first column names the row and gets the room; the rest are numbers and are
        // fixed, so the columns line up down the table instead of shifting with content.
        private static GUILayoutOption ColumnWidth(int index, int count) =>
            index == 0 ? GUILayout.Width(150f) : GUILayout.Width(90f);

        private static string Coins(int value) => value.ToString("N0");

        private static string Label(MetaLocation location) =>
            string.IsNullOrWhiteSpace(location.DisplayName)
                ? (string.IsNullOrWhiteSpace(location.Id) ? "<no id>" : location.Id)
                : location.DisplayName;

        // The location the player moves on to. Found by walking FORWARD in list order and
        // taking the first one that opens strictly later, rather than by trusting list
        // position alone: the catalog is meant to be ordered by unlock day, and a catalog
        // that is not would otherwise produce a negative window.
        private static MetaLocation NextLocation(MetaCatalog catalog, int locationIndex)
        {
            var current = catalog.Locations[locationIndex];
            if (current == null) return null;

            for (var i = locationIndex + 1; i < catalog.Locations.Count; i++)
            {
                var candidate = catalog.Locations[i];
                if (candidate != null && candidate.UnlockAtDayIndex > current.UnlockAtDayIndex) return candidate;
            }

            return null;
        }

        // The last Day this location is the newest one -- the day before its successor
        // opens, or the last authored Day when it has none. Never below the location's own
        // unlock day, so a window is at worst a single day rather than a negative range.
        private static int WindowEndDay(MetaCatalog catalog, int locationIndex, MetaEconomyProjection projection)
        {
            var current = catalog.Locations[locationIndex];
            var next = NextLocation(catalog, locationIndex);
            var end = next == null ? projection.LastDayIndex : next.UnlockAtDayIndex - 1;

            return Mathf.Max(current?.UnlockAtDayIndex ?? 0, end);
        }
    }
}
