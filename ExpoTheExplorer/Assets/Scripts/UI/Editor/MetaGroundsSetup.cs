using System.IO;
using ExpoTheExplorer.Data;
using ExpoTheExplorer.UI;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

namespace ExpoTheExplorer.UI.EditorTools
{
    // Builds the container the meta grounds live in, once, and wires MetaGroundsView to it.
    // The props themselves are NOT built here -- they are instantiated at run time from the
    // catalog, which stays their single authority (D-015); nineteen hand-placed objects
    // would be a second copy free to disagree with it.
    //
    // Same posture as MainScreenSessionSetup and HudCanvasPrefabSetup: it touches only the
    // scene that is already open and it does NOT save, so saving stays the author's call
    // and the undo stack survives. It also refuses to REBUILD: if the hierarchy is already
    // there it only refreshes the references, so a layout the author has since adjusted by
    // hand is never overwritten -- the same promise MainScreenSceneBuilder makes by
    // refusing to overwrite an existing scene.
    internal static class MetaGroundsSetup
    {
        private const string SceneName = "MainScreen";
        private const string RootName = "--MetaGrounds--";
        private const float LocationBarHeight = 56f;

        [MenuItem("ExpoTheExplorer/Meta/Build Meta Grounds")]
        private static void Build()
        {
            var scene = EditorSceneManager.GetActiveScene();
            if (scene.name != SceneName)
            {
                EditorUtility.DisplayDialog(
                    "Wrong scene",
                    $"Open the '{SceneName}' scene first — this step only touches the scene that is already open, " +
                    "so that saving stays your decision.",
                    "OK");
                return;
            }

            // The MainScreen canvas, found via MainScreenView rather than by taking the
            // first Canvas in the scene: the HUD is its own Canvas prefab, and building the
            // grounds inside it would put them on the wrong sorting layer.
            var screen = Object.FindAnyObjectByType<MainScreenView>();
            if (screen == null)
            {
                EditorUtility.DisplayDialog(
                    "No MainScreenView",
                    "Could not find MainScreenView, so there is no way to tell which Canvas is the screen's own " +
                    "(the HUD has a Canvas of its own). Open the built MainScreen scene.",
                    "OK");
                return;
            }

            var canvas = screen.GetComponentInParent<Canvas>();
            if (canvas == null)
            {
                EditorUtility.DisplayDialog("No Canvas", "MainScreenView is not under a Canvas.", "OK");
                return;
            }

            var existing = FindChild(canvas.transform, RootName);
            var view = existing != null
                ? RefreshExisting(existing)
                : BuildFresh(canvas);

            AssignData(view);

            EditorSceneManager.MarkSceneDirty(scene);
            Selection.activeObject = view;

            Debug.Log(
                existing != null
                    ? $"Meta grounds already present on '{canvas.name}'; references refreshed and your layout left alone. Scene is dirty — save it yourself."
                    : $"Meta grounds built under '{canvas.name}'. It is the FIRST child so it draws behind the day readout and Play. Scene is dirty — save it yourself.",
                view);
        }

        private static MetaGroundsView BuildFresh(Canvas canvas)
        {
            var root = CreateRect(RootName, canvas.transform);
            Undo.RegisterCreatedObjectUndo(root.gameObject, "Build Meta Grounds");
            Stretch(root);

            // First sibling = drawn first = behind everything else on this canvas. The
            // grounds are a backdrop; the day readout and Play button must stay on top of
            // them, and sibling order is the only thing that decides that in a Canvas.
            root.SetAsFirstSibling();

            var view = Undo.AddComponent<MetaGroundsView>(root.gameObject);

            var viewport = CreateRect("Viewport", root);
            Stretch(viewport);
            viewport.offsetMax = new Vector2(0f, -LocationBarHeight);
            // RectMask2D rather than Mask: no extra material and no stencil buffer, which
            // is what a plain rectangular clip wants.
            viewport.gameObject.AddComponent<RectMask2D>();

            var background = CreateRect("Background", viewport);
            // Top-stretched with a top pivot, so the height this component computes grows
            // DOWNWARD from the top edge and scrolling starts at the top of the art.
            background.anchorMin = new Vector2(0f, 1f);
            background.anchorMax = new Vector2(1f, 1f);
            background.pivot = new Vector2(0.5f, 1f);
            background.offsetMin = new Vector2(0f, 0f);
            background.offsetMax = new Vector2(0f, 0f);
            var backgroundImage = background.gameObject.AddComponent<Image>();
            backgroundImage.raycastTarget = true;

            var scroll = root.gameObject.AddComponent<ScrollRect>();
            scroll.viewport = viewport;
            scroll.content = background;
            scroll.horizontal = false;
            scroll.vertical = true;
            // Clamped, not Elastic: the art either overflows and scrolls, or fits and does
            // nothing. Elastic would let a background that already fits bounce, which reads
            // as a bug rather than a flourish.
            scroll.movementType = ScrollRect.MovementType.Clamped;
            scroll.scrollSensitivity = 20f;

            var bar = BuildLocationBar(root);

            WireSerialized(view, scroll, backgroundImage, bar);
            return view;
        }

        private static LocationBar BuildLocationBar(RectTransform root)
        {
            var bar = CreateRect("LocationBar", root);
            bar.anchorMin = new Vector2(0f, 0f);
            bar.anchorMax = new Vector2(1f, 0f);
            bar.pivot = new Vector2(0.5f, 0f);
            bar.sizeDelta = new Vector2(0f, LocationBarHeight);

            var label = CreateText("Name", bar, 28f, TextAlignmentOptions.Center);
            Stretch(label.rectTransform);
            label.rectTransform.offsetMin = new Vector2(90f, 0f);
            label.rectTransform.offsetMax = new Vector2(-90f, 0f);

            var hint = CreateText("LockedHint", bar, 18f, TextAlignmentOptions.Center);
            hint.rectTransform.anchorMin = new Vector2(0.5f, 1f);
            hint.rectTransform.anchorMax = new Vector2(0.5f, 1f);
            hint.rectTransform.pivot = new Vector2(0.5f, 0f);
            hint.rectTransform.sizeDelta = new Vector2(320f, 26f);
            hint.color = new Color(1f, 1f, 1f, 0.65f);

            return new LocationBar
            {
                Label = label,
                LockedHint = hint,
                Previous = CreateButton("Previous", bar, "<", new Vector2(0f, 0.5f), new Vector2(46f, 0f)),
                Next = CreateButton("Next", bar, ">", new Vector2(1f, 0.5f), new Vector2(-46f, 0f)),
            };
        }

        private struct LocationBar
        {
            public TMP_Text Label;
            public TMP_Text LockedHint;
            public Button Previous;
            public Button Next;
        }

        // Existing hierarchy: only the component is looked up. Nothing is moved, resized or
        // recreated, because the whole point of running this twice is to fix a reference
        // without losing a layout.
        private static MetaGroundsView RefreshExisting(Transform root)
        {
            var view = root.GetComponent<MetaGroundsView>();
            if (view != null) return view;

            return Undo.AddComponent<MetaGroundsView>(root.gameObject);
        }

        // Data references, separate from the layout: these are the two an author is most
        // likely to need re-pointed (a second MetaCatalog, a re-created session root), and
        // they are filled only when empty so a deliberate choice is never overwritten.
        private static void AssignData(MetaGroundsView view)
        {
            var serialized = new SerializedObject(view);

            FillIfEmpty(serialized, "sessionHost", Object.FindAnyObjectByType<MainScreenRoot>());
            FillIfEmpty(serialized, "catalog", FindFirstAsset<MetaCatalog>());

            serialized.ApplyModifiedProperties();
        }

        private static void WireSerialized(MetaGroundsView view, ScrollRect scroll, Image background, LocationBar bar)
        {
            var serialized = new SerializedObject(view);

            Set(serialized, "scroll", scroll);
            Set(serialized, "background", background);
            Set(serialized, "locationLabel", bar.Label);
            Set(serialized, "previousButton", bar.Previous);
            Set(serialized, "nextButton", bar.Next);
            Set(serialized, "lockedHintLabel", bar.LockedHint);

            // The unlock popup is deliberately NOT wired here. It lives in its own step,
            // ExpoTheExplorer > Meta > Build Unlock Popup: this one rebuilds the grounds
            // container and rewires the whole view, so needing it to obtain one prefab would
            // mean accepting a pile of unrelated side effects (the user's objection, and a
            // fair one). One setup script per concern, like every other step in this folder.
            serialized.ApplyModifiedProperties();
        }

        private static void Set(SerializedObject serialized, string name, Object value)
        {
            var property = serialized.FindProperty(name);
            if (property != null) property.objectReferenceValue = value;
        }

        private static void FillIfEmpty(SerializedObject serialized, string name, Object value)
        {
            var property = serialized.FindProperty(name);
            if (property == null || property.objectReferenceValue != null) return;
            property.objectReferenceValue = value;
        }

        private static RectTransform CreateRect(string name, Transform parent)
        {
            var go = new GameObject(name, typeof(RectTransform));
            var rect = (RectTransform)go.transform;
            rect.SetParent(parent, worldPositionStays: false);
            rect.localScale = Vector3.one;
            return rect;
        }

        private static void Stretch(RectTransform rect)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
        }

        private static TMP_Text CreateText(string name, Transform parent, float size, TextAlignmentOptions alignment)
        {
            var rect = CreateRect(name, parent);
            var text = rect.gameObject.AddComponent<TextMeshProUGUI>();
            text.fontSize = size;
            text.alignment = alignment;
            text.raycastTarget = false;
            // Left blank on purpose: authored text belongs in data or in the scene, not in
            // a construction script, and the view fills this one at run time anyway.
            text.text = string.Empty;
            return text;
        }

        private static Button CreateButton(string name, Transform parent, string caption, Vector2 anchor, Vector2 offset)
        {
            var rect = CreateRect(name, parent);
            rect.anchorMin = anchor;
            rect.anchorMax = anchor;
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = offset;
            rect.sizeDelta = new Vector2(64f, 44f);

            rect.gameObject.AddComponent<Image>().color = new Color(1f, 1f, 1f, 0.12f);
            var button = rect.gameObject.AddComponent<Button>();

            var label = CreateText("Label", rect, 30f, TextAlignmentOptions.Center);
            Stretch(label.rectTransform);
            label.text = caption;

            return button;
        }

        private static Transform FindChild(Transform parent, string name)
        {
            foreach (Transform child in parent)
            {
                if (child.name == name) return child;
            }
            return null;
        }

        private static T FindFirstAsset<T>() where T : Object
        {
            var guids = AssetDatabase.FindAssets($"t:{typeof(T).Name}");
            return guids.Length == 0 ? null : AssetDatabase.LoadAssetAtPath<T>(AssetDatabase.GUIDToAssetPath(guids[0]));
        }
    }
}
