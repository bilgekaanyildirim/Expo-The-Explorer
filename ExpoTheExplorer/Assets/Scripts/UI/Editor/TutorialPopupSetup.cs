using System.IO;
using ExpoTheExplorer.Data;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

namespace ExpoTheExplorer.UI.EditorTools
{
    // Menu step: builds the powerup tutorial's two popups as PREFABS, so the author can
    // restyle them (D-116, the user's ask on 2026-08-28).
    //
    // WHY A SCRIPT RATHER THAN HAND-WRITTEN .prefab YAML: a UI hierarchy of Canvas +
    // CanvasScaler + GraphicRaycaster + Images + TMP + Button is exactly the kind of YAML
    // that goes silently wrong -- a mistyped fileID produces a prefab that imports "fine"
    // and is missing a component. scene-structure.md names this the preferred path, and it
    // is the one PowerupShop and HUDCanvas already took.
    //
    // IT IS A ONE-SHOT SEED, NOT AN AUTHORITY. Run it once; from then on the prefabs belong
    // to whoever styles them. It REFUSES to overwrite an existing prefab for that reason --
    // the alternative is a menu item that silently throws away an afternoon of layout work.
    // Delete the asset first if you genuinely want the original back.
    //
    // The two prefabs reproduce what the two views used to build in code, so the first run
    // after this change looks identical to the last run before it.
    public static class TutorialPopupSetup
    {
        private const string PrefabFolder = "Assets/Prefabs/UI";
        private const string IntroPath = PrefabFolder + "/TutorialPowerupIntro.prefab";
        private const string SpotlightPath = PrefabFolder + "/TutorialPowerupSpotlight.prefab";
        private const string StepHintsPath = PrefabFolder + "/TutorialStepHints.prefab";

        // The reference resolution the game's own CanvasScaler uses. Authored here rather
        // than read off an open scene so the menu item works with no scene loaded; if the
        // game's reference ever changes, these prefabs are edited like any other asset.
        private static readonly Vector2 ReferenceResolution = new(1080f, 1920f);

        // Below Popup Canvas (Overlay, order 0) on purpose: the day is frozen while a panel
        // is up, but a tutorial panel floating OVER a popup would be worse than one behind it.
        private const int CanvasSortingOrder = -1;

        [MenuItem("ExpoTheExplorer/Tutorial/Build Powerup Popups")]
        private static void BuildBoth()
        {
            Directory.CreateDirectory(PrefabFolder);

            var built = 0;
            if (Build(IntroPath, BuildIntro)) built++;
            if (Build(SpotlightPath, BuildSpotlight)) built++;
            if (Build(StepHintsPath, BuildStepHints)) built++;

            if (built > 0)
            {
                AssetDatabase.SaveAssets();
                AssetDatabase.Refresh();
            }

            Debug.Log(built > 0
                ? $"Tutorial popups: built {built} prefab(s) under {PrefabFolder}."
                : "Tutorial popups: both prefabs already existed and were left untouched. Delete one to have it rebuilt.");

            WireStepHintsIntoConfig();
            WireOpenScene();
        }

        // The step message needs NO scene reference, which is the whole reason it lives on the
        // config: TutorialSpotlightView is built by WorldTrayView, a scene component with three
        // instances, so a prefab field there would be three drags that must never disagree.
        // BoardAnimationConfig is already handed to every one of them, and an asset pointing at
        // a prefab is an ordinary asset reference.
        //
        // Only fills an EMPTY field, for the reason Build refuses to overwrite a prefab: the
        // author may have pointed this at a variant of their own.
        private static void WireStepHintsIntoConfig()
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(StepHintsPath);
            if (prefab == null) return;

            // Found by TYPE across the project's assets, not by a hardcoded path: there is one
            // BoardAnimationConfig and its location is not this script's business.
            var guids = AssetDatabase.FindAssets("t:BoardAnimationConfig");
            if (guids.Length == 0)
            {
                Debug.LogWarning(
                    "Tutorial popups: no BoardAnimationConfig found, so the step-message prefab was built but not " +
                    "wired. Point its Tutorial Step Hints Prefab field at " + StepHintsPath + " by hand.");
                return;
            }

            var config = AssetDatabase.LoadAssetAtPath<BoardAnimationConfig>(AssetDatabase.GUIDToAssetPath(guids[0]));
            if (config == null) return;

            var serialized = new SerializedObject(config);
            var property = serialized.FindProperty("tutorialStepHintsPrefab");
            if (property == null || property.objectReferenceValue != null) return;

            property.objectReferenceValue = prefab;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(config);
            AssetDatabase.SaveAssets();
            Debug.Log($"Tutorial popups: wired the step hints into {config.name}.");
        }

        // The step that used to be a sentence in a log line telling the user to drag two
        // assets by hand. It does not have to be: assigning a prefab ASSET to a field on a
        // scene component is ordinary Editor work -- HudCanvasPrefabSetup and
        // MainScreenSessionSetup both do it -- and only a reference to another SCENE OBJECT
        // would be beyond a script that does not own the scene.
        //
        // Found by TYPE, never by name. A name lookup is banned in this project's editor
        // steps as well as at runtime, because it silently stops working the day someone
        // renames an object; a type lookup fails loudly by finding nothing.
        //
        // NOTHING IS PLACED IN THE SCENE, and that is worth saying because it is the obvious
        // misreading of "wire the popups in": these prefabs are INSTANTIATED at runtime by
        // PowerupBarView. A copy sitting in the hierarchy would be on screen from the first
        // frame of every day. What enters the scene is two asset references and nothing else.
        private static void WireOpenScene()
        {
            var bar = Object.FindAnyObjectByType<PowerupBarView>();
            if (bar == null)
            {
                Debug.Log(
                    "Tutorial popups: no PowerupBarView in the open scene, so nothing was wired. Open the day scene " +
                    "and run this again -- the prefabs themselves are already built.");
                return;
            }

            var intro = AssetDatabase.LoadAssetAtPath<GameObject>(IntroPath);
            var spotlight = AssetDatabase.LoadAssetAtPath<GameObject>(SpotlightPath);

            var serialized = new SerializedObject(bar);
            var wired = 0;
            wired += AssignIfEmpty(serialized, "introPrefab", intro != null ? intro.GetComponent<TutorialPowerupIntroView>() : null);
            wired += AssignIfEmpty(serialized, "spotlightPrefab", spotlight != null ? spotlight.GetComponent<TutorialPowerupSpotlightView>() : null);

            if (wired == 0)
            {
                Debug.Log($"Tutorial popups: {bar.name} already had both prefabs wired, so nothing was changed.");
                return;
            }

            serialized.ApplyModifiedPropertiesWithoutUndo();
            EditorSceneManager.MarkSceneDirty(bar.gameObject.scene);
            Debug.Log($"Tutorial popups: wired {wired} reference(s) onto '{bar.name}'. Save the scene to keep it.");
        }

        // Only fills an EMPTY field. Overwriting is how a script quietly replaces the variant
        // an author deliberately pointed this at -- the same reason Build refuses to
        // regenerate a prefab that already exists.
        private static int AssignIfEmpty(SerializedObject serialized, string fieldName, Object value)
        {
            if (value == null) return 0;

            var property = serialized.FindProperty(fieldName);
            if (property == null || property.objectReferenceValue != null) return 0;

            property.objectReferenceValue = value;
            return 1;
        }

        // Refusing beats overwriting: these become hand-styled assets the moment the author
        // touches them, and a rebuild would be indistinguishable from losing that work.
        private static bool Build(string path, System.Func<GameObject> build)
        {
            if (AssetDatabase.LoadAssetAtPath<GameObject>(path) != null)
            {
                Debug.Log($"Tutorial popups: {path} already exists, so it was left alone.");
                return false;
            }

            var root = build();
            try
            {
                PrefabUtility.SaveAsPrefabAsset(root, path);
            }
            finally
            {
                // Destroyed whether or not the save threw: this object only ever existed to
                // be serialized, and leaving it behind would drop a stray popup into the
                // author's open scene.
                Object.DestroyImmediate(root);
            }

            return true;
        }

        // ---- the intro panel -------------------------------------------------------------

        // Its ROOT carries the Screen Space - OVERLAY canvas, and that is load-bearing
        // (D-086): the day scene's InGameCanvas is Screen Space - CAMERA at sortingOrder -1,
        // so anything parented under it is drawn UNDER world sprites. Overlay is the only
        // mode that composites above everything the camera renders unconditionally.
        private static GameObject BuildIntro()
        {
            var root = new GameObject("TutorialPowerupIntro", typeof(RectTransform));
            var canvas = root.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = CanvasSortingOrder;
            AddScaler(root);

            // Modal, so it needs its own raycaster and a backdrop that swallows presses --
            // otherwise a tap aimed at the panel falls through to whatever is behind it. The
            // board is gated anyway, but relying on that would make this panel's correctness
            // depend on a rule living somewhere else.
            root.AddComponent<GraphicRaycaster>();
            var group = root.AddComponent<CanvasGroup>();

            var backdrop = NewRect("Backdrop", root.transform);
            Stretch(backdrop);
            backdrop.gameObject.AddComponent<Image>().color = new Color(0f, 0f, 0f, 0.78f);

            var fontSize = ReferenceResolution.y * 0.022f;
            var rowHeight = ReferenceResolution.y * 0.13f;

            var panel = NewRect("Panel", root.transform);
            panel.anchorMin = new Vector2(0.5f, 0.5f);
            panel.anchorMax = new Vector2(0.5f, 0.5f);
            panel.pivot = new Vector2(0.5f, 0.5f);
            panel.sizeDelta = new Vector2(ReferenceResolution.x * 0.86f, ReferenceResolution.y * (0.13f + 0.16f));
            panel.anchoredPosition = Vector2.zero;
            panel.gameObject.AddComponent<Image>().color = new Color(0.07f, 0.08f, 0.11f, 0.96f);

            var title = NewText("Title", panel, fontSize * 1.25f, TextAlignmentOptions.Center);
            title.rectTransform.anchorMin = new Vector2(0f, 1f);
            title.rectTransform.anchorMax = new Vector2(1f, 1f);
            title.rectTransform.pivot = new Vector2(0.5f, 1f);
            title.rectTransform.sizeDelta = new Vector2(0f, fontSize * 2f);
            title.rectTransform.anchoredPosition = new Vector2(0f, -fontSize);
            title.text = "Powerup";
            title.fontStyle = FontStyles.Bold;

            var row = NewRect("Row", panel);
            row.anchorMin = new Vector2(0f, 1f);
            row.anchorMax = new Vector2(1f, 1f);
            row.pivot = new Vector2(0.5f, 1f);
            row.sizeDelta = new Vector2(-fontSize * 2f, rowHeight);
            row.anchoredPosition = new Vector2(0f, -(fontSize * 3.2f));

            var iconWidth = panel.sizeDelta.x * 0.2f;
            var iconSize = Mathf.Min(iconWidth, rowHeight * 0.8f);

            var iconRect = NewRect("Icon", row);
            iconRect.anchorMin = new Vector2(0f, 0.5f);
            iconRect.anchorMax = new Vector2(0f, 0.5f);
            iconRect.pivot = new Vector2(0f, 0.5f);
            iconRect.sizeDelta = new Vector2(iconSize, iconSize);
            iconRect.anchoredPosition = Vector2.zero;

            var icon = iconRect.gameObject.AddComponent<Image>();

            // The button art is not necessarily square; keeping its aspect is what makes the
            // row read as "that button" rather than as a stretched approximation.
            icon.preserveAspect = true;

            // Text starts after the icon COLUMN rather than after the icon itself, so the row
            // stays aligned whether or not a sprite was found at runtime.
            var textRect = NewRect("Text", row);
            Stretch(textRect);
            textRect.offsetMin = new Vector2(iconWidth + fontSize * 0.6f, 0f);
            textRect.offsetMax = Vector2.zero;

            var description = NewText("Description", textRect, fontSize * 0.9f, TextAlignmentOptions.Left);
            Stretch(description.rectTransform);
            description.text = "What this powerup does.";
            description.color = new Color(0.82f, 0.85f, 0.9f);

            var buttonRect = NewRect("GotItButton", panel);
            buttonRect.anchorMin = new Vector2(0.5f, 0f);
            buttonRect.anchorMax = new Vector2(0.5f, 0f);
            buttonRect.pivot = new Vector2(0.5f, 0f);
            buttonRect.sizeDelta = new Vector2(ReferenceResolution.x * 0.34f, fontSize * 2.6f);
            buttonRect.anchoredPosition = new Vector2(0f, fontSize * 0.9f);

            var buttonImage = buttonRect.gameObject.AddComponent<Image>();
            buttonImage.color = new Color(0.24f, 0.55f, 0.36f, 1f);

            var label = NewText("Label", buttonRect, fontSize, TextAlignmentOptions.Center);
            Stretch(label.rectTransform);
            label.text = "Got it";
            label.fontStyle = FontStyles.Bold;

            var button = buttonRect.gameObject.AddComponent<Button>();
            button.targetGraphic = buttonImage;

            var view = root.AddComponent<TutorialPowerupIntroView>();
            var serialized = new SerializedObject(view);
            serialized.FindProperty("titleText").objectReferenceValue = title;
            serialized.FindProperty("descriptionText").objectReferenceValue = description;
            serialized.FindProperty("iconImage").objectReferenceValue = icon;
            serialized.FindProperty("dismissButton").objectReferenceValue = button;
            serialized.FindProperty("fadeGroup").objectReferenceValue = group;
            serialized.ApplyModifiedPropertiesWithoutUndo();

            return root;
        }

        // ---- the forced-press spotlight ---------------------------------------------------

        // No canvas on the ROOT: the frame is reparented onto the live powerup button at
        // runtime and inherits that button's canvas, which is what makes it sit on the button
        // rather than over it. Only the MESSAGE gets a canvas of its own, and it is Overlay
        // for the same D-086 reason the panel's is.
        private static GameObject BuildSpotlight()
        {
            var root = new GameObject("TutorialPowerupSpotlight", typeof(RectTransform));

            var thickness = ReferenceResolution.y * 0.006f;
            var gap = ReferenceResolution.y * 0.008f;

            // Stretched with a NEGATIVE inset, so the frame sits just outside whatever button
            // it is attached to. The view preserves these offsets across the reparent, which
            // is what makes the inset the author's decision rather than a constant in code.
            var frame = NewRect("Frame", root.transform);
            Stretch(frame);
            frame.offsetMin = new Vector2(-gap, -gap);
            frame.offsetMax = new Vector2(gap, gap);

            var frameGroup = frame.gameObject.AddComponent<CanvasGroup>();
            frameGroup.blocksRaycasts = false;
            frameGroup.interactable = false;

            var colour = new Color(1f, 0.86f, 0.35f, 1f);

            // Horizontals span the full width including the corners; verticals stop short of
            // them, so the four bars meet exactly once at each corner rather than stacking two
            // half-transparent layers there while the group fades.
            AddBar(frame, "Top", colour, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0.5f, 0f), new Vector2(0f, thickness));
            AddBar(frame, "Bottom", colour, new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(0.5f, 1f), new Vector2(0f, thickness));
            AddBar(frame, "Left", colour, new Vector2(0f, 0f), new Vector2(0f, 1f), new Vector2(1f, 0.5f), new Vector2(thickness, -thickness * 2f));
            AddBar(frame, "Right", colour, new Vector2(1f, 0f), new Vector2(1f, 1f), new Vector2(0f, 0.5f), new Vector2(thickness, -thickness * 2f));

            var messageCanvasObject = new GameObject("MessageCanvas", typeof(RectTransform));
            messageCanvasObject.transform.SetParent(root.transform, false);
            var messageCanvas = messageCanvasObject.AddComponent<Canvas>();
            messageCanvas.renderMode = RenderMode.ScreenSpaceOverlay;
            messageCanvas.sortingOrder = CanvasSortingOrder;
            AddScaler(messageCanvasObject);

            var fontSize = ReferenceResolution.y * 0.024f;
            var message = NewText("Message", (RectTransform)messageCanvasObject.transform, fontSize, TextAlignmentOptions.Center);
            message.rectTransform.anchorMin = new Vector2(0.5f, 0f);
            message.rectTransform.anchorMax = new Vector2(0.5f, 0f);
            message.rectTransform.pivot = new Vector2(0.5f, 0f);
            message.rectTransform.sizeDelta = new Vector2(ReferenceResolution.x * 0.8f, fontSize * 4f);
            message.text = "Press it now.";
            message.fontStyle = FontStyles.Bold;

            var view = root.AddComponent<TutorialPowerupSpotlightView>();
            var serialized = new SerializedObject(view);
            serialized.FindProperty("frame").objectReferenceValue = frame;
            serialized.FindProperty("frameGroup").objectReferenceValue = frameGroup;
            serialized.FindProperty("messageText").objectReferenceValue = message;
            serialized.FindProperty("messageCanvasRect").objectReferenceValue = messageCanvasObject.transform;
            serialized.FindProperty("messageGap").floatValue = fontSize * 1.2f;
            serialized.ApplyModifiedPropertiesWithoutUndo();

            return root;
        }

        // ---- everything a forced-move step draws ---------------------------------------------

        // THREE PIECES IN ONE PREFAB (D-126): the sentence, the arrow that marks the
        // modification on the board item, and the arrow that marks the matching row on the
        // ticket card. The step decides which appear -- `highlightModification` and a non-empty
        // message are independent flags -- so the view switches off whatever was not asked for.
        //
        // THE ROOT IS A PLAIN TRANSFORM, not a canvas, which is what lets the world arrow live
        // here at all: a SpriteRenderer under a Screen Space - Overlay canvas sits at screen
        // coordinates and looks broken in the prefab stage. The canvas is a CHILD holding the
        // message, and it must keep its Overlay render mode (D-086): on the ticket cards'
        // Screen Space - CAMERA canvas the message was drawn and then buried by the dim, every
        // single time.
        //
        // NEITHER ARROW STAYS WHERE IT IS AUTHORED -- the view reparents the world one onto the
        // board item's modification layer and the canvas one into the ticket card's row,
        // keeping the local transform each was given. Position them against a stand-in here and
        // they land exactly there on the real thing.
        private static GameObject BuildStepHints()
        {
            // THE ROOT IS THE CANVAS, and that is what makes this prefab editable at all. It
            // was a plain RectTransform with the canvas as a CHILD for one round -- needed then
            // to host a world-space SpriteRenderer arrow -- and the prefab stage showed an
            // empty frame, because Unity DRIVES an Overlay canvas's rect from the screen and
            // only gives that treatment to a ROOT canvas. A nested one sat at 0x0 and collapsed
            // every child into it. Authoring a size does not help either: the Canvas overwrites
            // it. With both arrows now UI Images there is nothing left that needs a non-canvas
            // root.
            var root = new GameObject("TutorialStepHints", typeof(RectTransform));
            var canvas = root.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = CanvasSortingOrder;
            AddScaler(root);

            var fontSize = ReferenceResolution.y * 0.026f;
            var arrowSprite = AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/Knob.psd");

            var plate = NewRect("Plate", root.transform);
            plate.anchorMin = new Vector2(0.06f, 0.28f);
            plate.anchorMax = new Vector2(0.94f, 0.28f);
            plate.pivot = new Vector2(0.5f, 0.5f);
            plate.anchoredPosition = Vector2.zero;

            // Width comes purely from the anchors above; NewRect's default 100 would otherwise
            // add a hundred pixels to an already-stretched plate.
            plate.sizeDelta = new Vector2(0f, plate.sizeDelta.y);

            var plateImage = plate.gameObject.AddComponent<Image>();

            // Opaque enough to read as a PANEL rather than the 0.55 shadow it used to be --
            // the user's actual complaint was legibility, and a plate you can see through is
            // most of it.
            plateImage.color = new Color(0.05f, 0.06f, 0.09f, 0.9f);
            plateImage.raycastTarget = false;

            var layout = plate.gameObject.AddComponent<VerticalLayoutGroup>();
            layout.childControlHeight = true;
            layout.childControlWidth = true;
            layout.childForceExpandHeight = false;
            layout.childForceExpandWidth = true;
            var padX = Mathf.RoundToInt(fontSize * 0.6f);
            var padY = Mathf.RoundToInt(fontSize * 0.45f);
            layout.padding = new RectOffset(padX, padX, padY, padY);

            // Width comes from the anchors, height from the text. The padding above is what
            // keeps the plate non-zero even for an empty string.
            var fitter = plate.gameObject.AddComponent<ContentSizeFitter>();
            fitter.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            var text = NewText("Text", plate, fontSize, TextAlignmentOptions.Center);
            text.text = "The step's message goes here.";

            // BOTH ARROWS ARE UI IMAGES ON THIS CANVAS (the user's ask, 2026-08-28), and that
            // is what makes them editable at all. The item arrow was a SpriteRenderer parked
            // outside the canvas and the card arrow was an Image parented OUTSIDE one -- an
            // Image with no Canvas above it never renders, so the prefab stage showed nothing
            // to click.
            //
            // THE ITEM ARROW is positioned over the board item at runtime through screen
            // space, and its anchoredPosition here is kept as the OFFSET from the ingredient.
            // Anchored centre so that offset reads as "from the item", not "from a corner".
            var itemArrowRect = NewRect("ItemArrow", root.transform);
            itemArrowRect.anchorMin = new Vector2(0.5f, 0.5f);
            itemArrowRect.anchorMax = new Vector2(0.5f, 0.5f);
            itemArrowRect.pivot = new Vector2(0.5f, 0.5f);
            itemArrowRect.sizeDelta = new Vector2(fontSize * 2.2f, fontSize * 2.2f);
            itemArrowRect.anchoredPosition = new Vector2(fontSize * 1.4f, fontSize * 1.4f);
            itemArrowRect.localRotation = Quaternion.Euler(0f, 0f, -135f);
            var itemArrow = itemArrowRect.gameObject.AddComponent<Image>();
            itemArrow.sprite = arrowSprite;
            itemArrow.raycastTarget = false;

            // THE CARD ARROW is reparented INTO the ticket card's modification row, so it rides
            // that card. Anchored to the row's left edge with its own right edge (the tip) as
            // the pivot, so the tip lands just outside the box however wide the row is and the
            // body extends away from the card rather than across it.
            //
            // The right-hand side was tried first and was wrong twice over: the arrow ended up
            // far from the ingredient it labels, and rotating it 180 degrees to aim back turned
            // it about that same pivot, swinging the body over the row and leaving the tip in
            // the middle of the box. No rotation is needed -- the sprite points right already.
            var cardArrowRect = NewRect("CardArrow", root.transform);
            cardArrowRect.anchorMin = new Vector2(0f, 0.5f);
            cardArrowRect.anchorMax = new Vector2(0f, 0.5f);
            cardArrowRect.pivot = new Vector2(1f, 0.5f);
            cardArrowRect.sizeDelta = new Vector2(fontSize * 1.6f, fontSize * 1.6f);
            cardArrowRect.anchoredPosition = new Vector2(-fontSize * 0.25f, 0f);
            var cardArrow = cardArrowRect.gameObject.AddComponent<Image>();
            cardArrow.sprite = arrowSprite;
            cardArrow.raycastTarget = false;

            var hints = root.AddComponent<TutorialStepHints>();
            var serialized = new SerializedObject(hints);
            serialized.FindProperty("messageRoot").objectReferenceValue = plate.gameObject;
            serialized.FindProperty("messageText").objectReferenceValue = text;
            serialized.FindProperty("itemArrow").objectReferenceValue = itemArrow;
            serialized.FindProperty("cardArrow").objectReferenceValue = cardArrow;
            serialized.ApplyModifiedPropertiesWithoutUndo();

            return root;
        }

        // ---- shared -----------------------------------------------------------------------

        private static void AddScaler(GameObject canvasObject)
        {
            var scaler = canvasObject.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = ReferenceResolution;
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = 0.5f;
        }

        private static void AddBar(RectTransform frame, string name, Color colour,
            Vector2 anchorMin, Vector2 anchorMax, Vector2 pivot, Vector2 sizeDelta)
        {
            var bar = NewRect(name, frame);
            bar.anchorMin = anchorMin;
            bar.anchorMax = anchorMax;
            bar.pivot = pivot;
            bar.sizeDelta = sizeDelta;
            bar.anchoredPosition = Vector2.zero;

            var image = bar.gameObject.AddComponent<Image>();
            image.color = colour;

            // Never a raycast target: the button underneath is the thing the player has to
            // hit, and a decoration that swallowed the press would make the step impossible in
            // exactly the way that looks like a broken button.
            image.raycastTarget = false;
        }

        private static RectTransform NewRect(string name, Transform parent)
        {
            var rect = (RectTransform)new GameObject(name, typeof(RectTransform)).transform;
            rect.SetParent(parent, false);
            return rect;
        }

        private static TextMeshProUGUI NewText(string name, RectTransform parent, float size, TextAlignmentOptions alignment)
        {
            var rect = NewRect(name, parent);
            var text = rect.gameObject.AddComponent<TextMeshProUGUI>();
            text.fontSize = size;
            text.alignment = alignment;
            text.color = Color.white;
            text.raycastTarget = false;
            text.textWrappingMode = TextWrappingModes.Normal;

            // Assigned explicitly so a missing TMP Essentials import fails loudly rather than
            // producing an invisible label -- the same call the other setup steps make.
            if (TMP_Settings.defaultFontAsset != null)
            {
                text.font = TMP_Settings.defaultFontAsset;
            }
            else
            {
                Debug.LogWarning(
                    "No TMP default font asset found while building the tutorial popups. Import TMP Essentials " +
                    "(Window > TextMeshPro > Import TMP Essential Resources) and set the fonts by hand.");
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
