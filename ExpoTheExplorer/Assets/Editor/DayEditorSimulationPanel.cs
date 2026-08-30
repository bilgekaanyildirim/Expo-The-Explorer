using ExpoTheExplorer.Data;
using ExpoTheExplorer.Simulation;
using ExpoTheExplorer.Systems.DaySystem;
using UnityEditor;
using UnityEngine;

namespace ExpoTheExplorer.Editor
{
    public static class DayEditorSimulationPanel
    {
        public class UiState
        {
            public int Seed = 1000;
            public float ActionDelaySeconds = 0.5f;
            public float StepSeconds = 0.1f;
            public float MaxVirtualSeconds = 600f;
            public EconomyConfig EconomyConfig;
            public StarScoreConfig StarScoreConfig;
            public SimulationResult LastResult;
        }

        public static void Draw(
            DayEditorModel model,
            GameConfig gameConfig,
            TicketGenerationConfig ticketConfig,
            DayValidationResult validation,
            ref UiState state)
        {
            state ??= new UiState();
            state.EconomyConfig ??= FindFirstAsset<EconomyConfig>();
            state.StarScoreConfig ??= FindFirstAsset<StarScoreConfig>();

            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            DrawOptions(state);

            state.EconomyConfig = (EconomyConfig)EditorGUILayout.ObjectField(
                "Economy Config", state.EconomyConfig, typeof(EconomyConfig), false);
            state.StarScoreConfig = (StarScoreConfig)EditorGUILayout.ObjectField(
                "Star Score Config", state.StarScoreConfig, typeof(StarScoreConfig), false);

            var setupMessage = SetupMessage(gameConfig, ticketConfig, state);
            if (!validation.IsValid)
            {
                EditorGUILayout.HelpBox("Fix validation errors before running a simulation.", MessageType.Error);
            }
            else if (!string.IsNullOrEmpty(setupMessage))
            {
                EditorGUILayout.HelpBox(setupMessage, MessageType.Error);
            }
            else if (model.DayIndex == 0)
            {
                EditorGUILayout.HelpBox("Day 0 is a tutorial day; timing metrics can diverge from play mode tutorial gating.", MessageType.Warning);
            }

            EditorGUILayout.HelpBox(
                "V1 uses the production Auto Collect planner as a deterministic skilled player. It does not make wrong deliveries, so life losses here primarily point to structural timeout pressure.",
                MessageType.Info);

            using (new EditorGUI.DisabledScope(!validation.IsValid || !string.IsNullOrEmpty(setupMessage)))
            {
                if (GUILayout.Button("Run Simulation", GUILayout.Height(28f)))
                {
                    var options = new SimulationOptions
                    {
                        Seed = state.Seed,
                        ActionDelaySeconds = state.ActionDelaySeconds,
                        StepSeconds = state.StepSeconds,
                        MaxVirtualSeconds = state.MaxVirtualSeconds,
                        GameConfig = gameConfig,
                        TicketGenerationConfig = ticketConfig,
                        EconomyConfig = state.EconomyConfig,
                        StarScoreConfig = state.StarScoreConfig,
                    };

                    state.LastResult = SimulationRunner.RunOne(model.ToDayDefinition(), options);
                }
            }

            DrawResult(state.LastResult);
            EditorGUILayout.EndVertical();
        }

        private static void DrawOptions(UiState state)
        {
            EditorGUILayout.BeginHorizontal();
            state.Seed = EditorGUILayout.IntField("Seed", state.Seed);
            state.ActionDelaySeconds = Mathf.Max(0f, EditorGUILayout.FloatField("Action Delay", state.ActionDelaySeconds));
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.BeginHorizontal();
            state.StepSeconds = Mathf.Max(0.01f, EditorGUILayout.FloatField("Step Seconds", state.StepSeconds));
            state.MaxVirtualSeconds = Mathf.Max(state.StepSeconds, EditorGUILayout.FloatField("Max Seconds", state.MaxVirtualSeconds));
            EditorGUILayout.EndHorizontal();
        }

        private static string SetupMessage(GameConfig gameConfig, TicketGenerationConfig ticketConfig, UiState state)
        {
            if (gameConfig == null) return "Game Config is missing in the Day Editor toolbar.";
            if (ticketConfig == null) return "Ticket Generation Config is missing in the Day Editor toolbar.";
            if (state.EconomyConfig == null) return "No Economy Config asset found.";
            if (state.StarScoreConfig == null) return "No Star Score Config asset found.";
            return null;
        }

        private static void DrawResult(SimulationResult result)
        {
            if (result == null) return;

            EditorGUILayout.Space(6f);
            EditorGUILayout.LabelField("Result", EditorStyles.boldLabel);

            if (!string.IsNullOrEmpty(result.Message))
            {
                EditorGUILayout.HelpBox(result.Message, MessageType.Warning);
            }

            DrawRow("Completion", result.EndReason.ToString());
            DrawRow("Stars", $"{Stars(result.StarCount)}   score {result.StarScore:0.000}");
            DrawRow("Delivered", $"{result.TicketsDelivered} / {result.TicketsRequired}");
            DrawRow("Failures", $"lives {result.LivesLost}, timeouts {result.Timeouts}, wrong {result.WrongDeliveries}");
            DrawRow("Duration", $"{result.DurationSeconds:0.0}s virtual");
            DrawRow("Revenue", $"{result.Revenue}  (orders {result.OrdersValue} + tips {result.TipsValue})");
            DrawRow("Saved Time", $"{result.SavedSeconds:0.0}s / {result.TotalTicketSeconds:0.0}s  avg remaining {result.AverageRemainingTimeRatio:P0}");
            DrawRow("Tip Tiers", $"full {result.TipTiers.Full}, warning {result.TipTiers.Warning}, critical {result.TipTiers.Critical}");
            DrawRow("Board Occupancy", $"avg {result.AverageBoardOccupancy:0.0} / {result.BoardCellCount}, peak {result.PeakBoardOccupancy}");
            DrawRow("Board Pressure", $"full samples {result.BoardFullSamples}, peak pending {result.PeakPendingSpawns}");
            DrawRow("Completable Rounds", $"zero {result.ZeroCompletableRounds} / {result.OrderPlacedRounds}");
        }

        private static void DrawRow(string label, string value)
        {
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField(label, GUILayout.Width(150f));
            EditorGUILayout.LabelField(value);
            EditorGUILayout.EndHorizontal();
        }

        private static string Stars(int count)
        {
            count = Mathf.Clamp(count, 0, 3);
            return new string('*', count) + new string('-', 3 - count);
        }

        private static T FindFirstAsset<T>() where T : Object
        {
            var guids = AssetDatabase.FindAssets($"t:{typeof(T).Name}");
            if (guids == null || guids.Length == 0) return null;
            return AssetDatabase.LoadAssetAtPath<T>(AssetDatabase.GUIDToAssetPath(guids[0]));
        }
    }
}
