using System.Collections.Generic;
using DG.Tweening;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace ExpoTheExplorer.UI
{
    // The forced-press step's visuals: a pulsing frame drawn around the one powerup the
    // player is being asked to press, and the step's sentence floating above it.
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
    // Assets/Prefabs/UI/TutorialPowerupSpotlight.prefab, built once by the menu step
    // TutorialPopupSetup; this class now positions it and animates it and nothing else.
    //
    // TWO PARTS, ONE PREFAB, because they are one lesson. The FRAME is reparented onto the
    // live HUD button at runtime -- it has to be, since it is drawn around a specific button
    // that only PowerupBarView can name -- and it is sized by ANCHORS rather than numbers, so
    // it fits whatever button it lands on and keeps whatever inset the author gave it. The
    // MESSAGE keeps its own Screen Space - Overlay canvas: the powerup bar lives on a Screen
    // Space - CAMERA canvas (D-086), so a sentence parented there would be drawn under the
    // world. Overlay composites above everything the camera renders.
    //
    // THE FRAME IS PARENTED TO THE BUTTON AND THEREFORE OUTLIVES THIS OBJECT, which is why
    // Dismiss exists and why destroying the host alone is not enough: it would leave a
    // pulsing frame around a button nobody is being asked to press.
    public class TutorialPowerupSpotlightView : MonoBehaviour
    {
        [Tooltip("The four-bar frame. Reparented onto the powerup button at runtime and stretched to it, keeping the inset authored here as its offsets.")]
        [SerializeField] private RectTransform frame;

        [Tooltip("Optional. Pulsed between full and Pulse Minimum Alpha so the frame breathes. One group for the whole frame, so its bars can never fall out of phase.")]
        [SerializeField] private CanvasGroup frameGroup;

        [Tooltip("Optional. The step's sentence. Left empty, the frame teaches on its own.")]
        [SerializeField] private TMP_Text messageText;

        [Tooltip("The message's own Screen Space - Overlay canvas. Positioned above the target button at runtime.")]
        [SerializeField] private RectTransform messageCanvasRect;

        [Tooltip("Gap between the top of the button and the sentence, in this canvas's units.")]
        [SerializeField] private float messageGap = 46f;

        [Tooltip("Seconds for one half of the pulse. 0 turns the pulse off and leaves the frame steady.")]
        [Min(0f)]
        [SerializeField] private float pulsePeriodSeconds = 0.7f;

        [Tooltip("How faint the frame gets at the bottom of each pulse.")]
        [Range(0f, 1f)]
        [SerializeField] private float pulseMinimumAlpha = 0.35f;

        // What was moved out of this prefab and onto the button, so Dismiss can bring it back
        // down. A list rather than one field because the frame is the only thing today and a
        // second lifted piece should not need a second field to be cleaned up.
        private readonly List<GameObject> reparented = new();
        private Tween pulseTween;

        // Created with everything already resolved -- this view looks nothing up. The target
        // is the live HUD button, which only PowerupBarView can hand over.
        public static TutorialPowerupSpotlightView Create(
            TutorialPowerupSpotlightView instance,
            RectTransform targetButton,
            string message)
        {
            if (instance == null || targetButton == null) return null;

            instance.Bind(targetButton, message);
            return instance;
        }

        private void Bind(RectTransform targetButton, string message)
        {
            AttachFrameTo(targetButton);
            ShowMessage(targetButton, message);
        }

        // Stretched to the button and given back the offsets the prefab was authored with, so
        // the inset around the button is the author's decision rather than a constant here.
        // worldPositionStays: false because the frame is moving into a different canvas's
        // space and should take its anchored layout with it, not its world position.
        private void AttachFrameTo(RectTransform targetButton)
        {
            if (frame == null)
            {
                Debug.LogWarning(
                    $"{nameof(TutorialPowerupSpotlightView)} on '{name}' has no Frame wired, so nothing marks the " +
                    "button the player is being asked to press. The lesson still runs. Drag the frame in.", this);
                return;
            }

            var authoredMin = frame.offsetMin;
            var authoredMax = frame.offsetMax;

            frame.SetParent(targetButton, worldPositionStays: false);
            reparented.Add(frame.gameObject);

            frame.anchorMin = Vector2.zero;
            frame.anchorMax = Vector2.one;
            frame.offsetMin = authoredMin;
            frame.offsetMax = authoredMax;
            frame.localScale = Vector3.one;
            frame.localRotation = Quaternion.identity;

            if (frameGroup == null || pulsePeriodSeconds <= 0f) return;

            // SetUpdate(true) for the reason the panel's fade uses it: this plays while the
            // day is held still, and a tween on scaled time would freeze with it.
            pulseTween = frameGroup
                .DOFade(pulseMinimumAlpha, pulsePeriodSeconds)
                .SetLoops(-1, LoopType.Yoyo)
                .SetLink(frame.gameObject)
                .SetUpdate(true);
        }

        private void ShowMessage(RectTransform targetButton, string message)
        {
            if (messageText == null) return;

            if (string.IsNullOrEmpty(message))
            {
                // A step with no authored sentence still gets its frame. The words are content
                // and an unauthored one is an authoring gap, not a reason to teach nothing --
                // the pulsing button is already most of the message.
                messageText.gameObject.SetActive(false);
                return;
            }

            messageText.text = message;

            if (messageCanvasRect == null) return;

            messageText.rectTransform.anchoredPosition = PositionAbove(targetButton);
        }

        // The button's top edge, taken through SCREEN space and back into the message
        // canvas's own rect. Screen space is the one coordinate system the two canvases
        // share: they are different render modes with different cameras, so a world position
        // from one means nothing in the other.
        private Vector2 PositionAbove(RectTransform targetButton)
        {
            var corners = new Vector3[4];
            targetButton.GetWorldCorners(corners);

            // Corner 1 is the top-left and 2 the top-right, so their midpoint is the middle
            // of the button's top edge -- the point the sentence should sit above.
            var topCentre = (corners[1] + corners[2]) * 0.5f;

            // A Screen Space - Overlay canvas is fed a null camera by contract; the button's
            // canvas hands over whichever one it renders through.
            var buttonCanvas = targetButton.GetComponentInParent<Canvas>();
            var sourceCamera = buttonCanvas != null && buttonCanvas.renderMode != RenderMode.ScreenSpaceOverlay
                ? buttonCanvas.worldCamera
                : null;

            var screenPoint = RectTransformUtility.WorldToScreenPoint(sourceCamera, topCentre);

            if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(messageCanvasRect, screenPoint, null, out var local))
            {
                // Wrong, but readable, and it beats a sentence parked at the canvas origin in
                // the corner.
                return new Vector2(0f, messageCanvasRect.rect.height * 0.3f);
            }

            // Relative to the canvas's own bottom-centre, which is where the text's anchor
            // sits, plus the authored gap so the sentence clears the frame.
            return new Vector2(0f, local.y + messageCanvasRect.rect.height * 0.5f + messageGap);
        }

        // Everything this view moved onto another object, taken down explicitly rather than
        // by destroying this object alone: the frame lives on the BUTTON's hierarchy, which
        // outlives this view, so destroying only the host would leave a pulsing frame around
        // a button nobody is being asked to press any more.
        public void Dismiss()
        {
            KillAndClear();
            if (this != null) Destroy(gameObject);
        }

        // The same guard, for the paths that destroy this object without going through
        // Dismiss -- a scene load, or the bar being torn down mid-step.
        private void OnDestroy() => KillAndClear();

        private void KillAndClear()
        {
            pulseTween?.Kill();
            pulseTween = null;

            foreach (var moved in reparented)
            {
                if (moved != null) Destroy(moved);
            }

            reparented.Clear();
        }
    }
}
