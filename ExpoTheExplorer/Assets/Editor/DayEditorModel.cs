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


        // Per-Day BoardDistributor balancing, replacing the shared BoardDistributionConfig
        // asset as the runtime authority. Odin draws this as a plain nested box for now;
        // grouping and seeding from the config asset land in a later step of
        // .claude/day-config-plan.md.
        //
        // Deliberately the LAST block in the inspector, right above Save/Duplicate/Delete:
        // it is the balancing pass you make after the Day's content exists, so it belongs at
        // the end of the authoring flow rather than between Generation Settings and the
        // ticket strip. Those three buttons carry no PropertyOrder, so they sit at the
        // default 0 and everything else in this class is negative -- which makes any value
        // between the lowest neighbour (-0.5, the selected-ticket editor) and 0 the bottom
        // slot. Both members of the group move together; splitting them would tear the
        // previews away from the knobs they visualise.
        [BoxGroup("Board Distribution"), HideLabel, PropertyOrder(-0.2f)]
        public DayEditorBoardDistribution BoardDistribution = new();

        // Drawn from THIS Day's values, which is the whole point: these two distributions
        // are what the balance knobs above actually mean, and before this they could only be
        // seen on the shared config asset's inspector -- i.e. never for the Day being tuned.
        [BoxGroup("Board Distribution"), OnInspectorGUI, PropertyOrder(-0.15f)]
        private void DrawBoardDistributionPreviews()
        {
            // Two columns, with no width given to either: GUILayout splits what is available.
            // Fixed widths are what made the whole inspector overflow sideways earlier in this
            // window's history (the food grid and the toolbar both did it), so none are used
            // here. The columns come out uneven -- the leak list runs to MaxLeakCount rows
            // while the guaranteed one is at most three -- which is fine.
            EditorGUILayout.BeginHorizontal();

            EditorGUILayout.BeginVertical();
            DayEditorSettingsPreviews.DrawLeakPreview(
                BoardDistribution.MaxLeakCount, BoardDistribution.NoiseLeakCountLambda);
            EditorGUILayout.EndVertical();

            EditorGUILayout.BeginVertical();
            DayEditorSettingsPreviews.DrawGuaranteedTicketPreview(
                BoardDistribution.GuaranteedTicketCountMode,
                BoardDistribution.GuaranteedTicketCount,
                BoardDistribution.GuaranteedTicketCountLambda);
            EditorGUILayout.EndVertical();

            EditorGUILayout.EndHorizontal();
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

            // Modifications sits directly under Main Item, and that is structural rather than
            // cosmetic: the picker is keyed on entry.MainItem, because TicketFactory pulls a
            // ticket's modifications from the main dish and nothing else. Putting it under the
            // dish it belongs to makes that relationship readable off the screen -- between
            // Side and Drink, as it was, it looked like it belonged to the whole ticket.
            entry.MainItem = DrawItemSlot("Main Item", entry.MainItem, FoodCategory.Main);

            EditorGUILayout.Space();
            DrawModificationPicker(entry.MainItem, entry.Modifications, "Main dish");

            EditorGUILayout.Space();
            entry.SideItem = DrawItemSlot("Side Item", entry.SideItem, FoodCategory.Side);
            entry.DrinkItem = DrawItemSlot("Drink Item", entry.DrinkItem, FoodCategory.Drink);

            EditorGUILayout.Space();
            entry.PatienceType = (PatienceType)EditorGUILayout.EnumPopup("Patience Type", entry.PatienceType);
            entry.CustomerNameOverride = EditorGUILayout.TextField("Name Override", entry.CustomerNameOverride);
            entry.TimeLimitSecondsOverride = EditorGUILayout.FloatField("Time Override", entry.TimeLimitSecondsOverride);

            EditorGUILayout.Space();
            if (UnityEngine.GUILayout.Button("Delete This Ticket"))
            {
                TicketSequence.RemoveAt(selectedTicketIndex);
                selectedTicketIndex = -1;
            }
        }

        // One of a ticket's three food slots, as pictures. Replaced a name popup (and, when no
        // catalog was assigned, a raw unfiltered ObjectField) -- which was the last ObjectField
        // in this file. The category filter and the AllowedFoodPool restriction are exactly the
        // ones that popup already applied: a Side can still never land in the Main slot, and a
        // food this Day did not select is still not on offer. Only the presentation changed.
        //
        // Unlike the Start Board cell picker, the grid stays OPEN when the slot is filled. That
        // is not an oversight: the common action on a ticket slot is SWITCHING (Hotdog to
        // Burger), and hiding the alternatives would make that two clicks instead of one. The
        // cell hides them because a filled cell hands its 220px of room to the modifications
        // below it, which a slot in this wide column does not need to do.
        private FoodItemConfig DrawItemSlot(string label, FoodItemConfig current, FoodCategory category)
        {
            EditorGUILayout.LabelField(label, UnityEditor.EditorStyles.boldLabel);

            var pool = AllowedFoodPool;
            if (pool == null)
            {
                EditorGUILayout.HelpBox("Food Catalog not assigned (toolbar above).", UnityEditor.MessageType.Info);
                return current;
            }

            var options = pool.Where(item => item != null && item.Category == category).ToList();
            if (options.Count == 0)
            {
                EditorGUILayout.HelpBox(
                    $"No {category} is in this Day -- pick one under Food Selection to offer it here.",
                    UnityEditor.MessageType.Info);
                return DrawSlotOrphanNotice(current, options);
            }

            var picked = current;
            DrawTileGrid(options.Count, ref lastTicketSlotGridWidth, (tileRect, i) =>
            {
                // Same toggle as the cell picker: clicking the highlighted tile clears the
                // slot, which is what took over from the popup's "None" entry. Side and Drink
                // are genuinely optional, so clearing has to stay reachable.
                if (DrawCellItemTile(tileRect, options[i], current))
                {
                    picked = current == options[i] ? null : options[i];
                }
            });

            return DrawSlotOrphanNotice(picked, options);
        }

        // A slot holding food this Day no longer serves -- authored earlier, or deselected from
        // Food Selection afterwards. The popup this replaced kept such a value visible by
        // injecting it into its own option list; surfacing it in a warning says the same thing
        // out loud, and matches how the cell picker and the modification picker report it. It is
        // never cleared for the designer: it still ships in the Day JSON, and DayValidator is
        // already refusing the Save over it.
        private static FoodItemConfig DrawSlotOrphanNotice(FoodItemConfig current, List<FoodItemConfig> options)
        {
            if (current == null || options.Contains(current)) return current;

            var name = string.IsNullOrEmpty(current.DisplayName) ? current.Id : current.DisplayName;
            EditorGUILayout.HelpBox(
                $"Holding {name}, which is not in this Day's food selection. Pick another above to replace it.",
                UnityEditor.MessageType.Warning);
            return current;
        }

        // Shared by all three ticket slots, unlike the food/modification/cell grids which each
        // keep their own: those sit in sections of different widths, while these three are
        // drawn one after another in the same column, so one measurement is the correct one.
        private float lastTicketSlotGridWidth = 600f;

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
            var newItem = DrawCellItemPicker(currentItem);

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
            DrawModificationPicker(entry.Item, entry.Modifications, "Item", hideWhenEmpty: true);

            EditorGUILayout.Space();
            if (UnityEngine.GUILayout.Button("Clear Cell"))
            {
                BoardTimeline.Remove(entry);
            }

            UnityEngine.GUILayout.EndVertical();
        }

        // What goes in the selected Start Board cell, picked by clicking a picture instead of
        // hunting through every FoodItemConfig in the project. Returns the item the cell
        // should hold: `current` when nothing was clicked, the clicked food, or null when the
        // already-selected tile is clicked again -- which is how the cell is emptied, taking
        // over from the old ObjectField's "None".
        //
        // Fed from AllowedFoodPool, so this Day can only put food on the board that it
        // actually selected. That is a fix rather than a side effect: the ObjectField this
        // replaces was completely unfiltered, so it could author a board item DayValidator
        // then refused (it checks the board against the same pool). Every category is offered
        // -- a board cell can hold a Main, a Side or a Drink -- grouped so a long catalog
        // stays readable.
        private FoodItemConfig DrawCellItemPicker(FoodItemConfig current)
        {
            var pool = AllowedFoodPool;
            if (pool == null)
            {
                EditorGUILayout.HelpBox("Food Catalog not assigned (toolbar above).", UnityEditor.MessageType.Info);
                return current;
            }

            var options = pool.Where(item => item != null).ToList();
            if (options.Count == 0)
            {
                EditorGUILayout.HelpBox(
                    "No food is in this Day yet -- pick some under Food Selection first.", UnityEditor.MessageType.Info);
                return current;
            }

            // Two states, and the cell itself is what selects between them -- there is no flag
            // to keep in sync and nothing to reset. A FILLED cell shows only what is in it, so
            // the picker gets out of the way and leaves the room to that item's modifications;
            // an EMPTY one shows the whole larder again. Emptying therefore needs no "go back"
            // path of its own: clicking the tile again and the Clear Cell button both leave the
            // cell empty, and the next draw is the grid.
            var picked = current;

            if (current != null)
            {
                EditorGUILayout.LabelField("Click it again to empty the cell", UnityEditor.EditorStyles.miniLabel);
                DrawTileGrid(1, ref lastCellGridWidth, (tileRect, _) =>
                {
                    if (DrawCellItemTile(tileRect, current, current)) picked = null;
                });
            }
            else
            {
                EditorGUILayout.LabelField("Click a food to place it here", UnityEditor.EditorStyles.miniLabel);

                foreach (var category in CellPickerCategories)
                {
                    var inCategory = options.Where(item => item.Category == category).ToList();
                    if (inCategory.Count == 0) continue;

                    EditorGUILayout.LabelField(category.ToString(), UnityEditor.EditorStyles.miniBoldLabel);
                    DrawTileGrid(inCategory.Count, ref lastCellGridWidth, (tileRect, i) =>
                    {
                        // The cell is empty in this branch, so a click can only ever be a pick --
                        // the toggle-to-empty case lives in the branch above.
                        if (DrawCellItemTile(tileRect, inCategory[i], null)) picked = inCategory[i];
                    });
                }
            }

            // An item the Day no longer serves: authored earlier, or deselected from Food
            // Selection afterwards. Surfaced rather than silently cleared -- it still ships in
            // the Day JSON, and DayValidator is already complaining about it separately.
            if (current != null && !options.Contains(current))
            {
                var name = string.IsNullOrEmpty(current.DisplayName) ? current.Id : current.DisplayName;
                EditorGUILayout.HelpBox(
                    $"This cell holds {name}, which is not in this Day's food selection. "
                    + "Pick a food above to replace it, or use Clear Cell.",
                    UnityEditor.MessageType.Warning);
            }

            return picked;
        }

        // Returns whether this tile was clicked. Single-select, so "selected" is just identity
        // against the cell's current item rather than membership in a list.
        private bool DrawCellItemTile(UnityEngine.Rect tileRect, FoodItemConfig item, FoodItemConfig current)
        {
            var isSelected = item == current;

            if (isSelected)
            {
                EditorGUI.DrawRect(
                    new UnityEngine.Rect(
                        tileRect.x - FoodTileHighlightMargin, tileRect.y - FoodTileHighlightMargin,
                        tileRect.width + FoodTileHighlightMargin * 2f, tileRect.height + FoodTileHighlightMargin * 2f),
                    FoodTileSelectedColor);
            }
            EditorGUI.DrawRect(tileRect, FoodTileBackgroundColor);

            var previousColor = UnityEngine.GUI.color;
            if (!isSelected) UnityEngine.GUI.color = new UnityEngine.Color(1f, 1f, 1f, 0.3f) * previousColor;
            DayEditorSpriteGUI.DrawSpriteFit(tileRect, item.Sprite);
            UnityEngine.GUI.color = previousColor;

            var name = string.IsNullOrEmpty(item.DisplayName) ? item.Id : item.DisplayName;
            UnityEngine.GUI.Label(tileRect, new UnityEngine.GUIContent(string.Empty, name));

            if (UnityEngine.Event.current.type != UnityEngine.EventType.MouseDown
                || UnityEngine.Event.current.button != 0
                || !tileRect.Contains(UnityEngine.Event.current.mousePosition))
            {
                return false;
            }

            UnityEngine.GUI.changed = true;
            UnityEngine.Event.current.Use();
            return true;
        }

        // Presentation order for the cell picker, written out rather than iterating the enum
        // so it reads Main first the way a dish does on a ticket card.
        private static readonly FoodCategory[] CellPickerCategories =
        {
            FoodCategory.Main, FoodCategory.Side, FoodCategory.Drink,
        };

        // The cell picker's own width measurement. It matters more here than for the other two
        // grids: this one is drawn inside a fixed 220px column, so it settles on ~3 columns
        // while the others get the full content width.
        private float lastCellGridWidth = 220f;

        private DayEditorBoardSpawnEntry FindStartBoardEntry(int x, int y) =>
            BoardTimeline.FirstOrDefault(e => e.TriggerStepIndex == -1 && e.UseExactCell && e.X == x && e.Y == y);

        // Only TriggerStepIndex == -1 (Day Start) entries are ever replayed at runtime
        // (BoardDistributor took over everything after Day Start, see decisions.md D-001
        // Phase 2/3). Authored entirely through the Start Board grid's click-to-place
        // editor above now -- no table, no separate "Add Board Spawn" button.
        [UnityEngine.HideInInspector]
        public List<DayEditorBoardSpawnEntry> BoardTimeline = new();

        // CARRIED, NOT EDITED -- the raw JSON block, held so Save writes back exactly what
        // Load read. It exists because ToDayJson rebuilds the runtime section field by field
        // rather than mutating what it loaded: any runtime field this model does not name is
        // silently DELETED the first time a Day is opened here and saved, which for the
        // tutorial would mean day_00 quietly losing its forced first move because someone
        // retimed a ticket. There is no editor UI for it yet (D-082 authors day_00's block
        // by hand); this is the pass-through that makes that safe, and the place a real
        // inspector would hang off later. HideInInspector for the same reason BoardTimeline
        // is: showing a raw serializable here would invite editing it without validation.
        [UnityEngine.HideInInspector]
        public TutorialJson Tutorial;

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
                    .Select(w => (w.Food, w.Weight, w.ModificationCountLambda, w.MaxModificationCount)).ToList(),
                sharedCatalog != null ? AllowedFoodPool : null);

        // What the three Patience Mix counts will ACTUALLY produce. Lives on the model rather
        // than beside the counts because it needs TicketsRequiredForDay, which
        // DayEditorTicketGeneration has no access to -- same split as the food selection.
        //
        // This readout is the whole reason the counts are allowed to disagree with the ticket
        // count: the padding and the tail-truncation are decided by
        // DayContentGenerator.BuildPatiencePlan, and asking that same function what it would
        // do is what keeps the answer from drifting away from what Generate really does. A
        // second copy of the arithmetic here would be a preview that lies.
        [FoldoutGroup("Generation Settings (authoring only)"), OnInspectorGUI, PropertyOrder(-2.85f)]
        private void DrawPatienceMixReadout()
        {
            var plan = DayContentGenerator.BuildPatiencePlan(
                EditorMeta.TicketGeneration.PatientTicketCount,
                EditorMeta.TicketGeneration.NormalTicketCount,
                EditorMeta.TicketGeneration.ImpatientTicketCount,
                TicketsRequiredForDay);

            if (plan.Count == 0)
            {
                EditorGUILayout.HelpBox(
                    "Patience Mix is unset -- Generate rolls each ticket's patience at random, as it always did. "
                    + "Set any of the three counts above to author the mix instead.",
                    UnityEditor.MessageType.Info);
                return;
            }

            var patient = plan.Count(p => p == PatienceType.Patient);
            var normal = plan.Count(p => p == PatienceType.Normal);
            var impatient = plan.Count(p => p == PatienceType.Impatient);

            var authored = EditorMeta.TicketGeneration.PatientTicketCount
                           + EditorMeta.TicketGeneration.NormalTicketCount
                           + EditorMeta.TicketGeneration.ImpatientTicketCount;

            var effective = $"Generate will lay down {patient} Patient, then {normal} Normal, then {impatient} Impatient.";

            if (authored == TicketsRequiredForDay)
            {
                EditorGUILayout.HelpBox($"{authored} / {TicketsRequiredForDay} tickets. {effective}", UnityEditor.MessageType.Info);
                return;
            }

            var reason = authored < TicketsRequiredForDay
                ? $"{TicketsRequiredForDay - authored} short -- the rest come in as Normal."
                : $"{authored - TicketsRequiredForDay} over -- the excess is cut off the end, so Impatient goes first.";

            EditorGUILayout.HelpBox(
                $"{authored} authored vs {TicketsRequiredForDay} tickets this Day: {reason}\n{effective}",
                UnityEditor.MessageType.Warning);
        }

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

            // The one place the derived Main Dish Weights list is kept in step with the
            // selection above. Here rather than in the writers (SetFoodAllowed, FromDayJson,
            // CopySettingsFrom) because this is the section that OWNS the input and, at
            // PropertyOrder -5, it draws before the Generation Settings block at -3 -- so
            // whatever Odin renders below is already reconciled, on every path there is:
            // opening a file, clicking a tile, the All/None buttons, a catalog assigned late
            // in the toolbar, or a new Day seeded from the previous one. A future fourth
            // writer is covered for free, which three hand-placed call sites would not be.
            // Free to sit in a draw method because the sync is idempotent: when the list
            // already matches, it compares and returns without allocating (see the method).
            EditorMeta.SyncMainDishWeights(sharedCatalog);

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
            DrawTileGrid(items.Count, ref lastFoodGridWidth, (tileRect, i) => DrawFoodTile(tileRect, items[i]));
        }

        // The geometry every tile grid in this window shares: how many columns fit, how tall
        // the block has to be, and where each tile lands. Extracted when the third grid
        // arrived (Food Selection, the modification picker, the Start Board cell picker),
        // because THIS is the code with the bug history documented above -- keeping three
        // copies of it would scatter that lesson across three places, and the next person to
        // reach for EditorGUIUtility.currentViewWidth would only be corrected in one of them.
        //
        // Per-tile drawing deliberately stays with each caller: they do genuinely different
        // work (toggling a selection, drawing a direction badge, single-select), and only the
        // measuring is the same.
        //
        // `lastWidth` is per-grid and passed by reference for a reason -- these grids sit in
        // sections of DIFFERENT widths (the cell picker lives inside a fixed 220px column),
        // so one shared measurement would have each overwrite the other's column count every
        // repaint. Nothing here may assume the full inspector width.
        private static void DrawTileGrid(int count, ref float lastWidth, Action<UnityEngine.Rect, int> drawTile)
        {
            var columns = UnityEngine.Mathf.Max(1, UnityEngine.Mathf.FloorToInt(lastWidth / FoodTileStride));
            var rows = UnityEngine.Mathf.CeilToInt(count / (float)columns);
            var gridRect = UnityEngine.GUILayoutUtility.GetRect(
                FoodTileStride, rows * FoodTileStride, UnityEngine.GUILayout.ExpandWidth(true));

            // Layout passes report a meaningless width; only repaints carry the real rect.
            // Converges on the first repaint after any resize, which IMGUI does continuously.
            if (UnityEngine.Event.current.type == UnityEngine.EventType.Repaint && gridRect.width > 1f)
            {
                lastWidth = gridRect.width;
            }

            for (var i = 0; i < count; i++)
            {
                drawTile(
                    new UnityEngine.Rect(
                        gridRect.x + i % columns * FoodTileStride,
                        gridRect.y + i / columns * FoodTileStride,
                        FoodTileSize, FoodTileSize),
                    i);
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

        // The modification editor for one ticket or one Start Board cell. Both used to carry
        // their own byte-identical copy of an unfiltered ObjectField plus a "+ Add
        // Modification" button that appended a blank row; one helper now serves both, so the
        // rule cannot be fixed in one and left wrong in the other.
        //
        // The options come from the OWNING FOOD, which is the same authority TicketFactory
        // reads (Create pulls from main.AvailableModifications and nothing else). The old
        // picker offered every ModificationConfig in the project, so a Hotdog could be given
        // a Burger's modification -- data the generator could never produce and the game
        // would show as a topping the dish has no sprite layer for.
        //
        // Drawn as a tile grid on the Food Selection grid's conventions (same stride,
        // highlight, fade and tooltip) because it is the same gesture: click a picture to
        // put it on this thing. `ownerLabel` is what the empty-state message calls the food
        // -- "Main dish" for a ticket, "Item" for a board cell.
        //
        // `hideWhenEmpty` draws NOTHING -- not even the header -- when the owning food offers
        // no modifications. The Start Board cell editor passes it because a filled cell should
        // show its item and, only if there is something to say, its modifications; a box
        // reading "this food has no modifications" is noise in a panel that narrow. The ticket
        // editor leaves it at false, where the same box is useful advice (it names the
        // FoodItemConfig to go and author them on), so that side is unchanged by construction.
        //
        // It does NOT silence orphans. Modifications the owning food no longer offers are
        // surfaced either way: D-054 made them visible precisely because they still ship in
        // the Day JSON, and quieting them to tidy up an empty state would undo that decision
        // sideways. "Show nothing" means nothing to show, not nothing to admit.
        private void DrawModificationPicker(
            FoodItemConfig owner, List<DayEditorModification> mods, string ownerLabel, bool hideWhenEmpty = false)
        {
            var available = owner != null
                ? owner.AvailableModifications.Where(m => m != null).ToList()
                : new List<ModificationConfig>();

            if (hideWhenEmpty && available.Count == 0)
            {
                DrawOrphanModifications(owner, mods);
                return;
            }

            EditorGUILayout.LabelField("Modifications", UnityEditor.EditorStyles.boldLabel);

            if (owner == null)
            {
                EditorGUILayout.HelpBox(
                    $"Assign a {ownerLabel} first -- modifications come from the food itself.",
                    UnityEditor.MessageType.Info);
                DrawOrphanModifications(owner, mods);
                return;
            }

            var ownerName = string.IsNullOrEmpty(owner.DisplayName) ? owner.Id : owner.DisplayName;
            if (available.Count == 0)
            {
                EditorGUILayout.HelpBox(
                    $"{ownerName} has no modifications authored -- add them to its FoodItemConfig first.",
                    UnityEditor.MessageType.Info);
                DrawOrphanModifications(owner, mods);
                return;
            }

            EditorGUILayout.LabelField(
                $"Click a modification {ownerName} offers. Right-click a two-way one to flip +/-.",
                UnityEditor.EditorStyles.miniLabel);

            // Own width field, not the food grid's -- this one is drawn inside a foldout that
            // can be a different width from the Food Selection block (see DrawTileGrid).
            DrawTileGrid(available.Count, ref lastModGridWidth, (tileRect, i) => DrawModificationTile(tileRect, available[i], mods));

            DrawOrphanModifications(owner, mods);
        }

        // One clickable modification tile: the icon the game itself draws, plus -- when this
        // modification is on the entry -- the same +/- badge DayEditorTicketCardPreview puts
        // on a card, so the direction is READ off the tile rather than off a checkbox.
        private void DrawModificationTile(
            UnityEngine.Rect tileRect, ModificationConfig config, List<DayEditorModification> mods)
        {
            var existing = mods.FirstOrDefault(m => m != null && m.Config == config);
            var isOn = existing != null;

            if (isOn)
            {
                EditorGUI.DrawRect(
                    new UnityEngine.Rect(
                        tileRect.x - FoodTileHighlightMargin, tileRect.y - FoodTileHighlightMargin,
                        tileRect.width + FoodTileHighlightMargin * 2f, tileRect.height + FoodTileHighlightMargin * 2f),
                    FoodTileSelectedColor);
            }
            EditorGUI.DrawRect(tileRect, FoodTileBackgroundColor);

            var previousColor = UnityEngine.GUI.color;
            if (!isOn) UnityEngine.GUI.color = new UnityEngine.Color(1f, 1f, 1f, 0.3f) * previousColor;
            DayEditorSpriteGUI.DrawSpriteFit(tileRect, config.Icon);
            UnityEngine.GUI.color = previousColor;

            if (isOn) DrawDirectionBadge(tileRect, existing.IsAddition);

            var name = string.IsNullOrEmpty(config.DisplayName) ? config.Id : config.DisplayName;
            var tip = DayEditorModification.CanFlipDirection(config)
                ? $"{name} (two-way -- right-click to flip +/-)"
                : $"{name} ({config.AllowedDirection})";
            UnityEngine.GUI.Label(tileRect, new UnityEngine.GUIContent(string.Empty, tip));

            if (UnityEngine.Event.current.type != UnityEngine.EventType.MouseDown
                || !tileRect.Contains(UnityEngine.Event.current.mousePosition))
            {
                return;
            }

            // Left toggles membership, right flips direction -- the same split the Start Board
            // grid uses (left relocates, right selects), so the two grids read the same way.
            if (UnityEngine.Event.current.button == 0)
            {
                if (isOn) mods.Remove(existing);
                else mods.Add(DayEditorModification.ForConfig(config));
                UnityEngine.GUI.changed = true;
                UnityEngine.Event.current.Use();
            }
            else if (UnityEngine.Event.current.button == 1 && isOn && DayEditorModification.CanFlipDirection(config))
            {
                existing.IsAddition = !existing.IsAddition;
                UnityEngine.GUI.changed = true;
                UnityEngine.Event.current.Use();
            }
        }

        // The badge sits bottom-centre and overlaps the tile's edge, matching the card
        // preview's placement. Falls back to a "+"/"-" label when TicketCardVisualsConfig is
        // unassigned: the direction is the one thing on this tile that must never be
        // invisible, and a missing sprite would otherwise draw nothing at all.
        private void DrawDirectionBadge(UnityEngine.Rect tileRect, bool isAddition)
        {
            var badgeSize = FoodTileSize * 0.4f;
            var badgeRect = new UnityEngine.Rect(
                tileRect.center.x - badgeSize / 2f, tileRect.yMax - badgeSize * 0.75f, badgeSize, badgeSize);

            var sprite = sharedTicketCardVisuals != null
                ? (isAddition ? sharedTicketCardVisuals.AdditionSprite : sharedTicketCardVisuals.RemovalSprite)
                : null;

            if (sprite != null)
            {
                DayEditorSpriteGUI.DrawSpriteFit(badgeRect, sprite);
                return;
            }

            EditorGUI.DrawRect(badgeRect, FoodTileBackgroundColor);
            UnityEngine.GUI.Label(badgeRect, isAddition ? "+" : "-", DirectionFallbackStyle);
        }

        // Modifications the entry carries that the owning food does not offer -- authored
        // before this picker existed, or left behind when the food was changed afterwards.
        // Shown rather than silently dropped, the same call the Main-dish-weight rows make
        // for a deleted FoodItemConfig: the designer has to be able to see that something is
        // there, and it does ship in the Day JSON until it is removed.
        private void DrawOrphanModifications(FoodItemConfig owner, List<DayEditorModification> mods)
        {
            var offered = owner != null
                ? owner.AvailableModifications.Where(m => m != null).ToList()
                : new List<ModificationConfig>();
            var orphans = mods.Where(m => m == null || !offered.Contains(m.Config)).ToList();
            if (orphans.Count == 0) return;

            EditorGUILayout.Space();
            EditorGUILayout.HelpBox(
                owner == null
                    ? "This entry carries modifications but has no food to check them against."
                    : "Not offered by this food -- left over from an earlier edit. These still ship in the Day JSON.",
                UnityEditor.MessageType.Warning);

            foreach (var orphan in orphans)
            {
                EditorGUILayout.BeginHorizontal();
                // Same name-or-id fallback as every other food/modification label in this
                // file -- an unset DisplayName is an empty string, not a null, so `??` would
                // have shown a blank row.
                var label = orphan?.Config != null
                    ? $"{(string.IsNullOrEmpty(orphan.Config.DisplayName) ? orphan.Config.Id : orphan.Config.DisplayName)} ({(orphan.IsAddition ? "+" : "-")})"
                    : "<missing modification asset>";
                EditorGUILayout.LabelField(label);
                var removeClicked = UnityEngine.GUILayout.Button("x", UnityEngine.GUILayout.Width(20));
                EditorGUILayout.EndHorizontal();

                if (removeClicked)
                {
                    mods.Remove(orphan);
                    break; // list mutated mid-loop -- the next OnGUI pass redraws the rest
                }
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

        // The modification grid's own measurement, deliberately not shared with the food
        // grid's: the two are drawn in different sections whose widths need not match, and
        // one field would let each overwrite the other's column count every repaint.
        private float lastModGridWidth = 600f;

        // Centred, so the fallback "+"/"-" sits where the badge sprite would have. Built
        // lazily rather than in a field initializer because EditorStyles is not available
        // while this object is being constructed.
        private static UnityEngine.GUIStyle directionFallbackStyle;
        private static UnityEngine.GUIStyle DirectionFallbackStyle =>
            directionFallbackStyle ??= new UnityEngine.GUIStyle(UnityEditor.EditorStyles.boldLabel)
            {
                alignment = UnityEngine.TextAnchor.MiddleCenter,
            };

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
                    tutorial = Tutorial,
                },
                editorMeta = EditorMeta.ToJson(),
            };
        }

        public DayDefinition ToDayDefinition()
        {
            var ticketSequence = TicketSequence.Select(e => e.ToResolved()).ToList();
            var boardTimeline = BoardTimeline.Select(e => e.ToResolved()).ToList();
            // Resolved through the runtime parser's own method rather than re-implemented
            // here, so the Day Editor's validation preview cannot disagree with what the
            // game will actually load. This is what makes DayValidator's tutorial rule fire
            // on the Save button instead of only at runtime.
            return new DayDefinition(DayIndex, TicketsRequiredForDay, ticketSequence, boardTimeline,
                BoardDistribution.ToResolved(), TicketRuntime.ToResolved(),
                DayCatalogParser.ResolveTutorial(Tutorial));
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
                Tutorial = runtime?.tutorial,
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

        // Hand-authoring a modification goes through here so the editor cannot produce a
        // pair TicketFactory never would. Direction is intrinsic to the modification type
        // (ModificationConfig.AllowedDirection), and TicketFactory.CreateModification reads
        // it exactly this way -- only Both is a free choice there (a coin flip) and here (the
        // designer's right-click). The old "+ Add Modification" button made a blank entry
        // with IsAddition left at false, which authored "remove Extra Ketchup" for an
        // AdditionOnly modification: valid data, impossible content.
        //
        // Both defaults to addition rather than removal only because it has to default to
        // something and a positive reads as the friendlier first guess; CanFlipDirection is
        // what makes it a choice rather than a decision.
        public static DayEditorModification ForConfig(ModificationConfig config) => new()
        {
            Config = config,
            IsAddition = config == null || config.AllowedDirection != ModificationDirection.RemovalOnly,
        };

        // Only a Both-direction modification can be flipped; the other two carry their
        // direction in the type, so offering a toggle would be offering to author a lie.
        public static bool CanFlipDirection(ModificationConfig config) =>
            config != null && config.AllowedDirection == ModificationDirection.Both;

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

        // Makes TicketGeneration.MainDishWeights hold exactly the Mains this Day serves.
        // Lives on THIS class because it owns both sides of the derivation -- the selected
        // ids and the weights list; DayEditorTicketGeneration has no idea what the Day
        // serves, and DayEditorModel would only be forwarding. Called from the Food
        // Selection draw, the single place the input can change (D-052).
        //
        // Three rules, in the order they matter:
        //   - an existing row is CARRIED OVER by reference, so the weight, lambda and
        //     ceiling a designer tuned survive every re-sync -- only membership is derived,
        //     never the numbers in it;
        //   - a newly picked Main enters at the shared MainDishWeight defaults, the same
        //     values a hand-added row used to get;
        //   - a row whose dish is no longer picked is dropped, and so is one whose Food came
        //     back null (its FoodItemConfig was deleted). Dropping is safe rather than data
        //     loss: TicketFactory only ever picks from the Day's food pool, which is built
        //     from AllowedFoodItemIds, so a weight for an unpicked dish was never read.
        //
        // Catalog order, not selection order, so the list reads in the same sequence as the
        // tile grid above it -- and so the comparison below can be positional.
        public void SyncMainDishWeights(FoodCatalog catalog)
        {
            if (catalog == null) return;

            var weights = TicketGeneration.MainDishWeights;
            if (MainDishWeightsMatch(catalog, weights)) return;

            var synced = new List<DayEditorMainDishWeight>();
            for (var i = 0; i < catalog.Items.Count; i++)
            {
                var item = catalog.Items[i];
                if (!IsSelectedMain(item)) continue;

                synced.Add(weights.FirstOrDefault(w => w.Food == item)
                           ?? new DayEditorMainDishWeight { Food = item });
            }

            TicketGeneration.MainDishWeights = synced;
        }

        // The early-out that lets the sync sit in a draw method: an inspector repaints
        // continuously, and the list is already correct on all but the few frames where the
        // selection just changed. Indexed loops and no LINQ on purpose -- this is the path
        // that runs every repaint, while the rebuild above runs only when something moved.
        private bool MainDishWeightsMatch(FoodCatalog catalog, List<DayEditorMainDishWeight> weights)
        {
            var matched = 0;
            for (var i = 0; i < catalog.Items.Count; i++)
            {
                if (!IsSelectedMain(catalog.Items[i])) continue;
                if (matched >= weights.Count || weights[matched].Food != catalog.Items[i]) return false;
                matched++;
            }

            // Anything left over is a row for a dish that is no longer picked.
            return matched == weights.Count;
        }

        private bool IsSelectedMain(FoodItemConfig item) =>
            item != null && item.Category == FoodCategory.Main && AllowedFoodItemIds.Contains(item.Id);

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

        // Kept, and kept in the JSON, even though the sync now makes it unreachable on this
        // Day's own Generate: every Main in the pool has a row of its own, so TicketFactory
        // never reaches the fallback. The field still travels to the runtime config clone,
        // and dropping it would be a Day-JSON schema change for a value that costs nothing.
        [PropertyRange(0f, 10f)]
        [UnityEngine.Tooltip("Fallback Poisson rate for a Main dish with no row in Main Dish Weights below. Since that list now covers every Main this Day serves, Generate does not consult this -- each dish uses its own rate.")]
        public float ModificationCountLambda = MainDishWeight.DefaultModificationCountLambda;

        [PropertyRange(0f, 1f)]
        [UnityEngine.Tooltip("Only consulted for Both-direction modifications; AdditionOnly/RemovalOnly get their direction from the modification itself.")]
        public float ModificationAdditionChance = 0.5f;

        // Membership is DERIVED from Food Selection (DayEditorMetaModel.SyncMainDishWeights),
        // so the add/remove buttons are gone: with them, "which Mains are listed" would have
        // two writers, and the sync would silently undo whichever one the designer used last.
        // Each row's weight/lambda/max stays hand-authored -- that is the content this list
        // exists to carry, and the sync never touches it.
        // How many tickets of each patience type Generate lays down, in the order it lays
        // them down. All three at 0 is UNAUTHORED, not an empty Day: DayContentGenerator
        // falls back to the uniform roll then, which is what every Day did before this
        // existed. Counts are checked against TicketsRequiredForDay rather than replacing it
        // -- that stays the single authority for a Day's length -- and the readout under
        // these three shows what Generate will actually produce.
        [BoxGroup("Patience Mix"), LabelText("Patient"), MinValue(0)]
        public int PatientTicketCount;

        [BoxGroup("Patience Mix"), LabelText("Normal"), MinValue(0)]
        public int NormalTicketCount;

        [BoxGroup("Patience Mix"), LabelText("Impatient"), MinValue(0)]
        public int ImpatientTicketCount;

        [UnityEngine.Tooltip("Relative spawn weight per Main dish. Filled automatically from the Mains picked under Food Selection -- pick a Main there to add a row, deselect it to remove one.")]
        [ListDrawerSettings(HideAddButton = true, HideRemoveButton = true, DraggableItems = false)]
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
                PatientTicketCount = json.patientTicketCount,
                NormalTicketCount = json.normalTicketCount,
                ImpatientTicketCount = json.impatientTicketCount,
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
            patientTicketCount = PatientTicketCount,
            normalTicketCount = NormalTicketCount,
            impatientTicketCount = ImpatientTicketCount,
            modificationCountLambda = ModificationCountLambda,
            modificationAdditionChance = ModificationAdditionChance,
            mainDishWeights = MainDishWeights.Select(w => w.ToJson()).ToArray(),
        };
    }

    [Serializable]
    public class DayEditorMainDishWeight
    {
        // Held as a FoodItemConfig rather than a raw id because that is what the sync
        // resolves the selected ids to, and what ToJson writes back out as an id, like every
        // other food reference. Read-only rather than an object picker since D-052: the row
        // exists BECAUSE this dish is in Food Selection, so repointing it here would either
        // be reverted by the next sync or create a weight for a dish the Day never serves.
        [ReadOnly] public FoodItemConfig Food;
        public float Weight = MainDishWeight.DefaultWeight;
        [PropertyRange(0f, 10f)] public float ModificationCountLambda = MainDishWeight.DefaultModificationCountLambda;

        // Directly under the lambda on purpose: the two are one setting read together --
        // the rate and the ceiling it is truncated at. Ranges from 1, not 0, for the same
        // reason MaxLeakCount does: 0 is reserved as "this Day file predates the field"
        // (see MainDishWeight.NormalizeMaxModificationCount). "Never any modifications"
        // is authored as lambda 0, which this list already supports.
        [PropertyRange(1, 10)]
        [UnityEngine.Tooltip("Hard ceiling on this dish's modification count. The effective ceiling is the smaller of this and the dish's own available modifications, so raising it past that does nothing.")]
        public int MaxModificationCount = MainDishWeight.DefaultMaxModificationCount;

        // Same tolerance as DayEditorTicketEntry.FromJson: an id whose FoodItemConfig is
        // gone leaves Food null (an empty slot in the editor) instead of dropping the row,
        // so the designer sees that something was there and can repoint it.
        public static DayEditorMainDishWeight FromJson(MainDishWeightJson json, FoodCatalog catalog) => new()
        {
            Food = string.IsNullOrEmpty(json.foodItemId) ? null : catalog.GetById(json.foodItemId),
            Weight = json.weight,
            ModificationCountLambda = json.modificationCountLambda,
            // Through the shared normalizer rather than a local clamp: an old Day's 0 has
            // to become the default here too, or the slider would sit at an out-of-range
            // value and the next Save would write the cap the runtime never applied.
            MaxModificationCount = MainDishWeight.NormalizeMaxModificationCount(json.maxModificationCount),
        };

        public MainDishWeightJson ToJson() => new()
        {
            foodItemId = Food != null ? Food.Id : string.Empty,
            weight = Weight,
            modificationCountLambda = ModificationCountLambda,
            maxModificationCount = MaxModificationCount,
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

            // Counts are clamped into the same ranges the sliders enforce. A Day file edited
            // outside Unity can hold e.g. leakDepth 0, which DayCatalogParser refuses -- and
            // without this the editor would load it, hand it straight back on Save, and the
            // game would come up with the Day dropped. DayValidator cannot catch that: it
            // sees BoardDistributionSettings, whose constructor has already clamped the value
            // away. Clamping at the point the bad value enters is the only place it works.
            return new DayEditorBoardDistribution
            {
                NoiseLeakCountLambda = json.noiseLeakCountLambda,
                GuaranteedTicketCountMode = Enum.TryParse<GuaranteedTicketCountMode>(json.guaranteedTicketCountMode, out var mode) ? mode : GuaranteedTicketCountMode.Manual,
                GuaranteedTicketCount = UnityEngine.Mathf.Clamp(json.guaranteedTicketCount, 1, 3),
                GuaranteedTicketCountLambda = json.guaranteedTicketCountLambda,
                EarlyTicketWeightDecay = json.earlyTicketWeightDecay,
                UrgentTimeThresholdSeconds = json.urgentTimeThresholdSeconds,
                LeakDepth = UnityEngine.Mathf.Clamp(json.leakDepth, 1, 10),
                MaxLeakCount = UnityEngine.Mathf.Clamp(json.maxLeakCount, 1, 10),
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
        // Odin's [MinValue], NOT UnityEngine's [Min] -- the same rule the numeric settings in
        // every other block already follow, and for the same reason the window's [Range] ban
        // exists. This window is an OdinMenuEditorWindow whose menu items are plain
        // DayEditorModel objects, so there is NO SerializedObject behind them; a UnityEngine
        // value attribute is drawn by a Unity PropertyDrawer (MinAttribute -> MinDrawer),
        // which needs a SerializedProperty, so Odin has to bridge it through an emulated one.
        // That bridge is what made this box misbehave: its height came from Unity's
        // GetPropertyHeight and disagreed between the layout and the repaint pass, so the
        // fields arrived a frame late while scrolling, and it consumed the event against its
        // own rect, so the mouse wheel died over this box instead of scrolling the page.
        // Odin's own attributes never leave Odin's drawer chain. Do not reintroduce a
        // UnityEngine value attribute anywhere in this file.
        //
        // Floor 0, not 1, on the three limits: a 0-second limit MUST stay authorable. It is
        // what DayValidator refuses the Save over, with the reason spelled out (see FromJson
        // below, which deliberately does not clamp these three either). [Min(1f)] had made
        // that path unreachable by hand -- the inspector silently prevented the very value
        // the validator exists to catch. Negatives are still blocked, being nonsense rather
        // than an authoring mistake worth reporting.
        [MinValue(0)] public float ImpatientTimeLimitSeconds = 45f;
        [MinValue(0)] public float NormalTimeLimitSeconds = 90f;
        [MinValue(0)] public float PatientTimeLimitSeconds = 150f;

        // 1, matching FromJson's Mathf.Max(1, ...) clamp -- unlike the limits above, a queue
        // size of 0 is not a mistake worth surfacing to the designer, it is just invalid.
        [MinValue(1)]
        [UnityEngine.Tooltip("How many tickets are pre-generated ahead of the 3 active slots. BoardDistributor's noise pool leaks from these, so LeakDepth above this is wasted reach.")]
        public int UpcomingQueueSize = 10;

        // Tolerates a Day file written before this block existed (all-zero, since
        // JsonUtility cannot produce null here) by falling back to the designed defaults,
        // so an old Day can still be opened and re-saved into a form the runtime accepts.
        // DayCatalogParser is the strict one; the editor is deliberately the forgiving one.
        public static DayEditorTicketRuntime FromJson(TicketRuntimeJson json)
        {
            if (json == null || json.impatientTimeLimitSeconds <= 0f) return new DayEditorTicketRuntime();

            // Queue size is clamped for the same reason as the board counts above. The three
            // time limits deliberately are NOT: TicketRuntimeSettings lets a 0 through, so
            // DayValidator can still see it and refuse the Save with the reason spelled out.
            // Silently rounding a 0-second limit up to 1 would hide an authoring mistake
            // behind a value nobody chose.
            return new DayEditorTicketRuntime
            {
                ImpatientTimeLimitSeconds = json.impatientTimeLimitSeconds,
                NormalTimeLimitSeconds = json.normalTimeLimitSeconds,
                PatientTimeLimitSeconds = json.patientTimeLimitSeconds,
                UpcomingQueueSize = UnityEngine.Mathf.Max(1, json.upcomingQueueSize),
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
