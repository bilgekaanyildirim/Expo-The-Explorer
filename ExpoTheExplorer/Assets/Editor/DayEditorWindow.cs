using System.Collections.Generic;
using System.IO;
using System.Linq;
using ExpoTheExplorer.Data;
using Sirenix.OdinInspector.Editor;
using Sirenix.Utilities;
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

        // Reordering the Day list by dragging a row. The key is what tells a drag STARTED HERE
        // apart from anything else the editor may be dragging over this window.
        private const string DayDragKey = "ExpoTheExplorer.DayEditor.Day";
        private const float DragThreshold = 4f;
        private const float InsertionLineHeight = 2f;
        private static readonly Color InsertionLineColor = new Color(0.3f, 0.6f, 1f);

        // The Day the mouse went down on, and where it went down. A drag only begins once the
        // pointer has travelled past DragThreshold, so a plain click still selects the row.
        private DayEditorModel pressedDay;
        private Vector2 pressedAt;

        // Where a dragged Day would land, as a slot in loadedDays as drawn; -1 when nothing is
        // being dragged over the list.
        private int dropPosition = -1;

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
                AddDayRow(tree, day);
            }

            return tree;
        }

        // Every row in the Day list carries its own delete button. The Day's inspector has had a
        // Delete button all along, but it sits at the bottom of a page several screens tall, and
        // the list is where a designer is standing when they decide a Day should go. Both call the
        // same OnDeleteRequested -- one delete path, not two that can drift apart.
        private void AddDayRow(OdinMenuTree tree, DayEditorModel day)
        {
            foreach (var item in tree.Add($"Day {day.DayIndex}", day))
            {
                item.OnDrawItem += drawn =>
                {
                    // Delete first: it is a GUI.Button, so a click that lands on the X is already
                    // used up by the time the drag handler looks at the event.
                    DrawRowDeleteButton(drawn, day);
                    HandleRowDrag(drawn, day);
                };
            }
        }

        // OnDrawItem is Odin's own per-row hook: it runs after the row has drawn and BEFORE the
        // row handles its own mouse input, which is what makes the button swallow the click
        // instead of the row also reading it as "select this Day".
        private void DrawRowDeleteButton(OdinMenuItem item, DayEditorModel day)
        {
            var rect = item.Rect.AlignRight(16f).AlignCenterY(16f);
            rect.x -= 4f;

            if (!SirenixEditorGUI.IconButton(rect, EditorIcons.X, $"Delete Day {day.DayIndex}"))
            {
                return;
            }

            // Queued, not run here. OnDeleteRequested opens a modal dialog and rebuilds the menu
            // tree, and this runs from inside Odin's pass over the menu items -- rebuilding the
            // list being iterated is the same hazard GuardDayChange defers for (D-114).
            EditorApplication.delayCall += () => OnDeleteRequested(day);
        }

        // Dragging a row to a new place in the list. Unity's own DragAndDrop carries the drag --
        // it owns the cursor feedback and the state machine, so what is left here is deciding
        // where a drop lands and drawing the line that says so.
        private void HandleRowDrag(OdinMenuItem item, DayEditorModel day)
        {
            var e = Event.current;
            var rect = item.Rect;
            var position = loadedDays?.IndexOf(day) ?? -1;
            if (position < 0)
            {
                return;
            }

            switch (e.type)
            {
                // Not consumed: Odin still needs this to select the row. A plain click has to keep
                // working, so the press is only remembered here and the drag starts once the
                // pointer has actually travelled.
                case EventType.MouseDown when e.button == 0 && rect.Contains(e.mousePosition):
                    pressedDay = day;
                    pressedAt = e.mousePosition;
                    break;

                // Deliberately NOT gated on this row's rect: by the time the pointer has moved far
                // enough it is often over a different row, and the drag still belongs to the row it
                // started on. Whichever row's handler sees the event first starts it; Use() means
                // the rest of the pass sees an event of type Used and does nothing.
                case EventType.MouseDrag when pressedDay != null && Vector2.Distance(e.mousePosition, pressedAt) > DragThreshold:
                    var dragging = pressedDay;
                    pressedDay = null;
                    DragAndDrop.PrepareStartDrag();
                    DragAndDrop.objectReferences = new UnityEngine.Object[0];
                    DragAndDrop.SetGenericData(DayDragKey, dragging);
                    DragAndDrop.StartDrag($"Day {dragging.DayIndex}");
                    e.Use();
                    break;

                case EventType.MouseUp:
                    pressedDay = null;
                    break;

                case EventType.DragUpdated when rect.Contains(e.mousePosition) && DragAndDrop.GetGenericData(DayDragKey) is DayEditorModel:
                    DragAndDrop.visualMode = DragAndDropVisualMode.Move;
                    dropPosition = DropPositionOver(rect, position);
                    Repaint();
                    e.Use();
                    break;

                case EventType.DragPerform when rect.Contains(e.mousePosition) && DragAndDrop.GetGenericData(DayDragKey) is DayEditorModel dropped:
                    DragAndDrop.AcceptDrag();
                    var target = DropPositionOver(rect, position);
                    dropPosition = -1;

                    // Queued for the same reason the delete button's click is (D-114): MoveDay
                    // opens a dialog and rebuilds the menu tree, and this is inside Odin's own
                    // pass over the menu items.
                    EditorApplication.delayCall += () => MoveDay(dropped, target);
                    e.Use();
                    break;

                case EventType.DragExited:
                    dropPosition = -1;
                    pressedDay = null;
                    break;

                // The line is drawn from stored state rather than while handling DragUpdated,
                // because DragUpdated is not a repaint event -- drawing there draws nothing.
                case EventType.Repaint when dropPosition == position:
                    DrawInsertionLine(rect, rect.yMin);
                    break;

                // Only the last row has a below-it gap of its own; every other "after row n" is
                // the same line as "before row n + 1", drawn by that row.
                case EventType.Repaint when dropPosition == position + 1 && position == loadedDays.Count - 1:
                    DrawInsertionLine(rect, rect.yMax - InsertionLineHeight);
                    break;
            }
        }

        // Above or below the row's midline, as a slot in the list AS DRAWN -- the dragged Day is
        // still counted, which is what MoveDay's adjustment then takes back out.
        private static int DropPositionOver(Rect rect, int position) =>
            Event.current.mousePosition.y > rect.center.y ? position + 1 : position;

        private static void DrawInsertionLine(Rect row, float y) =>
            EditorGUI.DrawRect(new Rect(row.x, y, row.width, InsertionLineHeight), InsertionLineColor);

        private void MoveDay(DayEditorModel day, int targetPosition)
        {
            // The drop was queued, so the Day may already be gone -- and loadedDays may be a
            // different list than the one the drop was measured against.
            if (loadedDays == null || !loadedDays.Contains(day))
            {
                return;
            }

            var from = loadedDays.IndexOf(day);

            // targetPosition counts the gaps in the list with the Day still sitting in it. Once it
            // is lifted out, every slot below its old place is one lower.
            var to = Mathf.Clamp(targetPosition > from ? targetPosition - 1 : targetPosition, 0, loadedDays.Count - 1);
            if (to == from)
            {
                return;
            }

            var problem = ReorderProblem();
            if (problem != null)
            {
                EditorUtility.DisplayDialog("Cannot Reorder Days", problem, "OK");
                return;
            }

            if (!EditorUtility.DisplayDialog("Move Day", MoveQuestionFor(day, to), "Move", "Cancel"))
            {
                return;
            }

            PromptForUnsavedDays("Reordering the Days rewrites every Day file and reloads them, which");

            DayFileIO.Move(daysFolderPath, day.LastSavedDayIndex.Value, to);
            AssetDatabase.Refresh();
            ForceMenuTreeRebuild();
        }

        // A drop is measured against the rows as they are DRAWN, and then the files are rewritten
        // in that order -- so the two orderings have to be the same one. They are, as long as every
        // Day's number is settled on disk: a Day that was never saved has no file to move at all,
        // and a Day whose Day Index was typed over without saving sorts by one number in the list
        // and by another on disk. Rather than guess which one the user pointed at, name the Day
        // that is in the way and let them settle it.
        private string ReorderProblem()
        {
            foreach (var day in loadedDays)
            {
                if (!day.LastSavedDayIndex.HasValue)
                {
                    return $"Day {day.DayIndex} has never been saved, so it has no file to move.\n\nSave it (or delete it) first, then reorder.";
                }

                if (day.LastSavedDayIndex.Value != day.DayIndex)
                {
                    return $"Day {day.LastSavedDayIndex.Value} has had its Day Index changed to {day.DayIndex} without being saved, so the list and the files disagree about where it belongs.\n\nSave that change (or revert it) first, then reorder.";
                }
            }

            return null;
        }

        // Says "position", not "Day", for the place it is going: the Days are not necessarily
        // numbered 0..n-1 before the move (a hand-deleted file leaves a hole), and they always are
        // after it, so the destination is only a Day number once the rewrite has happened.
        private string MoveQuestionFor(DayEditorModel day, int to)
        {
            return $"Move Day {day.DayIndex} to position {to} in the list?\n\n" +
                   $"The Days are renumbered 0-{loadedDays.Count - 1} in the new order, so this one becomes Day {to} and the Days it passed each shift by one.\n\n" +
                   "Anything that names a Day by NUMBER somewhere else -- a location's or a prop's unlock Day in the Meta Catalog, a powerup's tutorial Day -- is not touched, so it will point at a different Day afterwards.\n\n" +
                   "This cannot be undone.";
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
            AddDayRow(MenuTree, day);
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
            AddDayRow(MenuTree, duplicate);
            TrySelectMenuItemWithObject(duplicate);
        }

        private void OnDeleteRequested(DayEditorModel day)
        {
            // The row button queues this rather than running it, so between the click and here the
            // Day may already be gone -- and acting on a gone Day would be worse than doing
            // nothing: after the renumber its LastSavedDayIndex names a file that now belongs to
            // somebody else. Same check AskAboutLeavingDay makes, for the same reason.
            if (loadedDays == null || !loadedDays.Contains(day))
            {
                return;
            }

            if (!EditorUtility.DisplayDialog("Delete Day", DeleteQuestionFor(day), "Delete", "Cancel"))
            {
                return;
            }

            // ForceMenuTreeRebuild below re-runs BuildMenuTree, which re-reads every Day from
            // disk -- so unsaved edits on any OTHER Day go with it. That has always been true
            // of this path; it just used to happen without a word.
            PromptForUnsavedDays("Deleting a Day reloads every Day from disk, which", day);

            // LastSavedDayIndex is the ONLY thing that names a file here. It is set for every Day
            // that came off disk (BuildMenuTree) and after every save, so a Day without one has no
            // file -- and falling back to the live DayIndex would delete somebody else's file, the
            // moment a never-saved Day had its Day Index typed over an existing Day's number.
            if (day.LastSavedDayIndex.HasValue)
            {
                DayFileIO.Delete(daysFolderPath, day.LastSavedDayIndex.Value);

                // Close the hole: the Days after the deleted one slide down, file name and the
                // dayIndex inside it together, so the list stays 0,1,2,... A Day that never had a
                // file left no hole, so it renumbers nothing -- moving everyone else's number
                // because a scratch Day was thrown away would be a surprise, not a service.
                DayFileIO.Renumber(daysFolderPath);
            }

            AssetDatabase.Refresh();
            loadedDays.Remove(day);
            ForceMenuTreeRebuild();
        }

        // Said before anything is written, because the renumber reaches past the Day being
        // deleted. The count is worked out the way Renumber works it out -- the Days that have a
        // file, in dayIndex order, compared against their position -- so what the dialog promises
        // is what happens.
        private string DeleteQuestionFor(DayEditorModel day)
        {
            var question = $"Delete Day {day.DayIndex}? This cannot be undone.";
            if (!day.LastSavedDayIndex.HasValue)
            {
                return question;
            }

            var remaining = loadedDays
                .Where(d => d != day && d.LastSavedDayIndex.HasValue)
                .Select(d => d.LastSavedDayIndex.Value)
                .OrderBy(index => index)
                .ToList();

            var firstMoved = -1;
            var movedCount = 0;
            for (var position = 0; position < remaining.Count; position++)
            {
                if (remaining[position] == position)
                {
                    continue;
                }

                firstMoved = firstMoved < 0 ? position : firstMoved;
                movedCount++;
            }

            if (movedCount == 0)
            {
                return question;
            }

            return $"{question}\n\n" +
                   $"{movedCount} other Day(s) are then renumbered so the list has no gap: " +
                   $"Day {remaining[firstMoved]} becomes Day {firstMoved}, and so on down to Day {remaining.Count - 1}.\n\n" +
                   "Anything that names a Day by NUMBER somewhere else -- a location's or a prop's unlock Day in the Meta Catalog, a powerup's tutorial Day -- is not touched, so it will point at a different Day afterwards.";
        }
    }
}
