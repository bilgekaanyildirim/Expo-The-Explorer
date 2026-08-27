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

        // The Day whose editor was on screen last pass. The unsaved-changes prompt hangs off
        // this rather than off OdinMenuTreeSelection.SelectionChanged: that event fires from
        // inside Odin's own selection bookkeeping, and "Keep Editing" has to re-select the Day
        // being left -- i.e. mutate the list Odin is in the middle of iterating.
        private DayEditorModel shownDay;

        // True from the moment the prompt is queued until it has been answered. Without it,
        // every OnImGUI pass while the dialog is up would queue another one.
        private bool unsavedPromptQueued;

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

            // These Days came straight off disk, so what they hold right now IS their file:
            // that is the baseline every later "has this changed?" is measured against. After
            // Configure, because the baseline has to be the state the inspector will draw.
            foreach (var day in loadedDays)
            {
                day.MarkSaved();
            }

            // Every model in the old tree was just replaced by a freshly loaded one, so the
            // Day the leave-guard thinks is on screen no longer exists.
            shownDay = null;

            var tree = new OdinMenuTree();
            foreach (var day in loadedDays)
            {
                tree.Add($"Day {day.DayIndex}", day);
            }

            return tree;
        }

        protected override void OnImGUI()
        {
            GuardDayChange();
            RefreshSaveStateLabels();
            base.OnImGUI();
        }

        // A window close cannot be cancelled, so this one is Save-or-lose rather than the
        // three-way prompt switching Days gets.
        protected override void OnDestroy()
        {
            PromptForUnsavedDays("Closing the Day Editor");
            base.OnDestroy();
        }

        private void GuardDayChange()
        {
            if (MenuTree == null || unsavedPromptQueued)
            {
                return;
            }

            var selected = MenuTree.Selection?.SelectedValue as DayEditorModel;
            if (ReferenceEquals(selected, shownDay))
            {
                return;
            }

            var leaving = shownDay;
            shownDay = selected;

            // A Day that was deleted, or one belonging to a menu tree that has since been
            // rebuilt, is not being "left" -- it is gone, and there is nothing to save it to.
            if (leaving == null || loadedDays == null || !loadedDays.Contains(leaving))
            {
                return;
            }

            leaving.RefreshUnsavedState(force: true);
            if (!leaving.HasUnsavedChanges)
            {
                return;
            }

            // Asked on the next editor tick, not here. A modal dialog opened in the middle of
            // OnGUI leaves this pass's layout half-built, and re-selecting the previous Day
            // (what "Keep Editing" does) would run against a menu tree that is mid-draw.
            unsavedPromptQueued = true;
            EditorApplication.delayCall += () => AskAboutLeavingDay(leaving);
        }

        private void AskAboutLeavingDay(DayEditorModel leaving)
        {
            unsavedPromptQueued = false;

            // Between queueing and now, the Day may have been saved, reverted or deleted.
            if (loadedDays == null || !loadedDays.Contains(leaving) || !leaving.HasUnsavedChanges)
            {
                return;
            }

            // A Day that has never been written has nothing to fall back to, so "Discard"
            // would mean deleting it outright. That is the Delete button's job, and it asks
            // first; here the second option simply leaves the Day alone, unsaved.
            if (!leaving.HasSavedState)
            {
                if (EditorUtility.DisplayDialog(
                        "Unsaved Day",
                        $"Day {leaving.DayIndex} has never been saved. Save it now?",
                        "Save", "Later"))
                {
                    SaveOrExplain(leaving);
                }

                Repaint();
                return;
            }

            var choice = EditorUtility.DisplayDialogComplex(
                "Unsaved Changes",
                $"Day {leaving.DayIndex} has changes that were never saved.",
                "Save", "Keep Editing", "Discard");

            switch (choice)
            {
                case 0:
                    SaveOrExplain(leaving);
                    break;
                case 1:
                    // Put the guard's own idea of the current Day back first, so re-selecting
                    // does not read as another Day change on the next pass.
                    shownDay = leaving;
                    TrySelectMenuItemWithObject(leaving);
                    break;
                default:
                    leaving.RevertToSaved();
                    break;
            }

            Repaint();
        }

        // The Save BUTTON is disabled while a Day has validation errors (EnableIf on
        // DayEditorModel.Save). These prompts must not become a way around that -- an invalid
        // Day written to disk is a file the runtime parser then chokes on.
        private void SaveOrExplain(DayEditorModel day)
        {
            if (!day.IsSaveable)
            {
                EditorUtility.DisplayDialog(
                    "Cannot Save",
                    $"Day {day.DayIndex} still has validation errors, so it was left unsaved:\n\n{day.ValidationSummary}",
                    "OK");
                return;
            }

            OnSaveRequested(day);
        }

        // Used by the two paths that throw away in-memory Days wholesale: closing the window,
        // and deleting a Day (which rebuilds the menu tree from disk).
        private void PromptForUnsavedDays(string reason, DayEditorModel except = null)
        {
            if (loadedDays == null)
            {
                return;
            }

            foreach (var day in loadedDays)
            {
                day.RefreshUnsavedState(force: true);
            }

            var unsaved = loadedDays.Where(d => d != except && d.HasUnsavedChanges).ToList();
            if (unsaved.Count == 0)
            {
                return;
            }

            var list = string.Join(", ", unsaved.Select(d => $"Day {d.DayIndex}"));
            if (!EditorUtility.DisplayDialog(
                    "Unsaved Changes",
                    $"{reason} would discard unsaved changes on: {list}.",
                    "Save All", "Discard"))
            {
                return;
            }

            // Collected rather than reported one dialog at a time: "Save All" is one decision,
            // so its failures are one message.
            var failed = unsaved.Where(day => !day.IsSaveable).ToList();
            foreach (var day in unsaved.Except(failed))
            {
                OnSaveRequested(day);
            }

            if (failed.Count > 0)
            {
                EditorUtility.DisplayDialog(
                    "Some Days Were Not Saved",
                    $"These Days still have validation errors and were left as they are: {string.Join(", ", failed.Select(d => $"Day {d.DayIndex}"))}.",
                    "OK");
            }
        }

        // The Day list is the only place a Day that is NOT on screen can report itself, and
        // the asterisk there is what makes "I edited Day 12 and wandered off" visible at all.
        // Cheap enough for every pass: HasUnsavedChanges reads a cached bool, and only the Day
        // being drawn ever recomputes it.
        private void RefreshSaveStateLabels()
        {
            if (MenuTree == null || loadedDays == null)
            {
                return;
            }

            foreach (var item in MenuTree.MenuItems)
            {
                if (item.Value is not DayEditorModel day)
                {
                    continue;
                }

                // The number comes from the model rather than from what the item was added
                // with, so an edited Day Index shows up in the list instead of going stale.
                var wanted = day.HasUnsavedChanges ? $"Day {day.DayIndex} *" : $"Day {day.DayIndex}";
                if (item.Name != wanted)
                {
                    item.Name = wanted;
                }
            }

            var wantedTitle = loadedDays.Any(d => d.HasUnsavedChanges) ? "Day Editor*" : "Day Editor";
            if (titleContent.text != wantedTitle)
            {
                titleContent.text = wantedTitle;
            }
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
        // wrong -- the override exists because "find the first asset of this type in the project"
        // is a guess, and a project with two of them would otherwise be stuck with the wrong one).
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

            // What is on disk is now what the model holds: this becomes the state every later
            // "has this changed?" is measured against.
            day.MarkSaved();
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

            // ForceMenuTreeRebuild below re-runs BuildMenuTree, which re-reads every Day from
            // disk -- so unsaved edits on any OTHER Day go with it. That has always been true
            // of this path; it just used to happen without a word.
            PromptForUnsavedDays("Deleting a Day reloads every Day from disk, which", day);

            // Delete whatever file this Day actually landed on last (its DayIndex may have been
            // edited since the last save without saving again).
            DayFileIO.Delete(daysFolderPath, day.LastSavedDayIndex ?? day.DayIndex);
            AssetDatabase.Refresh();
            loadedDays.Remove(day);
            ForceMenuTreeRebuild();
        }
    }
}
