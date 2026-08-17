using System.Collections.Generic;
using System.IO;
using System.Linq;
using ExpoTheExplorer.Data;
using Sirenix.OdinInspector.Editor;
using Sirenix.Utilities.Editor;
using UnityEditor;
using UnityEngine;

namespace ExpoTheExplorer.Editor
{
    public class DayEditorWindow : OdinMenuEditorWindow
    {
        [MenuItem("ExpoTheExplorer/Day Editor")]
        private static void Open()
        {
            var window = GetWindow<DayEditorWindow>();
            window.titleContent = new GUIContent("Day Editor");
            window.Show();
        }

        // Pixels, not the inherited default. Odin reads this as a PERCENTAGE when it is
        // between 0 and 1 ("values between 0 and 1 are treated as percentages, and values
        // above as pixels"), and the inherited default is a percentage -- so the label
        // column grew in step with the window and the value column never gained any room.
        // Widening the window could not fix it, which is exactly how it showed up: the
        // sliders' number fields sat permanently past the right edge. A fixed width gives
        // every extra pixel of window to the value column instead.
        public override float DefaultLabelWidth => 260f;

        private FoodCatalog catalog;
        private GameConfig gameConfig;
        private TicketGenerationConfig ticketConfig;
        private TicketCardVisualsConfig ticketCardVisuals;
        private BoardVisualsConfig boardVisuals;
        private string daysFolderPath;
        private List<DayEditorModel> loadedDays;

        protected override OdinMenuTree BuildMenuTree()
        {
            daysFolderPath = Path.Combine(Application.dataPath, "Resources", "Days");
            AutoDiscoverConfigs();

            loadedDays = DayFileIO.LoadAll(daysFolderPath)
                .Select(f => DayEditorModel.FromDayJson(f.Json, catalog))
                .OrderBy(d => d.DayIndex)
                .ToList();

            // These came from real files on disk -- record the index each was loaded under so a
            // later DayIndex edit can be detected as a rename (see OnSaveRequested).
            foreach (var day in loadedDays)
            {
                day.LastSavedDayIndex = day.DayIndex;
            }

            ConfigureAllDays();

            var tree = new OdinMenuTree();
            foreach (var day in loadedDays)
            {
                tree.Add($"Day {day.DayIndex}", day);
            }

            return tree;
        }

        private void ConfigureAllDays()
        {
            foreach (var day in loadedDays)
            {
                day.Configure(catalog, gameConfig, ticketConfig, ticketCardVisuals, boardVisuals, OnSaveRequested, OnDuplicateRequested, OnDeleteRequested);
            }
        }

        private void AutoDiscoverConfigs()
        {
            catalog ??= FindFirstAsset<FoodCatalog>();
            gameConfig ??= FindFirstAsset<GameConfig>();
            ticketConfig ??= FindFirstAsset<TicketGenerationConfig>();
            ticketCardVisuals ??= FindFirstAsset<TicketCardVisualsConfig>();
            boardVisuals ??= FindFirstAsset<BoardVisualsConfig>();
        }

        private static T FindFirstAsset<T>() where T : UnityEngine.Object
        {
            var guids = AssetDatabase.FindAssets($"t:{typeof(T).Name}");
            return guids.Length == 0 ? null : AssetDatabase.LoadAssetAtPath<T>(AssetDatabase.GUIDToAssetPath(guids[0]));
        }

        // Toolbar above the selected Day's Odin-drawn content: "+ New Day" plus the four config
        // asset fields (auto-discovered by AutoDiscoverConfigs, overridable here if it guessed
        // wrong -- same escape-hatch precedent as EconomyConfigEditor's own auto-discovery).
        protected override void OnBeginDrawEditors()
        {
            base.OnBeginDrawEditors();

            SirenixEditorGUI.BeginHorizontalToolbar();

            if (GUILayout.Button("+ New Day", EditorStyles.toolbarButton, GUILayout.Width(90)))
            {
                CreateNewDay();
            }

            GUILayout.FlexibleSpace();

            EditorGUILayout.LabelField("Catalog", GUILayout.Width(50));
            var newCatalog = (FoodCatalog)EditorGUILayout.ObjectField(catalog, typeof(FoodCatalog), false, GUILayout.Width(130));

            EditorGUILayout.LabelField("Game", GUILayout.Width(38));
            var newGameConfig = (GameConfig)EditorGUILayout.ObjectField(gameConfig, typeof(GameConfig), false, GUILayout.Width(130));

            EditorGUILayout.LabelField("Ticket Gen", GUILayout.Width(65));
            var newTicketConfig = (TicketGenerationConfig)EditorGUILayout.ObjectField(ticketConfig, typeof(TicketGenerationConfig), false, GUILayout.Width(130));

            EditorGUILayout.LabelField("Card Visuals", GUILayout.Width(75));
            var newTicketCardVisuals = (TicketCardVisualsConfig)EditorGUILayout.ObjectField(ticketCardVisuals, typeof(TicketCardVisualsConfig), false, GUILayout.Width(130));

            EditorGUILayout.LabelField("Board Visuals", GUILayout.Width(80));
            var newBoardVisuals = (BoardVisualsConfig)EditorGUILayout.ObjectField(boardVisuals, typeof(BoardVisualsConfig), false, GUILayout.Width(130));

            SirenixEditorGUI.EndHorizontalToolbar();

            if (newCatalog != catalog || newGameConfig != gameConfig || newTicketConfig != ticketConfig || newTicketCardVisuals != ticketCardVisuals || newBoardVisuals != boardVisuals)
            {
                catalog = newCatalog;
                gameConfig = newGameConfig;
                ticketConfig = newTicketConfig;
                ticketCardVisuals = newTicketCardVisuals;
                boardVisuals = newBoardVisuals;
                ConfigureAllDays();
            }
        }

        // Adds directly to the live MenuTree rather than ForceMenuTreeRebuild() -- a rebuild
        // re-reads BuildMenuTree() from disk, which would silently drop this brand-new,
        // not-yet-saved Day (it doesn't exist as a file yet).
        private void CreateNewDay()
        {
            var nextIndex = loadedDays.Count == 0 ? 0 : loadedDays.Max(d => d.DayIndex) + 1;
            var day = new DayEditorModel { DayIndex = nextIndex };
            day.Configure(catalog, gameConfig, ticketConfig, ticketCardVisuals, boardVisuals, OnSaveRequested, OnDuplicateRequested, OnDeleteRequested);

            loadedDays.Add(day);
            MenuTree.Add($"Day {day.DayIndex}", day);
            TrySelectMenuItemWithObject(day);
        }

        private void OnSaveRequested(DayEditorModel day)
        {
            if (loadedDays.Any(other => other != day && other.DayIndex == day.DayIndex))
            {
                EditorUtility.DisplayDialog("Duplicate Day Index", $"Another Day already uses dayIndex {day.DayIndex}. Change one of them before saving.", "OK");
                return;
            }

            // If DayIndex changed since this Day was loaded/last saved, write to the new
            // day_XX.json path and remove the old one so no orphan file is left behind.
            if (day.LastSavedDayIndex.HasValue && day.LastSavedDayIndex.Value != day.DayIndex)
            {
                DayFileIO.Delete(daysFolderPath, day.LastSavedDayIndex.Value);
            }

            DayFileIO.Save(daysFolderPath, day.ToDayJson());
            day.LastSavedDayIndex = day.DayIndex;
            AssetDatabase.Refresh();
        }

        private void OnDuplicateRequested(DayEditorModel day)
        {
            var nextIndex = loadedDays.Max(d => d.DayIndex) + 1;
            var duplicate = day.Clone(catalog);
            duplicate.DayIndex = nextIndex;
            duplicate.Configure(catalog, gameConfig, ticketConfig, ticketCardVisuals, boardVisuals, OnSaveRequested, OnDuplicateRequested, OnDeleteRequested);

            loadedDays.Add(duplicate);
            MenuTree.Add($"Day {duplicate.DayIndex}", duplicate);
            TrySelectMenuItemWithObject(duplicate);
        }

        private void OnDeleteRequested(DayEditorModel day)
        {
            if (!EditorUtility.DisplayDialog("Delete Day", $"Delete Day {day.DayIndex}? This cannot be undone.", "Delete", "Cancel"))
            {
                return;
            }

            // Delete whatever file this Day actually landed on last (its DayIndex may have been
            // edited since the last save without saving again).
            DayFileIO.Delete(daysFolderPath, day.LastSavedDayIndex ?? day.DayIndex);
            AssetDatabase.Refresh();
            loadedDays.Remove(day);
            ForceMenuTreeRebuild();
        }
    }
}
