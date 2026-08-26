using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using ExpoTheExplorer.UI;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace ExpoTheExplorer.EditorTools
{
    // One-shot construction script for the MainScreen scene (decisions.md D-012).
    // Run it once from the menu; after that the scene is yours to restyle, and this
    // REFUSES to overwrite it -- re-running can never wipe hand-made work.
    //
    // Why a construction script instead of a committed .unity file: a Canvas + TMP
    // scene hand-written as serialized YAML is the kind of edit D-007 already
    // declined to make on this project's one scene, and it cannot be reviewed. This
    // builds the same thing in ~150 readable lines and also fixes Build Settings,
    // which a scene file cannot do for itself.
    //
    // Why it lives under Scripts/UI/Editor/ rather than in Assets/Editor/: it has to
    // reference MainScreenView, which lives in the predefined Assembly-CSharp
    // (Scripts/UI has no asmdef). Assets/Editor/ carries ExpoTheExplorer.Editor.asmdef,
    // and an asmdef assembly cannot reference a predefined one -- so an Editor folder
    // OUTSIDE any asmdef, which compiles into Assembly-CSharp-Editor, is the only
    // place this can compile at all. shards.json still files it under the editor
    // shard: its first pattern is **/Editor/**.
    public static class MainScreenSceneBuilder
    {
        private const string ScenePath = "Assets/Scenes/MainScreen.unity";
        private const string DayScenePath = "Assets/Scenes/SampleScene.unity";

        // Matches the gameplay Canvas in SampleScene (1080x1920 portrait, match 0.5)
        // so the two screens scale identically on a device rather than drifting.
        private static readonly Vector2 ReferenceResolution = new(1080f, 1920f);

        [MenuItem("ExpoTheExplorer/Create MainScreen Scene")]
        public static void CreateMainScreenScene()
        {
            if (File.Exists(ScenePath))
            {
                Debug.LogWarning(
                    $"{ScenePath} already exists -- not touching it. Delete it first if you really want it rebuilt from scratch " +
                    "(you will lose any styling done in the Editor).");
                return;
            }

            // The build replaces whatever scene is open, so give unsaved work a
            // chance to survive; a cancel here aborts everything.
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;

            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            CreateCamera();
            CreateEventSystem();
            var view = CreateUi();

            if (!EditorSceneManager.SaveScene(scene, ScenePath))
            {
                Debug.LogError($"Failed to save the new scene to {ScenePath}.");
                return;
            }

            AddScenesToBuildSettings();
            AssetDatabase.Refresh();

            Debug.Log(
                $"Created {ScenePath} with {nameof(MainScreenView)} wired ({(view != null ? "ok" : "FAILED")}), " +
                "and put it first in Build Settings so a launch opens it. Style it freely -- this menu item will not " +
                "overwrite it again.", view);
        }

        private static void CreateCamera()
        {
            var cameraObject = new GameObject("Main Camera", typeof(Camera));
            cameraObject.tag = "MainCamera";

            var camera = cameraObject.GetComponent<Camera>();
            camera.orthographic = true;
            camera.orthographicSize = 5f;
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(0.09f, 0.11f, 0.16f);
            cameraObject.transform.position = new Vector3(0f, 0f, -10f);
        }

        private static void CreateEventSystem()
        {
            var eventSystemObject = new GameObject("EventSystem", typeof(EventSystem));

            // Resolved by name rather than referenced: this project drives input
            // through the Input System package, whose UI module is the right one to
            // add, but hard-referencing it would make this file fail to compile in a
            // checkout where that package is absent. StandaloneInputModule is the
            // fallback and still works.
            var inputModuleType =
                Type.GetType("UnityEngine.InputSystem.UI.InputSystemUIInputModule, Unity.InputSystem")
                ?? typeof(StandaloneInputModule);

            eventSystemObject.AddComponent(inputModuleType);
        }

        private static MainScreenView CreateUi()
        {
            var canvasObject = new GameObject("Canvas", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));

            var canvas = canvasObject.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;

            var scaler = canvasObject.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = ReferenceResolution;
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = 0.5f;

            var root = canvasObject.transform;

            // Buttons, and nothing else (decisions.md D-025). The title, the "DAY"
            // caption and the day number were all removed on the user's instruction: the
            // day number now lives on the button itself as "Continue Day X", and the meta
            // grounds are what fills this screen. Building them here again would put the
            // builder and the scene permanently at odds.
            //
            // No coins/gems labels either: since D-013 the shared HUD Canvas prefab
            // displays the wallet in this scene, so a second pair would put one balance on
            // screen twice.
            //
            // The caption reads "PLAY" only until MainScreenView's Start overwrites it with
            // the day number -- it is a placeholder for the Editor, not authored text.
            var playButton = CreateButton("PlayButton", root, "PLAY", new Vector2(0f, -20f), new Vector2(520f, 170f));

            // A Start Over button used to be built here too (decisions.md D-026), muted red
            // under Play. D-095 moved that capability into the SRDebugger debug panel, so a
            // freshly built MainScreen no longer ships a button whose job is erasing the
            // save -- and MainScreenResetButtonSetup, the step that retrofitted it into an
            // already-built scene, went with it.
            var view = canvasObject.AddComponent<MainScreenView>();
            WireView(view, playButton);
            return view;
        }

        // The view's references are private [SerializeField]s -- assigning them from
        // an editor script goes through SerializedObject, and the property names
        // below must stay in step with the field names in MainScreenView.
        private static void WireView(MainScreenView view, Button playButton)
        {
            var serialized = new SerializedObject(view);
            var wiring = new Dictionary<string, UnityEngine.Object>
            {
                ["playButton"] = playButton,
            };

            foreach (var pair in wiring)
            {
                var property = serialized.FindProperty(pair.Key);
                if (property == null)
                {
                    Debug.LogError(
                        $"{nameof(MainScreenView)} has no serialized field '{pair.Key}' -- this builder is out of step " +
                        "with the view and the reference is left unwired.");
                    continue;
                }

                property.objectReferenceValue = pair.Value;
            }

            serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        private static TMP_Text CreateText(
            string name,
            Transform parent,
            string text,
            float fontSize,
            Vector2 anchoredPosition,
            Vector2 size)
        {
            var textObject = new GameObject(name, typeof(RectTransform));
            textObject.transform.SetParent(parent, false);

            var label = textObject.AddComponent<TextMeshProUGUI>();
            label.text = text;
            label.fontSize = fontSize;
            label.alignment = TextAlignmentOptions.Center;
            label.color = Color.white;

            // TMP normally resolves this itself on first use, but assigning it here
            // means a missing TMP Essentials import fails LOUDLY at build time
            // rather than producing an invisible label nobody can explain.
            if (TMP_Settings.defaultFontAsset != null)
            {
                label.font = TMP_Settings.defaultFontAsset;
            }
            else
            {
                Debug.LogWarning(
                    $"No TMP default font asset found while creating '{name}'. Import TMP Essentials " +
                    "(Window > TextMeshPro > Import TMP Essential Resources), then delete and rebuild the scene.");
            }

            SetRect((RectTransform)textObject.transform, anchoredPosition, size);
            return label;
        }

        private static Button CreateButton(
            string name,
            Transform parent,
            string label,
            Vector2 anchoredPosition,
            Vector2 size,
            float fontSize = 64f)
        {
            var buttonObject = new GameObject(name, typeof(RectTransform));
            buttonObject.transform.SetParent(parent, false);

            var background = buttonObject.AddComponent<Image>();
            background.color = new Color(0.20f, 0.55f, 0.32f);

            // Unity's own built-in UI sprite, the same one the GameObject > UI menu
            // uses -- gives the grey-box button rounded corners instead of a raw
            // rectangle. Absent in some project setups, hence the null tolerance.
            var builtinSprite = AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/UISprite.psd");
            if (builtinSprite != null)
            {
                background.sprite = builtinSprite;
                background.type = Image.Type.Sliced;
            }

            var button = buttonObject.AddComponent<Button>();
            button.targetGraphic = background;

            SetRect((RectTransform)buttonObject.transform, anchoredPosition, size);

            var textLabel = CreateText($"{name}Label", buttonObject.transform, label, fontSize, Vector2.zero, size);
            StretchToParent((RectTransform)textLabel.transform);

            return button;
        }

        // Everything is anchored to the canvas centre, so a position is a plain
        // offset from the middle of the screen at the reference resolution above.
        private static void SetRect(RectTransform rect, Vector2 anchoredPosition, Vector2 size)
        {
            rect.anchorMin = new Vector2(0.5f, 0.5f);
            rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.sizeDelta = size;
            rect.anchoredPosition = anchoredPosition;
        }

        private static void StretchToParent(RectTransform rect)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.sizeDelta = Vector2.zero;
            rect.anchoredPosition = Vector2.zero;
        }

        // MainScreen goes FIRST: index 0 is what a built player loads on launch, and
        // the whole point of this scene is to be where the game opens. Any other
        // scene already in the list is kept, in its existing order, behind it.
        private static void AddScenesToBuildSettings()
        {
            var existing = EditorBuildSettings.scenes
                .Where(s => s.path != ScenePath)
                .ToList();

            var ordered = new List<EditorBuildSettingsScene>
            {
                new(ScenePath, true),
            };

            if (existing.All(s => s.path != DayScenePath) && File.Exists(DayScenePath))
            {
                ordered.Add(new EditorBuildSettingsScene(DayScenePath, true));
            }

            ordered.AddRange(existing);
            EditorBuildSettings.scenes = ordered.ToArray();
        }
    }
}
