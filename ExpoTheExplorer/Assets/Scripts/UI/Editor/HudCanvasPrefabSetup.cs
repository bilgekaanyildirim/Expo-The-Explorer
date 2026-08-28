using System.Collections.Generic;
using System.Linq;
using ExpoTheExplorer.Bootstrap;
using ExpoTheExplorer.UI;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace ExpoTheExplorer.EditorTools
{
    // Migration tooling for D-013: one HUD Canvas prefab, instanced in both scenes.
    //
    // The HUD prefab is NOT created here -- it already exists, made by hand. So this
    // tool's job is only to finish it: add HudWalletSource and point the three views
    // at it. Both edits go into the PREFAB ASSET (via LoadPrefabContents /
    // SaveAsPrefabAsset), never into a scene instance, so every instance in every
    // scene inherits them instead of one scene silently disagreeing with another.
    //
    // The prefab is found BY CONTENT -- the prefab whose hierarchy carries a
    // SoftMoneyView -- rather than by a hardcoded path. An earlier version of this
    // file looked for "Assets/Prefabs/UI/HudCanvas.prefab" and a root named
    // "HUD Canvas"; the real asset is "HUDCanvas.prefab" with a root named
    // "HUDCanvas", and AssetDatabase paths are case-SENSITIVE while macOS's
    // filesystem is not, so a path constant failed in both directions at once.
    // Content matching cannot drift like that.
    //
    // Two menu items rather than one sweep: step 2 edits whichever scene you have
    // open, changes nothing else, and leaves it DIRTY so saving stays your call and
    // Ctrl+Z works. Step 1 needs no scene at all.
    //
    // Lives under Scripts/UI/Editor/ for the reason in blueprint.md: it references
    // HudWalletSource and the three views, which live in the predefined
    // Assembly-CSharp that an asmdef assembly cannot reference.
    public static class HudCanvasPrefabSetup
    {
        // The labels MainScreenSceneBuilder used to create for MainScreenView's own
        // wallet readout. The shared HUD owns that job now, so a MainScreen built
        // before D-013 shows one balance twice.
        private static readonly string[] RedundantMainScreenLabels =
        {
            "CoinsCaption", "CoinsValue", "GemsCaption", "GemsValue",
        };

        [MenuItem("ExpoTheExplorer/HUD/1. Wire HUD Prefab To Wallet Source")]
        public static void WirePrefab()
        {
            var path = FindHudPrefabPath();
            if (path == null) return;

            // The one correct way to edit a prefab asset from script: load its
            // contents into an isolated scene, change them, save, unload. Editing a
            // scene instance instead would leave the change as a per-scene override
            // that the other scene never sees.
            var root = PrefabUtility.LoadPrefabContents(path);
            try
            {
                var source = root.GetComponent<HudWalletSource>();
                if (source == null)
                {
                    source = root.AddComponent<HudWalletSource>();
                    Debug.Log($"Added {nameof(HudWalletSource)} to '{root.name}'.");
                }

                var wired = WireViews(root, source);
                if (wired.Count == 0)
                {
                    Debug.LogWarning(
                        $"No HUD views found under '{root.name}'. Nothing was wired -- is this the right prefab?");
                    return;
                }

                PrefabUtility.SaveAsPrefabAsset(root, path);
                AssetDatabase.SaveAssets();

                Debug.Log(
                    $"Wired {string.Join(", ", wired)} to {nameof(HudWalletSource)} in {path}. " +
                    "Every instance of this prefab now reads the wallet from the scene it is in: the live GameState in " +
                    "the day scene, the save file on the main screen. Next: open MainScreen and run step 2.");
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
        }

        [MenuItem("ExpoTheExplorer/HUD/2. Add HUD To Open Scene")]
        public static void AddToOpenScene()
        {
            var path = FindHudPrefabPath();
            if (path == null) return;

            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            var scene = SceneManager.GetActiveScene();

            var hud = FindPrefabInstance(scene, path);
            if (hud != null)
            {
                Debug.LogWarning($"'{scene.name}' already contains an instance of {path}; not adding a second one.");
            }
            else
            {
                hud = (GameObject)PrefabUtility.InstantiatePrefab(prefab, scene);
                Undo.RegisterCreatedObjectUndo(hud, "Add HUD Canvas");
                Debug.Log($"Added a {path} instance to '{scene.name}'.", hud);
            }

            BindGameManager(hud, scene);

            var removed = RemoveRedundantLabels();
            if (removed.Count > 0)
            {
                Debug.Log($"Deleted the now-duplicate label(s): {string.Join(", ", removed)}.");
            }

            EditorSceneManager.MarkSceneDirty(scene);
            Debug.Log(
                $"'{scene.name}' is left UNSAVED on purpose -- check the HUD sits where you want and nothing overlaps, " +
                "then save. Ctrl+Z backs this out.");
        }

        // Writes this scene's GameManager into the instance's HudWalletSource. The
        // reference is necessarily a prefab OVERRIDE -- a prefab asset cannot store a
        // scene reference -- and setting it here rather than leaving it to a manual
        // drag is the point: an unwired field is not an error, it means "read the save
        // file", so forgetting it in the day scene would fail silently.
        //
        // No GameManager in the scene (the main screen) is the expected case, not a
        // problem: the field stays empty and HudWalletSource reads the profile.
        private static void BindGameManager(GameObject hud, Scene scene)
        {
            var source = hud.GetComponentInChildren<HudWalletSource>(true);
            if (source == null)
            {
                Debug.LogError(
                    $"The HUD instance in '{scene.name}' has no {nameof(HudWalletSource)} -- run step 1 first, then " +
                    "delete and re-add this instance.", hud);
                return;
            }

            var gameManager = Object.FindAnyObjectByType<GameManager>();
            var serialized = new SerializedObject(source);
            var property = serialized.FindProperty("gameManager");
            if (property == null)
            {
                Debug.LogError(
                    $"{nameof(HudWalletSource)} has no serialized field 'gameManager' -- this tool is out of step with " +
                    "it and the reference is left unset.", source);
                return;
            }

            property.objectReferenceValue = gameManager;
            serialized.ApplyModifiedProperties();

            Debug.Log(
                gameManager != null
                    ? $"Wired '{gameManager.name}' into the HUD's {nameof(HudWalletSource)} for '{scene.name}' (a prefab " +
                      "override -- do not Apply All from this scene, it would drop it)."
                    : $"No GameManager in '{scene.name}', so the HUD's {nameof(HudWalletSource)} is left empty on purpose: " +
                      "it will read the save file here.",
                source);
        }

        // Identified by what it CONTAINS, not by name or path: the prefab whose
        // hierarchy has a SoftMoneyView is the HUD. Renaming the asset or moving it
        // cannot break this.
        private static string FindHudPrefabPath()
        {
            var matches = AssetDatabase.FindAssets("t:Prefab")
                .Select(AssetDatabase.GUIDToAssetPath)
                .Where(p => !string.IsNullOrEmpty(p))
                .Where(p =>
                {
                    var root = AssetDatabase.LoadAssetAtPath<GameObject>(p);
                    return root != null && root.GetComponentInChildren<SoftMoneyView>(true) != null;
                })
                .ToList();

            if (matches.Count == 0)
            {
                Debug.LogError(
                    $"Found no prefab containing a {nameof(SoftMoneyView)}. The HUD has to be a prefab before these " +
                    "steps can do anything -- drag the HUD Canvas into Assets/Prefabs/UI first.");
                return null;
            }

            if (matches.Count > 1)
            {
                Debug.LogWarning(
                    $"More than one prefab contains a {nameof(SoftMoneyView)}: {string.Join(", ", matches)}. " +
                    $"Using {matches[0]} -- delete the duplicates so there is one shared HUD, not several.");
            }

            return matches[0];
        }

        // Points every HUD view under the prefab root at the source beside them. The
        // views' fields are private [SerializeField]s, so this goes through
        // SerializedObject and the property-name string must stay in step with the
        // field name in each view.
        //
        // LivesView was a third target until D-064. It is deliberately NOT one now: the
        // heart row is a day-scene object rather than prefab content, and it binds to
        // GameManager directly, so there is no 'walletSource' on it to point anywhere.
        // Leaving it in the list would not merely be dead -- FindProperty would miss and
        // this tool would log its own out-of-step error on every run.
        private static List<string> WireViews(GameObject root, HudWalletSource source)
        {
            var wired = new List<string>();
            var targets = new List<MonoBehaviour>();
            targets.AddRange(root.GetComponentsInChildren<SoftMoneyView>(true));
            targets.AddRange(root.GetComponentsInChildren<GemsView>(true));

            // KeysView joins the list where LivesView left it (D-065 step 3). It belongs
            // here for the reason LivesView stopped belonging: this readout IS prefab
            // content shown on both screens, so its walletSource is prefab data that every
            // instance should inherit -- exactly what this step exists to write.
            targets.AddRange(root.GetComponentsInChildren<KeysView>(true));

            // DayNumberView joins on the same ticket as KeysView (D-130): the day badge is
            // prefab content shown on both screens, so its walletSource is prefab data
            // every instance should inherit. The COMPONENT is added by hand -- the DayUI
            // object was authored by hand and this tool does not create hierarchy -- but
            // the reference it needs is exactly what this step exists to write.
            targets.AddRange(root.GetComponentsInChildren<DayNumberView>(true));

            foreach (var view in targets)
            {
                var serialized = new SerializedObject(view);
                var property = serialized.FindProperty("walletSource");
                if (property == null)
                {
                    Debug.LogError(
                        $"{view.GetType().Name} has no serialized field 'walletSource' -- this tool is out of step " +
                        "with the view and its reference is left unwired.", view);
                    continue;
                }

                property.objectReferenceValue = source;
                serialized.ApplyModifiedPropertiesWithoutUndo();
                wired.Add(view.GetType().Name);
            }

            return wired;
        }

        // Name-matched and non-destructive when it misses: if these were renamed,
        // nothing is deleted and the log says which, rather than guessing.
        private static List<string> RemoveRedundantLabels()
        {
            var view = Object.FindAnyObjectByType<MainScreenView>();
            if (view == null) return new List<string>();

            var removed = new List<string>();
            foreach (var name in RedundantMainScreenLabels)
            {
                var label = view.GetComponentsInChildren<Transform>(true).FirstOrDefault(t => t.name == name);
                if (label == null) continue;

                removed.Add(name);
                Undo.DestroyObjectImmediate(label.gameObject);
            }

            var missing = RedundantMainScreenLabels.Except(removed).ToList();
            if (missing.Count > 0)
            {
                Debug.Log(
                    "Left alone (not found, so nothing was guessed at): " + string.Join(", ", missing) +
                    ". Delete them by hand if you renamed them.");
            }

            return removed;
        }

        private static GameObject FindPrefabInstance(Scene scene, string prefabPath)
        {
            return scene.GetRootGameObjects()
                .FirstOrDefault(go => PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(go) == prefabPath);
        }
    }
}
