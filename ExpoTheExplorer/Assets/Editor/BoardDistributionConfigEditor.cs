using ExpoTheExplorer.Core;
using ExpoTheExplorer.Data;
using UnityEditor;

namespace ExpoTheExplorer.Editor
{
    // Read-only preview appended below the normal Inspector — never edits
    // anything itself. previewUpcomingTicketCount is deliberately NOT
    // serialized: BoardDistributionConfig has no fixed upcoming-queue-size
    // field of its own (that lives in TicketGenerationConfig, a different
    // asset), so this is just an illustrative "what if N tickets were
    // queued" slider for balancing, not a persisted gameplay value.
    [CustomEditor(typeof(BoardDistributionConfig))]
    public class BoardDistributionConfigEditor : UnityEditor.Editor
    {
        private const int PercentDecimalPlaces = 1;
        private const int MaxPreviewUpcomingTicketCount = 20;

        private int previewUpcomingTicketCount = 10;

        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();
            DrawLeakPreview((BoardDistributionConfig)target);
        }

        private void DrawLeakPreview(BoardDistributionConfig config)
        {
            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Noise Leak Preview (read-only)", EditorStyles.boldLabel);

            previewUpcomingTicketCount = EditorGUILayout.IntSlider(
                "Preview: upcoming tickets in queue",
                previewUpcomingTicketCount, 0, MaxPreviewUpcomingTicketCount);

            var n = previewUpcomingTicketCount;
            if (n == 0)
            {
                EditorGUILayout.LabelField("0 leaks: 100% (no upcoming tickets)");
                return;
            }

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
