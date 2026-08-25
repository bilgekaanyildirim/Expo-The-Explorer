using ExpoTheExplorer.Data;
using ExpoTheExplorer.Session;
using ExpoTheExplorer.UI;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace ExpoTheExplorer.UI.EditorTools
{
    // One menu step that gives the open MainScreen a session and points its HUD at it.
    // The alternative was three manual drags an author has to remember, one of which
    // (HudWalletSource's host) fails QUIETLY when forgotten -- the HUD just keeps reading
    // the save file and looks right until a purchase does not update the coin count.
    //
    // Same posture as HudCanvasPrefabSetup: it touches only the scene that is already
    // open, and it does NOT save. Saving would commit changes before they have been looked
    // at and would throw away the undo stack, so that stays the author's call.
    //
    // It lives under Scripts/UI/Editor/ rather than Assets/Editor/ for the reason
    // blueprint.md records for that folder: it references MainScreenRoot and
    // HudWalletSource, which are in the predefined Assembly-CSharp, and an asmdef
    // assembly cannot reference a predefined one at all.
    internal static class MainScreenSessionSetup
    {
        [MenuItem("ExpoTheExplorer/Meta/Wire MainScreen Session")]
        private static void Wire()
        {
            var scene = EditorSceneManager.GetActiveScene();
            if (scene.name != SceneFlowSceneName)
            {
                EditorUtility.DisplayDialog(
                    "Wrong scene",
                    $"Open the '{SceneFlowSceneName}' scene first — this step only touches the scene that is already open, " +
                    "so that saving stays your decision.",
                    "OK");
                return;
            }

            var root = Object.FindAnyObjectByType<MainScreenRoot>();
            if (root == null)
            {
                var host = new GameObject("--Session--");
                Undo.RegisterCreatedObjectUndo(host, "Wire MainScreen Session");
                root = Undo.AddComponent<MainScreenRoot>(host);
            }

            AssignConfigs(root);
            var hudWired = PointHudAtSession(root);

            EditorSceneManager.MarkSceneDirty(scene);

            Debug.Log(
                $"MainScreen session wired: configs assigned on '{root.name}'" +
                (hudWired ? ", HudWalletSource pointed at it." : ", but NO HudWalletSource was found in the scene — the HUD will keep reading the save file.") +
                " The scene is dirty; save it yourself.",
                root);

            // Reported separately and as a WARNING, because this one is not a degraded
            // screen -- MainScreenRoot refuses to build a session without a KeyConfig, so
            // the main screen simply will not run. Saying it here, at the moment the tool
            // could not fill the field, is far cheaper than finding it at play time.
            if (new SerializedObject(root).FindProperty("keyConfig")?.objectReferenceValue == null)
            {
                Debug.LogWarning(
                    $"No {nameof(KeyConfig)} asset was found, so '{root.name}' still has that field empty and " +
                    "will refuse to build a session. Create one via Create > ExpoTheExplorer > Data > Key Config, " +
                    "then run this menu item again.",
                    root);
            }

            // A plain Log rather than a Warning, and the difference is the point: this
            // field failing open is the DESIGNED behaviour (GDD 5.2), so an author who
            // has not built the powerup shop yet should not be nagged in yellow. It is
            // said at all because an empty field and a player with no charges look the
            // same on screen.
            if (new SerializedObject(root).FindProperty("powerupConfig")?.objectReferenceValue == null)
            {
                Debug.Log(
                    $"No {nameof(PowerupConfig)} asset was found, so '{root.name}' cannot show or sell powerup " +
                    "charges. Everything else on this screen still works. Create one via " +
                    "Create > ExpoTheExplorer > Data > Powerup Config, then run this menu item again.",
                    root);
            }
        }

        // Matches SceneFlow.MainScreenSceneName without referencing Core from an editor
        // helper that has no other reason to. If the scene is ever renamed, blueprint.md's
        // scene inventory is the place that says so.
        private const string SceneFlowSceneName = "MainScreen";

        // "The first asset of this type" is a guess, so it is only used to FILL a field
        // that is empty -- an author who deliberately pointed at a second GameConfig keeps
        // their choice.
        private static void AssignConfigs(MainScreenRoot root)
        {
            var serialized = new SerializedObject(root);

            Fill<GameConfig>(serialized, "gameConfig");
            Fill<LivesConfig>(serialized, "livesConfig");

            // Added with the key economy (decisions.md D-065). Worth more here than the
            // others: MainScreenRoot refuses to build a session without it, so a forgotten
            // drag takes the whole main screen down rather than degrading something.
            //
            // It fills nothing when the asset does not exist yet -- FindFirstAsset returns
            // null and Fill leaves the field empty -- so running this before creating
            // KeyConfig.asset is not an error, it just has nothing to assign. The report
            // below is what tells the author which is which.
            Fill<KeyConfig>(serialized, "keyConfig");

            // Added with the powerup stock (GDD 5.2, .claude/powerup-plan.md Adım 1).
            // Unlike KeyConfig above, a missing one degrades rather than blocks -- the
            // screen opens and simply cannot sell charges -- so this line is a convenience
            // rather than a rescue. It is here anyway because the alternative is an author
            // discovering the empty field only when the powerup shop shows nothing.
            Fill<PowerupConfig>(serialized, "powerupConfig");

            Fill<FoodCatalog>(serialized, "foodCatalog");

            serialized.ApplyModifiedProperties();
        }

        private static void Fill<T>(SerializedObject serialized, string propertyName) where T : Object
        {
            var property = serialized.FindProperty(propertyName);
            if (property == null || property.objectReferenceValue != null) return;

            property.objectReferenceValue = FindFirstAsset<T>();
        }

        // The step that actually matters: without it the HUD has no session to follow and
        // silently stays on the save file.
        private static bool PointHudAtSession(MainScreenRoot root)
        {
            var hud = Object.FindAnyObjectByType<HudWalletSource>();
            if (hud == null) return false;

            var serialized = new SerializedObject(hud);
            var property = serialized.FindProperty("sessionHost");
            if (property == null) return false;

            property.objectReferenceValue = root;
            serialized.ApplyModifiedProperties();
            return true;
        }

        private static T FindFirstAsset<T>() where T : Object
        {
            var guids = AssetDatabase.FindAssets($"t:{typeof(T).Name}");
            return guids.Length == 0 ? null : AssetDatabase.LoadAssetAtPath<T>(AssetDatabase.GUIDToAssetPath(guids[0]));
        }
    }
}
