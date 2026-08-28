using DG.Tweening;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace ExpoTheExplorer.UI
{
    // Everything a forced-move step can draw ON TOP of the spotlight, in one authored prefab:
    // the sentence, the arrow that marks the modification on the board item, and the arrow
    // that marks the matching row on the ticket card.
    //
    // A PALETTE, NOT A LAYOUT. The step decides which of the three appear, and it decides them
    // from TWO INDEPENDENT authored flags -- `highlightModification` and a non-empty `message`
    // -- so a step can speak without pointing, or point without speaking. That is why the
    // arrows live here beside the message rather than inside it: burying them in a
    // message-only prefab would have quietly tied the two together (D-126).
    //
    // BOTH ARROWS ARE UI IMAGES ON THIS PREFAB'S OWN CANVAS (the user's ask, 2026-08-28), and
    // that is what makes them editable at all: the item arrow was a SpriteRenderer parked
    // outside the canvas and the card arrow was an Image parented OUTSIDE one, which never
    // renders -- so in the prefab stage there was nothing to click.
    //
    // THEY STILL MOVE DIFFERENTLY, and the difference is the point. The CARD arrow is
    // reparented into the ticket card's row, so it rides that card. The ITEM arrow stays on
    // this canvas and is merely POSITIONED over the board item's modification, with its
    // authored anchoredPosition kept as the offset: parented to the item it rode along when
    // the player picked the food up, and it marks a CELL to look at rather than an object to
    // follow. The author owns the offset, rotation, size and sprite of both.
    //
    // In a file of its own because it is a MonoBehaviour, which Unity can only bind when the
    // file name matches the class name (D-125, learned by having two prefabs refuse to save).
    public class TutorialStepHints : MonoBehaviour
    {
        [Tooltip("The message's plate, switched off for a step that authors no sentence. NOT the prefab root — hiding that would take the arrows with it.")]
        [SerializeField] private GameObject messageRoot;

        [Tooltip("The step's sentence, filled from the Day's authored message.")]
        [SerializeField] private TMP_Text messageText;

        [Tooltip("The arrow that marks the modification on the BOARD item. Stays on this prefab's canvas and is moved over the item, so it is editable here like everything else.")]
        [SerializeField] private Image itemArrow;

        [Tooltip("The arrow that marks the modification row on the TICKET CARD. Reparented into that card, so it rides it.")]
        [SerializeField] private Image cardArrow;

        // The nudge lives HERE rather than on BoardAnimationConfig, the way the powerup
        // spotlight's pulse lives on its own prefab: these are numbers an author tunes while
        // looking at the arrow they belong to, so they belong beside it.
        [Tooltip("How far each arrow slides back and forth along the direction it points, in canvas units. 0 holds it still.")]
        [Min(0f)]
        [SerializeField] private float arrowNudgeDistance = 26f;

        [Tooltip("Seconds for one half of that slide — out, then back. 0 holds it still.")]
        [Min(0f)]
        [SerializeField] private float arrowNudgePeriod = 0.55f;

        public Image ItemArrow => itemArrow;
        public Image CardArrow => cardArrow;

        // Every piece is optional. An author who deletes one costs that piece and nothing
        // else: the lesson still runs, the spotlight still dims, and the step is still
        // completable -- the standing rule for this whole tutorial.
        public void ShowMessage(string message)
        {
            if (messageRoot != null) messageRoot.SetActive(true);
            if (messageText != null) messageText.text = message;
        }

        public void HideMessage()
        {
            if (messageRoot != null) messageRoot.SetActive(false);
        }

        // Started by the view AFTER it has placed the arrows, because the nudge is measured
        // from wherever each one ended up -- the item arrow is moved over the board item and
        // the card arrow into a ticket row, so their resting positions are not known until
        // then.
        public void NudgeArrows()
        {
            Nudge(itemArrow);
            Nudge(cardArrow);
        }

        // ALONG THE ARROW'S OWN FACING, not along a fixed axis: the two arrows point in
        // different directions (one down-left at an ingredient, one right into a ticket row),
        // and an author who rotates either one should get a nudge that still reads as pointing
        // rather than as drifting sideways. The direction is the local rotation applied to
        // "right" -- the resting direction of the sprite -- which lands in the parent's space,
        // the same space anchoredPosition is measured in.
        private void Nudge(Image arrow)
        {
            if (arrow == null || !arrow.gameObject.activeSelf) return;
            if (arrowNudgeDistance <= 0f || arrowNudgePeriod <= 0f) return;

            var rect = arrow.rectTransform;
            var direction = (Vector2)(rect.localRotation * Vector3.right);

            // SetLink to the arrow rather than to this object: the card arrow is reparented
            // into the ticket card and outlives this prefab instance, so linking it here would
            // leave a tween running against a destroyed target.
            rect.DOAnchorPos(rect.anchoredPosition + direction * arrowNudgeDistance, arrowNudgePeriod)
                .SetEase(Ease.InOutSine)
                .SetLoops(-1, LoopType.Yoyo)
                .SetLink(arrow.gameObject);
        }

        // Called for a step that does not highlight a modification. The arrows are switched
        // off rather than destroyed so the prefab instance stays whole and one teardown path
        // takes everything.
        public void HideArrows()
        {
            if (itemArrow != null) itemArrow.gameObject.SetActive(false);
            if (cardArrow != null) cardArrow.gameObject.SetActive(false);
        }
    }
}
