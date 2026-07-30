using System;
using ExpoTheExplorer.Data;
using UnityEditor;

namespace ExpoTheExplorer.Editor
{
    // Read-only preview appended below the normal Inspector — never edits
    // anything itself. Recomputes every OnInspectorGUI call (Unity re-runs this
    // on every repaint), so dragging a MainDishWeight entry's own
    // modificationCountLambda slider updates the numbers below live, with no
    // extra event wiring needed.
    [CustomEditor(typeof(TicketGenerationConfig))]
    public class TicketGenerationConfigEditor : UnityEditor.Editor
    {
        private const int PercentDecimalPlaces = 1;

        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();
            DrawMainDishPreview((TicketGenerationConfig)target);
        }

        private static void DrawMainDishPreview(TicketGenerationConfig config)
        {
            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Main Dish Preview (read-only)", EditorStyles.boldLabel);

            var weights = config.MainDishWeights;
            if (weights.Count == 0)
            {
                EditorGUILayout.HelpBox("No main dish weights configured.", MessageType.Info);
                return;
            }

            var totalWeight = 0d;
            foreach (var entry in weights)
            {
                if (entry.Food == null) continue;
                totalWeight += Math.Max(0d, entry.Weight);
            }

            foreach (var entry in weights)
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

                var normalizedChance = totalWeight > 0d ? Math.Max(0d, entry.Weight) / totalWeight * 100d : 0d;

                EditorGUILayout.LabelField(
                    entry.Food.DisplayName,
                    $"Spawn chance: {normalizedChance.ToString($"F{PercentDecimalPlaces}")}% — mod count λ (Poisson): {entry.ModificationCountLambda:F2}");

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

            var probabilities = PoissonProbabilities(n, lambda);
            for (var k = 0; k <= n; k++)
            {
                var probabilityPercent = probabilities[k] * 100d;
                var label = k == 1 ? "1 mod" : $"{k} mods";
                EditorGUILayout.LabelField($"{label}: {probabilityPercent.ToString($"F{PercentDecimalPlaces}")}%");
            }
        }

        // Presentation-only math for this preview — mirrors
        // TicketFactory.PickModificationCount exactly (Poisson pmf truncated to
        // k = 0..n and renormalized; the e^-lambda factor cancels out of the
        // normalization so it's never computed) and isn't consulted by any
        // gameplay code itself.
        private static double[] PoissonProbabilities(int n, float lambda)
        {
            var terms = new double[n + 1];
            terms[0] = 1d;
            for (var k = 1; k <= n; k++)
            {
                terms[k] = terms[k - 1] * lambda / k;
            }

            var total = 0d;
            foreach (var term in terms) total += term;

            for (var k = 0; k <= n; k++) terms[k] /= total;
            return terms;
        }
    }
}
