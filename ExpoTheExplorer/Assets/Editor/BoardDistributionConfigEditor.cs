using ExpoTheExplorer.Core;
using ExpoTheExplorer.Data;
using UnityEditor;

namespace ExpoTheExplorer.Editor
{
    // Read-only preview appended below the normal Inspector — never edits
    // anything itself. LeakDepth IS "how many upcoming tickets are eligible"
    // (BoardDistributor.LeakNoiseItems windows candidates with
    // Take(LeakDepth)), and the real upcoming queue is kept topped up to at
    // least that many entries at essentially all times (TicketSlotManager's
    // EnsureQueueFilled), so no separate "what if N tickets were queued"
    // slider is needed — LeakDepth itself is the eligible-ticket count.
    [CustomEditor(typeof(BoardDistributionConfig))]
    public class BoardDistributionConfigEditor : UnityEditor.Editor
    {
        private const int PercentDecimalPlaces = 1;

        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();
            DrawLeakPreview((BoardDistributionConfig)target);
        }

        private void DrawLeakPreview(BoardDistributionConfig config)
        {
            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Noise Leak Preview (read-only)", EditorStyles.boldLabel);

            var n = config.LeakDepth;
            var probabilities = TruncatedPoisson.Probabilities(n, config.NoiseLeakCountLambda);
            for (var k = 0; k <= n; k++)
            {
                var probabilityPercent = probabilities[k] * 100d;
                var label = k == 1 ? "1 leak" : $"{k} leaks";
                EditorGUILayout.LabelField($"{label}: {probabilityPercent.ToString($"F{PercentDecimalPlaces}")}%");
            }
        }
    }
}
