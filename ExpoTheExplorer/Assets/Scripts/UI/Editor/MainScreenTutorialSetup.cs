using System.IO;
using ExpoTheExplorer.Data;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

namespace ExpoTheExplorer.UI.EditorTools
{
    // Wires the main screen's first-run tutorial: creates its text asset if there isn't one,
    // puts a MainScreenTutorialView in the open scene, and assigns the three references it
    // needs (decisions.md D-089). Replaces the six-step manual list D-088 left behind.
    //
    // Same posture as MetaShopSetup and the other meta steps: it touches only the scene that
    // is already open, it does NOT save, and it refuses to REBUILD -- run it twice and the
    // second run re-assigns references on the object that is already there rather than making
    // a second one, so anything moved or restyled by hand survives.
    //
    // Nothing is looked up by NAME -- project rule since 2026-08-21 (D-028), whose cautionary
    // tale is MetaGroundsSetup: after its root was renamed, its name search found nothing and
    // it would have built a duplicate hierarchy while logging that it had not. Everything here
    // is resolved by TYPE, which cannot be renamed out from under it.
    internal static class MainScreenTutorialSetup
    {
        private const string SceneName = "MainScreen";
        private const string ConfigPath = "Assets/Data/TutorialTextConfig.asset";

        // Only used when CREATING the object, never to find it again.
        private const string RootName = "MainScreenTutorial";

        private const string PrefabFolder = "Assets/Prefabs/UI";
        private const string WelcomePrefabPath = PrefabFolder + "/MainScreenTutorialWelcome.prefab";
        private const string StoreHintPrefabPath = PrefabFolder + "/MainScreenTutorialStoreHint.prefab";

        // The reference resolution the main screen's own CanvasScaler uses. Authored here so
        // the seeds are built with no scene loaded; from the first edit the prefabs own it.
        private static readonly Vector2 ReferenceResolution = new(1080f, 1920f);

        // Above the main screen's own canvas (0) so the welcome panel is not buried. It was a
        // serialized field on the view until D-123 and is now the prefab's own canvas setting;
        // this is only the seed value.
        private const int CanvasSortingOrder = 50;

        [MenuItem("ExpoTheExplorer/Tutorial/Set Up Main Screen Tutorial")]
        private static void SetUp()
        {
            var scene = EditorSceneManager.GetActiveScene();
            if (scene.name != SceneName)
            {
                Debug.LogError(
                    $"Set Up Main Screen Tutorial: the open scene is '{scene.name}', not '{SceneName}'. Open the " +
                    "main screen scene and run this again.");
                return;
            }

            // Resolved before anything is created, so a scene missing a piece is left exactly
            // as it was rather than half-wired -- the failure mode that makes a setup script
            // worse than no setup script.
            var root = Object.FindFirstObjectByType<MainScreenRoot>(FindObjectsInactive.Include);
            if (root == null)
            {
                Debug.LogError(
                    "Set Up Main Screen Tutorial: no MainScreenRoot in this scene, so the tutorial would have no way " +
                    "to tell whether this is a new player. Run ExpoTheExplorer > Meta > Wire MainScreen Session first.");
                return;
            }

            var shop = Object.FindFirstObjectByType<MetaShopView>(FindObjectsInactive.Include);
            if (shop == null)
            {
                Debug.LogError(
                    "Set Up Main Screen Tutorial: no MetaShopView in this scene, so there is no store button to " +
                    "point at. Run ExpoTheExplorer > Meta > Build Meta Shop first.");
                return;
            }

            var canvas = shop.GetComponentInParent<Canvas>();
            if (canvas == null)
            {
                Debug.LogError(
                    "Set Up Main Screen Tutorial: the shop is not under a Canvas, so the tutorial has nowhere to " +
                    "live. Check the scene's UI hierarchy.");
                return;
            }

            var texts = LoadOrCreateConfig();

            var view = Object.FindFirstObjectByType<MainScreenTutorialView>(FindObjectsInactive.Include);
            var created = view == null;
            if (created)
            {
                var host = new GameObject(RootName, typeof(RectTransform));
                Undo.RegisterCreatedObjectUndo(host, "Set Up Main Screen Tutorial");
                host.transform.SetParent(canvas.transform, false);
                view = Undo.AddComponent<MainScreenTutorialView>(host);
            }

            // Assigned through SerializedObject rather than by making the fields public: the
            // three references stay private, which is what keeps them un-writable at run time.
            Undo.RecordObject(view, "Set Up Main Screen Tutorial");
            // BUILT BEFORE THE SerializedObject IS OPENED, deliberately. These calls write
            // assets and run AssetDatabase.SaveAssets, and doing that with unapplied edits
            // pending on a live SerializedObject is asking for the whole block to be dropped
            // -- which would take sessionHost, shop and texts down with it and leave the
            // tutorial silently unwired.
            var welcomePrefab = EnsureWelcomePrefab();
            var storeHintPrefab = EnsureStoreHintPrefab();

            var serialized = new SerializedObject(view);
            serialized.FindProperty("sessionHost").objectReferenceValue = root;
            serialized.FindProperty("shop").objectReferenceValue = shop;
            serialized.FindProperty("texts").objectReferenceValue = texts;

            // The two popups became authored prefabs in D-123, so this step seeds them and
            // assigns them here -- the whole point of putting the references on this view
            // rather than anywhere else is that there is exactly ONE of it, so nothing has
            // to be dragged.
            AssignIfEmpty(serialized, "welcomePrefab", welcomePrefab);
            AssignIfEmpty(serialized, "storeHintPrefab", storeHintPrefab);

            serialized.ApplyModifiedProperties();

            EditorUtility.SetDirty(view);

            // The Play gate's half of the wiring (D-090). Done here rather than in a second
            // menu item because the two are one feature: the gate is useless without a
            // tutorial view to answer with, and a player who ran only one of two setup steps
            // would get a Play button that blocks and explains nothing.
            //
            // Absence is reported, not fatal: MainScreenView is entitled to exist without a
            // tutorial, and its gate is written to let the player through when unwired.
            var mainScreen = Object.FindFirstObjectByType<MainScreenView>(FindObjectsInactive.Include);
            if (mainScreen != null)
            {
                Undo.RecordObject(mainScreen, "Set Up Main Screen Tutorial");
                var mainScreenSerialized = new SerializedObject(mainScreen);
                mainScreenSerialized.FindProperty("sessionHost").objectReferenceValue = root;
                mainScreenSerialized.FindProperty("tutorial").objectReferenceValue = view;
                mainScreenSerialized.ApplyModifiedProperties();
                EditorUtility.SetDirty(mainScreen);
            }
            else
            {
                Debug.LogWarning(
                    "Set Up Main Screen Tutorial: no MainScreenView in this scene, so Play cannot be gated on buying " +
                    "the first building. The welcome and store hint are wired regardless.");
            }

            EditorSceneManager.MarkSceneDirty(scene);

            Debug.Log(
                $"Set Up Main Screen Tutorial: {(created ? "created" : "re-wired")} '{view.name}' under " +
                $"'{canvas.name}', pointing at {shop.name} and reading {Path.GetFileName(ConfigPath)}. " +
                "The scene is marked dirty but NOT saved -- save it yourself, or undo if this was a mistake.",
                view);
        }

        // Reuses an existing asset and leaves its text ALONE. Re-running this must never
        // overwrite wording someone has edited, which is the whole reason the words moved out
        // of the code in the first place.
        private static TutorialTextConfig LoadOrCreateConfig()
        {
            var existing = AssetDatabase.LoadAssetAtPath<TutorialTextConfig>(ConfigPath);
            if (existing != null) return existing;

            var directory = Path.GetDirectoryName(ConfigPath);
            if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
                AssetDatabase.Refresh();
            }

            // Created with no field assignments: TutorialTextConfig's own initializers ARE the
            // drafted wording, so the asset arrives readable and there is exactly one place
            // that spelling lives.
            var config = ScriptableObject.CreateInstance<TutorialTextConfig>();
            AssetDatabase.CreateAsset(config, ConfigPath);
            AssetDatabase.SaveAssets();

            Debug.Log($"Set Up Main Screen Tutorial: created {ConfigPath}. Edit it to change what the tutorial says.", config);
            return config;
        }

        // ---- the two popup prefabs (D-123) --------------------------------------------------

        // Only fills an EMPTY field, the same refusal the rest of this step makes: run it twice
        // and the second run must not replace a variant the author pointed the view at.
        private static void AssignIfEmpty(SerializedObject serialized, string fieldName, Object value)
        {
            if (value == null) return;

            var property = serialized.FindProperty(fieldName);
            if (property == null || property.objectReferenceValue != null) return;

            property.objectReferenceValue = value;
        }

        // REFUSES to rebuild an existing prefab, for the reason this whole step refuses to
        // rebuild the scene object: from the author's first edit these are hand-styled assets,
        // and a regeneration would be indistinguishable from losing that work. Delete the asset
        // to have it seeded again.
        private static T EnsurePrefab<T>(string path, System.Func<GameObject> build) where T : Component
        {
            var existing = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (existing != null)
            {
                var component = existing.GetComponent<T>();
                if (component == null)
                {
                    // A prefab whose binder does not resolve is unusable, and D-125 is how one
                    // got made: the two binders lived in another script's file, so Unity had no
                    // MonoScript to bind them to and wrote `m_Script: {fileID: 0}`.
                    //
                    // REFUSING rather than rebuilding, deliberately. A rebuild here is tempting
                    // and wrong: during a compile error every script unloads, so this same check
                    // returns null for perfectly good prefabs, and regenerating then would
                    // destroy real styling work. Naming the file to delete is the safe half.
                    Debug.LogError(
                        $"Set Up Main Screen Tutorial: '{path}' has no {typeof(T).Name} on it — its script does not " +
                        "resolve, so the prefab cannot be used. Delete that asset from the Project window and run this " +
                        "again to have it rebuilt. (If the console is also showing compile errors, fix those first: " +
                        "unloaded scripts look exactly like this.)");
                }

                return component;
            }

            Directory.CreateDirectory(PrefabFolder);

            var root = build();
            GameObject saved;
            try
            {
                saved = PrefabUtility.SaveAsPrefabAsset(root, path);
            }
            finally
            {
                // Destroyed whether or not the save threw: this object existed only to be
                // serialized, and leaving it behind drops a stray popup into the open scene.
                Object.DestroyImmediate(root);
            }

            AssetDatabase.SaveAssets();
            Debug.Log($"Set Up Main Screen Tutorial: built {path}.");
            return saved != null ? saved.GetComponent<T>() : null;
        }

        private static MainScreenWelcomePopup EnsureWelcomePrefab() =>
            EnsurePrefab<MainScreenWelcomePopup>(WelcomePrefabPath, BuildWelcome);

        private static MainScreenStoreHintPopup EnsureStoreHintPrefab() =>
            EnsurePrefab<MainScreenStoreHintPopup>(StoreHintPrefabPath, BuildStoreHint);

        // MODAL: a full-screen backdrop with its own raycaster, so a tap aimed at the panel
        // cannot fall through to the Play button underneath and start a day the player has not
        // read about yet. Deleting that backdrop in the prefab silently ends that guarantee.
        private static GameObject BuildWelcome()
        {
            var root = NewCanvasRoot("MainScreenTutorialWelcome");
            root.AddComponent<GraphicRaycaster>();

            var fontSize = ReferenceResolution.y * 0.024f;

            var backdrop = NewRect("Backdrop", root.transform);
            Stretch(backdrop);
            backdrop.gameObject.AddComponent<Image>().color = new Color(0f, 0f, 0f, 0.78f);

            var panel = NewRect("Panel", root.transform);
            panel.anchorMin = new Vector2(0.5f, 0.5f);
            panel.anchorMax = new Vector2(0.5f, 0.5f);
            panel.sizeDelta = new Vector2(ReferenceResolution.x * 0.84f, ReferenceResolution.y * 0.34f);
            panel.gameObject.AddComponent<Image>().color = new Color(0.07f, 0.08f, 0.11f, 0.96f);

            var title = NewText("Title", panel, fontSize * 1.4f);
            title.rectTransform.anchorMin = new Vector2(0f, 1f);
            title.rectTransform.anchorMax = new Vector2(1f, 1f);
            title.rectTransform.pivot = new Vector2(0.5f, 1f);
            title.rectTransform.sizeDelta = new Vector2(-fontSize * 2f, fontSize * 2.2f);
            title.rectTransform.anchoredPosition = new Vector2(0f, -fontSize * 1.2f);
            title.fontStyle = FontStyles.Bold;
            title.text = "Welcome";

            var body = NewText("Body", panel, fontSize);
            Stretch(body.rectTransform);
            body.rectTransform.offsetMin = new Vector2(fontSize * 1.4f, fontSize * 4.2f);
            body.rectTransform.offsetMax = new Vector2(-fontSize * 1.4f, -fontSize * 4f);
            body.color = new Color(0.86f, 0.89f, 0.94f);
            body.text = "What this game is.";

            var (button, label) = BuildButton("StartButton", panel, fontSize);

            var popup = root.AddComponent<MainScreenWelcomePopup>();
            var serialized = new SerializedObject(popup);
            serialized.FindProperty("title").objectReferenceValue = title;
            serialized.FindProperty("body").objectReferenceValue = body;
            serialized.FindProperty("dismissLabel").objectReferenceValue = label;
            serialized.FindProperty("dismissButton").objectReferenceValue = button;
            serialized.ApplyModifiedPropertiesWithoutUndo();

            return root;
        }

        // NOT modal and no backdrop: the hint asks the player to tap the store, so the store
        // has to stay tappable.
        //
        // The PLATE hugs its text (VerticalLayoutGroup + ContentSizeFitter) rather than taking
        // the fixed `fontSize * 6.4` this replaces -- that constant carried a comment working
        // out how to keep the text band and the button band from overlapping, which is exactly
        // the arithmetic that breaks the next time somebody changes a font size.
        private static GameObject BuildStoreHint()
        {
            var root = NewCanvasRoot("MainScreenTutorialStoreHint");
            root.AddComponent<GraphicRaycaster>();

            var fontSize = ReferenceResolution.y * 0.030f;

            // Authored, where this used to be a texture generated in code. It starts under the
            // canvas and is reparented onto the store button at runtime -- the view sizes and
            // positions it there, because only the running layout knows how big that button is.
            // Rotated -90 so the arrow, drawn pointing right, aims DOWN at the button.
            var arrow = NewRect("Arrow", root.transform);
            arrow.pivot = new Vector2(0.5f, 0.5f);
            arrow.sizeDelta = new Vector2(fontSize * 2f, fontSize * 2f);
            arrow.localRotation = Quaternion.Euler(0f, 0f, -90f);
            var arrowImage = arrow.gameObject.AddComponent<Image>();
            arrowImage.raycastTarget = false;
            arrowImage.sprite = AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/Knob.psd");

            var plate = NewRect("Plate", root.transform);
            plate.anchorMin = new Vector2(0.08f, 0.18f);
            plate.anchorMax = new Vector2(0.92f, 0.18f);
            plate.pivot = new Vector2(0.5f, 0f);
            plate.gameObject.AddComponent<Image>().color = new Color(0.07f, 0.08f, 0.11f, 0.94f);

            var layout = plate.gameObject.AddComponent<VerticalLayoutGroup>();
            layout.childControlHeight = true;
            layout.childControlWidth = true;
            layout.childForceExpandHeight = false;
            layout.childForceExpandWidth = true;
            layout.spacing = fontSize * 0.5f;
            var pad = Mathf.RoundToInt(fontSize * 0.6f);
            layout.padding = new RectOffset(pad, pad, pad, pad);

            var fitter = plate.gameObject.AddComponent<ContentSizeFitter>();
            fitter.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            var text = NewText("Text", plate, fontSize);
            text.text = "What the hint says.";

            var (button, label) = BuildButton("GotItButton", plate, fontSize * 0.9f);

            var popup = root.AddComponent<MainScreenStoreHintPopup>();
            var serialized = new SerializedObject(popup);
            serialized.FindProperty("arrow").objectReferenceValue = arrow;
            serialized.FindProperty("text").objectReferenceValue = text;
            serialized.FindProperty("dismissLabel").objectReferenceValue = label;
            serialized.FindProperty("dismissButton").objectReferenceValue = button;
            serialized.ApplyModifiedPropertiesWithoutUndo();

            return root;
        }

        // ---- shared prefab building --------------------------------------------------------

        // A ROOT canvas, never a child of the view. A Canvas nested inside another Canvas
        // inherits its parent's RectTransform rather than the screen's, and the view's own
        // rect is zero-sized -- which once crushed the hint to one character per line down a
        // sliver of the screen. Instantiated parentless at runtime for the same reason.
        private static GameObject NewCanvasRoot(string name)
        {
            var root = new GameObject(name, typeof(RectTransform));
            var canvas = root.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = CanvasSortingOrder;

            var scaler = root.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = ReferenceResolution;
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = 0.5f;

            return root;
        }

        private static (Button Button, TMP_Text Label) BuildButton(string name, RectTransform parent, float fontSize)
        {
            var rect = NewRect(name, parent);
            rect.sizeDelta = new Vector2(ReferenceResolution.x * 0.34f, fontSize * 2.6f);

            // Inside a layout group the height has to be asked for, not set: the group drives
            // the rect, and a sizeDelta alone would be overwritten on the first layout pass.
            var element = rect.gameObject.AddComponent<LayoutElement>();
            element.preferredHeight = fontSize * 2.6f;
            element.preferredWidth = ReferenceResolution.x * 0.34f;
            element.flexibleWidth = 0f;

            var image = rect.gameObject.AddComponent<Image>();
            image.color = new Color(0.24f, 0.55f, 0.36f, 1f);

            var label = NewText("Label", rect, fontSize);
            Stretch(label.rectTransform);
            label.fontStyle = FontStyles.Bold;
            label.text = "Got it";

            var button = rect.gameObject.AddComponent<Button>();
            button.targetGraphic = image;

            return (button, label);
        }

        private static RectTransform NewRect(string name, Transform parent)
        {
            var rect = (RectTransform)new GameObject(name, typeof(RectTransform)).transform;
            rect.SetParent(parent, false);
            return rect;
        }

        private static TextMeshProUGUI NewText(string name, Transform parent, float size)
        {
            var rect = NewRect(name, parent);
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
                    "No TMP default font asset found while building the main screen tutorial popups. Import TMP " +
                    "Essentials (Window > TextMeshPro > Import TMP Essential Resources) and set the fonts by hand.");
            }

            return text;
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
