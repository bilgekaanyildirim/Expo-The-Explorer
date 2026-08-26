using System.IO;
using ExpoTheExplorer.Data;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

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
            var serialized = new SerializedObject(view);
            serialized.FindProperty("sessionHost").objectReferenceValue = root;
            serialized.FindProperty("shop").objectReferenceValue = shop;
            serialized.FindProperty("texts").objectReferenceValue = texts;
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
    }
}
