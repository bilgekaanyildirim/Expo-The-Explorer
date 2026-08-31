using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace ExpoTheExplorer.UI
{
    // "Darken the screen, keep these few things bright, and put everything back afterwards."
    // One black sheet in front of the camera, plus sorting-order arithmetic against it, plus
    // the bookkeeping that undoes both.
    //
    // IT IS A CLASS RATHER THAN A COMPONENT, and owned by whichever view is running a step:
    // the sheet is parented to that view's transform, so a view that dies without calling
    // Restore still takes its own sheet with it. What it cannot take with it are the sorting
    // orders it changed on OTHER people's renderers, which is the whole reason this
    // bookkeeping exists rather than a bare AddComponent at the call site.
    //
    // IT WAS EXTRACTED FROM TutorialSpotlightView on 2026-08-31 (D-146) when the powerup
    // lesson needed the same effect. Copying it would have meant two copies of a
    // sorting-order trick whose correctness depends on numbers agreeing across files -- the
    // exact shape of duplicate this project has already deleted twice (D-123 and D-126 each
    // removed a second arrow-texture generator). The numbers now live in one place, and the
    // comments on them are the reason they are those numbers.
    public sealed class TutorialDim
    {
        // Sits above the board (cells 0, item layers 1+) and above a tray's own sprites, but
        // well below BoardItemDragHandler's +1000 drag boost -- so an item the player has
        // actually picked up stays visible over the dim for free, with no cooperation needed
        // between the two classes.
        public const int DimSortingOrder = 500;

        // What a lit world sprite is raised by. Above the dim, still under a dragged item.
        public const int LitSortingBoost = 600;

        // What a lit CANVAS element is raised to. Above the dim (500) and below a dragged item
        // (+1000), so an item dragged across it still passes in front.
        //
        // A CANVAS ELEMENT NEEDS RAISING AT ALL because this scene's canvases are Screen Space
        // - CAMERA at sortingOrder -1, not Overlay: InGameCanvas, which carries the ticket
        // cards, and HUDCanvas, which carries the powerup bar. Only an Overlay canvas
        // composites above everything the camera renders unconditionally; a camera-space one
        // sorts against sprites like any other renderer, so the dim at 500 covers it -- and
        // anything parented to it. The original design assumed otherwise and the target card,
        // its modification arrow and the step message were all being drawn correctly and then
        // buried.
        public const int LitElementSortingOrder = 800;

        // The sheet, and any curtain hung over a single element. Destroyed together.
        private readonly List<GameObject> ownedObjects = new();

        // Every renderer this lifted, with the order it had before. Restored from this
        // snapshot rather than by subtracting the boost again: an item destroyed mid-step then
        // costs nothing, because a null entry is simply skipped instead of writing a wrong
        // number back.
        private readonly List<(SpriteRenderer Renderer, int OriginalOrder)> liftedRenderers = new();

        // Canvas components added to other people's objects to lift them over the dim, removed
        // again on Restore. Tracked apart from ownedObjects because the OBJECT is not ours --
        // destroying a ticket card would be catastrophic; destroying the Canvas we added to it
        // is exactly right.
        private readonly List<Canvas> liftedCanvases = new();

        // Raycasters added beside those canvases for the lifted things that must still be
        // CLICKABLE. Destroyed BEFORE the canvases, and that order is not cosmetic:
        // GraphicRaycaster carries [RequireComponent(typeof(Canvas))], so Unity refuses to
        // remove a Canvas while one of these is still sitting on the object.
        private readonly List<GraphicRaycaster> liftedRaycasters = new();

        public bool IsBuilt { get; private set; }

        // A single black quad scaled to the camera's whole view. Camera.main rather than a
        // handed-down reference for the same reason GameManager.EnsurePhysics2DRaycaster uses
        // it: this runs once per step, and the day scene has exactly one camera.
        //
        // Returns false when there is no camera to measure, so a caller can decide whether a
        // step without its dim is still worth running. Nothing is half-built on that path.
        public bool Build(Transform parent, float opacity)
        {
            if (IsBuilt) return true;

            var cam = Camera.main;
            if (cam == null) return false;

            var dimObject = new GameObject("Dim");
            dimObject.transform.SetParent(parent, false);
            dimObject.transform.position = new Vector3(cam.transform.position.x, cam.transform.position.y, 0f);

            var renderer = dimObject.AddComponent<SpriteRenderer>();
            renderer.sprite = CreateWhitePixelSprite();
            renderer.color = new Color(0f, 0f, 0f, opacity);
            renderer.sortingOrder = DimSortingOrder;

            // Oversized on purpose: the sheet has to survive a camera that is not exactly
            // where it was at Day Start, and covering more than the viewport costs nothing for
            // one untextured quad.
            var height = 2f * cam.orthographicSize;
            dimObject.transform.localScale = new Vector3(height * cam.aspect * 1.5f, height * 1.5f, 1f);

            ownedObjects.Add(dimObject);
            IsBuilt = true;
            return true;
        }

        // Raises every world sprite under one transform, and remembers where each came from.
        public void LiftSprites(Transform target)
        {
            if (target == null) return;

            foreach (var renderer in target.GetComponentsInChildren<SpriteRenderer>(true))
            {
                liftedRenderers.Add((renderer, renderer.sortingOrder));
                renderer.sortingOrder += LitSortingBoost;
            }
        }

        // Raises one DISPLAY-ONLY Canvas element above the dim. Anything the player still has
        // to press goes through LiftInteractiveElement instead -- see the warning there, which
        // is the whole reason these are two methods.
        //
        // An element that ALREADY has a Canvas is left alone rather than reconfigured: that
        // would be someone else's sorting decision, and putting it back on teardown means
        // storing and restoring their values, which is a lot of machinery for a case that does
        // not exist in this project today.
        public void LiftElement(GameObject target) => Lift(target, keepsTakingClicks: false);

        // The same lift, for an element that must STAY CLICKABLE -- the powerup button the
        // lesson is asking the player to press.
        //
        // A NESTED CANVAS SILENTLY BREAKS CLICKS, and this method exists because it did. Unity
        // registers every Graphic against its NEAREST enabled Canvas ancestor, and a
        // GraphicRaycaster only ever tests the graphics registered to its OWN canvas -- so the
        // moment a Canvas is added to the button for sorting, the root canvas's raycaster stops
        // seeing it and the button goes dead. Nothing reports it: the button still draws, still
        // highlights nothing, and the step becomes impossible in exactly the way that looks
        // like a broken game. Adding a raycaster beside the canvas puts it back.
        public void LiftInteractiveElement(GameObject target) => Lift(target, keepsTakingClicks: true);

        // Adding a Canvas to a RectTransform is Unity's standard per-element sorting override;
        // it does not disturb a layout group arranging that element's parent.
        private void Lift(GameObject target, bool keepsTakingClicks)
        {
            if (target == null || target.TryGetComponent<Canvas>(out _)) return;

            var canvas = target.AddComponent<Canvas>();
            canvas.overrideSorting = true;
            canvas.sortingOrder = LitElementSortingOrder;
            liftedCanvases.Add(canvas);

            if (!keepsTakingClicks) return;

            liftedRaycasters.Add(target.AddComponent<GraphicRaycaster>());
        }

        // A stretched black Image parented to one element and pushed to the front of its own
        // children, so it covers that element and nothing else. For Canvas content the
        // world-space sheet cannot reach -- anything already lifted above the dim, or on an
        // Overlay canvas, which composites over everything the camera renders.
        //
        // raycastTarget is off: this is a curtain, and leaving it on would be a silent input
        // blocker for whatever ends up behind it later.
        public void Curtain(RectTransform element, float opacity)
        {
            if (element == null) return;

            var curtain = new GameObject("TutorialDim", typeof(RectTransform));
            var rect = (RectTransform)curtain.transform;
            rect.SetParent(element, false);
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
            rect.SetAsLastSibling();

            var image = curtain.AddComponent<Image>();
            image.color = new Color(0f, 0f, 0f, opacity);
            image.raycastTarget = false;

            ownedObjects.Add(curtain);
        }

        // Idempotent, because both of this class's callers restore explicitly at a step
        // boundary AND again from their own OnDestroy: the restore has to be synchronous at
        // the boundary, since Unity defers Destroy to the end of the frame while the next
        // step's visuals are built immediately -- so a boundary would otherwise lift the new
        // step's renderers and only then put the old step's back, leaving anything they share
        // at the wrong order permanently.
        public void Restore()
        {
            foreach (var (renderer, originalOrder) in liftedRenderers)
            {
                if (renderer != null) renderer.sortingOrder = originalOrder;
            }
            liftedRenderers.Clear();

            foreach (var owned in ownedObjects)
            {
                if (owned != null) Object.Destroy(owned);
            }
            ownedObjects.Clear();

            // RAYCASTERS FIRST: GraphicRaycaster requires a Canvas, so Unity refuses to remove
            // the canvas underneath one and the lift would never come back off.
            foreach (var raycaster in liftedRaycasters)
            {
                if (raycaster != null) Object.Destroy(raycaster);
            }
            liftedRaycasters.Clear();

            // The lifted OBJECT is not ours to destroy -- only the Canvas we added to it, which
            // puts its sorting back under the scene's own rules.
            //
            // overrideSorting is cleared BEFORE the destroy, and that belt is deliberate: if a
            // Unity version ever refuses this removal while the raycaster above is still
            // pending its own deferred destroy, the leftover canvas is then inert -- the lift
            // is undone and the button still takes clicks, instead of the element staying
            // parked above a dim that no longer exists.
            foreach (var canvas in liftedCanvases)
            {
                if (canvas == null) continue;
                canvas.overrideSorting = false;
                Object.Destroy(canvas);
            }
            liftedCanvases.Clear();

            IsBuilt = false;
        }

        // Same one-pixel trick BoardView uses for its cells and frame -- a solid sprite the
        // project can scale to any size, so the dim needs no imported texture.
        private static Sprite CreateWhitePixelSprite()
        {
            var texture = new Texture2D(1, 1);
            texture.SetPixel(0, 0, Color.white);
            texture.Apply();
            return Sprite.Create(texture, new Rect(0, 0, 1, 1), new Vector2(0.5f, 0.5f), 1);
        }
    }
}
