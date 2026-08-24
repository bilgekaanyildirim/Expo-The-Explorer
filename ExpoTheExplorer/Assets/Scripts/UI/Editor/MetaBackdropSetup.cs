using ExpoTheExplorer.Data;
using ExpoTheExplorer.Session;
using ExpoTheExplorer.UI;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

namespace ExpoTheExplorer.UI.EditorTools
{
    // Turns the day scene's existing background into the meta backdrop: swaps its Image for a
    // RawImage (a RenderTexture is not a sprite) and wires MetaBackdropView to it.
    //
    // Same posture as MetaGroundsSetup: it touches only the scene that is already open, it
    // does NOT save, and it refuses to redo work it has already done. What it does that the
    // others do not is DESTROY a component -- the Image -- so it goes out of its way to be
    // reversible: the sprite that Image was showing is carried over onto the RawImage as its
    // texture, which is exactly what MetaBackdropView falls back to when the catalog or the
    // save has nothing to show. Undo covers the whole swap as one step.
    internal static class MetaBackdropSetup
    {
        private const string SceneName = "SampleScene";
        private const string BackgroundCanvasName = "Backgroundcanvas";

        [MenuItem("ExpoTheExplorer/Meta/Build Day Backdrop")]
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

            var canvas = FindBackgroundCanvas();
            if (canvas == null)
            {
                EditorUtility.DisplayDialog(
                    "No background canvas",
                    $"Could not find a Canvas named '{BackgroundCanvasName}' with a sorting order below the board. " +
                    "The backdrop has to live on the canvas that draws behind the world sprites, and there is no " +
                    "way to guess which one that is.",
                    "OK");
                return;
            }

            // Already done: only refresh the references, so a look the author has since tuned
            // (the tint, the blur, the RawImage's own rect) is never overwritten. Same promise
            // MetaGroundsSetup makes.
            var existing = Object.FindAnyObjectByType<MetaBackdropView>();
            if (existing != null)
            {
                Wire(existing);
                Debug.Log(
                    $"Day backdrop was already built on '{existing.name}'. References refreshed; " +
                    "the look was left alone.", existing);
                return;
            }

            // The FIRST child that actually draws. Found by component rather than by the name
            // "Background", because this scene has two objects with that name and D-028's rule
            // exists precisely because a name search fails silently the moment a hierarchy
            // changes. Whichever graphic sits under the background canvas is the background.
            var image = canvas.GetComponentInChildren<Image>(includeInactive: true);
            var raw = canvas.GetComponentInChildren<RawImage>(includeInactive: true);

            if (image == null && raw == null)
            {
                EditorUtility.DisplayDialog(
                    "Nothing to convert",
                    $"'{BackgroundCanvasName}' has no Image or RawImage under it, so there is no background to " +
                    "take over. Add the background graphic first.",
                    "OK");
                return;
            }

            var host = raw != null ? raw.gameObject : image.gameObject;
            Undo.SetCurrentGroupName("Build Day Backdrop");
            var group = Undo.GetCurrentGroup();

            if (raw == null)
            {
                // Carried across before the Image dies: this texture is what the backdrop
                // shows if the catalog or the save has nothing for it, so losing it here would
                // turn every failure path into a blank screen.
                var carried = image.sprite != null ? image.sprite.texture : null;
                var carriedColor = image.color;
                var carriedRaycast = image.raycastTarget;

                Undo.DestroyObjectImmediate(image);
                raw = Undo.AddComponent<RawImage>(host);
                raw.texture = carried;
                raw.color = carriedColor;
                raw.raycastTarget = carriedRaycast;
            }

            var view = Undo.AddComponent<MetaBackdropView>(host);
            Wire(view);

            Undo.CollapseUndoOperations(group);
            EditorSceneManager.MarkSceneDirty(scene);

            Debug.Log(
                $"Day backdrop built on '{host.name}'. The grounds are composed and blurred at day start; " +
                "the sprite that was here is kept as the fallback. Save the scene when you are happy with it.",
                host);
        }

        // The canvas that draws BEHIND the world sprites. Identified by name and then checked
        // by sorting order, because the name alone is an author's label while the sorting
        // order is the thing that actually decides what ends up behind the board.
        private static Canvas FindBackgroundCanvas()
        {
            foreach (var canvas in Object.FindObjectsByType<Canvas>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                if (canvas.name != BackgroundCanvasName) continue;
                if (canvas.sortingOrder >= 0) continue;
                return canvas;
            }
            return null;
        }

        // Every reference is set from an object this method has in hand or from the project,
        // never by a name search at run time (D-028). The session host in the day scene is the
        // GameManager -- it is the SessionHost the scene has, and asking for the base type is
        // what keeps this from caring which one it is.
        private static void Wire(MetaBackdropView view)
        {
            var serialized = new SerializedObject(view);

            var host = Object.FindAnyObjectByType<SessionHost>(FindObjectsInactive.Include);
            if (host == null)
            {
                Debug.LogWarning(
                    "No SessionHost in this scene, so the backdrop's session field was left empty. " +
                    "In the day scene that host is the GameManager.", view);
            }

            serialized.FindProperty("sessionHost").objectReferenceValue = host;
            serialized.FindProperty("target").objectReferenceValue = view.GetComponent<RawImage>();

            var catalog = FindCatalog();
            if (catalog == null)
            {
                Debug.LogWarning(
                    "No MetaCatalog asset found, so the backdrop's catalog field was left empty. " +
                    "Assign it by hand and the backdrop will build on the next play.", view);
            }

            serialized.FindProperty("catalog").objectReferenceValue = catalog;
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        // An AssetDatabase search, which is editor tooling and therefore stays (D-028 removed
        // NAME lookups from runtime; type lookups at author time, under the author's eye and
        // logged, are the category that rule deliberately left alone). More than one catalog
        // is a project-level mistake, so the first is taken and the count is reported rather
        // than picked between silently.
        private static MetaCatalog FindCatalog()
        {
            var guids = AssetDatabase.FindAssets($"t:{nameof(MetaCatalog)}");
            if (guids.Length == 0) return null;

            if (guids.Length > 1)
            {
                Debug.LogWarning(
                    $"{guids.Length} {nameof(MetaCatalog)} assets exist; the backdrop was wired to the first. " +
                    "The meta screen reads one catalog and so must this — two would let the day scene and the " +
                    "meta screen show different expos.");
            }

            return AssetDatabase.LoadAssetAtPath<MetaCatalog>(AssetDatabase.GUIDToAssetPath(guids[0]));
        }
    }
}
