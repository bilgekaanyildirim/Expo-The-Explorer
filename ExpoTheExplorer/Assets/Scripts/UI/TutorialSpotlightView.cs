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
    // Built entirely at runtime and authored nowhere: no prefab, no scene object, no art,
    // nothing dragged into an Inspector. One of these exists per STEP, created by that
    // step's target WorldTrayView -- the one object already holding both of the ghost's
    // endpoints (a serialized BoardView for the source cell, its own transform for the
    // destination).
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

        // The message's own Overlay canvas sits BELOW Popup Canvas (Overlay, order 0) on
        // purpose: a ticket can time out mid-step and put the Game Over popup on screen, and
        // a tutorial line floating over that popup would be worse than one hidden behind it.
        // Any negative order is above every camera-rendered thing regardless, because Overlay
        // always is.
        private const int MessageCanvasSortingOrder = -1;

        // The world arrow's length, as a multiple of the layer it points at. Relative rather
        // than absolute so it holds up across board and camera sizes.
        private const float WorldArrowLengthFactor = 1.6f;

        // The canvas arrow's length, as a multiple of the modification row's height.
        private const float CanvasArrowLengthFactor = 1.8f;

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
            if (step != null && step.HighlightModification)
            {
                BuildModificationArrows(sourceItem, sourceBoardItem, targetCard);
            }

            if (step != null && !string.IsNullOrEmpty(step.Message))
            {
                BuildMessage(step.Message, targetCard);
            }
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

        // Two arrows for the same fact, one in each rendering world: the ingredient a
        // modification added to the item on the board, and that modification's box on the
        // ticket. Pointing at the ingredient is only possible because ResolvedLayer carries
        // the modification that made it visible -- "which of these sprites IS the extra
        // mustard" has no other answer, since a food's layers are just a stack of sprites.
        private void BuildModificationArrows(Transform sourceItem, BoardItem sourceBoardItem, TicketCardView targetCard)
        {
            var layer = FindModificationLayer(sourceItem, sourceBoardItem);
            if (layer != null)
            {
                var size = ApproximateWorldSize(layer);
                BuildWorldArrow(layer, size * WorldArrowLengthFactor);
            }

            var row = targetCard != null ? targetCard.FirstModificationRow : null;
            if (row != null)
            {
                BuildCanvasArrow(row);
            }
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

        private static float ApproximateWorldSize(Transform target)
        {
            if (target.TryGetComponent<SpriteRenderer>(out var renderer) && renderer.sprite != null)
            {
                var bounds = renderer.bounds.size;
                return Mathf.Max(bounds.x, bounds.y);
            }

            return Mathf.Max(target.lossyScale.x, target.lossyScale.y);
        }

        // Sits above and to the right of what it points at, angled down-left at it. A fixed
        // diagonal rather than anything computed: the thing it indicates is small and the
        // screen above it is the dimmed board, so there is nothing to collide with, and a
        // predictable angle reads better than one that moves between steps.
        private void BuildWorldArrow(Transform target, float length)
        {
            var arrow = new GameObject("ModificationArrow");
            arrow.transform.SetParent(transform, false);

            var renderer = arrow.AddComponent<SpriteRenderer>();
            renderer.sprite = CreateArrowSprite();
            renderer.sortingOrder = DimSortingOrder + LitSortingBoost + 1;

            // The sprite points RIGHT at rest, so -135 degrees aims it down-left, and the
            // object is placed up-right of the target by the same diagonal.
            arrow.transform.rotation = Quaternion.Euler(0f, 0f, -135f);
            arrow.transform.localScale = Vector3.one * length;

            var offset = new Vector3(length * 0.55f, length * 0.55f, 0f);
            arrow.transform.position = target.position + offset;

            FadeIn(renderer);
            attachedObjects.Add(arrow);
        }

        // The canvas twin of the arrow above, parented to the modification row itself so it
        // follows the card's layout instead of being positioned against a screen that the
        // HorizontalLayoutGroup can re-flow at any time.
        private void BuildCanvasArrow(RectTransform row)
        {
            var arrow = new GameObject("ModificationArrow", typeof(RectTransform));
            var rect = (RectTransform)arrow.transform;
            rect.SetParent(row, false);

            var size = Mathf.Max(row.rect.height, 1f) * CanvasArrowLengthFactor;

            // Sits to the LEFT of the row and points right, into it. Anchored to the row's
            // left edge with the arrow's own right edge (its tip) as the pivot, so the tip
            // lands just outside the box no matter how wide the row is and the body extends
            // away from the card rather than across it.
            //
            // The right-hand side was tried first and was wrong twice over: the arrow ended
            // up far from the ingredient icon it labels, and rotating it 180 degrees to aim
            // back at the row turned it about that same pivot, which swung the whole body
            // over the row and left the tip somewhere in the middle of the box. No rotation
            // is needed here -- the generated sprite already points right.
            rect.anchorMin = new Vector2(0f, 0.5f);
            rect.anchorMax = new Vector2(0f, 0.5f);
            rect.pivot = new Vector2(1f, 0.5f);
            rect.sizeDelta = new Vector2(size, size);
            rect.anchoredPosition = new Vector2(-size * 0.15f, 0f);

            var image = arrow.AddComponent<Image>();
            image.sprite = CreateArrowSprite();
            image.raycastTarget = false;

            FadeIn(image);
            attachedObjects.Add(arrow);
        }

        // The step's line of text, on the same Canvas the ticket cards live on and in the
        // same font -- borrowed off the card rather than serialized, so the message matches
        // the game's own type without anything being wired by hand.
        //
        // THE PLATE IS THE PARENT AND THE TEXT ITS CHILD, and that order is the whole
        // correctness of this method rather than a stylistic choice. UGUI draws a Graphic
        // before its children, always, so the first version -- text on the outer object with
        // the plate parented under it -- painted the plate straight over the glyphs and the
        // message never appeared at all. SetAsFirstSibling did not save it: sibling index
        // orders siblings, and a child is never behind its parent.
        //
        // Every path that declines to draw says why. This method fails INVISIBLY by nature
        // (no message is also what an unauthored step looks like), so a silent return here
        // is a bug that hides itself.
        private void BuildMessage(string message, TicketCardView targetCard)
        {
            if (targetCard == null)
            {
                Debug.LogWarning($"{nameof(TutorialSpotlightView)}: this step authors a message but the target tray has no ticket card, so there is nothing to hang it on and no message is shown.", this);
                return;
            }

            var gameCanvas = targetCard.GetComponentInParent<Canvas>();
            if (gameCanvas == null)
            {
                Debug.LogWarning($"{nameof(TutorialSpotlightView)}: the ticket card is not under a Canvas, so the step's message has no scaler to match and is not shown.", this);
                return;
            }

            // The font is borrowed rather than loaded so the message matches the game. A
            // card with no TMP text at all means no font to copy -- and inventing one with
            // TMP's default would silently look nothing like the rest of the UI.
            var fontSource = targetCard.GetComponentInChildren<TMP_Text>(true);
            if (fontSource == null || fontSource.font == null)
            {
                Debug.LogWarning($"{nameof(TutorialSpotlightView)}: no TextMeshPro font could be borrowed from the ticket card, so the step's message is not shown.", this);
                return;
            }

            // ITS OWN OVERLAY CANVAS, not the game's. The message went on the ticket cards'
            // canvas first and was never once visible, because that canvas is Screen Space -
            // CAMERA at sortingOrder -1 and the dim is a sprite at 500 -- so the whole canvas,
            // message included, was drawn and then buried. An Overlay canvas is the only thing
            // that composites above everything the camera renders no matter what else is on
            // screen, and building our own means depending on nothing in the scene.
            var messageCanvasObject = new GameObject("TutorialMessageCanvas");
            messageCanvasObject.transform.SetParent(transform, false);

            var messageCanvas = messageCanvasObject.AddComponent<Canvas>();
            messageCanvas.renderMode = RenderMode.ScreenSpaceOverlay;
            messageCanvas.sortingOrder = MessageCanvasSortingOrder;

            // The game's own scaler is copied rather than guessed at, so the authored font
            // size means the same thing here as it does on a ticket card. Without it this
            // canvas would scale with raw pixels and the message would be a different size
            // on every device than everything around it.
            var gameScaler = gameCanvas.GetComponent<CanvasScaler>();
            if (gameScaler != null)
            {
                var scaler = messageCanvasObject.AddComponent<CanvasScaler>();
                scaler.uiScaleMode = gameScaler.uiScaleMode;
                scaler.referenceResolution = gameScaler.referenceResolution;
                scaler.screenMatchMode = gameScaler.screenMatchMode;
                scaler.matchWidthOrHeight = gameScaler.matchWidthOrHeight;
                scaler.referencePixelsPerUnit = gameScaler.referencePixelsPerUnit;
            }

            // The outer object carries the PLATE. Spans the width with a margin either side,
            // at the authored height; anchoring rather than positioning keeps it correct on
            // every aspect ratio, which matters because this is the one element with no
            // object to hang off.
            var plateObject = new GameObject("TutorialMessage", typeof(RectTransform));
            var plateRect = (RectTransform)plateObject.transform;
            plateRect.SetParent(messageCanvasObject.transform, false);

            var height = Mathf.Clamp01(animConfig.TutorialMessageScreenHeight);
            plateRect.anchorMin = new Vector2(0.06f, height);
            plateRect.anchorMax = new Vector2(0.94f, height);
            plateRect.pivot = new Vector2(0.5f, 0.5f);
            plateRect.anchoredPosition = Vector2.zero;
            plateRect.sizeDelta = new Vector2(0f, animConfig.TutorialMessageFontSize * 3f);
            plateRect.SetAsLastSibling();

            var plateImage = plateObject.AddComponent<Image>();
            plateImage.color = new Color(0f, 0f, 0f, 0.55f);
            plateImage.raycastTarget = false;

            // The TEXT is the child, so it draws on top of the plate. Inset a little so the
            // glyphs do not touch the plate's edges.
            var textObject = new GameObject("Text", typeof(RectTransform));
            var textRect = (RectTransform)textObject.transform;
            textRect.SetParent(plateRect, false);
            Stretch(textRect);
            var padX = animConfig.TutorialMessageFontSize * 0.5f;
            var padY = animConfig.TutorialMessageFontSize * 0.25f;
            textRect.offsetMin = new Vector2(padX, padY);
            textRect.offsetMax = new Vector2(-padX, -padY);

            var text = textObject.AddComponent<TextMeshProUGUI>();
            text.font = fontSource.font;
            text.text = message;
            text.fontSize = animConfig.TutorialMessageFontSize;
            text.alignment = TextAlignmentOptions.Center;
            text.textWrappingMode = TextWrappingModes.Normal;
            text.raycastTarget = false;
            text.color = Color.white;

            FadeIn(plateImage, 0.55f);
            FadeIn(text);

            // Only the plate is registered: the text goes with it as its child.
            attachedObjects.Add(plateObject);
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

        // A right-pointing arrow drawn in code for the same reason the ghost is a clone of a
        // live item: this feature ships no art. A filled triangle head over the right half and
        // a shaft along the middle of the left half, on a transparent square, so one sprite
        // serves both the world arrow and the canvas one and both can simply be rotated.
        private static Sprite CreateArrowSprite()
        {
            const int size = 64;
            var texture = new Texture2D(size, size) { filterMode = FilterMode.Bilinear };
            var pixels = new Color[size * size];

            for (var y = 0; y < size; y++)
            {
                for (var x = 0; x < size; x++)
                {
                    // Distance from the horizontal centre line, so both halves are described
                    // by one expression instead of two mirrored ones.
                    var fromCentre = Mathf.Abs(y - (size - 1) / 2f);

                    // The head occupies the right 45%, narrowing linearly to a point at the
                    // right edge; the shaft is a constant-thickness bar across the left.
                    var inHead = x >= size * 0.55f && fromCentre <= (size - x) * 0.62f;
                    var inShaft = x < size * 0.6f && fromCentre <= size * 0.11f;

                    pixels[y * size + x] = inHead || inShaft ? Color.white : Color.clear;
                }
            }

            texture.SetPixels(pixels);
            texture.Apply();
            return Sprite.Create(texture, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), size);
        }
    }
}
