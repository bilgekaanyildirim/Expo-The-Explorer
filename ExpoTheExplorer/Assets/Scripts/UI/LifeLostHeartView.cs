using System.Collections.Generic;
using DG.Tweening;
using ExpoTheExplorer.Core;
using ExpoTheExplorer.Data;
using UnityEngine;
using UnityEngine.UI;

namespace ExpoTheExplorer.UI
{
    // A broken heart rising over the tray that just cost a life, fading as it goes.
    //
    // THE AUTHORED OBJECT IS THE ANIMATED OBJECT. Each slot's heart is an inactive
    // Image the author parks on the canvas, over that tray, exactly where it should
    // start; this script switches it on, drifts it up, fades it, and puts it back.
    // No template and no cloning, so the thing you place is the thing you see.
    //
    // A CANVAS Image rather than a world SpriteRenderer, which is the user's call
    // and settles a real trap along the way: the trays are world objects, so a
    // sprite parked inside one has to win a sorting-order fight with the tray
    // (order -2) and everything sitting in it, and losing that fight is silent --
    // the heart simply never appears. A canvas draws over the world entirely, so
    // there is nothing to lose. It also means these hearts are NOT children of the
    // tray: the tray's own animations (the wrong-order shake, the delivery fade
    // that sweeps every child renderer) cannot reach them, so there is no second
    // writer on this alpha by construction rather than by checking.
    //
    // Driven from GameManager.HandleLifeLoss rather than from the tray, because the
    // tray can only see half of it: WorldTrayView notices a timeout by watching its
    // item count fall to zero, so a ticket expiring over an untouched tray leaves
    // no trace there. Both life-loss routes end in that one handler, which is why
    // the slot index was threaded through TicketSlotManager and TrayManager to
    // reach it (decisions.md D-078).
    public class LifeLostHeartView : MonoBehaviour
    {
        // Safety valve, not a design number (the shape TicketCardView's
        // MaxTimerDividers set): the authored duration decides how slowly the heart
        // drifts, and this only stops a 0 from producing a heart that is gone before
        // it can be seen -- which reads as the feature failing rather than as a
        // number wanting raised.
        private const float MinVisibleDuration = 0.1f;

        [Tooltip("One INACTIVE broken-heart Image per ticket slot, placed on the canvas over that slot's tray at the exact position and size it should START from — this script animates that object itself and puts it back afterwards. Order matters: index 0 is slot 0 (the leftmost tray).")]
        [SerializeField] private Image[] slotHearts;
        [Tooltip("Read for how far the heart rises and how long it takes.")]
        [SerializeField] private BoardAnimationConfig animConfig;

        // Where each heart was authored, captured once. Re-applied every time a
        // heart plays, because after the first flight the live values are wherever
        // the tween left them -- the same reason TicketCardView captures its paper's
        // authored colour before the danger flash can overwrite it.
        private Vector2[] restPositions;
        private Color[] restColors;
        private bool isValid;

        private void Start()
        {
            isValid = ValidateReferences();
            if (!isValid) return;

            restPositions = new Vector2[slotHearts.Length];
            restColors = new Color[slotHearts.Length];

            for (var i = 0; i < slotHearts.Length; i++)
            {
                var heart = slotHearts[i];
                if (heart == null) continue;

                restPositions[i] = heart.rectTransform.anchoredPosition;
                restColors[i] = heart.color;

                // Forced off whatever the scene was saved as: a heart left visible
                // hangs over the tray for the whole day, and it is easy to leave
                // ticked after positioning it. Same call TicketCardView makes on its
                // own templates, for the same reason.
                heart.gameObject.SetActive(false);
            }
        }

        // Called by GameManager.HandleLifeLoss, after the life is actually gone.
        public void Show(int slotIndex)
        {
            if (!isValid) return;
            if (slotIndex < 0 || slotIndex >= slotHearts.Length) return;

            var heart = slotHearts[slotIndex];
            if (heart == null) return;

            // Two lives can be lost on one slot in quick succession (a wrong order,
            // then the replacement ticket timing out), so a flight already in the air
            // is killed and restarted from the authored rest rather than tweened from
            // wherever it had drifted to -- which would make the second heart start
            // halfway up and half faded.
            heart.rectTransform.DOKill();
            heart.DOKill();

            var rest = restPositions[slotIndex];
            heart.rectTransform.anchoredPosition = rest;
            heart.color = restColors[slotIndex];
            heart.gameObject.SetActive(true);

            var duration = Mathf.Max(MinVisibleDuration, animConfig.LifeLostHeartDuration);

            var sequence = DOTween.Sequence();
            sequence.Append(heart.rectTransform
                .DOAnchorPosY(rest.y + animConfig.LifeLostHeartRiseDistance, duration)
                .SetEase(Ease.OutSine));

            // Ease.InQuad on the fade against OutSine on the rise: the heart is still
            // solid while it is moving fastest and thins out as it slows, which reads
            // as drifting away rather than as a light being switched off.
            sequence.Join(heart.DOFade(0f, duration).SetEase(Ease.InQuad));

            // Put back exactly as authored, not just switched off: the next life lost
            // on this slot has to start from the position and colour in the scene, and
            // leaving it faded at the top of its climb would make the second heart
            // invisible.
            sequence.OnComplete(() =>
            {
                heart.gameObject.SetActive(false);
                heart.rectTransform.anchoredPosition = rest;
                heart.color = restColors[slotIndex];
            });
        }

        // Wired by hand in the Editor, so a missing reference should name itself
        // rather than fail silently. A hole in the array is a warning rather than an
        // error: the other slots still work, and there is no sensible fallback for a
        // heart whose whole point is the position the author gave it.
        private bool ValidateReferences()
        {
            if (animConfig == null)
            {
                Debug.LogError($"{nameof(LifeLostHeartView)} on '{name}' is missing its {nameof(animConfig)}. No broken heart will be shown when a life is lost.", this);
                return false;
            }

            if (slotHearts == null || slotHearts.Length == 0)
            {
                Debug.LogError($"{nameof(LifeLostHeartView)} on '{name}' has no {nameof(slotHearts)} wired. Place an inactive heart Image over each tray and drop them here in slot order.", this);
                return false;
            }

            var empty = new List<int>();
            for (var i = 0; i < GameState.TicketSlotCount; i++)
            {
                if (i >= slotHearts.Length || slotHearts[i] == null) empty.Add(i);
            }

            if (empty.Count > 0)
            {
                Debug.LogWarning($"{nameof(LifeLostHeartView)} on '{name}' has no heart for slot(s) {string.Join(", ", empty)} — a life lost on those trays will show nothing.", this);
            }

            return true;
        }
    }
}
