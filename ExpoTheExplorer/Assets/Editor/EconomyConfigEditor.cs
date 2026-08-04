using System.Collections.Generic;
using ExpoTheExplorer.Core;
using ExpoTheExplorer.Data;
using UnityEditor;

namespace ExpoTheExplorer.Editor
{
    // Read-only preview appended below the normal Inspector — never edits
    // anything itself. Recomputes every OnInspectorGUI call, same pattern as
    // TicketGenerationConfigEditor. Exists to catch a "cliff then flat"
    // patience curve (tip drops early and never moves again for most of the
    // ticket's remaining life) before it ships — each decay step's elapsed
    // time is also shown as a percentage of that patience type's actual
    // TimeLimitSeconds, read from whatever TicketGenerationConfig asset
    // exists in the project purely for this comparison (EconomyConfig itself
    // has no reference to it — the two configs are intentionally independent).
    [CustomEditor(typeof(EconomyConfig))]
    public class EconomyConfigEditor : UnityEditor.Editor
    {
        private const float FlatTailWarningFraction = 0.3f;

        private int previewItemCount = 3;

        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();

            var config = (EconomyConfig)target;

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Tip Curve Preview (read-only)", EditorStyles.boldLabel);
            previewItemCount = EditorGUILayout.IntSlider("Preview Item Count", previewItemCount, 1, 6);

            EditorGUILayout.Space(4);
            DrawSpeedTierPreview(config);

            EditorGUILayout.Space(8);
            DrawPatiencePreview(config);
        }

        private void DrawSpeedTierPreview(EconomyConfig config)
        {
            EditorGUILayout.LabelField("Speed Tiers", EditorStyles.miniBoldLabel);

            var lightningSeconds = config.LightningSecondsPerItem * previewItemCount;
            var fastSeconds = config.FastSecondsPerItem * previewItemCount;

            EditorGUILayout.LabelField($"Lightning (x{config.LightningMultiplier:F2}): 0s - {lightningSeconds:F1}s");
            EditorGUILayout.LabelField($"Fast (x{config.FastMultiplier:F2}): {lightningSeconds:F1}s - {fastSeconds:F1}s");
            EditorGUILayout.LabelField($"Standard (x{config.StandardMultiplier:F2}): {fastSeconds:F1}s+");
        }

        private void DrawPatiencePreview(EconomyConfig config)
        {
            EditorGUILayout.LabelField("Patience Decay", EditorStyles.miniBoldLabel);

            DrawPatienceCurve("Impatient", config.ImpatientDecaySteps, PatienceType.Impatient);
            EditorGUILayout.Space(4);
            DrawPatienceCurve("Normal", config.NormalDecaySteps, PatienceType.Normal);
            EditorGUILayout.Space(4);
            DrawPatienceCurve("Patient", config.PatientDecaySteps, PatienceType.Patient);
        }

        private void DrawPatienceCurve(string label, IReadOnlyList<PatienceDecayStep> steps, PatienceType patienceType)
        {
            EditorGUILayout.LabelField(label, EditorStyles.boldLabel);

            if (steps.Count == 0)
            {
                EditorGUILayout.LabelField("No decay steps configured -- always 100% tip.");
                return;
            }

            var timeLimitSeconds = FindTimeLimitSeconds(patienceType);

            EditorGUILayout.LabelField($"0s - {steps[0].SecondsPerItemThreshold * previewItemCount:F1}s: 100%");
            for (var i = 0; i < steps.Count; i++)
            {
                var thresholdSeconds = steps[i].SecondsPerItemThreshold * previewItemCount;
                var rangeEnd = i + 1 < steps.Count
                    ? $"{steps[i + 1].SecondsPerItemThreshold * previewItemCount:F1}s"
                    : "end of ticket";
                var percent = steps[i].DecayCoefficient * 100f;

                var line = $"{thresholdSeconds:F1}s - {rangeEnd}: {percent:F0}%";
                if (timeLimitSeconds > 0f)
                {
                    line += $"  ({thresholdSeconds / timeLimitSeconds * 100f:F0}% of this ticket's {timeLimitSeconds:F0}s time limit)";
                }

                EditorGUILayout.LabelField(line);
            }

            if (timeLimitSeconds <= 0f)
            {
                EditorGUILayout.HelpBox("No TicketGenerationConfig asset found in the project -- showing raw seconds only.", MessageType.Info);
                return;
            }

            var lastThresholdSeconds = steps[^1].SecondsPerItemThreshold * previewItemCount;
            var flatTailFraction = 1f - lastThresholdSeconds / timeLimitSeconds;
            if (flatTailFraction > FlatTailWarningFraction)
            {
                EditorGUILayout.HelpBox(
                    $"Tip bottoms out at {steps[^1].DecayCoefficient * 100f:F0}% with {flatTailFraction * 100f:F0}% of this ticket's time limit still remaining -- delivering early vs. late in that remaining window makes no tip difference.",
                    MessageType.Warning);
            }
        }

        // Purely for this preview -- EconomyConfig has no reference to
        // TicketGenerationConfig, so this looks up whichever
        // TicketGenerationConfig asset exists in the project to compare
        // against. Returns 0 if none is found (or none is assigned).
        private static float FindTimeLimitSeconds(PatienceType patienceType)
        {
            var guids = AssetDatabase.FindAssets("t:TicketGenerationConfig");
            if (guids.Length == 0) return 0f;

            var path = AssetDatabase.GUIDToAssetPath(guids[0]);
            var genConfig = AssetDatabase.LoadAssetAtPath<TicketGenerationConfig>(path);
            if (genConfig == null) return 0f;

            return patienceType switch
            {
                PatienceType.Impatient => genConfig.ImpatientTimeLimitSeconds,
                PatienceType.Patient => genConfig.PatientTimeLimitSeconds,
                _ => genConfig.NormalTimeLimitSeconds,
            };
        }
    }
}
