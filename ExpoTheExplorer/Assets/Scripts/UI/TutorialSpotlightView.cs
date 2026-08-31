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
        // Three constants left with D-126: the message canvas's sorting order and the two
        // arrow length factors. All three are the prefab's business now -- the canvas is
        // authored in it (keep that order NEGATIVE, so a Game Over popup arriving mid-step
        // covers the tutorial line rather than the other way round), and each arrow's size is
        // whatever the author gave it.
        //
        // Three more left with D-146: the dim's sorting order and the two lift levels, which
        // now live on TutorialDim beside the code that uses them, because the powerup lesson
        // needs the same effect and two copies of a number that has to agree across files is
        // how the numbers stop agreeing.
        private TutorialDirector director;
        private BoardAnimationConfig animConfig;

        private GameObject ghostObject;
        private Sequence ghostLoop;
        private bool restored;

        // The dim sheet, the lifts over it, and the black curtains on the non-target cards --
        // all of it, including putting every borrowed sorting order back (D-146).
        private readonly TutorialDim dim = new();

        // Everything this view added to a Canvas or to another object's hierarchy, destroyed
        // together on teardown: the step-hints instance and the arrows it reparents. The dim's
        // own objects are NOT here -- TutorialDim owns those, so that one class is the only
        // thing that has to be right about undoing them.
        private readonly List<GameObject> attachedObjects = new();

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
            dim.Build(transform, animConfig.TutorialDimOpacity);
            dim.LiftSprites(sourceItem);

            if (litExtras != null)
            {
                foreach (var extra in litExtras) dim.LiftSprites(extra);
            }

            if (dimmedCards != null)
            {
                foreach (var card in dimmedCards) dim.Curtain(card, animConfig.TutorialDimOpacity);
            }

            BuildGhost(sourceItem, trayTarget);

            // After the dim exists, so the lift is measured against something already there.
            // This is what keeps the one card the player must READ -- the order the forced
            // move is filling -- legible, along with the modification arrow parented to it.
            if (targetCard != null) dim.LiftElement(targetCard.gameObject);

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
                renderer.sortingOrder += TutorialDim.LitSortingBoost;
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

            // The sheet, the curtains and every sorting order this step borrowed.
            dim.Restore();

            foreach (var attached in attachedObjects)
            {
                if (attached != null) Destroy(attached);
            }
            attachedObjects.Clear();
        }
    }
}
