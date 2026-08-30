using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace ExpoTheExplorer.UI
{
    // The celebration confetti: the user's own ParticleSystem, shown over UI that a
    // ParticleSystem cannot normally reach.
    //
    // THE PROBLEM THIS SOLVES, because everything below is shaped by it. A ParticleSystem is a
    // world renderer and a Screen Space - Overlay canvas is composited after every camera, so
    // particles simply do not appear over one. Both celebration beats live under Overlay
    // canvases -- checked in the scene files, not assumed: DayCompletePopupView sits under
    // `Popup Canvas` and MetaGroundsView under `Canvas`, both Overlay with no camera assigned.
    // D-126 hit the same wall with the tutorial's world-space arrow and solved it by giving up
    // the world renderer. This solves it the other way, because the user wants to author the
    // celebration in the ParticleSystem inspector and that is a good reason.
    //
    // HOW: the particles are staged far off-origin in world space, a small orthographic camera
    // renders just them into a transparent RenderTexture, and a RawImage draws that texture as
    // ordinary UI -- so the LAYERING becomes a UI question, which is a question this project can
    // already answer. On the main screen the RawImage's canvas is a top-level Overlay canvas
    // above everything; in the day scene it is mounted at the receipt popup's own sibling index,
    // under the popup and over the rest. Those are the user's two rules, unchanged.
    //
    // WHAT IT DELIBERATELY DOES NOT DO: it owns no look. There are no speed, colour, size or
    // count fields here -- they live in the ParticleSystem inspector, on the prefab, which was
    // the point. It sets exactly two things on the particles at runtime, and both are clock and
    // plumbing rather than appearance (see Play).
    //
    // NO NEW LAYER AND NO CHANGE TO ANY SCENE CAMERA: the stage is parked at x = 10000, where
    // nothing in either scene exists, so the game's own camera never frames it and its culling
    // mask is left exactly as it is. That is cheaper than a dedicated layer and it cannot be
    // silently undone by someone editing a camera.
    public class ConfettiView : MonoBehaviour
    {
        [Tooltip("The world branch: the camera and the particles it films. Parked far off-origin at runtime so no scene camera can see it.")]
        [SerializeField] private Transform stage;

        [Tooltip("Films the particles into the RenderTexture. Orthographic — its Size is the zoom knob that pairs with the particles' own speeds.")]
        [SerializeField] private Camera stageCamera;

        [Tooltip("One entry per cannon. These are instances of Assets/Prefabs/Confetti.prefab — open one and tune it in the ParticleSystem inspector; everything about the look lives there.")]
        [SerializeField] private ParticleSystem[] cannons;

        [Tooltip("The canvas the RawImage lives on. It is DETACHED at runtime and mounted wherever the layering needs it, so what is authored here is only its scaling.")]
        [SerializeField] private Canvas screenCanvas;

        [Tooltip("Draws the RenderTexture over the UI. Stretched to fill; never a raycast target.")]
        [SerializeField] private RawImage screenImage;

        [Tooltip("Fraction of the screen's resolution the RenderTexture is allocated at. Confetti is soft, so half is usually indistinguishable and costs a quarter of the memory.")]
        [SerializeField, Range(0.25f, 1f)] private float resolutionScale = 0.5f;

        [Tooltip("How long the rig lingers after the last particle dies, in seconds. A small margin, so a system that briefly reports nothing alive between bursts is not cut off.")]
        [SerializeField, Min(0f)] private float lingerSeconds = 0.35f;

        // Far enough that nothing in either scene is near it, and a round number so it is
        // obvious in the inspector that the rig was moved on purpose rather than dragged.
        private const float StageOriginX = 10000f;

        private RenderTexture texture;
        private bool playing;
        private bool pending;
        private float quietFor;

        // Fires the confetti as its OWN top-level canvas, above every other Overlay canvas in
        // the scene. What the main screen wants: at a purchase or an unlock nothing is covering
        // the map, so the celebration belongs over all of it.
        public static ConfettiView BurstOnTop(ConfettiView prefab)
        {
            if (prefab == null) return null;

            var instance = Instantiate(prefab);
            instance.Play(null);
            return instance;
        }

        // Fires it UNDER one thing and over everything else on that thing's canvas -- what the
        // day scene wants: the receipt popup stays readable and the confetti covers the rest.
        //
        // By SIBLING INDEX rather than a sorting number, the same idiom D-045 uses to stand a
        // prop's stand-in at the prop's own depth: the RawImage's canvas is inserted immediately
        // before `sibling`'s own branch, so the ordering survives that branch being moved.
        //
        // Walks UP to the ancestor that is a direct child of the canvas first, because the popup
        // is usually a panel or two deep and inserting beside it there would put the confetti
        // inside that panel -- where a mask would clip it to the popup's own rectangle, the
        // opposite of what was asked for.
        //
        // Falls back to BurstOnTop on every branch that cannot answer: this is decoration, and a
        // celebration in the wrong layer beats no celebration.
        public static ConfettiView BurstBelow(ConfettiView prefab, RectTransform sibling)
        {
            if (prefab == null) return null;
            if (sibling == null) return BurstOnTop(prefab);

            var canvas = sibling.GetComponentInParent<Canvas>();
            if (canvas == null) return BurstOnTop(prefab);

            var canvasTransform = canvas.transform;
            var branch = (Transform)sibling;
            while (branch.parent != null && branch.parent != canvasTransform) branch = branch.parent;
            if (branch.parent != canvasTransform) return BurstOnTop(prefab);

            var instance = Instantiate(prefab);
            instance.Play((canvasTransform, branch.GetSiblingIndex()));
            return instance;
        }

        // `mount` is null for "its own top-level canvas", or the canvas and the index to sit at.
        private void Play((Transform parent, int siblingIndex)? mount)
        {
            if (playing || pending) return;

            if (!ValidateReferences())
            {
                Destroy(gameObject);
                return;
            }

            // OFF TO THE STAGE. Authored at the origin so the prefab opens somewhere sensible
            // and the particles can be watched while they are tuned; moved out here the moment
            // it runs, which is what keeps the scene's own camera from ever framing it.
            stage.position = new Vector3(StageOriginX, 0f, 0f);

            MountScreen(mount);

            // The two things set on the particles at runtime, both plumbing rather than look:
            // the rig is torn down when the burst ends, so nothing may loop forever, and the
            // clock is unscaled because a day can be paused underneath a celebration and one
            // that freezes with the game reads as a bug rather than as a pause.
            foreach (var cannon in cannons)
            {
                if (cannon == null) continue;

                var main = cannon.main;
                main.loop = false;
                main.useUnscaledTime = true;
            }

            pending = true;
            StartCoroutine(FireOnceTheScreenIsReal());
        }

        // ONE FRAME, for the reason a canvas instantiated this frame is not yet sized: Unity
        // resolves that in the canvas update, just before rendering, and the RenderTexture is
        // allocated from the RawImage's own resolved size. Firing immediately would allocate
        // against a 0x0 rect. A frame is invisible inside a celebration.
        private IEnumerator FireOnceTheScreenIsReal()
        {
            yield return null;

            pending = false;

            AllocateTexture();

            foreach (var cannon in cannons)
            {
                if (cannon != null) cannon.Play(true);
            }

            playing = true;
        }

        // Detached from this rig either way, because the RawImage is UI and the stage is a
        // world object ten thousand units away -- one transform cannot sensibly parent both.
        // Its Canvas component decides the rest: left as a root it is the Overlay canvas the
        // prefab authored (top of everything); reparented into another canvas it becomes a
        // NESTED canvas, where Unity ignores the render mode and hierarchy order decides -- which
        // is exactly what the sibling index above is choosing.
        private void MountScreen((Transform parent, int siblingIndex)? mount)
        {
            var screenTransform = (RectTransform)screenCanvas.transform;

            if (mount == null)
            {
                screenTransform.SetParent(null, false);
                return;
            }

            screenTransform.SetParent(mount.Value.parent, false);
            screenTransform.SetSiblingIndex(mount.Value.siblingIndex);

            // A nested canvas keeps whatever rect the prefab authored, unlike a root Overlay one
            // whose rect is driven from the screen. Told to fill, or the confetti is drawn into
            // whatever box the prefab happened to be saved with.
            screenTransform.anchorMin = Vector2.zero;
            screenTransform.anchorMax = Vector2.one;
            screenTransform.offsetMin = Vector2.zero;
            screenTransform.offsetMax = Vector2.zero;
        }

        // TRANSPARENT, which is the one part of this that a build has to be checked against:
        // under URP the camera's background must be a solid colour with ALPHA 0 and post
        // processing off, or the confetti arrives on an opaque rectangle covering the screen.
        // Both are set here rather than left to the prefab, because getting them wrong does not
        // look like a mis-authored camera -- it looks like the celebration broke everything.
        private void AllocateTexture()
        {
            var rect = ((RectTransform)screenCanvas.transform).rect;
            var scale = screenCanvas.scaleFactor <= 0f ? 1f : screenCanvas.scaleFactor;

            var width = Mathf.Max(64, Mathf.RoundToInt(rect.width * scale * resolutionScale));
            var height = Mathf.Max(64, Mathf.RoundToInt(rect.height * scale * resolutionScale));

            // OWNED AND EXPLICITLY CREATED, WITH A DEPTH BUFFER, and every word of that is a bug
            // fix rather than ceremony. It was a RenderTexture.GetTemporary with a depth of 0,
            // and the 2D renderer refused it: "Renderer2D Pass: Fake or uninitialized surface is
            // not supported for attachment 0" -- a pooled texture is not a real surface until
            // something creates it, and a render target with no depth attachment is not one the
            // pipeline can attach. That is almost certainly what broke the camera's own clear
            // too, which is the bug underneath the first symptom: with nothing wiping the target
            // each particle painted its whole trajectory and the screen filled with ribbons.
            texture = new RenderTexture(width, height, 24, RenderTextureFormat.ARGB32)
            {
                name = "ConfettiStage",
                antiAliasing = 1,
                useMipMap = false,
                autoGenerateMips = false,
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp,
            };
            texture.Create();

            stageCamera.clearFlags = CameraClearFlags.SolidColor;
            stageCamera.backgroundColor = new Color(0f, 0f, 0f, 0f);
            stageCamera.allowHDR = false;
            stageCamera.allowMSAA = false;
            stageCamera.targetTexture = texture;

            screenImage.texture = texture;
            screenImage.raycastTarget = false;
            screenImage.enabled = true;
        }

        // Polls the particles rather than timing the burst, so the rig lives exactly as long as
        // what it is showing -- an author who doubles a lifetime in the inspector needs to change
        // nothing here. The linger covers a system that briefly reports nothing alive between
        // its own sub-emitters.
        private void Update()
        {
            if (!playing) return;

            foreach (var cannon in cannons)
            {
                if (cannon == null || !cannon.IsAlive(true)) continue;

                quietFor = 0f;
                return;
            }

            quietFor += Time.unscaledDeltaTime;
            if (quietFor < lingerSeconds) return;

            playing = false;
            Destroy(gameObject);
        }

        // NOTHING CLEARS THE TEXTURE BY HAND ANY MORE, and the attempt is worth recording so it is
        // not tried again. A GL.Clear against RenderTexture.active in LateUpdate did stop the
        // ribbons and broke the frame doing it -- "EndRenderPass: Not inside a Renderpass", every
        // frame, because reaching for the active render target from game code tears a hole in the
        // pass the scriptable pipeline is in the middle of building. The camera clears its own
        // target, as it always should have; what stopped it was the target not being a real
        // surface (see AllocateTexture).

        // The RawImage's canvas was detached, so it is NOT destroyed by this object going away
        // and has to be taken down by hand -- the same trap the tutorial's reparented arrows
        // already carry (D-126, D-127). The texture is released in the same breath, and the
        // camera is pointed away from it FIRST: releasing a texture a live camera still targets
        // is how a render loop ends up drawing into freed memory.
        private void OnDestroy()
        {
            if (stageCamera != null) stageCamera.targetTexture = null;

            if (texture != null)
            {
                // Released AND destroyed: this texture is owned rather than borrowed from the
                // temporary pool now, so returning it is not somebody else's job. Release frees
                // the GPU surface, Destroy frees the object that named it.
                texture.Release();
                Destroy(texture);
                texture = null;
            }

            if (screenCanvas != null) Destroy(screenCanvas.gameObject);
        }

        // Every field is authored on the prefab, so a missing one should name itself rather than
        // throw in the middle of a burst.
        private bool ValidateReferences()
        {
            var missing = new List<string>();
            if (stage == null) missing.Add(nameof(stage));
            if (stageCamera == null) missing.Add(nameof(stageCamera));
            if (screenCanvas == null) missing.Add(nameof(screenCanvas));
            if (screenImage == null) missing.Add(nameof(screenImage));
            if (cannons == null || cannons.Length == 0) missing.Add(nameof(cannons));

            if (missing.Count == 0) return true;

            Debug.LogError(
                $"{nameof(ConfettiView)} on '{name}' is missing Inspector reference(s): {string.Join(", ", missing)}. " +
                "Nothing was fired. Delete Assets/Prefabs/UI/CelebrationConfetti.prefab and run " +
                "ExpoTheExplorer > Celebration > Build Confetti to seed it again.",
                this);
            return false;
        }
    }
}
