using ExpoTheExplorer.UI;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

namespace ExpoTheExplorer.UI.EditorTools
{
    // Adds the Start Over button to a MainScreen that already exists, and wires it to
    // MainScreenView (decisions.md D-026).
    //
    // Why a step of its own rather than a line in MainScreenSceneBuilder: that builder is
    // a one-shot and REFUSES to touch an existing scene, which is the property that keeps
    // it from wiping hand-made styling. The scene in this project was built long ago, so
    // the builder alone can never deliver this button. Hand-editing MainScreen.unity's
    // YAML was the other option and is what D-007 declined to do on this project's scenes:
    // it cannot be reviewed.
    //
    // Same posture as the other setup steps: it touches only the scene that is already
    // open, does NOT save, and refuses to rebuild an existing button -- if one is there it
    // only refreshes the reference, so a position nudged by hand is never overwritten.
    internal static class MainScreenResetButtonSetup
    {
        // Matches SceneFlow.MainScreenSceneName. Named here rather than referenced for the
        // same reason MainScreenSessionSetup names it: an editor helper with no other
        // reason to depend on Core.
        private const string SceneName = "MainScreen";
        private const string ButtonName = "ResetButton";

        // Deliberately smaller than Play and directly beneath it: this is the button that
        // destroys progress, so it must not read as the screen's main action. The gap is
        // measured off Play's own rect at run time, so a Play button that has since been
        // moved or resized still gets this one under it rather than on top of it.
        private static readonly Vector2 ButtonSize = new(320f, 90f);
        private const float GapBelowPlay = 40f;

        [MenuItem("ExpoTheExplorer/Main Screen/Add Reset Progress Button")]
        private static void Add()
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

            var view = Object.FindAnyObjectByType<MainScreenView>();
            if (view == null)
            {
                EditorUtility.DisplayDialog(
                    "No MainScreenView",
                    "This scene has no MainScreenView, so there is nothing to wire the button to.",
                    "OK");
                return;
            }

            var serialized = new SerializedObject(view);
            var resetProperty = serialized.FindProperty("resetButton");
            if (resetProperty == null)
            {
                Debug.LogError(
                    $"{nameof(MainScreenView)} has no serialized field 'resetButton' — this step is out of step with " +
                    "the view and nothing was wired.", view);
                return;
            }

            var playButton = FindPlayButton(serialized);
            if (playButton == null)
            {
                EditorUtility.DisplayDialog(
                    "Play button not wired",
                    "MainScreenView's playButton is empty, and this step places the reset button underneath it. " +
                    "Wire Play first.",
                    "OK");
                return;
            }

            // The serialized field is the ONLY record of whether the button exists. There
            // used to be a name-based fallback here; it is gone under the project rule
            // adopted 2026-08-21 (no name lookups), and losing it costs nothing real --
            // a name search is what lets a step build a duplicate after a rename while
            // believing it found nothing.
            var existing = (Button)resetProperty.objectReferenceValue;

            TMP_Text label = null;
            var button = existing;
            if (button == null)
            {
                button = Create(playButton, out label);
            }

            resetProperty.objectReferenceValue = button;

            // Only filled when this step created the caption. A button the author wired
            // themselves keeps whatever label they pointed at -- overwriting it would be
            // this step guessing, which is the thing being removed.
            var labelProperty = serialized.FindProperty("resetLabel");
            if (labelProperty != null && label != null && labelProperty.objectReferenceValue == null)
            {
                labelProperty.objectReferenceValue = label;
            }

            serialized.ApplyModifiedProperties();

            EditorSceneManager.MarkSceneDirty(scene);
            Selection.activeObject = button.gameObject;

            Debug.Log(
                existing != null
                    ? $"'{button.name}' was already in the scene; reference refreshed and its layout left alone. Scene is dirty — save it yourself."
                    : $"Added '{button.name}' under the Play button and wired it. It erases the save after TWO taps. Scene is dirty — save it yourself.",
                button);
        }

        private static Button FindPlayButton(SerializedObject serializedView)
        {
            var property = serializedView.FindProperty("playButton");
            return property?.objectReferenceValue as Button;
        }

        // Creates the button AND hands back the caption it made, so the caller can wire
        // both fields. The caption is not searched for afterwards -- the object that built
        // it is the one that knows what it is.
        private static Button Create(Button playButton, out TMP_Text label)
        {
            var playRect = (RectTransform)playButton.transform;

            var buttonObject = new GameObject(ButtonName, typeof(RectTransform));
            Undo.RegisterCreatedObjectUndo(buttonObject, "Add Reset Progress Button");
            buttonObject.transform.SetParent(playRect.parent, worldPositionStays: false);

            var background = buttonObject.AddComponent<Image>();
            // Muted red: destructive, and visibly not the green Play button. Rough on
            // purpose -- colour is quicker to nudge in the Inspector than to argue about
            // here, and this step never touches it again.
            background.color = new Color(0.45f, 0.18f, 0.18f);

            var builtinSprite = AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/UISprite.psd");
            if (builtinSprite != null)
            {
                background.sprite = builtinSprite;
                background.type = Image.Type.Sliced;
            }

            var button = buttonObject.AddComponent<Button>();
            button.targetGraphic = background;

            var rect = (RectTransform)buttonObject.transform;
            rect.anchorMin = playRect.anchorMin;
            rect.anchorMax = playRect.anchorMax;
            rect.pivot = playRect.pivot;
            rect.sizeDelta = ButtonSize;
            rect.anchoredPosition = playRect.anchoredPosition -
                                    new Vector2(0f, playRect.sizeDelta.y * 0.5f + ButtonSize.y * 0.5f + GapBelowPlay);

            label = CreateLabel(rect);
            return button;
        }

        // The caption is authored data from here on: MainScreenView caches whatever the
        // scene says at Start and only swaps in its confirm text, so editing this label in
        // the Editor is how the button gets renamed -- no code change.
        private static TMP_Text CreateLabel(RectTransform parent)
        {
            var labelObject = new GameObject($"{ButtonName}Label", typeof(RectTransform));
            labelObject.transform.SetParent(parent, worldPositionStays: false);

            var label = labelObject.AddComponent<TextMeshProUGUI>();
            label.text = "Start Over";
            label.fontSize = 34;
            label.alignment = TextAlignmentOptions.Center;
            label.color = Color.white;
            label.raycastTarget = false;

            // Assigned explicitly so a missing TMP Essentials import fails loudly rather
            // than producing an invisible label, the same call MainScreenSceneBuilder makes.
            if (TMP_Settings.defaultFontAsset != null)
            {
                label.font = TMP_Settings.defaultFontAsset;
            }
            else
            {
                Debug.LogWarning(
                    "No TMP default font asset found while creating the reset button's label. Import TMP Essentials " +
                    "(Window > TextMeshPro > Import TMP Essential Resources) and set the font by hand.");
            }

            var rect = (RectTransform)labelObject.transform;
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.sizeDelta = Vector2.zero;
            rect.anchoredPosition = Vector2.zero;

            return label;
        }
    }
}
