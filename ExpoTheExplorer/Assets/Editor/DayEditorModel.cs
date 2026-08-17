using System;
using System.Collections.Generic;
using System.Linq;
using ExpoTheExplorer.Core;
using ExpoTheExplorer.Data;
using ExpoTheExplorer.Systems.DaySystem;
using ExpoTheExplorer.Systems.TicketSystem;
using Sirenix.OdinInspector;
using UnityEditor;

namespace ExpoTheExplorer.Editor
{
    [Serializable]
    public class DayEditorModel
    {
        [BoxGroup("Day"), LabelText("Day Index"), PropertyOrder(-4)]
        [InfoBox("$ValidationMessage", InfoMessageType.Error, nameof(HasValidationErrors))]
        [InfoBox("$ValidationMessage", InfoMessageType.Info, nameof(IsValid))]
        [InfoBox("$ValidationWarningMessage", InfoMessageType.Warning, nameof(HasValidationWarnings))]
        public int DayIndex;

        [BoxGroup("Day"), PropertyOrder(-4)]
        public int TicketsRequiredForDay = 10;

        // Day Complete popup's 3-star rating, compared against the day's earned score
        // (summed BaseTip + tip bonus). Authored per Day -- see ExpoTheExplorer/CLAUDE.md
        // "Day Complete Popup / Star Rating". These were absent from this model while the
        // JSON carried them, so every Save silently reset all three to 0.
        [BoxGroup("Star Thresholds"), PropertyOrder(-3), LabelText("1 Star")]
        public int Star1Threshold;

        [BoxGroup("Star Thresholds"), PropertyOrder(-3), LabelText("2 Stars")]
        public int Star2Threshold;

        [BoxGroup("Star Thresholds"), PropertyOrder(-3), LabelText("3 Stars")]
        public int Star3Threshold;

        // Per-Day BoardDistributor balancing, replacing the shared BoardDistributionConfig
        // asset as the runtime authority. Odin draws this as a plain nested box for now;
        // grouping, seeding from the config asset and the leak-distribution preview land in
        // a later step of .claude/day-config-plan.md.
        [BoxGroup("Board Distribution"), HideLabel, PropertyOrder(-2.5f)]
        public DayEditorBoardDistribution BoardDistribution = new();

        // Drawn from THIS Day's values, which is the whole point: these two distributions
        // are what the balance knobs above actually mean, and before this they could only be
        // seen on the shared config asset's inspector -- i.e. never for the Day being tuned.
        [BoxGroup("Board Distribution"), OnInspectorGUI, PropertyOrder(-2.45f)]
        private void DrawBoardDistributionPreviews()
        {
            DayEditorSettingsPreviews.DrawLeakPreview(
                BoardDistribution.MaxLeakCount, BoardDistribution.NoiseLeakCountLambda);

            DayEditorSettingsPreviews.DrawGuaranteedTicketPreview(
                BoardDistribution.GuaranteedTicketCountMode,
                BoardDistribution.GuaranteedTicketCount,
                BoardDistribution.GuaranteedTicketCountLambda);
        }

        // Per-Day ticket play-time balancing (time limits + lookahead depth). Same story as
        // BoardDistribution above: plain nested box for now, proper grouping in a later
        // step of .claude/day-config-plan.md.
        [BoxGroup("Ticket Runtime"), HideLabel, PropertyOrder(-2.4f)]
        public DayEditorTicketRuntime TicketRuntime = new();

        [FoldoutGroup("Ticket Sequence"), OnInspectorGUI, PropertyOrder(-1)]
        private void DrawTicketCardPreview() => DayEditorTicketCardPreview.DrawStrip(TicketSequence, sharedTicketCardVisuals, ref ticketStripScrollPos, ref selectedTicketIndex, ref draggedTicketIndex, ref ticketDragStartMousePos, ref ticketStripViewWidth);

        // Not part of the JSON, not serialized -- same "plain private field" convention as
        // sharedCatalog etc. below, just UI state for the preview/editor above.
        private UnityEngine.Vector2 ticketStripScrollPos;
        private int selectedTicketIndex = -1;
        private int draggedTicketIndex = -1;
        private UnityEngine.Vector2 ticketDragStartMousePos;

        // Width the strip was actually given, carried between frames -- see DrawStrip for why
        // it cannot be read fresh during a layout pass. Seeded non-zero so the first pass
        // before any repaint does not think the strip has no room at all.
        private float ticketStripViewWidth = 600f;

        [FoldoutGroup("Ticket Sequence"), OnInspectorGUI, PropertyOrder(-0.5f)]
        private void DrawSelectedTicketEditor()
        {
            if (selectedTicketIndex < 0 || selectedTicketIndex >= TicketSequence.Count)
            {
                EditorGUILayout.HelpBox("Right click to a ticket card above to edit it.", UnityEditor.MessageType.Info);
                return;
            }

            var entry = TicketSequence[selectedTicketIndex];

            EditorGUILayout.LabelField($"Editing Ticket #{selectedTicketIndex}", UnityEditor.EditorStyles.boldLabel);

            if (UnityEngine.GUILayout.Button("Generate Random Ticket"))
            {
                if (sharedCatalog == null || sharedTicketConfig == null)
                {
                    EditorUtility.DisplayDialog("Generate Random Ticket", "Config assets aren't assigned yet -- set them in the toolbar above.", "OK");
                }
                else if (!HasSelectableMainDish)
                {
                    EditorUtility.DisplayDialog("Generate Random Ticket", NoMainDishMessage, "OK");
                }
                else if (EditorUtility.DisplayDialog("Generate Random Ticket", "This overwrites this ticket's Main/Side/Drink/Modifications/Patience Type. Continue?", "Generate", "Cancel"))
                {
                    // Fresh, unseeded TicketFactory per click -- reuses the exact same generation
                    // logic (weighted Main pick, Side/Drink inclusion chance, modification
                    // count/selection) DayContentGenerator uses for a whole Day, just for this
                    // one ticket, so a re-click always gives a genuinely different result.
                    var factory = new TicketFactory(sharedTicketConfig);
                    var patienceType = factory.PickRandomPatienceType();
                    var ticket = factory.Create(AllowedFoodPool, factory.PickRandomCustomerName(), patienceType);
                    var regenerated = DayEditorTicketEntry.FromTicket(ticket);
                    entry.MainItem = regenerated.MainItem;
                    entry.SideItem = regenerated.SideItem;
                    entry.DrinkItem = regenerated.DrinkItem;
                    entry.Modifications = regenerated.Modifications;
                    entry.PatienceType = regenerated.PatienceType;
                }
            }

            EditorGUILayout.Space();

            var pool = AllowedFoodPool;
            entry.MainItem = DrawCategoryItemField("Main Item", entry.MainItem, FoodCategory.Main, pool);
            entry.SideItem = DrawCategoryItemField("Side Item", entry.SideItem, FoodCategory.Side, pool);
            entry.DrinkItem = DrawCategoryItemField("Drink Item", entry.DrinkItem, FoodCategory.Drink, pool);
            entry.PatienceType = (PatienceType)EditorGUILayout.EnumPopup("Patience Type", entry.PatienceType);

            entry.CustomerNameOverride = EditorGUILayout.TextField("Name Override", entry.CustomerNameOverride);
            entry.TimeLimitSecondsOverride = EditorGUILayout.FloatField("Time Override", entry.TimeLimitSecondsOverride);

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Modifications", UnityEditor.EditorStyles.boldLabel);
            for (var i = 0; i < entry.Modifications.Count; i++)
            {
                EditorGUILayout.BeginHorizontal();
                entry.Modifications[i].Config = (ModificationConfig)EditorGUILayout.ObjectField(entry.Modifications[i].Config, typeof(ModificationConfig), false);
                entry.Modifications[i].IsAddition = EditorGUILayout.ToggleLeft("Addition", entry.Modifications[i].IsAddition, UnityEngine.GUILayout.Width(80));
                var removeClicked = UnityEngine.GUILayout.Button("x", UnityEngine.GUILayout.Width(20));
                EditorGUILayout.EndHorizontal();
                if (removeClicked)
                {
                    entry.Modifications.RemoveAt(i);
                    break; // list mutated mid-loop -- next OnGUI pass redraws the rest
                }
            }
            if (UnityEngine.GUILayout.Button("+ Add Modification"))
            {
                entry.Modifications.Add(new DayEditorModification());
            }

            EditorGUILayout.Space();
            if (UnityEngine.GUILayout.Button("Delete This Ticket"))
            {
                TicketSequence.RemoveAt(selectedTicketIndex);
                selectedTicketIndex = -1;
            }
        }

        // Restricts the picker to items of the given category, out of the pool this Day is
        // allowed to use -- ObjectField can't filter by a field value, only by Type, so a
        // plain ObjectField would let e.g. a Side item be assigned into the Main slot (or a
        // food this Day's own selection excludes). Falls back to the old unfiltered
        // ObjectField when there is no pool yet (catalog unassigned), so editing isn't
        // blocked before the toolbar is configured.
        private static FoodItemConfig DrawCategoryItemField(string label, FoodItemConfig current, FoodCategory category, IReadOnlyList<FoodItemConfig> pool)
        {
            if (pool == null)
            {
                return (FoodItemConfig)EditorGUILayout.ObjectField(label, current, typeof(FoodItemConfig), false);
            }

            var options = pool.Where(item => item.Category == category).ToList();

            // Keep a mismatched/orphaned current value visible instead of silently dropping it
            // (e.g. data authored before this filter existed, or a category changed since).
            if (current != null && !options.Contains(current))
            {
                options.Insert(0, current);
            }

            var labels = new string[options.Count + 1];
            labels[0] = "None";
            for (var i = 0; i < options.Count; i++)
            {
                labels[i + 1] = options[i].DisplayName;
            }

            var currentIndex = current == null ? 0 : options.IndexOf(current) + 1;
            var selectedIndex = EditorGUILayout.Popup(label, currentIndex, labels);
            return selectedIndex == 0 ? null : options[selectedIndex - 1];
        }

        [UnityEngine.HideInInspector]
        public List<DayEditorTicketEntry> TicketSequence = new();

        [FoldoutGroup("Start Board"), OnInspectorGUI, PropertyOrder(-1)]
        private void DrawDayStartPreview()
        {
            UnityEngine.GUILayout.BeginHorizontal();
            DayEditorDayStartPreview.DrawGrid(
                sharedGameConfig, sharedBoardVisuals, BoardTimeline,
                ref selectedStartBoardX, ref selectedStartBoardY, ref draggedStartBoardX, ref draggedStartBoardY, ref startBoardDragStartMousePos);
            UnityEngine.GUILayout.Space(10);
            DrawStartBoardCellEditor();
            UnityEngine.GUILayout.EndHorizontal();
        }

        // Not part of the JSON, not serialized -- same UI-state convention as
        // selectedTicketIndex above. -1 = no cell clicked yet.
        private int selectedStartBoardX = -1;
        private int selectedStartBoardY = -1;
        private int draggedStartBoardX = -1;
        private int draggedStartBoardY = -1;
        private UnityEngine.Vector2 startBoardDragStartMousePos;

        // Click-to-place authoring for Day Start content: pick an item for whichever cell
        // was last clicked in the grid to its left. Finds the existing BoardTimeline entry
        // at that exact cell (TriggerStepIndex -1, UseExactCell true) if there is one, adds
        // one if the cell was empty and an item got picked, or removes it if the item field
        // is cleared back to None -- the same add/update/remove semantics the old
        // Trigger Step/Use Exact Cell/X/Y table columns exposed, just driven by clicking the
        // preview instead of typing coordinates.
        private void DrawStartBoardCellEditor()
        {
            UnityEngine.GUILayout.BeginVertical(UnityEngine.GUILayout.Width(220));

            if (selectedStartBoardX < 0)
            {
                EditorGUILayout.HelpBox("Click a cell in the grid to edit it.", UnityEditor.MessageType.Info);
                UnityEngine.GUILayout.EndVertical();
                return;
            }

            EditorGUILayout.LabelField($"Cell ({selectedStartBoardX}, {selectedStartBoardY})", UnityEditor.EditorStyles.boldLabel);

            var entry = FindStartBoardEntry(selectedStartBoardX, selectedStartBoardY);
            var currentItem = entry?.Item;
            var newItem = (FoodItemConfig)EditorGUILayout.ObjectField("Item", currentItem, typeof(FoodItemConfig), false);

            if (newItem != currentItem)
            {
                if (newItem == null)
                {
                    if (entry != null) BoardTimeline.Remove(entry);
                    entry = null;
                }
                else if (entry != null)
                {
                    entry.Item = newItem;
                }
                else
                {
                    entry = new DayEditorBoardSpawnEntry { TriggerStepIndex = -1, Item = newItem, UseExactCell = true, X = selectedStartBoardX, Y = selectedStartBoardY };
                    BoardTimeline.Add(entry);
                }
            }

            if (entry == null)
            {
                UnityEngine.GUILayout.EndVertical();
                return;
            }

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Modifications", UnityEditor.EditorStyles.boldLabel);
            for (var i = 0; i < entry.Modifications.Count; i++)
            {
                EditorGUILayout.BeginHorizontal();
                entry.Modifications[i].Config = (ModificationConfig)EditorGUILayout.ObjectField(entry.Modifications[i].Config, typeof(ModificationConfig), false);
                entry.Modifications[i].IsAddition = EditorGUILayout.ToggleLeft("Addition", entry.Modifications[i].IsAddition, UnityEngine.GUILayout.Width(80));
                var removeClicked = UnityEngine.GUILayout.Button("x", UnityEngine.GUILayout.Width(20));
                EditorGUILayout.EndHorizontal();
                if (removeClicked)
                {
                    entry.Modifications.RemoveAt(i);
                    break; // list mutated mid-loop -- next OnGUI pass redraws the rest
                }
            }
            if (UnityEngine.GUILayout.Button("+ Add Modification"))
            {
                entry.Modifications.Add(new DayEditorModification());
            }

            EditorGUILayout.Space();
            if (UnityEngine.GUILayout.Button("Clear Cell"))
            {
                BoardTimeline.Remove(entry);
            }

            UnityEngine.GUILayout.EndVertical();
        }

        private DayEditorBoardSpawnEntry FindStartBoardEntry(int x, int y) =>
            BoardTimeline.FirstOrDefault(e => e.TriggerStepIndex == -1 && e.UseExactCell && e.X == x && e.Y == y);

        // Only TriggerStepIndex == -1 (Day Start) entries are ever replayed at runtime
        // (BoardDistributor took over everything after Day Start, see decisions.md D-001
        // Phase 2/3). Authored entirely through the Start Board grid's click-to-place
        // editor above now -- no table, no separate "Add Board Spawn" button.
        [UnityEngine.HideInInspector]
        public List<DayEditorBoardSpawnEntry> BoardTimeline = new();

        // Named for what it is since D-006 removed the override layer: these settings are not
        // overrides of anything, they are the record of how this Day's ticketSequence was
        // generated, and the input the next Generate uses.
        [FoldoutGroup("Generation Settings (authoring only)"), HideLabel, PropertyOrder(-3)]
        public DayEditorMetaModel EditorMeta = new();

        // Same reasoning as the leak preview: the spawn-chance and modification-count
        // distributions belong next to the Day whose Generate they drive, not only on the
        // seed asset's inspector.
        [FoldoutGroup("Generation Settings (authoring only)"), OnInspectorGUI, PropertyOrder(-2.9f)]
        private void DrawMainDishPreview() =>
            DayEditorSettingsPreviews.DrawMainDishPreview(
                EditorMeta.TicketGeneration.MainDishWeights
                    .Select(w => (w.Food, w.Weight, w.ModificationCountLambda)).ToList(),
                sharedCatalog != null ? AllowedFoodPool : null);

        // Which foods exist in this Day: drives Generate's pool AND the ticket editor's
        // Main/Side/Drink pickers, so a Day can only ever contain food it actually
        // selected. Hand-drawn (rather than an Odin list of ids) because the options come
        // from FoodCatalog while the stored data is just the chosen subset -- a tile per
        // catalog item is the shape that matches, and it can't drift out of sync with a
        // catalog that gained or lost an item. Expanded by default: seeing the Day's food
        // set is the point, so it shouldn't need a click to reveal.
        [FoldoutGroup("Food Selection", expanded: true), OnInspectorGUI, PropertyOrder(-5)]
        private void DrawFoodSelection()
        {
            if (sharedCatalog == null)
            {
                EditorGUILayout.HelpBox("Food Catalog not assigned (toolbar above) -- nothing to select from.", UnityEditor.MessageType.Info);
                return;
            }

            var allItems = sharedCatalog.Items.Where(item => item != null).ToList();
            if (allItems.Count == 0)
            {
                EditorGUILayout.HelpBox("Food Catalog is empty -- nothing to select from.", UnityEditor.MessageType.Info);
                return;
            }

            // Label on its own line and the buttons on the next, rather than one shared
            // row: an EditorGUILayout label expands to fill, which can push fixed-width
            // buttons past the right edge of a narrow inspector and make them look absent.
            EditorGUILayout.LabelField("Click a food to put it in this Day", UnityEditor.EditorStyles.boldLabel);

            EditorGUILayout.BeginHorizontal();
            var selectAll = UnityEngine.GUILayout.Button("Select All", UnityEngine.GUILayout.Width(90));
            var clearAll = UnityEngine.GUILayout.Button("Clear All", UnityEngine.GUILayout.Width(90));
            UnityEngine.GUILayout.FlexibleSpace();
            EditorGUILayout.EndHorizontal();

            if (selectAll || clearAll)
            {
                foreach (var item in allItems) SetFoodAllowed(item, selectAll);
            }

            DrawFoodCategorySelection(allItems, FoodCategory.Main);
            DrawFoodCategorySelection(allItems, FoodCategory.Side);
            DrawFoodCategorySelection(allItems, FoodCategory.Drink);

            if (!HasSelectableMainDish)
            {
                EditorGUILayout.Space();
                EditorGUILayout.HelpBox(NoMainDishMessage, UnityEditor.MessageType.Warning);
            }
        }

        private void DrawFoodCategorySelection(List<FoodItemConfig> allItems, FoodCategory category)
        {
            var items = allItems.Where(item => item.Category == category).ToList();
            if (items.Count == 0) return;

            EditorGUILayout.Space();
            EditorGUILayout.BeginHorizontal();
            // Fixed-width label for the same reason as the header above -- an expanding
            // one can crowd the buttons off the right edge of a narrow inspector.
            EditorGUILayout.LabelField(category.ToString(), UnityEditor.EditorStyles.boldLabel, UnityEngine.GUILayout.Width(60));
            var selectAll = UnityEngine.GUILayout.Button("All", UnityEngine.GUILayout.Width(45));
            var selectNone = UnityEngine.GUILayout.Button("None", UnityEngine.GUILayout.Width(45));
            UnityEngine.GUILayout.FlexibleSpace();
            EditorGUILayout.EndHorizontal();

            if (selectAll || selectNone)
            {
                foreach (var item in items) SetFoodAllowed(item, selectAll);
            }

            // Wraps to the width this grid is actually given, which is NOT
            // EditorGUIUtility.currentViewWidth: that is the whole window, including the Day
            // list on the left, so sizing against it asked for a row wider than the content
            // column and forced a horizontal scrollbar onto the entire inspector -- every
            // row, not just this one, since GUILayout widens the whole column to its widest
            // child. The column count is therefore derived from the width measured on the
            // last repaint, and the rect asks only for one tile's worth of width while
            // expanding into whatever is on offer, so it can never demand more than exists.
            var columns = UnityEngine.Mathf.Max(1, UnityEngine.Mathf.FloorToInt(lastFoodGridWidth / FoodTileStride));
            var rows = UnityEngine.Mathf.CeilToInt(items.Count / (float)columns);
            var gridRect = UnityEngine.GUILayoutUtility.GetRect(
                FoodTileStride, rows * FoodTileStride, UnityEngine.GUILayout.ExpandWidth(true));

            // Layout passes report a meaningless width; only repaints carry the real rect.
            // Converges on the first repaint after any resize, which IMGUI does continuously.
            if (UnityEngine.Event.current.type == UnityEngine.EventType.Repaint && gridRect.width > 1f)
            {
                lastFoodGridWidth = gridRect.width;
            }

            for (var i = 0; i < items.Count; i++)
            {
                var tileRect = new UnityEngine.Rect(
                    gridRect.x + i % columns * FoodTileStride,
                    gridRect.y + i / columns * FoodTileStride,
                    FoodTileSize, FoodTileSize);
                DrawFoodTile(tileRect, items[i]);
            }
        }

        // One clickable food tile. Selected reads two ways at once on purpose: the same
        // highlight DayEditorTicketCardPreview draws behind a selected ticket card, plus
        // full opacity while excluded tiles fade back -- a frame alone is hard to scan
        // across a row where only a few items are in the Day.
        private void DrawFoodTile(UnityEngine.Rect tileRect, FoodItemConfig item)
        {
            var isAllowed = EditorMeta.AllowedFoodItemIds.Contains(item.Id);

            if (isAllowed)
            {
                EditorGUI.DrawRect(
                    new UnityEngine.Rect(
                        tileRect.x - FoodTileHighlightMargin, tileRect.y - FoodTileHighlightMargin,
                        tileRect.width + FoodTileHighlightMargin * 2f, tileRect.height + FoodTileHighlightMargin * 2f),
                    FoodTileSelectedColor);
            }
            EditorGUI.DrawRect(tileRect, FoodTileBackgroundColor);

            var previousColor = UnityEngine.GUI.color;
            if (!isAllowed) UnityEngine.GUI.color = new UnityEngine.Color(1f, 1f, 1f, 0.3f) * previousColor;
            DayEditorSpriteGUI.DrawSpriteFit(tileRect, item.Sprite);
            UnityEngine.GUI.color = previousColor;

            // Sprites alone can be ambiguous (two drinks, two buns), so the name rides
            // along as a hover tooltip -- an empty-label GUI.Label draws nothing but does
            // register the tooltip region. Falls back to the id when DisplayName is unset.
            var name = string.IsNullOrEmpty(item.DisplayName) ? item.Id : item.DisplayName;
            UnityEngine.GUI.Label(tileRect, new UnityEngine.GUIContent(string.Empty, name));

            if (UnityEngine.Event.current.type == UnityEngine.EventType.MouseDown
                && UnityEngine.Event.current.button == 0
                && tileRect.Contains(UnityEngine.Event.current.mousePosition))
            {
                SetFoodAllowed(item, !isAllowed);
                UnityEngine.GUI.changed = true;
                UnityEngine.Event.current.Use();
            }
        }

        private void SetFoodAllowed(FoodItemConfig item, bool allowed)
        {
            if (!allowed)
            {
                EditorMeta.AllowedFoodItemIds.Remove(item.Id);
                return;
            }

            if (!EditorMeta.AllowedFoodItemIds.Contains(item.Id))
            {
                EditorMeta.AllowedFoodItemIds.Add(item.Id);
            }
        }

        // Delegates to DayContentGenerator rather than re-deriving the rule, so the pickers
        // and Generate can never disagree about what this Day is allowed to use. Null only
        // when there is no catalog to resolve against; the pickers treat that as "unfiltered".
        private IReadOnlyList<FoodItemConfig> AllowedFoodPool =>
            sharedCatalog == null ? null : DayContentGenerator.ResolveFoodPool(sharedCatalog, EditorMeta.ToJson());

        // TicketFactory.Create throws without a Main dish in the pool. That's the right
        // behaviour for a direct caller, but reaching it through a button click would be a
        // console exception instead of an answer, so both generate paths check first.
        private bool HasSelectableMainDish =>
            AllowedFoodPool?.Any(item => item != null && item.Category == FoodCategory.Main) ?? false;

        private const string NoMainDishMessage =
            "No Main dish is in this Day -- pick at least one Main under Food Selection before generating.";

        // Seeded wide enough to look sensible on the very first layout pass, then corrected
        // from the real rect on the first repaint (see DrawFoodSelection).
        private float lastFoodGridWidth = 600f;

        private const float FoodTileSize = 52f;
        private const float FoodTileSpacing = 6f;
        private const float FoodTileStride = FoodTileSize + FoodTileSpacing;
        private const float FoodTileHighlightMargin = 3f;
        // Same blue DayEditorTicketCardPreview highlights a selected ticket card with, so
        // "selected" means one thing across the whole Day Editor.
        private static readonly UnityEngine.Color FoodTileSelectedColor = new(0.3f, 0.6f, 1f, 1f);
        private static readonly UnityEngine.Color FoodTileBackgroundColor = new(0.22f, 0.22f, 0.22f, 1f);

        // Null for a Day that has never been saved under any filename yet (new or duplicated,
        // pre-first-Save) -- set to DayIndex by FromDayJson (loaded from an existing file) and
        // updated by DayEditorWindow after each successful save. Lets the Window detect a
        // DayIndex rename and delete the old file instead of leaving an orphan behind.
        [UnityEngine.HideInInspector]
        public int? LastSavedDayIndex;

        [Button("Generate"), PropertyOrder(-2)]
        public void Generate()
        {
            if (sharedCatalog == null || sharedTicketConfig == null)
            {
                EditorUtility.DisplayDialog("Generate", "Config assets aren't assigned yet -- set them in the toolbar above.", "OK");
                return;
            }

            if (!HasSelectableMainDish)
            {
                EditorUtility.DisplayDialog("Generate", NoMainDishMessage, "OK");
                return;
            }

            if (!EditorUtility.DisplayDialog("Generate", "This overwrites the current ticket sequence. Continue?", "Generate", "Cancel"))
            {
                return;
            }

            // No user-facing seed field -- every click gets its own fresh randomness, not
            // reproducible on purpose (nothing else persists a seed for this Day either).
            var seed = UnityEngine.Random.Range(int.MinValue, int.MaxValue);
            var result = DayContentGenerator.Generate(sharedCatalog, sharedTicketConfig, EditorMeta.ToJson(), TicketsRequiredForDay, seed);
            TicketSequence = result.Select(e => DayEditorTicketEntry.FromJson(e, sharedCatalog)).ToList();
        }

        [Button("Add Ticket"), FoldoutGroup("Ticket Sequence")]
        private void AddTicket() => TicketSequence.Add(new DayEditorTicketEntry());

        // Recomputed on every draw pass -- DayValidator is a cheap ticketSequence.Count check,
        // negligible for live feedback without a timer.
        private bool IsValid => Validate().IsValid;
        private bool HasValidationErrors => !IsValid;

        private string ValidationMessage
        {
            get
            {
                var errors = Validate().Errors;
                return errors.Count > 0 ? string.Join("\n", errors) : "Valid.";
            }
        }

        // Shown in their own box, separate from the error one: these never block Save (see
        // DayValidationResult.IsValid), so mixing them into the error message would make a
        // saveable Day look broken.
        private bool HasValidationWarnings => Validate().Warnings.Count > 0;
        private string ValidationWarningMessage => string.Join("\n", Validate().Warnings);

        private DayValidationResult Validate()
        {
            if (sharedGameConfig == null || sharedTicketConfig == null)
            {
                return new DayValidationResult(new List<string> { "Not configured yet -- assign the config assets above." });
            }

            return DayValidator.Validate(ToDayDefinition(), AllowedFoodPool);
        }

        [Button("Save"), EnableIf(nameof(IsValid)), GUIColor(0.4f, 0.85f, 0.4f, 1f)]
        public void Save() => onSaveRequested?.Invoke(this);

        [Button("Duplicate")]
        private void Duplicate() => onDuplicateRequested?.Invoke(this);

        [Button("Delete"), GUIColor(0.85f, 0.35f, 0.35f, 1f)]
        private void Delete() => onDeleteRequested?.Invoke(this);

        // Injected by DayEditorWindow right after construction -- shared config assets used for
        // Generate/Validate, and callbacks back into the Window's file-level operations. Not part
        // of the JSON, not serialized. Stored here (rather than passed as Generate's parameters)
        // specifically so [Button("Generate")] stays parameterless -- a parameterized Odin button
        // would render its own argument fields in the inspector instead of using the shared toolbar.
        private FoodCatalog sharedCatalog;
        private GameConfig sharedGameConfig;
        private TicketGenerationConfig sharedTicketConfig;
        private TicketCardVisualsConfig sharedTicketCardVisuals;
        private BoardVisualsConfig sharedBoardVisuals;
        private Action<DayEditorModel> onSaveRequested;
        private Action<DayEditorModel> onDuplicateRequested;
        private Action<DayEditorModel> onDeleteRequested;

        public void Configure(
            FoodCatalog catalog,
            GameConfig gameConfig,
            TicketGenerationConfig ticketConfig,
            TicketCardVisualsConfig ticketCardVisuals,
            BoardVisualsConfig boardVisuals,
            Action<DayEditorModel> onSave,
            Action<DayEditorModel> onDuplicate,
            Action<DayEditorModel> onDelete)
        {
            sharedCatalog = catalog;
            sharedGameConfig = gameConfig;
            sharedTicketConfig = ticketConfig;
            sharedTicketCardVisuals = ticketCardVisuals;
            sharedBoardVisuals = boardVisuals;
            onSaveRequested = onSave;
            onDuplicateRequested = onDuplicate;
            onDeleteRequested = onDelete;
        }

        public DayJson ToDayJson()
        {
            return new DayJson
            {
                runtime = new DayRuntimeJson
                {
                    dayIndex = DayIndex,
                    ticketsRequiredForDay = TicketsRequiredForDay,
                    boardDistribution = BoardDistribution.ToJson(),
                    ticketRuntime = TicketRuntime.ToJson(),
                    ticketSequence = TicketSequence.Select(e => e.ToJson()).ToArray(),
                    boardTimeline = BoardTimeline.Select(e => e.ToJson()).ToArray(),
                    star1Threshold = Star1Threshold,
                    star2Threshold = Star2Threshold,
                    star3Threshold = Star3Threshold,
                },
                editorMeta = EditorMeta.ToJson(),
            };
        }

        public DayDefinition ToDayDefinition()
        {
            var ticketSequence = TicketSequence.Select(e => e.ToResolved()).ToList();
            var boardTimeline = BoardTimeline.Select(e => e.ToResolved()).ToList();
            return new DayDefinition(DayIndex, TicketsRequiredForDay, ticketSequence, boardTimeline,
                Star1Threshold, Star2Threshold, Star3Threshold, BoardDistribution.ToResolved(), TicketRuntime.ToResolved());
        }

        public static DayEditorModel FromDayJson(DayJson json, FoodCatalog catalog)
        {
            var runtime = json?.runtime;
            var model = new DayEditorModel
            {
                DayIndex = runtime?.dayIndex ?? 0,
                TicketsRequiredForDay = runtime?.ticketsRequiredForDay ?? 10,
                BoardDistribution = DayEditorBoardDistribution.FromJson(runtime?.boardDistribution),
                TicketRuntime = DayEditorTicketRuntime.FromJson(runtime?.ticketRuntime),
                TicketSequence = (runtime?.ticketSequence ?? Array.Empty<TicketEntryJson>())
                    .Select(e => DayEditorTicketEntry.FromJson(e, catalog)).ToList(),
                BoardTimeline = (runtime?.boardTimeline ?? Array.Empty<BoardSpawnEntryJson>())
                    .Select(e => DayEditorBoardSpawnEntry.FromJson(e, catalog)).ToList(),
                Star1Threshold = runtime?.star1Threshold ?? 0,
                Star2Threshold = runtime?.star2Threshold ?? 0,
                Star3Threshold = runtime?.star3Threshold ?? 0,
                EditorMeta = DayEditorMetaModel.FromJson(json?.editorMeta, catalog),
            };

            return model;
        }

        // Round-trips through the same JSON conversion used for Save/Load rather than a second,
        // hand-written deep-copy -- the two paths can't drift out of sync with each other.
        public DayEditorModel Clone(FoodCatalog catalog) => FromDayJson(ToDayJson(), catalog);

        // Copies the three balancing blocks and nothing else, for seeding a brand-new Day
        // from the previous one (decisions.md D-007). Goes through JSON for the same reason
        // Clone does -- a hand-written field-by-field copy is a second place to forget a
        // field, which is exactly how the star thresholds were being lost before.
        public void CopySettingsFrom(DayEditorModel source)
        {
            BoardDistribution = DayEditorBoardDistribution.FromJson(source.BoardDistribution.ToJson());
            TicketRuntime = DayEditorTicketRuntime.FromJson(source.TicketRuntime.ToJson());
            EditorMeta.TicketGeneration = DayEditorTicketGeneration.FromJson(
                source.EditorMeta.TicketGeneration.ToJson(), sharedCatalog);
        }
    }

    [Serializable]
    public class DayEditorModification
    {
        public ModificationConfig Config;
        public bool IsAddition;

        public static DayEditorModification FromJson(ModificationEntryJson json, FoodCatalog catalog) => new()
        {
            Config = catalog.GetModificationById(json.modificationId),
            IsAddition = json.isAddition,
        };

        public ModificationEntryJson ToJson() => new()
        {
            modificationId = Config != null ? Config.Id : string.Empty,
            isAddition = IsAddition,
        };

        public Modification ToResolved() => new(Config, IsAddition);
    }

    [Serializable]
    public class DayEditorTicketEntry
    {
        public FoodItemConfig MainItem;
        public FoodItemConfig SideItem;
        public FoodItemConfig DrinkItem;
        public List<DayEditorModification> Modifications = new();
        public PatienceType PatienceType = PatienceType.Normal;
        public string CustomerNameOverride;
        public float TimeLimitSecondsOverride;

        public static DayEditorTicketEntry FromJson(TicketEntryJson json, FoodCatalog catalog) => new()
        {
            MainItem = catalog.GetById(json.mainItemId),
            SideItem = string.IsNullOrEmpty(json.sideItemId) ? null : catalog.GetById(json.sideItemId),
            DrinkItem = string.IsNullOrEmpty(json.drinkItemId) ? null : catalog.GetById(json.drinkItemId),
            Modifications = (json.modifications ?? Array.Empty<ModificationEntryJson>())
                .Select(m => DayEditorModification.FromJson(m, catalog)).ToList(),
            PatienceType = Enum.TryParse<PatienceType>(json.patienceType, out var patienceType) ? patienceType : PatienceType.Normal,
            CustomerNameOverride = json.customerNameOverride,
            TimeLimitSecondsOverride = json.timeLimitSecondsOverride,
        };

        // Maps a freshly-rolled runtime Ticket (TicketFactory.Create) into editor data --
        // used by DrawSelectedTicketEditor's "Generate Random Ticket" button. Deliberately
        // doesn't set CustomerNameOverride/TimeLimitSecondsOverride: those are Day Editor
        // authoring concepts TicketFactory has no notion of, left untouched by design.
        public static DayEditorTicketEntry FromTicket(Ticket ticket) => new()
        {
            MainItem = ticket.RequiredItems.FirstOrDefault(item => item.Category == FoodCategory.Main),
            SideItem = ticket.RequiredItems.FirstOrDefault(item => item.Category == FoodCategory.Side),
            DrinkItem = ticket.RequiredItems.FirstOrDefault(item => item.Category == FoodCategory.Drink),
            Modifications = ticket.Modifications.Select(m => new DayEditorModification { Config = m.Config, IsAddition = m.IsAddition }).ToList(),
            PatienceType = ticket.PatienceType,
        };

        public TicketEntryJson ToJson() => new()
        {
            mainItemId = MainItem != null ? MainItem.Id : string.Empty,
            sideItemId = SideItem != null ? SideItem.Id : string.Empty,
            drinkItemId = DrinkItem != null ? DrinkItem.Id : string.Empty,
            modifications = Modifications.Select(m => m.ToJson()).ToArray(),
            patienceType = PatienceType.ToString(),
            customerNameOverride = CustomerNameOverride,
            timeLimitSecondsOverride = TimeLimitSecondsOverride,
        };

        public ResolvedTicketEntry ToResolved()
        {
            var mods = Modifications.Select(m => m.ToResolved()).ToList();
            return new ResolvedTicketEntry(MainItem, SideItem, DrinkItem, mods, PatienceType, CustomerNameOverride, TimeLimitSecondsOverride);
        }
    }

    [Serializable]
    public class DayEditorBoardSpawnEntry
    {
        [HideInTables, LabelText("Trigger Step (-1 = Day Start)")]
        public int TriggerStepIndex = -1;

        public FoodItemConfig Item;
        public List<DayEditorModification> Modifications = new();
        [HideInTables] public bool UseExactCell;
        [HideInTables, ShowIf(nameof(UseExactCell))] public int X;
        [HideInTables, ShowIf(nameof(UseExactCell))] public int Y;

        public static DayEditorBoardSpawnEntry FromJson(BoardSpawnEntryJson json, FoodCatalog catalog) => new()
        {
            TriggerStepIndex = json.triggerStepIndex,
            Item = catalog.GetById(json.itemId),
            Modifications = (json.modifications ?? Array.Empty<ModificationEntryJson>())
                .Select(m => DayEditorModification.FromJson(m, catalog)).ToList(),
            UseExactCell = json.useExactCell,
            X = json.x,
            Y = json.y,
        };

        public BoardSpawnEntryJson ToJson() => new()
        {
            triggerStepIndex = TriggerStepIndex,
            itemId = Item != null ? Item.Id : string.Empty,
            modifications = Modifications.Select(m => m.ToJson()).ToArray(),
            useExactCell = UseExactCell,
            x = X,
            y = Y,
        };

        public ResolvedBoardSpawnEntry ToResolved()
        {
            var mods = Modifications.Select(m => m.ToResolved()).ToList();
            return new ResolvedBoardSpawnEntry(TriggerStepIndex, Item, mods, UseExactCell, X, Y);
        }
    }

    [Serializable]
    public class DayEditorMetaModel
    {

        // Drawn by DayEditorModel's own "Food Selection" section, not by Odin here: the UI
        // has to render a tile for every FoodCatalog item, and this plain serializable
        // model has no catalog reference (DayEditorModel does, via Configure). The data
        // still lives here so ToJson/FromJson stay the single place editor-meta is
        // serialized.
        [UnityEngine.HideInInspector] public List<string> AllowedFoodItemIds = new();

        [BoxGroup("Ticket Generation"), HideLabel]
        public DayEditorTicketGeneration TicketGeneration = new();

        public DayEditorMetaJson ToJson() => new()
        {
            allowedFoodItemIds = AllowedFoodItemIds.ToArray(),
            ticketGeneration = TicketGeneration.ToJson(),
        };

        public static DayEditorMetaModel FromJson(DayEditorMetaJson json, FoodCatalog catalog)
        {
            if (json == null) return new DayEditorMetaModel();

            return new DayEditorMetaModel
            {
                AllowedFoodItemIds = (json.allowedFoodItemIds ?? Array.Empty<string>()).ToList(),
                TicketGeneration = DayEditorTicketGeneration.FromJson(json.ticketGeneration, catalog),
            };
        }
    }

    // The settings this Day's ticketSequence was generated with. Authoring-only: nothing
    // here changes how an already-generated Day plays, it just drives the next Generate and
    // records what the last one used (D-006). Defaults mirror TicketGenerationConfig's
    // declared initializers so a brand-new Day starts somewhere sensible.
    [Serializable]
    public class DayEditorTicketGeneration
    {
        [PropertyRange(0f, 1f)] public float SideInclusionChance = 0.5f;
        [PropertyRange(0f, 1f)] public float DrinkInclusionChance = 0.5f;

        [PropertyRange(0f, 10f)]
        [UnityEngine.Tooltip("Fallback Poisson rate for a Main dish missing from Main Dish Weights below -- a dish listed there uses its own rate instead.")]
        public float ModificationCountLambda = MainDishWeight.DefaultModificationCountLambda;

        [PropertyRange(0f, 1f)]
        [UnityEngine.Tooltip("Only consulted for Both-direction modifications; AdditionOnly/RemovalOnly get their direction from the modification itself.")]
        public float ModificationAdditionChance = 0.5f;

        [UnityEngine.Tooltip("Relative spawn weight per Main dish. A Main this Day serves but does not list here falls back to weight 1.")]
        public List<DayEditorMainDishWeight> MainDishWeights = new();

        // A Day file predating this block returns the defaults above rather than zeros --
        // zeros would mean "never a side, never a drink, never a modification", which is a
        // silently empty-looking Generate rather than an obviously broken one.
        public static DayEditorTicketGeneration FromJson(TicketGenerationJson json, FoodCatalog catalog)
        {
            if (json == null) return new DayEditorTicketGeneration();

            return new DayEditorTicketGeneration
            {
                SideInclusionChance = json.sideInclusionChance,
                DrinkInclusionChance = json.drinkInclusionChance,
                ModificationCountLambda = json.modificationCountLambda,
                ModificationAdditionChance = json.modificationAdditionChance,
                MainDishWeights = (json.mainDishWeights ?? Array.Empty<MainDishWeightJson>())
                    .Select(w => DayEditorMainDishWeight.FromJson(w, catalog)).ToList(),
            };
        }

        public TicketGenerationJson ToJson() => new()
        {
            sideInclusionChance = SideInclusionChance,
            drinkInclusionChance = DrinkInclusionChance,
            modificationCountLambda = ModificationCountLambda,
            modificationAdditionChance = ModificationAdditionChance,
            mainDishWeights = MainDishWeights.Select(w => w.ToJson()).ToArray(),
        };
    }

    [Serializable]
    public class DayEditorMainDishWeight
    {
        // Held as a FoodItemConfig rather than a raw id so the Day Editor can offer an
        // object picker; ToJson writes the id back out, like every other food reference.
        public FoodItemConfig Food;
        public float Weight = MainDishWeight.DefaultWeight;
        [PropertyRange(0f, 10f)] public float ModificationCountLambda = MainDishWeight.DefaultModificationCountLambda;

        // Same tolerance as DayEditorTicketEntry.FromJson: an id whose FoodItemConfig is
        // gone leaves Food null (an empty slot in the editor) instead of dropping the row,
        // so the designer sees that something was there and can repoint it.
        public static DayEditorMainDishWeight FromJson(MainDishWeightJson json, FoodCatalog catalog) => new()
        {
            Food = string.IsNullOrEmpty(json.foodItemId) ? null : catalog.GetById(json.foodItemId),
            Weight = json.weight,
            ModificationCountLambda = json.modificationCountLambda,
        };

        public MainDishWeightJson ToJson() => new()
        {
            foodItemId = Food != null ? Food.Id : string.Empty,
            weight = Weight,
            modificationCountLambda = ModificationCountLambda,
        };
    }

    // The Day's own BoardDistributor balancing. Field defaults mirror
    // BoardDistributionConfig's declared initializers, so a brand-new Day starts from the
    // designed values rather than from CLR zeros -- which is the failure the shared asset
    // itself fell into (its YAML predates half these fields, leaving the urgent-ticket
    // guarantee and the arrival-weighted lottery switched off). Ranges mirror the config's
    // [Range] attributes so the editor cannot author a value the runtime would clamp away.
    [Serializable]
    public class DayEditorBoardDistribution
    {
        [PropertyRange(0f, 10f)] public float NoiseLeakCountLambda = 0.5f;
        // Buttons rather than a dropdown, and not only because two options read better that
        // way: Odin's dropdown popup calls UnityEditor.ContainerWindow.FitWindowRectToScreen,
        // which exists in Unity 6000.0 but was removed by 6000.1 (verified against both
        // UnityEditor.dll files). Odin 3.3.1.12 predates that removal, so opening this
        // dropdown threw MissingMethodException every time. A workaround, not a fix -- the
        // same popup path is reachable from other Odin controls, and the real repair is
        // updating Odin to a build that supports Unity 6.1+.
        [EnumToggleButtons]
        public GuaranteedTicketCountMode GuaranteedTicketCountMode = GuaranteedTicketCountMode.Manual;
        [PropertyRange(1, 3)] public int GuaranteedTicketCount = 1;
        // Ranges to 10 like the other two lambda sliders. Nothing clamps this value at
        // runtime -- it is a Poisson rate and TruncatedPoisson bounds the outcome -- so the
        // ceiling is purely how far the slider lets a designer push it. At 10 the budget
        // comes out 3 about 82% of the time; the curve approaches certainty but never gets
        // there (95% would need λ≈39, 99% needs λ≈199), so Manual mode with a count of 3 is
        // the tool for "always three", not a large lambda.
        [PropertyRange(0f, 10f)] public float GuaranteedTicketCountLambda = 1f;
        [PropertyRange(0f, 1f)] public float EarlyTicketWeightDecay = 0.5f;
        [PropertyRange(0f, 30f)] public float UrgentTimeThresholdSeconds = 10f;
        [PropertyRange(1, 10)] public int LeakDepth = 10;
        [PropertyRange(1, 10)] public int MaxLeakCount = 10;

        public static DayEditorBoardDistribution FromJson(BoardDistributionJson json)
        {
            // A Day file written before this block existed parses to an empty mode string;
            // falling back to a fresh instance gives the designed defaults instead of zeros.
            // DayCatalogParser refuses the same file at runtime -- the editor is deliberately
            // the more forgiving of the two, so an old Day can be opened and re-saved.
            if (json == null || string.IsNullOrEmpty(json.guaranteedTicketCountMode)) return new DayEditorBoardDistribution();

            return new DayEditorBoardDistribution
            {
                NoiseLeakCountLambda = json.noiseLeakCountLambda,
                GuaranteedTicketCountMode = Enum.TryParse<GuaranteedTicketCountMode>(json.guaranteedTicketCountMode, out var mode) ? mode : GuaranteedTicketCountMode.Manual,
                GuaranteedTicketCount = json.guaranteedTicketCount,
                GuaranteedTicketCountLambda = json.guaranteedTicketCountLambda,
                EarlyTicketWeightDecay = json.earlyTicketWeightDecay,
                UrgentTimeThresholdSeconds = json.urgentTimeThresholdSeconds,
                LeakDepth = json.leakDepth,
                MaxLeakCount = json.maxLeakCount,
            };
        }

        public BoardDistributionJson ToJson() => new()
        {
            noiseLeakCountLambda = NoiseLeakCountLambda,
            guaranteedTicketCountMode = GuaranteedTicketCountMode.ToString(),
            guaranteedTicketCount = GuaranteedTicketCount,
            guaranteedTicketCountLambda = GuaranteedTicketCountLambda,
            earlyTicketWeightDecay = EarlyTicketWeightDecay,
            urgentTimeThresholdSeconds = UrgentTimeThresholdSeconds,
            leakDepth = LeakDepth,
            maxLeakCount = MaxLeakCount,
        };

        public BoardDistributionSettings ToResolved() => new(
            NoiseLeakCountLambda, GuaranteedTicketCountMode, GuaranteedTicketCount, GuaranteedTicketCountLambda,
            EarlyTicketWeightDecay, UrgentTimeThresholdSeconds, LeakDepth, MaxLeakCount);
    }

    // This Day's play-time ticket balancing. Defaults mirror TicketGenerationConfig's
    // declared initializers so a brand-new Day starts from the designed values; the GDD
    // Section 8 ordering (Impatient < Normal < Patient) is a design rule, not enforced
    // here -- DayValidator is where that check belongs (day-config-plan.md step 6).
    [Serializable]
    public class DayEditorTicketRuntime
    {
        [UnityEngine.Min(1f)] public float ImpatientTimeLimitSeconds = 45f;
        [UnityEngine.Min(1f)] public float NormalTimeLimitSeconds = 90f;
        [UnityEngine.Min(1f)] public float PatientTimeLimitSeconds = 150f;

        [UnityEngine.Min(1)]
        [UnityEngine.Tooltip("How many tickets are pre-generated ahead of the 3 active slots. BoardDistributor's noise pool leaks from these, so LeakDepth above this is wasted reach.")]
        public int UpcomingQueueSize = 10;

        // Tolerates a Day file written before this block existed (all-zero, since
        // JsonUtility cannot produce null here) by falling back to the designed defaults,
        // so an old Day can still be opened and re-saved into a form the runtime accepts.
        // DayCatalogParser is the strict one; the editor is deliberately the forgiving one.
        public static DayEditorTicketRuntime FromJson(TicketRuntimeJson json)
        {
            if (json == null || json.impatientTimeLimitSeconds <= 0f) return new DayEditorTicketRuntime();

            return new DayEditorTicketRuntime
            {
                ImpatientTimeLimitSeconds = json.impatientTimeLimitSeconds,
                NormalTimeLimitSeconds = json.normalTimeLimitSeconds,
                PatientTimeLimitSeconds = json.patientTimeLimitSeconds,
                UpcomingQueueSize = json.upcomingQueueSize,
            };
        }

        public TicketRuntimeJson ToJson() => new()
        {
            impatientTimeLimitSeconds = ImpatientTimeLimitSeconds,
            normalTimeLimitSeconds = NormalTimeLimitSeconds,
            patientTimeLimitSeconds = PatientTimeLimitSeconds,
            upcomingQueueSize = UpcomingQueueSize,
        };

        public TicketRuntimeSettings ToResolved() => new(
            ImpatientTimeLimitSeconds, NormalTimeLimitSeconds, PatientTimeLimitSeconds, UpcomingQueueSize);
    }
}
