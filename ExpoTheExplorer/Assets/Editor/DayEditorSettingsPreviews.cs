using System;
using System.Collections.Generic;
using System.Linq;
using ExpoTheExplorer.Core;
using ExpoTheExplorer.Data;
using UnityEditor;

namespace ExpoTheExplorer.Editor
{
    // Read-only tuning previews, shared by the Day Editor (a Day's own authored settings)
    // and TicketGenerationConfig's inspector (the seed asset's settings). The probability
    // maths used to live inside the two custom editors; it moved here when the Day became
    // the place these numbers are authored, so the same distribution is never worked out
    // twice and the two views cannot drift apart.
    public static class DayEditorSettingsPreviews
    {
        private const int PercentDecimalPlaces = 1;

        // MaxLeakCount is the Poisson truncation ceiling, so these probabilities depend only
        // on (maxLeakCount, lambda) -- not on LeakDepth or how many tickets happen to be
        // queued, mirroring BoardDistributor.LeakNoiseItems exactly. LeakDepth only decides
        // WHICH tickets are eligible as leak sources, so it has no bearing here.
        public static void DrawLeakPreview(int maxLeakCount, float noiseLeakCountLambda)
        {
            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Noise Leak Preview (read-only)", EditorStyles.boldLabel);

            var probabilities = TruncatedPoisson.Probabilities(maxLeakCount, noiseLeakCountLambda);
            for (var k = 0; k <= maxLeakCount; k++)
            {
                var label = k == 1 ? "1 leak" : $"{k} leaks";
                EditorGUILayout.LabelField($"{label}: {Percent(probabilities[k])}");
            }
        }

        // Mirrors BoardDistributor.SelectGuaranteedTickets' budget line:
        //     Poisson -> TruncatedPoisson.Sample(TicketSlotCount - 1, lambda) + 1
        //     Manual  -> GuaranteedTicketCount
        // The +1 is the part worth being careful about. The draw is over 0..SlotCount-1 and
        // then shifted up, so the budget can never come out 0 -- GDD Section 4's "at least
        // one ticket must always be completable" is enforced by that shift, not by a clamp.
        // Dropping it here would show an impossible "0 tickets" row.
        //
        // This is the per-round BUDGET, not the final guaranteed count: a ticket whose time
        // drops under Urgent Time Threshold Seconds is guaranteed regardless and may push
        // past the budget, and selection is sticky across rounds (decisions.md D-001). The
        // label says so, because "at most 3" would otherwise be the obvious wrong reading.
        public static void DrawGuaranteedTicketPreview(
            GuaranteedTicketCountMode mode, int guaranteedTicketCount, float guaranteedTicketCountLambda)
        {
            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Guaranteed Ticket Preview (read-only)", EditorStyles.boldLabel);

            if (mode == GuaranteedTicketCountMode.Manual)
            {
                var ticketWord = guaranteedTicketCount == 1 ? "ticket" : "tickets";
                EditorGUILayout.LabelField($"Manual: always {guaranteedTicketCount} {ticketWord} per round.");
            }
            else
            {
                var probabilities = TruncatedPoisson.Probabilities(GameState.TicketSlotCount - 1, guaranteedTicketCountLambda);
                for (var k = 0; k < probabilities.Length; k++)
                {
                    var tickets = k + 1;
                    var label = tickets == 1 ? "1 ticket" : $"{tickets} tickets";
                    EditorGUILayout.LabelField($"{label}: {Percent(probabilities[k])}");
                }
            }

            EditorGUILayout.LabelField(
                "Per-round budget. Tickets under the urgency threshold are guaranteed on top of this and can exceed it.",
                EditorStyles.miniLabel);
        }

        // Takes the resolved entries rather than either concrete weight type, so the Day
        // Editor's own model and the config asset's serialized list can both feed it.
        //
        // allowedFoods is the Day's food selection, and is null when there is no Day to
        // check against (the config asset's own inspector). A weight on a food the Day does
        // not serve is dead weight -- TicketFactory only ever rolls from the Day's pool --
        // and it is flagged here rather than in DayValidator because the weights live in
        // editorMeta, which DayDefinition does not carry.
        public static void DrawMainDishPreview(
            IReadOnlyList<(FoodItemConfig Food, float Weight, float ModificationCountLambda)> entries,
            IReadOnlyList<FoodItemConfig> allowedFoods = null)
        {
            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Main Dish Preview (read-only)", EditorStyles.boldLabel);

            if (entries.Count == 0)
            {
                EditorGUILayout.HelpBox(
                    "No main dish weights configured -- every Main this Day serves is equally likely.",
                    MessageType.Info);
                return;
            }

            var totalWeight = 0d;
            foreach (var entry in entries)
            {
                if (entry.Food == null) continue;
                totalWeight += Math.Max(0d, entry.Weight);
            }

            foreach (var entry in entries)
            {
                if (entry.Food == null)
                {
                    EditorGUILayout.HelpBox("An entry has no Food assigned.", MessageType.Warning);
                    continue;
                }

                if (entry.Food.Category != FoodCategory.Main)
                {
                    EditorGUILayout.HelpBox($"'{entry.Food.DisplayName}' is not a Main-category food.", MessageType.Warning);
                    continue;
                }

                if (allowedFoods != null && !allowedFoods.Contains(entry.Food))
                {
                    EditorGUILayout.HelpBox(
                        $"'{entry.Food.DisplayName}' is not in this Day's food selection, so this weight does nothing.",
                        MessageType.Warning);
                    continue;
                }

                var chance = totalWeight > 0d ? Math.Max(0d, entry.Weight) / totalWeight : 0d;
                EditorGUILayout.LabelField(
                    entry.Food.DisplayName,
                    $"Spawn chance: {Percent(chance)} — mod count λ (Poisson): {entry.ModificationCountLambda:F2}");

                using (new EditorGUI.IndentLevelScope())
                {
                    DrawModCountBreakdown(entry.Food, entry.ModificationCountLambda);
                }

                EditorGUILayout.Space(4);
            }
        }

        private static void DrawModCountBreakdown(FoodItemConfig food, float lambda)
        {
            var n = food.AvailableModifications.Count;
            if (n == 0)
            {
                EditorGUILayout.LabelField("0 mods: 100% (no modifications configured)");
                return;
            }

            var probabilities = TruncatedPoisson.Probabilities(n, lambda);
            for (var k = 0; k <= n; k++)
            {
                var label = k == 1 ? "1 mod" : $"{k} mods";
                EditorGUILayout.LabelField($"{label}: {Percent(probabilities[k])}");
            }
        }

        private static string Percent(double fraction) =>
            $"{(fraction * 100d).ToString($"F{PercentDecimalPlaces}")}%";
    }
}
