using System.Collections.Generic;
using DG.Tweening;
using ExpoTheExplorer.Core;
using ExpoTheExplorer.Data;
using ExpoTheExplorer.Systems.Tutorial;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace ExpoTheExplorer.UI
{
    // The look of one forced move: everything goes dark except ONE item and ONE tray (with
    // that tray's ticket card), a ghost copy of the item loops from the one to the other,
    // and -- for a step that teaches a modification -- an arrow points at the ingredient
    // that modification added, a second arrow points at its box on the ticket, and a line
    // of text says what to look at.
    //
    // THE DIM, THE LIFTS AND THE GHOST ARE BUILT AT RUNTIME; THE MESSAGE AND THE TWO ARROWS
    // ARE AN AUTHORED PREFAB (D-122, D-126). The line above used to say "built entirely at
    // runtime and authored nowhere", and the split is not a compromise: the dim is a sheet
    // sized to the camera and the lifts are sorting-order arithmetic against it, neither of
    // which anybody would want to style, while the sentence and the arrows are exactly what
    // an author needs to reach -- and could not, while they were literals in this file.
    //
    // One of these exists per STEP, created by that step's target WorldTrayView -- the one
    // object already holding both of the ghost's endpoints (a serialized BoardView for the
    // source cell, its own transform for the destination).
    //
    // IT NEVER WRITES A COLOUR IT DOES NOT OWN. Tinting each renderer would have been the
    // obvious way to darken the board, and it would have lost: BoardView rewrites its layer
    // colours on every RefreshCell, so the two would fight and the board would flicker back
    // to full brightness on the next spawn. Instead a black sheet is laid over the whole
    // camera view and the two things that stay lit are lifted ABOVE it by sorting order --
    // the same save-and-restore shape BoardItemDragHandler uses for its drag boost. The only
    // renderers this class recolours are ones it created itself.
    public class TutorialSpotlightView : MonoBehaviour
    {
        // Sits above the board (cells 0, item layers 1+) and above a tray's own sprites, but
        // well below BoardItemDragHandler's +1000 drag boost -- so an item the player has
        // actually picked up stays visible over the dim for free, with no cooperation needed
        // between the two classes.
        private const int DimSortingOrder = 500;

        // What the lit pair is raised by. Above the dim, still under a dragged item.
        private const int LitSortingBoost = 600;

        // What the target ticket card is raised to while its step runs. Above the dim (500)
        // and below a dragged item (+1000), so an item dragged across the card still passes
        // in front of it.
        //
        // THE CARD NEEDS RAISING AT ALL because this scene's InGameCanvas is Screen Space -
        // CAMERA at sortingOrder -1, not Overlay. Only an Overlay canvas composites above
        // everything the camera renders unconditionally; a camera-space one sorts against
        // sprites like any other renderer, so the dim at 500 covers it -- the cards, and
        // anything parented to them. The original design assumed otherwise and the target
        // card, its modification arrow and the step message were all being drawn correctly
        // and then buried.
        private const int LitCardSortingOrder = 800;

        // Three constants left with D-126: the message canvas's sorting order and the two
        // arrow length factors. All three are the prefab's business now -- the canvas is
        // authored in it (keep that order NEGATIVE, so a Game Over popup arriving mid-step
        // covers the tutorial line rather than the other way round), and each arrow's size is
        // whatever the author gave it.
        private TutorialDirector director;
        private BoardAnimationConfig animConfig;

        private GameObject dimObject;
        private GameObject ghostObject;
        private Sequence ghostLoop;
        private bool restored;

        // Every renderer this view lifted, with the order it had before. Restored from this
        // snapshot rather than by subtracting the boost again: an item destroyed mid-step
        // then costs nothing, because a null entry is simply skipped instead of writing a
        // wrong number back.
        private readonly List<(SpriteRenderer Renderer, int OriginalOrder)> liftedRenderers = new();

        // Everything this view added to a Canvas or to another object's hierarchy, destroyed
        // together on teardown. The black sheets over the non-target ticket cards are here
        // because Canvas UI composites over everything the camera renders, so the
        // world-space dim above can never cover a card -- these are the Canvas-side half of
        // the same effect. One stretched Image per card, added as its child, so
        // TicketCardView keeps sole ownership of the colours it paints and the
        // HorizontalLayoutGroup that arranges the cards is left undisturbed.
        private readonly List<GameObject> attachedObjects = new();

        // Canvas components added to ticket cards to lift them over the dim, removed again on
        // teardown. Components rather than objects, so they are tracked separately from
        // attachedObjects -- destroying the card would be catastrophic; destroying the Canvas
        // we added to it is exactly right.
        private readonly List<Canvas> liftedCardCanvases = new();

        // Created by the target tray, with everything already resolved -- this view looks
        // nothing up. sourceItem is the live board container the player must grab (the ghost
        // is cloned from it); trayTarget is where the ghost flies to; litExtras stay bright
        // alongside the item; dimmedCards go dark; targetCard is the lit card the
        // modification arrow points into, and sourceBoardItem is the model behind sourceItem,
        // which is what knows which of its sprite layers a modification put there.
        public static TutorialSpotlightView Create(
            TutorialDirector director,
            BoardAnimationConfig animConfig,
            Transform sourceItem,
            BoardItem sourceBoardItem,
            Transform trayTarget,
            IReadOnlyList<Transform> litExtras,
            IReadOnlyList<RectTransform> dimmedCards,
            TicketCardView targetCard)
        {
            if (director == null || animConfig == null || sourceItem == null || trayTarget == null) return null;

            var host = new GameObject(nameof(TutorialSpotlightView));
            var view = host.AddComponent<TutorialSpotlightView>();
            view.director = director;
            view.animConfig = animConfig;
            view.Build(sourceItem, sourceBoardItem, trayTarget, litExtras, dimmedCards, targetCard);
            return view;
        }

        private void Build(
            Transform sourceItem,
            BoardItem sourceBoardItem,
            Transform trayTarget,
            IReadOnlyList<Transform> litExtras,
            IReadOnlyList<RectTransform> dimmedCards,
            TicketCardView targetCard)
        {
            BuildDim();
            Lift(sourceItem);

            if (litExtras != null)
            {
                foreach (var extra in litExtras) Lift(extra);
            }

            if (dimmedCards != null)
            {
                foreach (var card in dimmedCards) DimCard(card);
            }

            BuildGhost(sourceItem, trayTarget);

            // After the dim exists, so the lift is measured against something already there.
            // This is what keeps the one card the player must READ -- the order the forced
            // move is filling -- legible, along with the modification arrow parented to it.
            LiftCardAboveDim(targetCard);

            var step = director.Current;
            if (step != null) BuildStepHints(step, sourceItem, sourceBoardItem, targetCard);
        }

        // The step's sentence and its two modification arrows, all three out of ONE authored
        // prefab (D-126). They were built here from literals and a generated arrow texture
        // until the user asked to be able to style them, which a shape assembled in C# cannot
        // be.
        //
        // ONE PREFAB, TWO INDEPENDENT FLAGS. `highlightModification` and a non-empty message
        // are separate authored decisions -- a step may point without speaking, or speak
        // without pointing -- so this instantiates when EITHER is set and then switches off
        // whatever that step did not ask for. Putting the arrows inside a message-only prefab
        // would have tied the two together silently.
        private void BuildStepHints(
            TutorialStep step, Transform sourceItem, BoardItem sourceBoardItem, TicketCardView targetCard)
        {
            var wantsMessage = !string.IsNullOrEmpty(step.Message);
            var wantsArrows = step.HighlightModification;
            if (!wantsMessage && !wantsArrows) return;

            var prefab = animConfig.TutorialStepHintsPrefab;
            if (prefab == null)
            {
                Debug.LogWarning(
                    $"{nameof(TutorialSpotlightView)}: no Tutorial Step Hints Prefab on {nameof(BoardAnimationConfig)}, " +
                    "so this step's message and arrows are not shown. Run ExpoTheExplorer > Tutorial > Build Powerup " +
                    "Popups.", this);
                return;
            }

            // Parentless: the prefab carries its own Screen Space - Overlay canvas for the
            // message, and this view's transform is a world object (D-086 -- on the ticket
            // cards' Screen Space - Camera canvas the message was drawn and then buried).
            var instance = Instantiate(prefab);
            attachedObjects.Add(instance);

            // The config's field is a GameObject rather than the component, because
            // BoardAnimationConfig lives in the Data assembly and this binder does not -- Data
            // references nothing, which is the property that keeps it loadable anywhere.
            var hints = instance.GetComponent<TutorialStepHints>();
            if (hints == null)
            {
                Debug.LogWarning(
                    $"{nameof(TutorialSpotlightView)}: the Tutorial Step Hints Prefab has no " +
                    $"{nameof(TutorialStepHints)} on its root, so this step's message and arrows cannot be filled in.",
                    instance);
                return;
            }

            if (wantsMessage)
            {
                hints.ShowMessage(step.Message);
            }
            else
            {
                hints.HideMessage();
            }

            if (!wantsArrows)
            {
                hints.HideArrows();
                return;
            }

            AttachItemArrow(hints, sourceItem, sourceBoardItem);
            AttachCardArrow(hints, targetCard);

            // After both placements: the nudge is measured from where each arrow ended up,
            // and neither resting position is known until it has been moved there.
            hints.NudgeArrows();
        }

        // POSITIONED OVER THE ITEM, NOT PARENTED TO IT, and living on the prefab's own canvas
        // rather than in world space (the user's two corrections, 2026-08-28). Parented to the
        // item it rode along when the player picked the food up; as a world SpriteRenderer it
        // was also invisible and unclickable in the prefab stage, which is why it is a UI Image
        // now like its sibling.
        //
        // The board item is a WORLD object and this arrow is on a Screen Space - Overlay
        // canvas, so the two are bridged through SCREEN space -- the only coordinate system
        // they share. The authored anchoredPosition is kept as the offset from the ingredient,
        // scaled by the canvas so a value tuned at the reference resolution means the same
        // thing on every device.
        //
        // A happy consequence: an Overlay canvas draws above every camera-rendered thing
        // unconditionally, so the arrow no longer needs the sorting order this class used to
        // set against its own dim.
        private void AttachItemArrow(TutorialStepHints hints, Transform sourceItem, BoardItem sourceBoardItem)
        {
            var arrow = hints.ItemArrow;
            if (arrow == null) return;

            var layer = FindModificationLayer(sourceItem, sourceBoardItem);
            if (layer == null)
            {
                // A step that asks to highlight a modification on an item carrying none. The
                // ticket-card arrow may still apply, so this hides one piece rather than
                // abandoning the pair.
                arrow.gameObject.SetActive(false);
                return;
            }

            var canvas = arrow.canvas;
            var camera = Camera.main;
            if (canvas == null || camera == null)
            {
                Debug.LogWarning(
                    $"{nameof(TutorialSpotlightView)}: the item arrow has no canvas or the scene has no main camera, " +
                    "so it cannot be placed over the item and is hidden.", arrow);
                arrow.gameObject.SetActive(false);
                return;
            }

            var rect = arrow.rectTransform;
            var authoredOffset = rect.anchoredPosition * canvas.scaleFactor;
            var screenPoint = RectTransformUtility.WorldToScreenPoint(camera, layer.position);

            // An Overlay canvas's world space IS screen pixels, so a world position assignment
            // is the shortest correct answer here and it ignores whatever anchors the author
            // gave the arrow -- which is the property that keeps this working after a restyle.
            rect.position = new Vector3(screenPoint.x + authoredOffset.x, screenPoint.y + authoredOffset.y, 0f);

            FadeIn(arrow);
        }

        // Reparented INTO the ticket card's own modification row, so it tracks a card of any
        // width and rides along with everything the card does. It therefore outlives the
        // prefab instance, which is why it is registered separately for teardown.
        private void AttachCardArrow(TutorialStepHints hints, TicketCardView targetCard)
        {
            var arrow = hints.CardArrow;
            if (arrow == null) return;

            var row = targetCard != null ? targetCard.FirstModificationRow : null;
            if (row == null)
            {
                arrow.gameObject.SetActive(false);
                return;
            }

            arrow.transform.SetParent(row, worldPositionStays: false);
            attachedObjects.Add(arrow.gameObject);

            FadeIn(arrow);
        }

        // A single black quad scaled to the camera's whole view. Camera.main rather than a
        // handed-down reference for the same reason GameManager.EnsurePhysics2DRaycaster
        // uses it: this runs once per step, and the day scene has exactly one camera.
        private void BuildDim()
        {
            var cam = Camera.main;
            if (cam == null) return;

            dimObject = new GameObject("Dim");
            dimObject.transform.SetParent(transform, false);
            dimObject.transform.position = new Vector3(cam.transform.position.x, cam.transform.position.y, 0f);

            var renderer = dimObject.AddComponent<SpriteRenderer>();
            renderer.sprite = CreateWhitePixelSprite();
            renderer.color = new Color(0f, 0f, 0f, animConfig.TutorialDimOpacity);
            renderer.sortingOrder = DimSortingOrder;

            // Oversized on purpose: the sheet has to survive a camera that is not exactly
            // where it was at Day Start, and covering more than the viewport costs nothing
            // for one untextured quad.
            var height = 2f * cam.orthographicSize;
            dimObject.transform.localScale = new Vector3(height * cam.aspect * 1.5f, height * 1.5f, 1f);
        }

        // Raises one ticket card above the dim by giving it its own sorting scope. Adding a
        // Canvas to a RectTransform is Unity's standard per-element sorting override; it does
        // not disturb the HorizontalLayoutGroup arranging the cards, and no GraphicRaycaster
        // is added because the card is display-only.
        //
        // A card that ALREADY has a Canvas is left alone rather than reconfigured: that would
        // be someone else's sorting decision, and putting it back on teardown means storing
        // and restoring their values, which is a lot of machinery for a case that does not
        // exist in this project today.
        private void LiftCardAboveDim(TicketCardView card)
        {
            if (card == null || card.TryGetComponent<Canvas>(out _)) return;

            var cardCanvas = card.gameObject.AddComponent<Canvas>();
            cardCanvas.overrideSorting = true;
            cardCanvas.sortingOrder = LitCardSortingOrder;
            liftedCardCanvases.Add(cardCanvas);
        }

        private void Lift(Transform target)
        {
            if (target == null) return;

            foreach (var renderer in target.GetComponentsInChildren<SpriteRenderer>(true))
            {
                liftedRenderers.Add((renderer, renderer.sortingOrder));
                renderer.sortingOrder += LitSortingBoost;
            }
        }

        // A stretched black Image parented to the card and pushed to the front of its own
        // children, so it covers that card and nothing else. raycastTarget is off: this is a
        // curtain, and leaving it on would be a silent input blocker for whatever ends up
        // behind these cards later.
        private void DimCard(RectTransform card)
        {
            if (card == null) return;

            var dim = new GameObject("TutorialDim", typeof(RectTransform));
            var rect = (RectTransform)dim.transform;
            rect.SetParent(card, false);
            Stretch(rect);
            rect.SetAsLastSibling();

            var image = dim.AddComponent<Image>();
            image.color = new Color(0f, 0f, 0f, animConfig.TutorialDimOpacity);
            image.raycastTarget = false;

            attachedObjects.Add(dim);
        }

        // The ghost is a CLONE of the real item, which is why this effect needs no art: it is
        // guaranteed to show the exact food, with the exact modifications, that the player is
        // being asked to move. Everything that would make the copy behave like a real board
        // item is stripped -- its colliders (it must never intercept the drag it is
        // advertising) and every MonoBehaviour, which removes the cloned BoardItemDragHandler
        // along with anything else added to an item container later.
        private void BuildGhost(Transform sourceItem, Transform trayTarget)
        {
            ghostObject = Instantiate(sourceItem.gameObject, transform);
            ghostObject.name = "Ghost";
            ghostObject.transform.position = sourceItem.position;

            // The item's RESTING scale, taken from its parent rather than copied off the item
            // itself. Copying was wrong: at Day Start the source item is running BoardView's
            // fly-in, which begins at localScale ZERO, and this clone is made a tween tick
            // later -- so the ghost was liable to be born invisible and stay that way for its
            // whole life. A board item rests at localScale one, so its parent's lossy scale IS
            // the resting size, and that number is not moving.
            ghostObject.transform.localScale = sourceItem.parent != null ? sourceItem.parent.lossyScale : Vector3.one;

            foreach (var collider in ghostObject.GetComponentsInChildren<Collider2D>(true)) Destroy(collider);
            foreach (var behaviour in ghostObject.GetComponentsInChildren<MonoBehaviour>(true)) Destroy(behaviour);

            foreach (var renderer in ghostObject.GetComponentsInChildren<SpriteRenderer>(true))
            {
                var color = renderer.color;
                renderer.color = new Color(color.r, color.g, color.b, color.a * animConfig.TutorialGhostOpacity);
                renderer.sortingOrder += LitSortingBoost;
            }

            var end = trayTarget.position;

            // One restarting sequence rather than a per-frame lerp: the gesture is "travel,
            // land, wait, reappear at the start", and Append/AppendInterval say exactly that.
            // SetLink ties it to this object so a scene change cannot leave a tween running
            // against a destroyed transform.
            //
            // Each lap re-reads the item's position instead of capturing it once, for the same
            // reason the scale above is not copied: this sequence is built while the item may
            // still be flying in from BoardView's Starting Point, and a captured start would
            // have pinned the ghost's origin to a spot the item was merely passing through --
            // for the rest of the step. Re-reading costs one property access per lap and is
            // self-correcting.
            ghostLoop = DOTween.Sequence();
            ghostLoop.AppendCallback(() =>
            {
                // The item is destroyed by the very drop that ends the step, and this object's
                // teardown does not necessarily run before the next tween tick -- so a lap can
                // begin against an item that is already gone.
                if (sourceItem == null || ghostObject == null) return;
                ghostObject.transform.position = sourceItem.position;
            });
            ghostLoop.Append(ghostObject.transform.DOMove(end, animConfig.TutorialGhostTravelDuration).SetEase(Ease.InOutSine));
            ghostLoop.AppendInterval(animConfig.TutorialGhostLoopPause);
            ghostLoop.SetLoops(-1).SetLink(gameObject);
        }


        // The item container's children ARE its sprite layers, in the order BoardView built
        // them from ResolvedLayers -- so the index of the modification's layer in that list is
        // the index of its child. Coupled to BoardView's construction, which is why both ends
        // say so; the alternative was a name lookup, which is worse in every way.
        private static Transform FindModificationLayer(Transform sourceItem, BoardItem sourceBoardItem)
        {
            if (sourceItem == null || sourceBoardItem == null) return null;

            var layers = sourceBoardItem.ResolvedLayers;
            for (var i = 0; i < layers.Count; i++)
            {
                if (layers[i].SourceModification == null) continue;
                return i < sourceItem.childCount ? sourceItem.GetChild(i) : null;
            }

            return null;
        }





        // Hints arrive a beat after the dim so the eye lands on the lit pair first rather
        // than on text. Every fade goes through here so a single config number controls them.
        private void FadeIn(Graphic graphic, float targetAlpha = 1f)
        {
            var color = graphic.color;
            graphic.color = new Color(color.r, color.g, color.b, 0f);
            graphic.DOFade(targetAlpha, animConfig.TutorialHintFadeDuration).SetLink(gameObject);
        }

        private void FadeIn(SpriteRenderer renderer, float targetAlpha = 1f)
        {
            var color = renderer.color;
            renderer.color = new Color(color.r, color.g, color.b, 0f);
            renderer.DOFade(targetAlpha, animConfig.TutorialHintFadeDuration).SetLink(gameObject);
        }

        private static void Stretch(RectTransform rect)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
        }

        // Called by the tray that built this, BEFORE the director is told the step is done.
        // The restore has to happen synchronously here rather than being left to OnDestroy,
        // because Unity defers Destroy to the end of the frame while the NEXT step's
        // spotlight is built immediately -- so a step boundary would otherwise lift the new
        // step's renderers and only then restore the old step's, with any renderer they share
        // ending up at the wrong order permanently.
        public void Dismiss()
        {
            RestoreAll();
            Destroy(gameObject);
        }

        // Also runs on the paths that never reach Dismiss -- a retry, a return to the main
        // screen, the day scene unloading mid-step. A spotlight that outlived its restore
        // would leave the game permanently dark, which is the worst failure available here.
        private void OnDestroy() => RestoreAll();

        private void RestoreAll()
        {
            if (restored) return;
            restored = true;

            ghostLoop?.Kill();

            foreach (var (renderer, originalOrder) in liftedRenderers)
            {
                if (renderer != null) renderer.sortingOrder = originalOrder;
            }
            liftedRenderers.Clear();

            foreach (var attached in attachedObjects)
            {
                if (attached != null) Destroy(attached);
            }
            attachedObjects.Clear();

            // The CARD is not ours to destroy -- only the Canvas we added to it, which puts
            // its sorting back under the scene's own rules.
            foreach (var cardCanvas in liftedCardCanvases)
            {
                if (cardCanvas != null) Destroy(cardCanvas);
            }
            liftedCardCanvases.Clear();
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
