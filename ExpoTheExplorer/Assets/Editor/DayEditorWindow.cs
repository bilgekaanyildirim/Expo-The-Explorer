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

        // A Day's inspector is far taller than any window (food selection, ticket strip,
        // per-ticket editor, Day Start grid, three settings blocks), and without this the
        // content simply ran off the bottom with no way to reach it: the only scrollable
        // thing on the page was the ticket strip's own nested scroll view, which is why the
        // wheel appeared to "scroll the ticket sequence" instead of the page. That nested
        // view is gone now (DayEditorTicketCardPreview), so this is what the wheel scrolls.
        public override bool UseScrollView => true;

        // Each config field shrinks with the window instead of pinning a width the toolbar
        // can never go below. 40px still shows the asset icon and stays clickable.
        private static readonly GUILayoutOption[] ConfigFieldWidth =
        {
            GUILayout.MinWidth(40f), GUILayout.MaxWidth(130f),
        };

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

            if (GUILayout.Button("+ New Day", EditorStyles.toolbarButton, GUILayout.MinWidth(70), GUILayout.MaxWidth(90)))
            {
                CreateNewDay();
            }

            GUILayout.FlexibleSpace();

            // Shrinkable, not fixed. Five 130px fields plus their labels used to add up to a
            // floor of roughly 1050px that the toolbar could never go below, and GUILayout
            // widens the whole content column to its widest child -- so on any window
            // narrower than that, every row in the inspector below was pushed past the right
            // edge along with it. Names are tooltips now instead of separate labels, which
            // removes another 300px of floor.
            var newCatalog = (FoodCatalog)EditorGUILayout.ObjectField(
                new GUIContent(string.Empty, "Food Catalog"), catalog, typeof(FoodCatalog), false, ConfigFieldWidth);

            var newGameConfig = (GameConfig)EditorGUILayout.ObjectField(
                new GUIContent(string.Empty, "Game Config"), gameConfig, typeof(GameConfig), false, ConfigFieldWidth);

            var newTicketConfig = (TicketGenerationConfig)EditorGUILayout.ObjectField(
                new GUIContent(string.Empty, "Ticket Generation Config (seed for new Days)"), ticketConfig, typeof(TicketGenerationConfig), false, ConfigFieldWidth);

            var newTicketCardVisuals = (TicketCardVisualsConfig)EditorGUILayout.ObjectField(
                new GUIContent(string.Empty, "Ticket Card Visuals Config"), ticketCardVisuals, typeof(TicketCardVisualsConfig), false, ConfigFieldWidth);

            var newBoardVisuals = (BoardVisualsConfig)EditorGUILayout.ObjectField(
                new GUIContent(string.Empty, "Board Visuals Config"), boardVisuals, typeof(BoardVisualsConfig), false, ConfigFieldWidth);

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
        // Seeded from the highest-numbered existing Day rather than from a shared template
        // asset (decisions.md D-007). Two reasons: the settings a designer wants for a new
        // Day are almost always the previous Day's, and it keeps the numbers in data --
        // there is no longer a BoardDistributionConfig to read them from, and the coded
        // defaults on DayEditorBoardDistribution are only ever reached for the very first
        // Day in an empty catalog.
        private void CreateNewDay()
        {
            var nextIndex = loadedDays.Count == 0 ? 0 : loadedDays.Max(d => d.DayIndex) + 1;
            var previous = loadedDays.Count == 0 ? null : loadedDays.OrderBy(d => d.DayIndex).Last();

            var day = new DayEditorModel { DayIndex = nextIndex };

            // Configure BEFORE copying: the copy resolves main-dish food ids through the
            // shared catalog, and an unconfigured model has none -- the weights would come
            // back with empty Food slots.
            day.Configure(catalog, gameConfig, ticketConfig, ticketCardVisuals, boardVisuals, OnSaveRequested, OnDuplicateRequested, OnDeleteRequested);

            if (previous != null)
            {
                // Balancing only. Content -- the ticket sequence, the Day Start board, the
                // star thresholds, the food selection -- is deliberately NOT copied: those
                // are what makes a Day a different Day, and silently inheriting them would
                // hide that the new Day is still empty.
                day.CopySettingsFrom(previous);
            }

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
