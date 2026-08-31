using System.Collections.Generic;
using DG.Tweening;
using ExpoTheExplorer.Data;
using UnityEngine;

namespace ExpoTheExplorer.UI
{
    // The forced-press step's visual: an arrow bouncing over the one powerup the player is
    // being asked to press -- plus, for a step that asks for them, a dim behind it and a line
    // of authored text.
    //
    // IT OWNS NO TEXT OBJECT OF ITS OWN, and that is deliberate twice over. This view carried
    // a sentence half from the day it was written and no step ever authored a line for it, so
    // on 2026-08-31 the user deleted it (D-121's "one argument away" had been standing for
    // three days with nothing to say). When a line was wanted hours later, it came back as a
    // reader rather than as a plate: the day's other tutorial sentences are drawn by the
    // TutorialStepHints prefab, so this step borrows that -- one look, one authored spot on
    // screen, and no second thing to restyle when the first one changes.
    //
    // IT POINTS WITH THE TUTORIAL'S ARROW SINCE 2026-08-31 (the user's ask), where it used to
    // draw a pulsing four-bar frame around the button. The arrow is Art/UI/Arrow.png -- the
    // same sprite the board item and ticket-row hints use (TutorialStepHints) and the same one
    // the main screen points at the store with (MainScreenStoreHintPopup), so the game has one
    // gesture for "press this" instead of a frame here and an arrow everywhere else.
    //
    // IT DELIBERATELY DIMS NOTHING, and that is the design rather than a saving. The move
    // steps dim the board because there the lesson IS the one item and everything else is
    // distraction. Here the lesson is the opposite: Time Reset is taught at the moment a
    // ticket is running out, and the thing the player has to see is that ticket's timer bar
    // draining. Dimming the cards would hide the very reason the powerup is being pressed.
    // Nothing needs to be dimmed for safety either -- TutorialDirector already refuses the
    // board, the trays and the other two powerups for the whole of an armed step, so the
    // screen is inert without a backdrop to make it so.
    //
    // IT IS AN AUTHORED PREFAB SINCE D-116 (the user's ask, 2026-08-28), where it used to
    // build every bar and every label from constants in C#. The hierarchy lives in
    // Assets/Prefabs/UI/TutorialPowerupSpotlight.prefab -- seeded once by a
    // TutorialPopupSetup menu step, which was deleted with the other one-shot builders on
    // 2026-08-30, so the prefab on disk is the only copy; this class now positions it and
    // animates it and nothing else.
    //
    // THE ARROW LIVES ON THIS PREFAB'S OWN OVERLAY CANVAS, and that is the load-bearing fact
    // here. THE POWERUP BAR IS ON A SCREEN SPACE - CAMERA CANVAS AT SORTING ORDER -1
    // (HUDCanvas, D-086), so anything parented there is drawn UNDER the world sprites. The
    // frame this arrow replaced got away with that by hugging the button, down in the strip of
    // screen the board never reaches; an arrow points UPWARD, and the first thing it reached
    // was the board's bottom row, which swallowed it whole with nothing reporting a thing --
    // no error, no warning, a wired reference and a clone in the hierarchy. So the arrow sits
    // on a Screen Space - Overlay canvas, which composites above everything the camera
    // renders, and is POSITIONED by measuring the button through SCREEN space -- the one
    // coordinate system two canvases with different render modes share. The canvas outlived
    // the sentence it was built for and is named for what it does now.
    //
    // NOTHING IS REPARENTED ONTO THE BUTTON any more, which is why this view has no cleanup
    // beyond killing its tween: every piece it owns is a child of its own object and dies with
    // it. The cost is that the arrow no longer RIDES the button, so a bar that moved mid-step
    // would leave it behind -- nothing moves the bar while a step is armed, and the previous
    // arrangement paid for that property with an arrow nobody could see.
    public class TutorialPowerupSpotlightView : MonoBehaviour
    {
        [Tooltip("The arrow. Lives on the Overlay canvas below and is positioned over the powerup button at runtime. Its sprite, colour and ROTATION are authored here — the rotation is what makes it point down at the button, and nothing in code overwrites it.")]
        [SerializeField] private RectTransform arrow;

        [Tooltip("Size of the arrow, as a multiple of the button's height. The one thing this prefab cannot know: only the running layout knows how big a powerup button ended up.")]
        [Range(0.4f, 3f)]
        [SerializeField] private float arrowScale = 1.35f;

        [Tooltip("Where the arrow's CENTRE rests, measured from the middle of the button's TOP EDGE, in this canvas's units. Y lifts it, X slides it sideways, and both may be negative. Because it is the CENTRE, a Y below half the arrow's height puts the arrow over the button itself. THIS is what positions the arrow — dragging it in the prefab stage does nothing, since the button it points at does not exist until the day runs.")]
        [SerializeField] private Vector2 arrowOffset = new Vector2(0f, 120f);

        [Tooltip("How far the arrow rises above its resting place and falls back, in this canvas's units. 0 holds it still.")]
        [Min(0f)]
        [SerializeField] private float bounceDistance = 68f;

        [Tooltip("Seconds for one half of the bounce — up, then back down. 0 holds the arrow still.")]
        [Min(0f)]
        [SerializeField] private float bouncePeriodSeconds = 0.7f;

        [Tooltip("The prefab's own Screen Space - Overlay canvas, which is the only thing here that draws above the world: the powerup bar's canvas is Screen Space - Camera and would put the arrow behind the board (D-086).")]
        [SerializeField] private RectTransform overlayCanvasRect;

        [Tooltip("OPTIONAL, and needed only by a step that dims: it carries the dim's opacity and the step-hints prefab the sentence is drawn on. Unwired, the lesson is the arrow alone — no dim, no line.")]
        [SerializeField] private BoardAnimationConfig animConfig;

        private Tween bounceTween;

        // The dim sheet and every sorting order borrowed to keep something bright over it.
        // Shared with the move steps' spotlight since D-146; this view builds one only when a
        // step hands it something to keep lit.
        private readonly TutorialDim dim = new();

        // The sentence's plate, when this step authored one. The SAME prefab the day's other
        // tutorial lines use, so every in-day sentence looks alike and speaks from the one
        // authored spot on screen.
        private GameObject hintsObject;

        // Created with everything already resolved -- this view looks nothing up. The target
        // is the live HUD button, which only PowerupBarView can hand over.
        public static TutorialPowerupSpotlightView Create(
            TutorialPowerupSpotlightView instance,
            RectTransform targetButton,
            IReadOnlyList<RectTransform> litExtras,
            string message)
        {
            if (instance == null || targetButton == null) return null;

            instance.Bind(targetButton, litExtras, message);
            return instance;
        }

        private void Bind(RectTransform targetButton, IReadOnlyList<RectTransform> litExtras, string message)
        {
            // One layout flush before anything is measured. The prefab is instantiated and
            // bound inside a single frame, so without it both rects PlaceArrow reads -- the
            // button's and this canvas's -- can still be the zeroes they were serialized with,
            // and the arrow would be sized and placed against them.
            Canvas.ForceUpdateCanvases();

            BuildDim(targetButton, litExtras);
            PlaceArrow(targetButton);
            ShowMessage(message);
        }

        // A DIM ONLY WHEN THE STEP HANDS OVER SOMETHING TO KEEP LIT, which is what makes this
        // one view serve both kinds of press lesson without a mode flag. The AtDayStart
        // lessons pass nothing and stay exactly as they were -- an arrow on a live screen,
        // which is right when the player has just closed a panel and nothing has changed. The
        // deferred one passes the three timers, because it fires minutes later against a board
        // the player is busy with, and its whole argument is "look at that clock".
        //
        // D-115's "IT DELIBERATELY DIMS NOTHING" was never about dimming being wrong here; it
        // was about not hiding the timer bars, which are the reason the powerup is being
        // pressed. Dimming everything EXCEPT them says the same thing louder.
        //
        // The arrow needs no lift: it lives on this prefab's Overlay canvas, which composites
        // above everything the camera renders, dim included. That is the same property the
        // sentence's plate relies on, and it fell out of the fix for the arrow being buried.
        private void BuildDim(RectTransform targetButton, IReadOnlyList<RectTransform> litExtras)
        {
            if (litExtras == null || litExtras.Count == 0) return;

            if (animConfig == null)
            {
                Debug.LogWarning(
                    $"{nameof(TutorialPowerupSpotlightView)} on '{name}' has no {nameof(BoardAnimationConfig)} wired, " +
                    "so this step cannot dim the screen and the lesson runs on the arrow alone. Drag the config in.",
                    this);
                return;
            }

            // No camera means no sheet, and a half-applied dim is worse than none: the lifts
            // below would raise the button and the timers over nothing while everything else
            // stayed at full brightness, which reads as a rendering bug rather than a lesson.
            if (!dim.Build(transform, animConfig.TutorialDimOpacity)) return;

            // INTERACTIVE, and that distinction is the one thing in this method that has
            // already gone wrong once: the button is the only thing on screen the player is
            // allowed to press, and a nested sorting canvas without a raycaster beside it
            // takes its clicks away without a word.
            dim.LiftInteractiveElement(targetButton.gameObject);

            foreach (var extra in litExtras)
            {
                if (extra != null) dim.LiftElement(extra.gameObject);
            }
        }

        // On the SAME plate the day's other tutorial sentences use, message-only. That prefab
        // was built as a palette rather than a layout (D-126) precisely so a step could speak
        // without pointing, and this is the step that finally does: the pointing is the arrow's
        // job here, so its two arrows are switched off.
        private void ShowMessage(string message)
        {
            if (string.IsNullOrEmpty(message)) return;

            var prefab = animConfig != null ? animConfig.TutorialStepHintsPrefab : null;
            if (prefab == null)
            {
                Debug.LogWarning(
                    $"{nameof(TutorialPowerupSpotlightView)} on '{name}': this step authors a line but there is no " +
                    $"Tutorial Step Hints Prefab on {nameof(BoardAnimationConfig)} to draw it on, so it goes unsaid. " +
                    "The lesson still runs.", this);
                return;
            }

            // Parentless, like the move steps' copy: the prefab carries its own Screen Space -
            // Overlay canvas, and a canvas nested inside another one inherits its parent's
            // rect rather than the screen's (D-086).
            hintsObject = Instantiate(prefab);

            var hints = hintsObject.GetComponent<TutorialStepHints>();
            if (hints == null)
            {
                Debug.LogWarning(
                    $"{nameof(TutorialPowerupSpotlightView)} on '{name}': the Tutorial Step Hints Prefab has no " +
                    $"{nameof(TutorialStepHints)} on its root, so this step's line cannot be filled in.", hintsObject);
                return;
            }

            hints.ShowMessage(message);
            hints.HideArrows();
        }

        // Placed OVER the button rather than parented to it, for the reason at the top of this
        // file. What survives from the store arrow's block is its arithmetic: the size comes
        // from the BUTTON's height x an authored scale, which is the one thing a prefab cannot
        // know, and the pivot is forced to the CENTRE so the authored rotation swings the arrow
        // about its middle rather than its base -- with a bottom pivot a -90 turn moves the
        // whole body an arrow-length sideways.
        private void PlaceArrow(RectTransform targetButton)
        {
            if (arrow == null)
            {
                Debug.LogWarning(
                    $"{nameof(TutorialPowerupSpotlightView)} on '{name}' has no Arrow wired, so nothing marks the " +
                    "button the player is being asked to press. The lesson still runs. Drag the arrow in.", this);
                return;
            }

            if (overlayCanvasRect == null)
            {
                Debug.LogWarning(
                    $"{nameof(TutorialPowerupSpotlightView)} on '{name}' has no Overlay Canvas Rect wired. That canvas " +
                    "is the only thing here that draws above the world, so an arrow without it would sit behind the " +
                    "board. Hiding it rather than drawing it where nobody can see it. Drag the canvas in.", this);
                arrow.gameObject.SetActive(false);
                return;
            }

            if (!TryMeasureButton(targetButton, out var buttonTop, out var buttonHeight))
            {
                // Off screen, or a camera that could not be resolved. An arrow bouncing at the
                // canvas origin points at nothing and reads as a bug, so the lesson goes on
                // without it.
                arrow.gameObject.SetActive(false);
                return;
            }

            var arrowSize = buttonHeight * arrowScale;

            arrow.SetParent(overlayCanvasRect, worldPositionStays: false);
            arrow.anchorMin = new Vector2(0.5f, 0f);
            arrow.anchorMax = new Vector2(0.5f, 0f);
            arrow.pivot = new Vector2(0.5f, 0.5f);
            arrow.sizeDelta = new Vector2(arrowSize, arrowSize);
            arrow.localScale = Vector3.one;

            // localRotation is deliberately NOT reset. Which way the arrow points is the
            // author's decision and the only place it is expressed; the frame could be
            // flattened to identity because a rectangle has no facing.

            // The offset is AUTHORED rather than a multiple of the arrow's size, which is the
            // shape TutorialStepHints' nudge already uses: these are numbers somebody tunes
            // while looking at the arrow, and a hidden 0.75 in here meant nothing in the
            // Inspector could move it at all.
            var resting = buttonTop + arrowOffset;
            arrow.anchoredPosition = resting;

            var travel = bouncePeriodSeconds > 0f ? bounceDistance : 0f;

            if (travel > 0f)
            {
                // Rises from its resting place and falls back. Kept ABOVE that place rather
                // than around it, so an arrow authored to sit just clear of the button never
                // bounces down onto the icon it is pointing at.
                //
                // SetUpdate(true) for the reason the pulse it replaced used it: this plays
                // while the day is held still, and a tween on scaled time would freeze with it.
                bounceTween = arrow
                    .DOAnchorPosY(resting.y + travel, bouncePeriodSeconds)
                    .SetEase(Ease.InOutSine)
                    .SetLoops(-1, LoopType.Yoyo)
                    .SetLink(arrow.gameObject)
                    .SetUpdate(true);
            }
        }

        // The button's top edge and its height, both brought into this canvas's own space.
        // SCREEN space is the bridge: the two canvases are different render modes with
        // different cameras, so a world position from one means nothing in the other -- and
        // their UNITS would differ too if their scalers ever diverged, which is why the height
        // is measured in pixels and divided by this canvas's scale factor rather than read
        // straight off the button's rect.
        private bool TryMeasureButton(RectTransform targetButton, out Vector2 buttonTop, out float height)
        {
            buttonTop = Vector2.zero;
            height = 0f;

            var corners = new Vector3[4];
            targetButton.GetWorldCorners(corners);

            // Corner 0 is the bottom-left, 1 the top-left, 2 the top-right, 3 the bottom-right,
            // so the midpoint of 1 and 2 is the middle of the top edge -- the point both halves
            // of this lesson sit above.
            var worldTop = (corners[1] + corners[2]) * 0.5f;
            var worldBottom = (corners[0] + corners[3]) * 0.5f;

            // A Screen Space - Overlay canvas is fed a null camera by contract; the button's
            // canvas hands over whichever one it renders through.
            var buttonCanvas = targetButton.GetComponentInParent<Canvas>();
            var sourceCamera = buttonCanvas != null && buttonCanvas.renderMode != RenderMode.ScreenSpaceOverlay
                ? buttonCanvas.worldCamera
                : null;

            var screenTop = RectTransformUtility.WorldToScreenPoint(sourceCamera, worldTop);
            var screenBottom = RectTransformUtility.WorldToScreenPoint(sourceCamera, worldBottom);

            var overlayCanvas = overlayCanvasRect.GetComponentInParent<Canvas>();
            var scaleFactor = overlayCanvas != null && overlayCanvas.scaleFactor > 0f ? overlayCanvas.scaleFactor : 1f;
            height = Mathf.Max(Mathf.Abs(screenTop.y - screenBottom.y) / scaleFactor, 1f);

            if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(overlayCanvasRect, screenTop, null, out var local))
            {
                return false;
            }

            // ScreenPointToLocalPointInRectangle answers relative to the rect's CENTRE, while
            // both pieces are anchored to its bottom-centre, so half the canvas height closes
            // the gap. That is the conversion the sentence has always used.
            buttonTop = new Vector2(local.x, local.y + overlayCanvasRect.rect.height * 0.5f);
            return true;
        }

        // The arrow is a child of this object and dies with it, but two things are not: the
        // sentence's plate is a parentless root (its canvas needs the screen's rect), and the
        // dim borrowed sorting orders from objects that outlive every step. The restore has to
        // happen HERE rather than being left to OnDestroy, because Unity defers Destroy to the
        // end of the frame while the next step's visuals are built immediately -- a boundary
        // would otherwise lift the new step's targets and only then put the old step's back.
        public void Dismiss()
        {
            TearDown();
            if (this != null) Destroy(gameObject);
        }

        // The same guard, for the paths that destroy this object without going through
        // Dismiss -- a scene load, or the bar being torn down mid-step. A dim that outlived
        // its restore would leave the game permanently dark, which is the worst failure
        // available here.
        private void OnDestroy() => TearDown();

        private void TearDown()
        {
            bounceTween?.Kill();
            bounceTween = null;

            dim.Restore();

            if (hintsObject != null) Destroy(hintsObject);
            hintsObject = null;
        }
    }
}
