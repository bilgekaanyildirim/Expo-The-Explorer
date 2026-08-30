using System.IO;
using ExpoTheExplorer.UI;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

namespace ExpoTheExplorer.UI.EditorTools
{
    // Menu step: seeds the confetti rig and points whichever celebrating view is in the open
    // scene at it. Run once with MainScreen open and once with SampleScene open.
    //
    // IT BUILDS A RIG, NOT A LOOK. The rig is the plumbing that gets a ParticleSystem on top of
    // an Overlay canvas -- a camera, a RenderTexture and a RawImage (the reasoning is in
    // ConfettiView). What the confetti actually looks like lives in Assets/Prefabs/Confetti.prefab,
    // the user's own tuned ParticleSystem, which is INSTANCED into this rig twice rather than
    // copied. Nested, so editing that one asset changes both cannons and nothing here has an
    // opinion about burst counts, colours or shapes.
    //
    // IT SEEDS, THEN GETS OUT OF THE WAY: it refuses to rebuild an existing prefab and only ever
    // fills an EMPTY field, the same refusal every seeding step in this folder makes. Running it
    // twice is safe and does nothing.
    internal static class ConfettiSetup
    {
        [MenuItem("ExpoTheExplorer/Celebration/Build Confetti")]
        private static void BuildAndWire()
        {
            // The prefab FIRST, before any SerializedObject on a scene object is opened (D-124):
            // creating assets and calling AssetDatabase.SaveAssets with unapplied edits pending on
            // a live one risks the whole block being dropped, and that failure looks exactly like
            // a step nobody ran.
            var confetti = EnsureConfettiPrefab();
            if (confetti == null) return;

            var wired = 0;
            wired += WireInto(Object.FindAnyObjectByType<MetaGroundsView>(FindObjectsInactive.Include), confetti);
            wired += WireInto(Object.FindAnyObjectByType<DayCompletePopupView>(FindObjectsInactive.Include), confetti);

            if (wired == 0)
            {
                Debug.Log(
                    $"Build Confetti: {ConfettiPath} is ready, but the open scene has no {nameof(MetaGroundsView)} or " +
                    $"{nameof(DayCompletePopupView)} with an empty slot to fill. Open MainScreen or SampleScene and run this again.");
                return;
            }

            Debug.Log(
                $"Build Confetti: wired {ConfettiPath} onto {wired} view(s). The scene is marked dirty but NOT saved — " +
                "save it yourself, or the reference is lost the way D-124's was.");
        }

        private const string PrefabFolder = "Assets/Prefabs/UI";
        private const string ConfettiPath = PrefabFolder + "/CelebrationConfetti.prefab";

        // The user's tuned ParticleSystem. This step exists to mount it, so its absence is fatal
        // rather than something to work around with a stand-in.
        private const string ParticlePath = "Assets/Prefabs/Confetti.prefab";

        private static readonly Vector2 ReferenceResolution = new(1080f, 1920f);

        // Above every Overlay canvas this project has: the settings popup sits at 200 and the
        // unlock popup at 10. Only read when the RawImage's canvas is left as a ROOT canvas (the
        // main screen's two beats); mounted into the day scene's popup canvas it is ignored and
        // hierarchy order decides, which is what ConfettiView.BurstBelow relies on.
        private const int SortingOrder = 300;

        // Half the height the camera sees, in world units, and therefore the knob that pairs with
        // the particles' own speeds: their launch is 6–15 units per second under gravity, which
        // peaks around eleven units, so a twenty-unit-tall view frames the whole arc. Change this
        // and the confetti reads bigger or smaller without touching the particles.
        private const float CameraSize = 10f;

        // Portrait, matching the reference resolution above. Only used to place the two cannons at
        // the bottom corners of what the camera sees; the RUNTIME aspect comes from the
        // RenderTexture, which is screen-shaped, so this is a seeding convenience and not a rule.
        private static float HalfWidth => CameraSize * (ReferenceResolution.x / ReferenceResolution.y);

        private static int WireInto(Component view, ConfettiView confetti)
        {
            if (view == null) return 0;

            var serialized = new SerializedObject(view);
            var property = serialized.FindProperty("confettiPrefab");
            if (property == null) return 0;

            // Only fills an EMPTY field: the author may have pointed this at a variant of their
            // own, which is the supported way to give one beat its own feel.
            if (property.objectReferenceValue != null)
            {
                Debug.Log($"Build Confetti: '{view.name}' already points at a confetti prefab, so nothing was changed.", view);
                return 0;
            }

            property.objectReferenceValue = confetti;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            EditorSceneManager.MarkSceneDirty(view.gameObject.scene);
            return 1;
        }

        // REFUSES to rebuild an existing prefab. Delete the asset to have it seeded again.
        private static ConfettiView EnsureConfettiPrefab()
        {
            var existing = AssetDatabase.LoadAssetAtPath<GameObject>(ConfettiPath);
            if (existing != null)
            {
                var component = existing.GetComponent<ConfettiView>();
                if (component == null)
                {
                    // Not auto-rebuilt, and the message says why: during a compile error every
                    // script unloads, so a perfectly good prefab looks exactly like this, and
                    // regenerating then would destroy real tuning work (D-125).
                    Debug.LogError(
                        $"Build Confetti: '{ConfettiPath}' has no {nameof(ConfettiView)} on it — its script does not " +
                        "resolve, so the prefab cannot be used. Delete that asset from the Project window and run this " +
                        "again. (If the console also shows compile errors, fix those first: unloaded scripts look " +
                        "exactly like this.)");
                }

                return component;
            }

            var particle = AssetDatabase.LoadAssetAtPath<GameObject>(ParticlePath);
            if (particle == null)
            {
                Debug.LogError(
                    $"Build Confetti: '{ParticlePath}' is missing, and it is the confetti itself — this step only builds " +
                    "the rig that gets it on screen. Put the ParticleSystem prefab back at that path and run this again.");
                return null;
            }

            if (particle.GetComponent<ParticleSystem>() == null)
            {
                Debug.LogError(
                    $"Build Confetti: '{ParticlePath}' has no ParticleSystem on its ROOT. The rig plays the root system " +
                    "(with children), so the particles have to be on it rather than on a child.");
                return null;
            }

            Directory.CreateDirectory(PrefabFolder);

            var root = BuildRig(particle);
            GameObject saved;
            try
            {
                saved = PrefabUtility.SaveAsPrefabAsset(root, ConfettiPath);
            }
            finally
            {
                // Destroyed whether or not the save threw: this object existed only to be
                // serialized, and leaving it behind drops a stray camera into the open scene.
                Object.DestroyImmediate(root);
            }

            AssetDatabase.SaveAssets();
            Debug.Log(
                $"Build Confetti: built {ConfettiPath}, with two instances of {ParticlePath} in it. Open it and tune the " +
                "particles there — they are nested prefab instances, so editing Confetti.prefab changes both cannons.");
            return saved != null ? saved.GetComponent<ConfettiView>() : null;
        }

        // THE ROOT IS A PLAIN TRANSFORM, not a canvas, and this is the one prefab here where that
        // is right (compare D-126): it has to hold BOTH a world branch ten thousand units from the
        // origin and a UI branch, and one transform cannot sensibly parent both. So they are
        // siblings and ConfettiView detaches the UI half at runtime.
        private static GameObject BuildRig(GameObject particlePrefab)
        {
            var root = new GameObject("CelebrationConfetti");
            var view = root.AddComponent<ConfettiView>();

            var stage = new GameObject("Stage").transform;
            stage.SetParent(root.transform, false);

            var camera = BuildStageCamera(stage);

            // Their prefab's cone already points up and to the RIGHT in the screen plane — its
            // authored rotation works out to (0.5, 0.866, 0), a 60-degree launch. So the LEFT
            // cannon needs no rotation at all: it is the aim the user tuned, untouched. The right
            // one is that same aim mirrored, which is a 60-degree turn about Z and lands exactly
            // on (-0.5, 0.866, 0). Neither cannon's own transform is touched — the holder carries
            // the rotation, so opening Confetti.prefab still shows the author their own aim.
            var left = BuildCannon(stage, particlePrefab, "CannonLeft", new Vector3(-HalfWidth, -CameraSize, 0f), 0f);
            var right = BuildCannon(stage, particlePrefab, "CannonRight", new Vector3(HalfWidth, -CameraSize, 0f), 60f);

            var (screenCanvas, screenImage) = BuildScreen(root.transform);

            var serialized = new SerializedObject(view);
            serialized.FindProperty("stage").objectReferenceValue = stage;
            serialized.FindProperty("stageCamera").objectReferenceValue = camera;
            serialized.FindProperty("screenCanvas").objectReferenceValue = screenCanvas;
            serialized.FindProperty("screenImage").objectReferenceValue = screenImage;

            var cannons = serialized.FindProperty("cannons");
            cannons.arraySize = 2;
            cannons.GetArrayElementAtIndex(0).objectReferenceValue = left;
            cannons.GetArrayElementAtIndex(1).objectReferenceValue = right;

            serialized.ApplyModifiedPropertiesWithoutUndo();

            return root;
        }

        // Orthographic and pulled back along -Z, looking at the plane the cannons stand on. A
        // transparent background is set again at runtime by ConfettiView rather than trusted to
        // this asset: getting the alpha wrong does not look like a mis-authored camera, it looks
        // like the celebration covered the screen with a black rectangle.
        private static Camera BuildStageCamera(Transform parent)
        {
            var go = new GameObject("ConfettiCamera");
            go.transform.SetParent(parent, false);
            go.transform.localPosition = new Vector3(0f, 0f, -20f);

            var camera = go.AddComponent<Camera>();
            camera.orthographic = true;
            camera.orthographicSize = CameraSize;
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(0f, 0f, 0f, 0f);
            camera.nearClipPlane = 0.1f;
            camera.farClipPlane = 100f;
            camera.allowHDR = false;
            camera.allowMSAA = false;

            // Renders before the scene's own camera, so the texture the RawImage samples this
            // frame was filled this frame rather than last one.
            camera.depth = -100f;

            // No audio listener is added with it, deliberately: a second listener in a scene makes
            // Unity warn on every load and silences nothing useful.
            return camera;
        }

        // A HOLDER carries the position and the aim; the user's prefab goes under it untouched.
        // Nested rather than copied, so tuning Confetti.prefab retunes both cannons at once.
        private static ParticleSystem BuildCannon(Transform parent, GameObject particlePrefab, string name, Vector3 position, float aimDegrees)
        {
            var holder = new GameObject(name).transform;
            holder.SetParent(parent, false);
            holder.localPosition = position;
            holder.localRotation = Quaternion.Euler(0f, 0f, aimDegrees);

            var instance = (GameObject)PrefabUtility.InstantiatePrefab(particlePrefab, holder);
            instance.transform.localPosition = Vector3.zero;

            return instance.GetComponent<ParticleSystem>();
        }

        // The UI half: one full-bleed RawImage on its own canvas. No GraphicRaycaster, and the
        // image is never a raycast target — confetti must not be able to eat a tap, and the
        // cheapest guarantee is having nothing in the hierarchy that could.
        private static (Canvas, RawImage) BuildScreen(Transform parent)
        {
            var canvasObject = new GameObject("ScreenCanvas", typeof(RectTransform));
            canvasObject.transform.SetParent(parent, false);

            var canvas = canvasObject.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = SortingOrder;

            var scaler = canvasObject.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = ReferenceResolution;
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = 0.5f;

            var imageObject = new GameObject("Screen", typeof(RectTransform));
            var rect = (RectTransform)imageObject.transform;
            rect.SetParent(canvasObject.transform, false);
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;

            var image = imageObject.AddComponent<RawImage>();
            image.raycastTarget = false;

            return (canvas, image);
        }
    }
}
