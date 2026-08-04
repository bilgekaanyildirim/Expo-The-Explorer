using ExpoTheExplorer.Core;
using ExpoTheExplorer.Data;
using UnityEditor;

namespace ExpoTheExplorer.Editor
{
    // Read-only preview appended below the normal Inspector — never edits
    // anything itself. MaxLeakCount is the Poisson truncation ceiling, so the
    // probabilities below depend only on (MaxLeakCount, NoiseLeakCountLambda)
    // — not on LeakDepth or how many tickets happen to be queued, mirroring
    // BoardDistributor.LeakNoiseItems exactly. LeakDepth only decides which
    // tickets are eligible as leak sources, so it has no bearing on this
    // preview.
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

            var n = config.MaxLeakCount;
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
