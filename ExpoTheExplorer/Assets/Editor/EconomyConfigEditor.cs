using ExpoTheExplorer.Core;
using ExpoTheExplorer.Data;
using UnityEditor;

namespace ExpoTheExplorer.Editor
{
    // Read-only preview appended below the normal Inspector — never edits
    // anything itself. Recomputes every OnInspectorGUI call, same pattern as
    // TicketGenerationConfigEditor.
    //
    // Exists because the two tier thresholds are authored as RATIOS while the
    // player experiences them as seconds, and the seconds differ per patience
    // type: 0.666/0.333 on a 45s Impatient ticket is a tier change every 15s,
    // on a 150s Patient one every 50s. This turns the ratios back into the
    // numbers a designer can reason about, and flags the two ways the authored
    // set can be nonsense (an unreachable tier, and a tier that pays MORE for
    // being slower) before either ships.
    [CustomEditor(typeof(EconomyConfig))]
    public class EconomyConfigEditor : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();

            var config = (EconomyConfig)target;

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Tip Tier Preview (read-only)", EditorStyles.boldLabel);

            DrawAuthoringWarnings(config);

            EditorGUILayout.Space(4);
            DrawTierWindows(config, "Impatient", PatienceType.Impatient);
            DrawTierWindows(config, "Normal", PatienceType.Normal);
            DrawTierWindows(config, "Patient", PatienceType.Patient);
        }

        private static void DrawAuthoringWarnings(EconomyConfig config)
        {
            if (config.CriticalRatio >= config.WarningRatio)
            {
                EditorGUILayout.HelpBox(
                    $"Critical Ratio ({config.CriticalRatio:F3}) is not below Warning Ratio ({config.WarningRatio:F3}), so the Warning tier can never be reached -- every delivery is either Full or Critical.",
                    MessageType.Warning);
            }

            // A later tier paying more than an earlier one inverts the whole
            // mechanic: the player is rewarded for stalling. Cheap to author by
            // accident, invisible without saying it out loud.
            if (config.TipRateWarning > config.TipRateFull || config.TipRateCritical > config.TipRateWarning)
            {
                EditorGUILayout.HelpBox(
                    $"Tip rates do not decrease across the tiers (Full {config.TipRateFull:P0} -> Warning {config.TipRateWarning:P0} -> Critical {config.TipRateCritical:P0}). As authored, delivering LATER pays a bigger tip.",
                    MessageType.Warning);
            }
        }

        // Ratios are "time still remaining", so each tier's ELAPSED window is
        // measured from the other end: Full runs until remaining falls to
        // WarningRatio, i.e. until (1 - WarningRatio) of the limit has been spent.
        private static void DrawTierWindows(EconomyConfig config, string label, PatienceType patienceType)
        {
            var timeLimit = FindTimeLimitSeconds(patienceType);
            if (timeLimit <= 0f)
            {
                EditorGUILayout.LabelField($"{label}: no TicketGenerationConfig time limit found to compare against.");
                return;
            }

            var fullEnds = timeLimit * (1f - config.WarningRatio);
            var warningEnds = timeLimit * (1f - config.CriticalRatio);

            EditorGUILayout.LabelField($"{label} ({timeLimit:F0}s limit)", EditorStyles.miniBoldLabel);
            EditorGUILayout.LabelField($"   Full ({config.TipRateFull:P0} of order): 0s - {fullEnds:F1}s elapsed");
            EditorGUILayout.LabelField($"   Warning ({config.TipRateWarning:P0}): {fullEnds:F1}s - {warningEnds:F1}s");
            EditorGUILayout.LabelField($"   Critical ({config.TipRateCritical:P0}): {warningEnds:F1}s - {timeLimit:F0}s");
            EditorGUILayout.Space(2);
        }

        // Purely for this preview -- EconomyConfig has no reference to
        // TicketGenerationConfig, so this looks up whichever
        // TicketGenerationConfig asset exists in the project to compare
        // against. Returns 0 if none is found (or none is assigned).
        //
        // Note this reads the AUTHORING seed, not what a Day actually plays with:
        // the live limits are authored per Day (decisions.md D-005), so a Day that
        // overrides them shifts these windows. Good enough for a sanity preview,
        // and the alternative is picking one arbitrary Day to speak for all.
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
