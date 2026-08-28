using System.IO;
using ExpoTheExplorer.UI;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

namespace ExpoTheExplorer.UI.EditorTools
{
    // Menu step: seeds the popup a Day-unlocked prop shows when it opens, and points the
    // scene's MetaGroundsView at it (D-128).
    //
    // ITS OWN STEP RATHER THAN A LINE INSIDE Build Meta Grounds, which is where it started and
    // where the user objected: that step rebuilds the grounds container and rewires the whole
    // view, so needing it to obtain one prefab would mean accepting a pile of unrelated side
    // effects. Every other step in this folder owns one concern -- MetaShopSetup, MetaBackdropSetup,
    // TutorialPopupSetup -- and a popup is a concern.
    //
    // It does BOTH halves, so it is self-sufficient: the prefab and the reference. Build Meta
    // Grounds is back to not knowing this popup exists.
    internal static class MetaUnlockPopupSetup
    {
        [MenuItem("ExpoTheExplorer/Meta/Build Unlock Popup")]
        private static void BuildAndWire()
        {
            var popup = EnsureUnlockPopupPrefab();
            if (popup == null) return;

            // Found by TYPE, never by name -- the project rule for editor steps as well as for
            // runtime (D-028). There is exactly one MetaGroundsView in the scene, which is why
            // the reference lives on it and nothing has to be dragged.
            var view = Object.FindAnyObjectByType<MetaGroundsView>(FindObjectsInactive.Include);
            if (view == null)
            {
                Debug.Log(
                    $"Build Unlock Popup: {UnlockPopupPath} is ready, but there is no {nameof(MetaGroundsView)} in the " +
                    "open scene to wire it to. Open MainScreen and run this again.");
                return;
            }

            var serialized = new SerializedObject(view);
            var property = serialized.FindProperty("unlockPopupPrefab");

            // Only fills an EMPTY field, the same refusal the prefab build makes: the author
            // may have pointed this at a variant of their own.
            if (property == null || property.objectReferenceValue != null)
            {
                Debug.Log($"Build Unlock Popup: '{view.name}' already points at a popup prefab, so nothing was changed.");
                return;
            }

            property.objectReferenceValue = popup;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            EditorSceneManager.MarkSceneDirty(view.gameObject.scene);
            Debug.Log(
                $"Build Unlock Popup: wired {UnlockPopupPath} onto '{view.name}'. The scene is marked dirty but NOT " +
                "saved -- save it yourself.", view);
        }

        private const string PrefabFolder = "Assets/Prefabs/UI";
        private const string UnlockPopupPath = PrefabFolder + "/MetaUnlockPopup.prefab";

        // The reference resolution the main screen's own CanvasScaler uses. Authored here so
        // the seed can be built with no scene loaded; from the first edit the prefab owns it.
        private static readonly Vector2 PopupReferenceResolution = new(1080f, 1920f);

        // REFUSES to rebuild an existing prefab, the same refusal every seeding step in this
        // project makes: from the author's first edit it is their asset, and regenerating
        // would be indistinguishable from losing that work. Delete it to have it seeded again.
        private static MetaUnlockPopup EnsureUnlockPopupPrefab()
        {
            var existing = AssetDatabase.LoadAssetAtPath<GameObject>(UnlockPopupPath);
            if (existing != null)
            {
                var component = existing.GetComponent<MetaUnlockPopup>();
                if (component == null)
                {
                    Debug.LogError(
                        $"Build Meta Grounds: '{UnlockPopupPath}' has no {nameof(MetaUnlockPopup)} on it — its script " +
                        "does not resolve, so the prefab cannot be used. Delete that asset from the Project window and " +
                        "run this again. (If the console also shows compile errors, fix those first: unloaded scripts " +
                        "look exactly like this.)");
                }

                return component;
            }

            Directory.CreateDirectory(PrefabFolder);

            var root = BuildUnlockPopup();
            GameObject saved;
            try
            {
                saved = PrefabUtility.SaveAsPrefabAsset(root, UnlockPopupPath);
            }
            finally
            {
                // Destroyed whether or not the save threw: this object existed only to be
                // serialized, and leaving it behind drops a stray popup into the open scene.
                Object.DestroyImmediate(root);
            }

            AssetDatabase.SaveAssets();
            Debug.Log($"Build Meta Grounds: built {UnlockPopupPath}.");
            return saved != null ? saved.GetComponent<MetaUnlockPopup>() : null;
        }

        // THE ROOT IS THE CANVAS. Unity drives an Overlay canvas's rect from the screen and
        // gives a ROOT canvas that treatment in the prefab stage; a NESTED one sits at 0x0 and
        // collapses every child into it, which is how another tutorial prefab came to open as
        // an empty frame (D-126). Authoring a size does not help -- the Canvas overwrites it.
        //
        // MODAL, with a backdrop that swallows presses: the map is zoomed in on the prop and
        // a full-screen skip catcher is live underneath, so a tap meant for this popup must
        // not reach either of them.
        private static GameObject BuildUnlockPopup()
        {
            var root = new GameObject("MetaUnlockPopup", typeof(RectTransform));
            var canvas = root.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;

            // Above the tutorial's own popups (-1) and above the screen's canvas (0): this one
            // is the reason the map is holding still, so nothing should sit over it.
            canvas.sortingOrder = 10;

            var scaler = root.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = PopupReferenceResolution;
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = 0.5f;
            root.AddComponent<GraphicRaycaster>();

            var fontSize = PopupReferenceResolution.y * 0.024f;

            var backdrop = CreateRect("Backdrop", root.transform);
            Stretch(backdrop);
            backdrop.gameObject.AddComponent<Image>().color = new Color(0f, 0f, 0f, 0.72f);

            var panel = CreateRect("Panel", root.transform);
            panel.anchorMin = new Vector2(0.5f, 0.5f);
            panel.anchorMax = new Vector2(0.5f, 0.5f);
            panel.pivot = new Vector2(0.5f, 0.5f);
            panel.sizeDelta = new Vector2(PopupReferenceResolution.x * 0.82f, PopupReferenceResolution.y * 0.46f);
            panel.gameObject.AddComponent<Image>().color = new Color(0.07f, 0.08f, 0.11f, 0.96f);

            // The PICTURE first and largest: it is what the player is here to see, and the
            // sentence underneath names it.
            var imageRect = CreateRect("Image", panel);
            imageRect.anchorMin = new Vector2(0.5f, 1f);
            imageRect.anchorMax = new Vector2(0.5f, 1f);
            imageRect.pivot = new Vector2(0.5f, 1f);
            imageRect.sizeDelta = new Vector2(PopupReferenceResolution.x * 0.52f, PopupReferenceResolution.x * 0.52f);
            imageRect.anchoredPosition = new Vector2(0f, -fontSize * 1.2f);
            var image = imageRect.gameObject.AddComponent<Image>();

            // The photograph is not necessarily square; keeping its aspect is what stops a
            // wide shot of a drinks shelf from being stretched into a portrait.
            image.preserveAspect = true;

            var title = CreateText("Title", panel, fontSize * 1.3f);
            title.rectTransform.anchorMin = new Vector2(0f, 0f);
            title.rectTransform.anchorMax = new Vector2(1f, 0f);
            title.rectTransform.pivot = new Vector2(0.5f, 0f);
            title.rectTransform.sizeDelta = new Vector2(-fontSize * 2f, fontSize * 2f);
            title.rectTransform.anchoredPosition = new Vector2(0f, fontSize * 6.4f);
            title.fontStyle = FontStyles.Bold;
            title.text = "What opened";

            var body = CreateText("Body", panel, fontSize);
            body.rectTransform.anchorMin = new Vector2(0f, 0f);
            body.rectTransform.anchorMax = new Vector2(1f, 0f);
            body.rectTransform.pivot = new Vector2(0.5f, 0f);
            body.rectTransform.sizeDelta = new Vector2(-fontSize * 2.4f, fontSize * 3.4f);
            body.rectTransform.anchoredPosition = new Vector2(0f, fontSize * 3f);
            body.color = new Color(0.86f, 0.89f, 0.94f);
            body.text = "What it brought.";

            var buttonRect = CreateRect("GotItButton", panel);
            buttonRect.anchorMin = new Vector2(0.5f, 0f);
            buttonRect.anchorMax = new Vector2(0.5f, 0f);
            buttonRect.pivot = new Vector2(0.5f, 0f);
            buttonRect.sizeDelta = new Vector2(PopupReferenceResolution.x * 0.34f, fontSize * 2.6f);
            buttonRect.anchoredPosition = new Vector2(0f, fontSize * 0.8f);
            var buttonImage = buttonRect.gameObject.AddComponent<Image>();
            buttonImage.color = new Color(0.24f, 0.55f, 0.36f, 1f);

            var label = CreateText("Label", buttonRect, fontSize);
            Stretch(label.rectTransform);
            label.fontStyle = FontStyles.Bold;
            label.text = "Got it";

            var button = buttonRect.gameObject.AddComponent<Button>();
            button.targetGraphic = buttonImage;

            var popup = root.AddComponent<MetaUnlockPopup>();
            var serialized = new SerializedObject(popup);
            serialized.FindProperty("title").objectReferenceValue = title;
            serialized.FindProperty("body").objectReferenceValue = body;
            serialized.FindProperty("image").objectReferenceValue = image;
            serialized.FindProperty("dismissButton").objectReferenceValue = button;
            serialized.ApplyModifiedPropertiesWithoutUndo();

            return root;
        }

        private static TextMeshProUGUI CreateText(string name, Transform parent, float size)
        {
            var rect = CreateRect(name, parent);
            var text = rect.gameObject.AddComponent<TextMeshProUGUI>();
            text.fontSize = size;
            text.alignment = TextAlignmentOptions.Center;
            text.color = Color.white;
            text.raycastTarget = false;
            text.textWrappingMode = TextWrappingModes.Normal;

            // Assigned explicitly so a missing TMP Essentials import fails loudly rather than
            // producing an invisible label -- the same call every other setup step makes.
            if (TMP_Settings.defaultFontAsset != null)
            {
                text.font = TMP_Settings.defaultFontAsset;
            }
            else
            {
                Debug.LogWarning(
                    "No TMP default font asset found while building the meta unlock popup. Import TMP Essentials " +
                    "(Window > TextMeshPro > Import TMP Essential Resources) and set the fonts by hand.");
            }

            return text;
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
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
        }
    }
}
