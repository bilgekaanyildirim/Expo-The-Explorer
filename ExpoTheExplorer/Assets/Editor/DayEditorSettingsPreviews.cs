using System;
using System.Collections.Generic;
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

        // Takes the resolved entries rather than either concrete weight type, so the Day
        // Editor's own model and the config asset's serialized list can both feed it.
        public static void DrawMainDishPreview(IReadOnlyList<(FoodItemConfig Food, float Weight, float ModificationCountLambda)> entries)
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
