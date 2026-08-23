using System.Collections.Generic;
using System.Linq;
using ExpoTheExplorer.Data;
using Sirenix.OdinInspector;
using Sirenix.OdinInspector.Editor;
using Sirenix.Utilities.Editor;
using UnityEditor;
using UnityEngine;

namespace ExpoTheExplorer.Editor
{
    // Authoring window for the meta catalog: one page per location, each showing the
    // background with its props DRAGGABLE on top. It exists because the alternative is
    // typing twenty pairs of normalized floats into nested Inspector lists, which is not
    // authoring so much as arithmetic.
    //
    // Same shell as DayEditorWindow (OdinMenuEditorWindow: left tree, right page), but a
    // deliberately different division of labour. The Day Editor owns a mutable MODEL and
    // writes JSON on Save; this window edits the ScriptableObject in place through
    // SerializedObject, so Unity's own dirtying, undo and Ctrl+S apply and there is no
    // second copy of the data to keep in step. Odin is used only for the window chrome
    // and the tree -- every field below is drawn by Unity's own property drawer, on
    // purpose: it is the drawer that is guaranteed to match whatever the schema does next.
    //
    // Storage stayed a ScriptableObject rather than becoming JSON like Days
    // (decisions.md D-018). Short version: Day JSON references foods by id BECAUSE foods
    // live elsewhere and are shared across every Day -- FoodCatalog.GetById is that
    // bridge. Meta props are not shared; each belongs to exactly one location, so the id
    // indirection would buy nothing and add a mismatch to get wrong.
    public class MetaEditorWindow : OdinMenuEditorWindow
    {
        [MenuItem("ExpoTheExplorer/Meta Editor")]
        private static void Open()
        {
            var window = GetWindow<MetaEditorWindow>();
            window.titleContent = new GUIContent("Meta Editor");
            window.Show();
        }

        // Same reasoning as DayEditorWindow's override: Odin reads a value between 0 and 1
        // as a percentage, and the inherited default is a percentage, so the label column
        // grows with the window and the value column never gains room. Pixels instead.
        public override float DefaultLabelWidth => 190f;

        private MetaCatalog catalog;
        private readonly List<MetaLocationPage> pages = new();

        protected override OdinMenuTree BuildMenuTree()
        {
            catalog ??= FindFirstAsset<MetaCatalog>();

            pages.Clear();
            var tree = new OdinMenuTree();

            if (catalog == null || catalog.Locations == null || catalog.Locations.Count == 0)
            {
                // Not an error dialog: an empty or missing catalog is the normal state
                // before anything has been authored. The toolbar says what to do about it.
                return tree;
            }

            for (var i = 0; i < catalog.Locations.Count; i++)
            {
                var page = new MetaLocationPage(catalog, i);
                pages.Add(page);

                var location = catalog.Locations[i];
                var label = string.IsNullOrWhiteSpace(location?.Id) ? $"<location {i}>" : location.Id;
                tree.Add($"{label}  (Day {location?.UnlockAtDayIndex ?? 0})", page);
            }

            return tree;
        }

        // A strip above the location tree, so adding a location is where the locations
        // are. The Day Editor puts "+ New Day" in the top toolbar instead, which works
        // there because a Day is the only thing that window creates -- here the toolbar
        // already belongs to the catalog and the selected location's view.
        protected override void DrawMenu()
        {
            EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);

            using (new EditorGUI.DisabledScope(catalog == null))
            {
                if (GUILayout.Button(
                        new GUIContent("+ Location", "Append a new, empty location to the catalog."),
                        EditorStyles.toolbarButton))
                {
                    AddLocation();
                }
            }

            GUILayout.FlexibleSpace();

            // Paired with the add button on purpose: an add with no remove turns a
            // mis-click into permanent junk that can only be cleared by hand-editing the
            // asset, since this window shows one location at a time and never the list of
            // them as an editable array.
            using (new EditorGUI.DisabledScope(SelectedPage() == null))
            {
                if (GUILayout.Button(
                        new GUIContent("−", "Delete the selected location and everything authored in it."),
                        EditorStyles.toolbarButton, GUILayout.Width(22)))
                {
                    RemoveSelectedLocation();
                }
            }

            EditorGUILayout.EndHorizontal();

            base.DrawMenu();
        }

        private MetaLocationPage SelectedPage() => MenuTree?.Selection?.SelectedValue as MetaLocationPage;

        private void AddLocation()
        {
            if (catalog == null) return;

            var serialized = new SerializedObject(catalog);
            var locations = serialized.FindProperty("locations");
            if (locations == null) return;

            var index = locations.arraySize;
            locations.InsertArrayElementAtIndex(index);

            // Unity's insert DUPLICATES the previous element, so a new location would be
            // born holding a copy of the last one's entire prop list. Every field is set
            // explicitly and the items array cleared, or "+ Location" would silently be
            // "duplicate location".
            var element = locations.GetArrayElementAtIndex(index);
            element.FindPropertyRelative("id").stringValue = NextLocationId();
            element.FindPropertyRelative("displayName").stringValue = string.Empty;
            element.FindPropertyRelative("backgroundSprite").objectReferenceValue = null;
            element.FindPropertyRelative("items").ClearArray();

            // Inherits the previous location's unlock day rather than defaulting to 0:
            // that keeps the list in the non-decreasing order the validator expects, and
            // 0 on a second location would claim it is available from the first day.
            var previousDay = index > 0
                ? catalog.Locations[index - 1]?.UnlockAtDayIndex ?? 0
                : 0;
            element.FindPropertyRelative("unlockAtDayIndex").intValue = previousDay;

            serialized.ApplyModifiedProperties();

            ForceMenuTreeRebuild();
            if (index < pages.Count) TrySelectMenuItemWithObject(pages[index]);
        }

        // "Meta1", "Meta2", ... skipping anything already taken, so the id is unique from
        // the moment it exists rather than tripping the duplicate-id error on creation.
        private string NextLocationId()
        {
            var taken = new HashSet<string>(
                catalog.Locations.Where(l => l != null && !string.IsNullOrWhiteSpace(l.Id)).Select(l => l.Id));

            for (var n = 1; n < 1000; n++)
            {
                var candidate = $"Meta{n}";
                if (!taken.Contains(candidate)) return candidate;
            }

            return string.Empty;
        }

        private void RemoveSelectedLocation()
        {
            var page = SelectedPage();
            if (page == null || catalog == null) return;

            var location = catalog.Locations.ElementAtOrDefault(page.LocationIndex);
            var label = string.IsNullOrWhiteSpace(location?.Id) ? $"location {page.LocationIndex}" : location.Id;
            var propCount = location?.Items?.Count ?? 0;

            // Confirmed, not undo-only: this throws away every prop, price and position
            // authored in the location, and Ctrl+Z after a domain reload will not bring
            // them back.
            if (!EditorUtility.DisplayDialog(
                    "Delete location",
                    $"Delete '{label}' and its {propCount} prop(s)?\n\nPlayers who bought anything here keep the entries in their save file, but nothing will resolve them any more.",
                    "Delete",
                    "Cancel"))
            {
                return;
            }

            var serialized = new SerializedObject(catalog);
            var locations = serialized.FindProperty("locations");
            locations.DeleteArrayElementAtIndex(page.LocationIndex);
            serialized.ApplyModifiedProperties();

            ForceMenuTreeRebuild();
        }

        // Toolbar above the selected location: the catalog field (auto-discovered, but
        // overridable, since "the first asset of this type" is a guess and a project with
        // two of them would otherwise be stuck with the wrong one) plus the validation
        // summary, which is the number an author actually wants in their eyeline.
        protected override void OnBeginDrawEditors()
        {
            base.OnBeginDrawEditors();

            SirenixEditorGUI.BeginHorizontalToolbar();

            var newCatalog = (MetaCatalog)EditorGUILayout.ObjectField(
                new GUIContent(string.Empty, "Meta Catalog"), catalog, typeof(MetaCatalog), false, GUILayout.Width(160));

            if (newCatalog != catalog)
            {
                catalog = newCatalog;
                ForceMenuTreeRebuild();
            }

            GUILayout.FlexibleSpace();

            if (catalog == null)
            {
                GUILayout.Label("No MetaCatalog found — create one via Assets > Create > ExpoTheExplorer > Data > Meta Catalog.");
            }
            else
            {
                DrawValidationSummary();
            }

            SirenixEditorGUI.EndHorizontalToolbar();
        }

        // The counts only. The messages themselves live on the page, next to the fields
        // they are about -- a toolbar is the wrong place to read a paragraph.
        private void DrawValidationSummary()
        {
            var result = MetaCatalogValidator.Validate(catalog);

            var previous = GUI.color;
            GUI.color = result.IsValid ? previous : new Color(1f, 0.55f, 0.55f);
            GUILayout.Label($"{result.Errors.Count} error(s), {result.Warnings.Count} warning(s)");
            GUI.color = previous;
        }

        private static T FindFirstAsset<T>() where T : Object
        {
            var guids = AssetDatabase.FindAssets($"t:{typeof(T).Name}");
            return guids.Length == 0 ? null : AssetDatabase.LoadAssetAtPath<T>(AssetDatabase.GUIDToAssetPath(guids[0]));
        }
    }

    // One location's page. Everything is drawn from a single [OnInspectorGUI] rather than
    // from serialized members: the page owns no data of its own, it is a view onto one
    // element of the catalog's list, and letting Odin reflect over it would draw the
    // catalog reference and the index as editable fields.
    internal class MetaLocationPage
    {
        private readonly MetaCatalog catalog;
        private readonly int locationIndex;

        // Exposed so the window can tell WHICH location the tree has selected -- the
        // remove button needs it, and the page is the only thing that knows.
        internal int LocationIndex => locationIndex;
        private readonly MetaLayoutState layoutState = new();

        // Hidden explicitly rather than trusting Odin's default member filter: these are
        // view state, and a reflection pass that decided to surface them would put two
        // mystery checkboxes and a SerializedObject field on the page.
        [HideInInspector] private SerializedObject serialized;
        [HideInInspector] private bool showFields;
        [HideInInspector] private bool showMessages = true;
        [HideInInspector] private Vector2 listScroll;

        internal MetaLocationPage(MetaCatalog catalog, int locationIndex)
        {
            this.catalog = catalog;
            this.locationIndex = locationIndex;
        }

        [OnInspectorGUI]
        private void Draw()
        {
            if (catalog == null) return;

            // Rebuilt lazily rather than cached across domain reloads: a stale
            // SerializedObject after a recompile throws on first access, and the window
            // survives reloads while this does not.
            serialized ??= new SerializedObject(catalog);
            serialized.Update();

            var locations = serialized.FindProperty("locations");
            if (locations == null || locationIndex >= locations.arraySize)
            {
                EditorGUILayout.HelpBox("This location no longer exists in the catalog.", MessageType.Warning);
                return;
            }

            var locationProperty = locations.GetArrayElementAtIndex(locationIndex);
            var location = catalog.Locations[locationIndex];

            // Before anything draws: a changed prop count invalidates the hidden-index set.
            layoutState.SyncTo(location?.Items?.Count ?? 0);

            DrawCanvas(location, locationProperty);
            DrawToolRow(location, locationProperty);
            DrawSelectedProp(location, locationProperty);

            // Collapsed by default now that the selected prop has its own panel: this
            // foldout is for structure (adding, removing and reordering props, and the
            // location's own id/background/unlock day), not for day-to-day editing.
            showFields = SirenixEditorGUI.Foldout(showFields, "Location & full prop list");
            if (showFields)
            {
                EditorGUILayout.PropertyField(locationProperty, GUIContent.none, true);
            }

            DrawMessages();

            // One ApplyModifiedProperties for the whole page: it records a single undo
            // step per repaint that changed something, which is what makes a drag undo as
            // one gesture instead of one step per mouse-move event.
            serialized.ApplyModifiedProperties();
        }

        private const float CanvasHeight = 520f;
        private const float PropListWidth = 190f;

        // Canvas on the left, prop list on the right, both the same height so they read as
        // one panel. The list replaced a toolbar dropdown: a dropdown shows one entry at a
        // time, and the thing an author actually wants while placing props is the whole
        // set in front of them, with the selected one obvious.
        private void DrawCanvas(MetaLocation location, SerializedProperty locationProperty)
        {
            EditorGUILayout.BeginHorizontal();

            // Tall on purpose: the backgrounds are phone-portrait (Main.png is 853x1844),
            // so a square preview area would waste most of its width.
            var area = GUILayoutUtility.GetRect(0f, CanvasHeight, GUILayout.ExpandWidth(true));
            MetaLocationLayoutGUI.Draw(area, location, locationProperty, layoutState);

            DrawPropList(location);

            EditorGUILayout.EndHorizontal();
        }

        private void DrawPropList(MetaLocation location)
        {
            var items = location?.Items;

            EditorGUILayout.BeginVertical(GUILayout.Width(PropListWidth));

            // Count in the header so "did that + actually add one" is answerable without
            // opening the list foldout below.
            EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);
            GUILayout.Label($"Props ({items?.Count ?? 0})", EditorStyles.miniLabel);
            GUILayout.FlexibleSpace();

            if (items != null && items.Count > 0)
            {
                var anyHidden = layoutState.Hidden.Count > 0;
                if (GUILayout.Button(
                        new GUIContent(anyHidden ? "Show all" : "Hide all", "Preview only — this never touches the catalog."),
                        EditorStyles.toolbarButton, GUILayout.Width(58)))
                {
                    layoutState.Hidden.Clear();
                    if (!anyHidden)
                    {
                        for (var i = 0; i < items.Count; i++) layoutState.SetHidden(i, true);
                    }
                }
            }

            EditorGUILayout.EndHorizontal();

            listScroll = EditorGUILayout.BeginScrollView(
                listScroll, GUILayout.Width(PropListWidth), GUILayout.Height(CanvasHeight - EditorGUIUtility.singleLineHeight));

            if (items == null || items.Count == 0)
            {
                EditorGUILayout.LabelField("No props yet.", EditorStyles.centeredGreyMiniLabel);
            }
            else
            {
                // Drawn in LIST order, not draw order: this is how an author refers to a
                // prop ("the third one"), and it matches the indices the raw list and the
                // validator messages use.
                for (var i = 0; i < items.Count; i++)
                {
                    DrawPropRow(items[i], i);
                }
            }

            EditorGUILayout.EndScrollView();
            EditorGUILayout.EndVertical();
        }

        private void DrawPropRow(MetaItemDefinition item, int index)
        {
            EditorGUILayout.BeginHorizontal();

            var visible = !layoutState.IsHidden(index);
            var nowVisible = EditorGUILayout.Toggle(visible, GUILayout.Width(14));
            if (nowVisible != visible) layoutState.SetHidden(index, !nowVisible);

            var id = item?.Id;
            var label = string.IsNullOrWhiteSpace(id) ? $"{index}: <no id>" : $"{index}: {id}";

            // A toggle drawn as a button, so the selected row is visibly pressed rather
            // than needing a separate highlight rect.
            var isSelected = layoutState.SelectedIndex == index;
            if (GUILayout.Toggle(isSelected, label, EditorStyles.miniButton) && !isSelected)
            {
                layoutState.SelectedIndex = index;
            }

            EditorGUILayout.EndHorizontal();
        }

        private void DrawToolRow(MetaLocation location, SerializedProperty locationProperty)
        {
            EditorGUILayout.BeginHorizontal();

            var selected = layoutState.SelectedIndex;
            var hasSelection = selected >= 0 && location?.Items != null && selected < location.Items.Count;

            GUILayout.Label(
                hasSelection
                    ? Format(location.Items[selected]?.NormalizedPosition)
                    : "click a prop, or pick one",
                EditorStyles.miniLabel);

            GUILayout.FlexibleSpace();

            // Wheel to zoom, middle-drag to pan; this only reports and resets, because a
            // slider would compete with the wheel for the same value and the two would
            // fight over which one is authoritative.
            GUILayout.Label($"{layoutState.Zoom:0.#}x", EditorStyles.miniLabel, GUILayout.Width(32));
            if (GUILayout.Button(
                    new GUIContent("Fit", "Back to the whole background, centred. Scroll over the canvas to zoom; middle-drag or right-drag to pan."),
                    EditorStyles.miniButton, GUILayout.Width(34)))
            {
                layoutState.ResetView();
            }

            // A button, not automatic. Deriving depth from Y on every drag would wipe any
            // hand-set exception the moment the prop it belongs to is nudged -- and
            // exceptions are the whole reason the field is authored rather than computed.
            if (GUILayout.Button(new GUIContent(
                    "Depth from Y",
                    "Rewrite every prop's Sort Order from its vertical position: lower on the art draws in front. Overwrites hand-set values."),
                EditorStyles.miniButton, GUILayout.Width(100)))
            {
                DeriveSortOrderFromY(location, locationProperty);
            }

            EditorGUILayout.EndHorizontal();
        }

        private static string Format(Vector2? position) =>
            position.HasValue ? $"({position.Value.x:0.###}, {position.Value.y:0.###})" : "-";

        // The clicked prop's own fields, right under the picture. This is what the window
        // is for: selecting a prop on the canvas and editing it without hunting for its
        // element in a nineteen-entry list.
        //
        // It also fixes the one real cost of the enum discriminator the schema uses
        // (decisions.md D-017): with `price` and `unlockAtDayIndex` both always present,
        // an author has to remember which one their Unlock mode actually reads. Here only
        // the live one is drawn, so there is nothing to remember and no chance of tuning
        // a number that is ignored.
        private void DrawSelectedProp(MetaLocation location, SerializedProperty locationProperty)
        {
            var items = locationProperty.FindPropertyRelative("items");
            var index = layoutState.SelectedIndex;

            if (items == null || index < 0 || index >= items.arraySize)
            {
                SirenixEditorGUI.MessageBox("Click a prop on the canvas to edit it here.");
                return;
            }

            var item = items.GetArrayElementAtIndex(index);
            var displayName = location?.Items?[index]?.Id;

            SirenixEditorGUI.BeginBox(string.IsNullOrWhiteSpace(displayName) ? "<no id>" : displayName);

            Field(item, "id");
            Field(item, "displayName");
            Field(item, "sprite");

            // Drawn right under the prop's own art, because the pair is the whole point:
            // this one is only what the shop row shows, and leaving it empty means "use the
            // art above". Listed explicitly like every field here -- this window draws props
            // field by field rather than by default inspector, so a new field is invisible
            // until it is named.
            Field(item, "shopIcon");

            EditorGUILayout.Space(2f);
            var unlock = item.FindPropertyRelative("unlock");
            if (unlock != null) EditorGUILayout.PropertyField(unlock);

            // MetaUnlockKind: 0 = Purchase, 1 = DayUnlock. Read from the property rather
            // than from the plain object so a change made this frame is already reflected.
            if (unlock != null && unlock.enumValueIndex == (int)MetaUnlockKind.Purchase)
            {
                Field(item, "price");
            }
            else
            {
                Field(item, "unlockAtDayIndex");
            }

            EditorGUILayout.Space(2f);

            // Editable, not just displayed: dragging is for roughing a layout out, and
            // typing is how two props end up on exactly the same line.
            Field(item, "normalizedPosition");
            Field(item, "pivot");
            Field(item, "sortOrder");

            EditorGUILayout.Space(2f);
            Field(item, "requiresAreaId");
            Field(item, "unlocksArea");

            SirenixEditorGUI.EndBox();
        }

        private static void Field(SerializedProperty parent, string relativeName)
        {
            var property = parent.FindPropertyRelative(relativeName);
            if (property != null) EditorGUILayout.PropertyField(property);
        }

        // Lower on the art (smaller normalized Y) is nearer the camera in a top-down
        // scene, so it needs the HIGHER sort order.
        private void DeriveSortOrderFromY(MetaLocation location, SerializedProperty locationProperty)
        {
            var items = locationProperty.FindPropertyRelative("items");
            if (items == null || location?.Items == null) return;

            var byHeight = Enumerable.Range(0, location.Items.Count)
                .Where(i => location.Items[i] != null)
                .OrderByDescending(i => location.Items[i].NormalizedPosition.y)
                .ToList();

            for (var order = 0; order < byHeight.Count; order++)
            {
                items.GetArrayElementAtIndex(byHeight[order])
                    .FindPropertyRelative("sortOrder").intValue = order;
            }
        }

        // Full messages, not just the toolbar's counts, and scoped to this location so an
        // author is not reading another location's problems while fixing this one.
        private void DrawMessages()
        {
            var result = MetaCatalogValidator.Validate(catalog);
            if (result.Errors.Count == 0 && result.Warnings.Count == 0) return;

            showMessages = SirenixEditorGUI.Foldout(
                showMessages, $"Validation ({result.Errors.Count} error(s), {result.Warnings.Count} warning(s))");
            if (!showMessages) return;

            foreach (var error in result.Errors) EditorGUILayout.HelpBox(error, MessageType.Error);
            foreach (var warning in result.Warnings) EditorGUILayout.HelpBox(warning, MessageType.Warning);
        }
    }
}
