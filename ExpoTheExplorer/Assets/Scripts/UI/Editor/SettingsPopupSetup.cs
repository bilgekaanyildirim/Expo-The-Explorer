using System.Collections.Generic;
using ExpoTheExplorer.Bootstrap;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

namespace ExpoTheExplorer.UI.EditorTools
{
    // Builds the day scene's settings menu and wires every serialized field on
    // SettingsPopupView (decisions.md D-094).
    //
    // Why a step rather than a page of Inspector instructions: this popup is FOURTEEN
    // required references across three nested panels, and a hand-built one is thirteen
    // chances to wire the Yes button of one confirmation into the other. The
    // MainScreenResetButtonSetup posture is kept exactly -- it touches only the scene that
    // is already open, it NEVER saves, and it refuses to rebuild a menu that is already
    // there, so styling done by hand afterwards is never overwritten.
    //
    // IT BUILDS ITS OWN CANVAS instead of hanging off the scene's. That keeps it away from
    // the shared HUD prefab INSTANCE, where an accidental Apply All would push a day-scene
    // menu onto the main screen -- the hazard the blueprint already warns about for the
    // heart row. The sorting order puts it over the HUD and over the two popups, which is
    // correct because the open button removes itself whenever either of those is up.
    //
    // The captions written here are PLACEHOLDERS in the language the rest of the scene
    // uses, meant to be restyled and reworded in the Inspector. They are text in an EDITOR
    // script, which is a different thing from the runtime invariant: SettingsPopupView
    // itself contains no authored string at all, which is why both confirmations get their
    // own panel instead of sharing one whose question code would have to write.
    internal static class SettingsPopupSetup
    {
        // Matches SceneFlow.DaySceneName. Named here rather than referenced, the same call
        // the other setup steps make: an editor helper with no other reason to depend on Core.
        private const string SceneName = "SampleScene";

        private const string CanvasName = "SettingsCanvas";

        // Above the HUD, and BELOW Popup Canvas since D-135 raised that to 300. The order
        // against the Game Over and Day Complete popups still decides nothing -- they are
        // never on screen with this menu, since the open button greys out and Open()
        // refuses once the day is over -- but the no-keys popup SHARES that canvas and is
        // opened BY this menu, so it has to land on top or it explains itself to the back
        // of a panel. What this number still guarantees is the part that was always the
        // point: the menu covers the HUD it pauses.
        private const int SortingOrder = 200;

        private static readonly Color PanelColor = new(0.10f, 0.12f, 0.18f, 0.98f);
        private static readonly Color BackdropColor = new(0f, 0f, 0f, 0.72f);
        private static readonly Color ButtonColor = new(0.22f, 0.26f, 0.36f, 1f);
        private static readonly Color DangerColor = new(0.45f, 0.20f, 0.22f, 1f);

        // The scene's haptics binder, found once per run and handed to every button this
        // step creates. A field rather than a parameter threaded through nine call sites:
        // it is set at the top of Build and read only during that same call, so its life
        // is one menu click even though the class is static.
        private static HapticsBinder haptics;

        [MenuItem("ExpoTheExplorer/Settings Popup/Build In Day Scene")]
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

            var gameManager = Object.FindAnyObjectByType<GameManager>();
            if (gameManager == null)
            {
                EditorUtility.DisplayDialog(
                    "No GameManager",
                    "This scene has no GameManager, so the menu would have nothing to pause and no way out of the day.",
                    "OK");
                return;
            }

            var existing = Object.FindAnyObjectByType<SettingsPopupView>();
            if (existing != null)
            {
                EditorUtility.DisplayDialog(
                    "Already built",
                    $"'{existing.name}' already carries a {nameof(SettingsPopupView)}. This step refuses to rebuild it, " +
                    "so nothing you have restyled is overwritten. Delete that object first if you want a fresh one.",
                    "OK");
                Selection.activeObject = existing.gameObject;
                return;
            }

            haptics = Object.FindAnyObjectByType<HapticsBinder>();

            // The scene's existing out-of-keys popup, so the retry confirmation is gated
            // the same way the Game Over popup's Retry is (D-135). Found rather than
            // built: there is one per scene by design (it needs a scene sessionHost, which
            // a prefab asset could not carry), and building a second would put two windows
            // on the same question. A scene without one leaves the field empty, which
            // SettingsPopupView treats as "never gate" -- warned about below, because the
            // quiet version of that is a free retry nobody notices.
            var noKeysPopup = Object.FindAnyObjectByType<NoKeysPopupView>();

            var canvas = CreateCanvas();
            var view = canvas.gameObject.AddComponent<SettingsPopupView>();

            var openButton = CreateButton(canvas.transform, "OpenButton", "II", ButtonColor, new Vector2(120f, 120f));
            Anchor(openButton, new Vector2(1f, 1f), new Vector2(-100f, -100f));

            var popupRoot = CreateFullScreen(canvas.transform, "PopupRoot", BackdropColor);
            var panel = CreatePanel(popupRoot.transform, "Panel", new Vector2(900f, 1180f));

            var title = CreateText(panel, "Title", "SETTINGS", 64f);
            Anchor(title.gameObject, new Vector2(0.5f, 1f), new Vector2(0f, -110f), new Vector2(700f, 90f));

            // "DAY" and the number are two objects on purpose: the WORD is a label the
            // author owns and can translate in the Inspector, and the NUMBER is the only
            // thing the runtime writes. That split is what keeps authored text out of C#.
            var dayLabel = CreateText(panel, "DayLabel", "DAY", 44f);
            Anchor(dayLabel.gameObject, new Vector2(0.5f, 1f), new Vector2(-90f, -230f), new Vector2(300f, 70f));

            var dayNumber = CreateText(panel, "DayNumber", "1", 44f);
            Anchor(dayNumber.gameObject, new Vector2(0.5f, 1f), new Vector2(160f, -230f), new Vector2(200f, 70f));

            var filledStars = CreateStarRow(panel);

            var hapticsButton = CreateButton(panel.transform, "HapticsToggleButton", "HAPTICS", ButtonColor, new Vector2(620f, 130f));
            Anchor(hapticsButton, new Vector2(0.5f, 1f), new Vector2(0f, -540f));

            // Two objects rather than one whose sprite swaps: swapping a sprite from code
            // means the code holds both sprites, and this way the author can make "on" and
            // "off" look like anything at all -- a tick, a colour, a whole word.
            var hapticsOn = CreateText(hapticsButton.transform, "OnIndicator", "ON", 40f);
            Anchor(hapticsOn.gameObject, new Vector2(1f, 0.5f), new Vector2(-90f, 0f), new Vector2(160f, 70f));

            var hapticsOff = CreateText(hapticsButton.transform, "OffIndicator", "OFF", 40f);
            Anchor(hapticsOff.gameObject, new Vector2(1f, 0.5f), new Vector2(-90f, 0f), new Vector2(160f, 70f));
            hapticsOff.gameObject.SetActive(false);

            var resumeButton = CreateButton(panel.transform, "ResumeButton", "RESUME", ButtonColor, new Vector2(620f, 140f));
            Anchor(resumeButton, new Vector2(0.5f, 1f), new Vector2(0f, -720f));

            var retryButton = CreateButton(panel.transform, "RetryButton", "RETRY DAY", ButtonColor, new Vector2(620f, 140f));
            Anchor(retryButton, new Vector2(0.5f, 1f), new Vector2(0f, -880f));

            var mainMenuButton = CreateButton(panel.transform, "MainMenuButton", "MAIN MENU", DangerColor, new Vector2(620f, 140f));
            Anchor(mainMenuButton, new Vector2(0.5f, 1f), new Vector2(0f, -1040f));

            // The two confirmations are siblings of Panel, not children, so they cover the
            // menu rather than sitting inside it. Built inactive: SettingsPopupView also
            // switches them off in Start, but a scene that LOOKS right when you open it is
            // worth more than one that only corrects itself at play time.
            // BOTH QUESTIONS NAME THE KEY (D-135), because both buttons now spend one --
            // and a resource that leaves without warning reads as a bug, not as a price.
            // It is the last line of each question for the same reason it is the last
            // thing the player decides on: the sentence above says what happens to the
            // day, this one says what it costs.
            var retryConfirm = CreateConfirmation(
                popupRoot.transform,
                "RetryConfirm",
                "Restart this day?\nEverything you have done today is lost.\nThis will cost 1 key.",
                out var retryYes,
                out var retryNo);

            var mainMenuConfirm = CreateConfirmation(
                popupRoot.transform,
                "MainMenuConfirm",
                "Leave this day?\nThis attempt's earnings are taken back.\nThis will cost 1 key.",
                out var mainMenuYes,
                out var mainMenuNo);

            popupRoot.SetActive(false);

            var serialized = new SerializedObject(view);
            var unwired = new List<string>();

            Wire(serialized, "gameManager", gameManager, unwired);
            Wire(serialized, "popupRoot", popupRoot, unwired);
            Wire(serialized, "openButton", openButton.GetComponent<Button>(), unwired);
            Wire(serialized, "resumeButton", resumeButton.GetComponent<Button>(), unwired);
            Wire(serialized, "retryButton", retryButton.GetComponent<Button>(), unwired);
            Wire(serialized, "mainMenuButton", mainMenuButton.GetComponent<Button>(), unwired);
            Wire(serialized, "retryConfirmRoot", retryConfirm, unwired);
            Wire(serialized, "retryConfirmYesButton", retryYes, unwired);
            Wire(serialized, "retryConfirmNoButton", retryNo, unwired);
            Wire(serialized, "mainMenuConfirmRoot", mainMenuConfirm, unwired);
            Wire(serialized, "mainMenuConfirmYesButton", mainMenuYes, unwired);
            Wire(serialized, "mainMenuConfirmNoButton", mainMenuNo, unwired);
            Wire(serialized, "hapticsToggleButton", hapticsButton.GetComponent<Button>(), unwired);
            Wire(serialized, "noKeysPopup", noKeysPopup, unwired);
            Wire(serialized, "hapticsOnIndicator", hapticsOn.gameObject, unwired);
            Wire(serialized, "hapticsOffIndicator", hapticsOff.gameObject, unwired);
            Wire(serialized, "dayNumberText", dayNumber, unwired);
            WireArray(serialized, "filledStars", filledStars, unwired);

            serialized.ApplyModifiedProperties();

            EditorSceneManager.MarkSceneDirty(scene);
            Selection.activeObject = canvas.gameObject;

            if (unwired.Count > 0)
            {
                // A field this step could not find is a rename that already happened: the
                // view moved and this builder did not. Loud, and specific about which one.
                Debug.LogError(
                    $"{nameof(SettingsPopupSetup)} built the menu but could not wire: {string.Join(", ", unwired)}. " +
                    $"Those field names are out of step with {nameof(SettingsPopupView)} — wire them by hand and fix this step.",
                    view);
            }

            if (noKeysPopup == null)
            {
                Debug.LogWarning(
                    $"{nameof(SettingsPopupSetup)}: this scene has no {nameof(NoKeysPopupView)}, so the menu's Retry is " +
                    "not gated on keys — at zero keys it will restart the day for free. Add the popup to the scene and " +
                    "drag it into the 'noKeysPopup' slot.",
                    view);
            }

            Debug.Log(
                $"Built '{CanvasName}' and wired {nameof(SettingsPopupView)}. Two things are still yours: drag the scene's " +
                $"{nameof(HapticsBinder)} object into ITS OWN new 'sessionHost' slot so the haptics switch is read, and " +
                "restyle the placeholder captions. Scene is dirty — save it yourself.",
                view);
        }

        private static Canvas CreateCanvas()
        {
            var canvasObject = new GameObject(CanvasName, typeof(RectTransform));
            Undo.RegisterCreatedObjectUndo(canvasObject, "Build Settings Popup");

            var canvas = canvasObject.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = SortingOrder;

            var scaler = canvasObject.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;

            // Portrait, matched on WIDTH -- the same shape the rest of this mobile game's
            // UI is authored at. Nudge it in the Inspector if the project's other canvases
            // disagree; this step never touches it again.
            scaler.referenceResolution = new Vector2(1080f, 1920f);
            scaler.matchWidthOrHeight = 0f;

            canvasObject.AddComponent<GraphicRaycaster>();

            return canvas;
        }

        // The backdrop is not decoration: it is what stops a paused player from still
        // dragging food around behind the menu. raycastTarget stays TRUE for that reason.
        private static GameObject CreateFullScreen(Transform parent, string name, Color color)
        {
            var fullScreen = new GameObject(name, typeof(RectTransform));
            fullScreen.transform.SetParent(parent, worldPositionStays: false);

            var rect = (RectTransform)fullScreen.transform;
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;

            var image = fullScreen.AddComponent<Image>();
            image.color = color;
            image.raycastTarget = true;

            return fullScreen;
        }

        private static GameObject CreatePanel(Transform parent, string name, Vector2 size)
        {
            var panel = new GameObject(name, typeof(RectTransform));
            panel.transform.SetParent(parent, worldPositionStays: false);

            var rect = (RectTransform)panel.transform;
            rect.anchorMin = new Vector2(0.5f, 0.5f);
            rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.sizeDelta = size;
            rect.anchoredPosition = Vector2.zero;

            var image = panel.AddComponent<Image>();
            image.color = PanelColor;

            return panel;
        }

        private static GameObject CreateButton(Transform parent, string name, string caption, Color color, Vector2 size)
        {
            var buttonObject = new GameObject(name, typeof(RectTransform));
            buttonObject.transform.SetParent(parent, worldPositionStays: false);

            var rect = (RectTransform)buttonObject.transform;
            rect.sizeDelta = size;

            var image = buttonObject.AddComponent<Image>();
            image.color = color;

            buttonObject.AddComponent<Button>().targetGraphic = image;

            // Every button in this menu buzzes on tap through the component that already
            // exists for it, rather than through a haptics field on the view -- the reason
            // HapticButton was written (see its codemap note).
            //
            // Its binder is wired HERE, at edit time, which is a different thing from the
            // runtime lookup this project has ruled out: the reference ends up serialized
            // in the scene exactly as a drag would leave it, and eight identical drags are
            // eight chances to miss one. Left empty when the scene has no binder, which
            // HapticButton already treats as "do not buzz".
            var hapticButton = buttonObject.AddComponent<HapticButton>();
            if (haptics != null)
            {
                var serializedHapticButton = new SerializedObject(hapticButton);
                var property = serializedHapticButton.FindProperty("haptics");
                if (property != null)
                {
                    property.objectReferenceValue = haptics;
                    serializedHapticButton.ApplyModifiedProperties();
                }
            }

            var label = CreateText(buttonObject, "Label", caption, 44f);
            var labelRect = (RectTransform)label.transform;
            labelRect.anchorMin = Vector2.zero;
            labelRect.anchorMax = Vector2.one;
            labelRect.offsetMin = Vector2.zero;
            labelRect.offsetMax = Vector2.zero;

            return buttonObject;
        }

        private static TMP_Text CreateText(GameObject parent, string name, string content, float size)
        {
            return CreateText(parent.transform, name, content, size);
        }

        private static TMP_Text CreateText(Transform parent, string name, string content, float size)
        {
            var textObject = new GameObject(name, typeof(RectTransform));
            textObject.transform.SetParent(parent, worldPositionStays: false);

            var text = textObject.AddComponent<TextMeshProUGUI>();
            text.text = content;
            text.fontSize = size;
            text.alignment = TextAlignmentOptions.Center;
            text.color = Color.white;
            text.raycastTarget = false;

            return text;
        }

        // Three slots, each an empty star with a filled child on top -- the SAME shape the
        // heart row uses, and for the same reason: switching a slot off would take its
        // filled child with it, so only the children are ever toggled.
        private static GameObject[] CreateStarRow(GameObject panel)
        {
            var filled = new GameObject[3];

            for (var i = 0; i < filled.Length; i++)
            {
                var slot = new GameObject($"Star{i + 1}", typeof(RectTransform));
                slot.transform.SetParent(panel.transform, worldPositionStays: false);

                var slotImage = slot.AddComponent<Image>();
                slotImage.color = new Color(1f, 1f, 1f, 0.18f);
                slotImage.raycastTarget = false;

                var rect = (RectTransform)slot.transform;
                rect.sizeDelta = new Vector2(110f, 110f);
                Anchor(slot, new Vector2(0.5f, 1f), new Vector2((i - 1) * 130f, -380f));

                var filledObject = new GameObject("Filled", typeof(RectTransform));
                filledObject.transform.SetParent(slot.transform, worldPositionStays: false);

                var filledImage = filledObject.AddComponent<Image>();
                filledImage.color = new Color(1f, 0.82f, 0.28f, 1f);
                filledImage.raycastTarget = false;

                var filledRect = (RectTransform)filledObject.transform;
                filledRect.anchorMin = Vector2.zero;
                filledRect.anchorMax = Vector2.one;
                filledRect.offsetMin = Vector2.zero;
                filledRect.offsetMax = Vector2.zero;

                filledObject.SetActive(false);
                filled[i] = filledObject;
            }

            return filled;
        }

        private static GameObject CreateConfirmation(Transform parent, string name, string question, out Button yes, out Button no)
        {
            var root = CreateFullScreen(parent, name, BackdropColor);
            var panel = CreatePanel(root.transform, "Panel", new Vector2(880f, 560f));

            var text = CreateText(panel, "Question", question, 46f);
            Anchor(text.gameObject, new Vector2(0.5f, 1f), new Vector2(0f, -160f), new Vector2(760f, 240f));

            var yesObject = CreateButton(panel.transform, "YesButton", "YES", DangerColor, new Vector2(340f, 130f));
            Anchor(yesObject, new Vector2(0.5f, 0f), new Vector2(-190f, 120f));
            yes = yesObject.GetComponent<Button>();

            var noObject = CreateButton(panel.transform, "NoButton", "NO", ButtonColor, new Vector2(340f, 130f));
            Anchor(noObject, new Vector2(0.5f, 0f), new Vector2(190f, 120f));
            no = noObject.GetComponent<Button>();

            root.SetActive(false);
            return root;
        }

        private static void Anchor(GameObject target, Vector2 anchor, Vector2 position, Vector2? size = null)
        {
            var rect = (RectTransform)target.transform;
            rect.anchorMin = anchor;
            rect.anchorMax = anchor;
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = position;

            if (size.HasValue) rect.sizeDelta = size.Value;
        }

        // Collects the names it could not find rather than throwing on the first one, so a
        // renamed field is reported once with the whole list instead of one run at a time.
        private static void Wire(SerializedObject serialized, string fieldName, Object value, List<string> unwired)
        {
            var property = serialized.FindProperty(fieldName);
            if (property == null)
            {
                unwired.Add(fieldName);
                return;
            }

            property.objectReferenceValue = value;
        }

        private static void WireArray(SerializedObject serialized, string fieldName, GameObject[] values, List<string> unwired)
        {
            var property = serialized.FindProperty(fieldName);
            if (property == null)
            {
                unwired.Add(fieldName);
                return;
            }

            property.arraySize = values.Length;
            for (var i = 0; i < values.Length; i++)
            {
                property.GetArrayElementAtIndex(i).objectReferenceValue = values[i];
            }
        }
    }
}
