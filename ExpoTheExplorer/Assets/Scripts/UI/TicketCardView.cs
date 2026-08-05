using System.Collections.Generic;
using System.Linq;
using DG.Tweening;
using ExpoTheExplorer.Bootstrap;
using ExpoTheExplorer.Core;
using ExpoTheExplorer.Data;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace ExpoTheExplorer.UI
{
    // One ticket slot's card. Pure "bind" component — every visual piece is a
    // reference wired up in the Inspector (the Canvas/card hierarchy is built by
    // hand in the Editor, not procedurally), so this script only ever changes
    // .sprite/.text/.SetActive on those existing references. Never creates or
    // destroys structural GameObjects except cloning modificationRowTemplate.
    //
    // Polls its slot every frame instead of binding to GameState.TicketDelivered/
    // TicketCancelled: those events fire BEFORE TicketSlotManager reassigns the
    // slot, and FillEmptySlots (initial population) never fires anything at all —
    // neither event can tell this view what's actually in the slot right now.
    // The same per-frame check drives the numeric timer, which has no event at
    // all since RemainingSeconds is mutated directly every frame.
    public class TicketCardView : MonoBehaviour
    {
        [SerializeField] private Image background;
        [SerializeField] private TMP_Text customerNameText;
        [SerializeField] private TMP_Text timeRemainingText;
        [SerializeField] private Image dishImage;
        [SerializeField] private Transform modificationsListParent;
        [SerializeField] private ModificationSlotView modificationRowTemplate;
        [SerializeField] private Image sideImage;
        [SerializeField] private Image drinkImage;
        [Tooltip("Needed to fade the whole card (including dynamically-created modification rows) as one unit during the delivery-success exit/entry transition.")]
        [SerializeField] private CanvasGroup canvasGroup;

        private GameManager gameManager;
        private int slotIndex;
        private TicketCardsView owner;
        private BoardAnimationConfig animConfig;
        private Ticket cachedTicket;
        private bool isValid;
        private bool transitionInProgress;
        private RectTransform rectTransform;
        private readonly List<ModificationSlotView> modificationRows = new();

        public void Initialize(GameManager gameManager, int slotIndex, TicketCardsView owner, BoardAnimationConfig animConfig)
        {
            this.gameManager = gameManager;
            this.slotIndex = slotIndex;
            this.owner = owner;
            this.animConfig = animConfig;

            isValid = ValidateReferences();
            if (!isValid) return;

            rectTransform = (RectTransform)transform;

            // The container itself must stay active — only the template row
            // inside it (and the clones built from it) toggle. Prefab authoring
            // sometimes leaves this off after hiding the two sample rows in the
            // Editor, which silently hides every real modification row too.
            modificationsListParent.gameObject.SetActive(true);
            modificationRowTemplate.gameObject.SetActive(false);
            RebuildContent(null);
        }

        // Every field here is wired by hand in the Editor (no procedural
        // fallback) — a missing one should fail loudly with a clear pointer to
        // which GameObject/field, not a bare NullReferenceException three
        // frames deep in Unity's own Instantiate code.
        private bool ValidateReferences()
        {
            var missing = new List<string>();
            if (background == null) missing.Add(nameof(background));
            if (customerNameText == null) missing.Add(nameof(customerNameText));
            if (timeRemainingText == null) missing.Add(nameof(timeRemainingText));
            if (dishImage == null) missing.Add(nameof(dishImage));
            if (modificationsListParent == null) missing.Add(nameof(modificationsListParent));
            if (modificationRowTemplate == null) missing.Add(nameof(modificationRowTemplate));
            if (sideImage == null) missing.Add(nameof(sideImage));
            if (drinkImage == null) missing.Add(nameof(drinkImage));
            if (canvasGroup == null) missing.Add(nameof(canvasGroup));

            if (missing.Count == 0) return true;

            Debug.LogError($"{nameof(TicketCardView)} on '{name}' is missing Inspector reference(s): {string.Join(", ", missing)}. Check the TicketCard prefab.", this);
            return false;
        }

        private void Update()
        {
            // Suppressed for the duration of PlayDeliveryTransition below —
            // that method already knows the ticket changed (it caused it)
            // and handles the rebuild itself once its exit animation
            // finishes; letting this poll rebuild instantly the moment the
            // reference changes would skip straight past the whole
            // exit/entry animation.
            if (!isValid || transitionInProgress) return;

            var ticket = gameManager.State.TicketSlots[slotIndex];
            if (!ReferenceEquals(ticket, cachedTicket))
            {
                cachedTicket = ticket;
                RebuildContent(ticket);
            }

            if (ticket != null)
            {
                RefreshTimer(ticket);
            }
        }

        // Called by WorldTrayView.TryAcceptDrop the instant it knows a
        // delivery just happened — must run in the very same frame as the
        // model's ticket reassignment (TicketSlotManager.DeliverTicket,
        // synchronous inside TrayManager.TryAddItem), otherwise Update()'s
        // own poll would catch the changed ticket reference on the very
        // next frame and instantly rebuild to the new ticket well before
        // PlayDeliveryTransition (deferred until the tray's lift phase
        // starts, ~DeliveryGrowDuration seconds later) gets a chance to
        // play its exit animation on the still-old content.
        public void SuppressPollUntilDeliveryTransition()
        {
            if (!isValid) return;
            transitionInProgress = true;
        }

        // Called by WorldTrayView.PlayDeliverySuccess (via TicketCardsView.
        // GetCard) exactly when the tray for this slot starts its own lift
        // phase after a successful delivery — slides this (still the OLD
        // ticket's) card up while fading out, then swaps to whatever ticket
        // is now actually in this slot (already reassigned synchronously by
        // the time this runs) and drops the new card in from above.
        public void PlayDeliveryTransition()
        {
            if (!isValid) return;

            transitionInProgress = true;
            rectTransform.DOKill();
            canvasGroup.DOKill();

            // Read fresh, not cached — cardsParent is a HorizontalLayoutGroup
            // (TicketCardsView), so "rest position" isn't a fixed constant:
            // it depends on every card's current size, which can shift
            // (e.g. a differing modification-row count changes this card's
            // own preferred size) — a value captured once back at
            // Initialize could already be stale by the time any particular
            // delivery happens.
            var exitFromPos = rectTransform.anchoredPosition;

            var sequence = DOTween.Sequence();
            sequence.Append(rectTransform.DOAnchorPosY(exitFromPos.y + animConfig.TicketExitLiftDistance, animConfig.TicketExitDuration).SetEase(Ease.InQuad));
            sequence.Join(canvasGroup.DOFade(0f, animConfig.TicketExitDuration));
            sequence.AppendCallback(() =>
            {
                var newTicket = gameManager.State.TicketSlots[slotIndex];
                cachedTicket = newTicket;
                RebuildContent(newTicket);

                // RebuildContent can change this card's own size (a
                // different modification-row count) — force the parent
                // layout group to react to that now, so the position read
                // right after is authoritative for the NEW ticket's content
                // instead of trusting a pre-rebuild value.
                if (rectTransform.parent is RectTransform parentRect)
                {
                    LayoutRebuilder.ForceRebuildLayoutImmediate(parentRect);
                }

                var settledPos = rectTransform.anchoredPosition;
                rectTransform.anchoredPosition = new Vector2(settledPos.x, settledPos.y + animConfig.TicketEntryDropDistance);
                canvasGroup.alpha = 0f;

                rectTransform.DOAnchorPos(settledPos, animConfig.TicketEntryDuration).SetEase(Ease.OutBack)
                    .OnComplete(() => transitionInProgress = false);
                canvasGroup.DOFade(1f, animConfig.TicketEntryDuration);
            });
        }

        private void RebuildContent(Ticket ticket)
        {
            background.sprite = owner.TicketSpriteFor(ticket?.PatienceType ?? PatienceType.Normal);

            ClearModificationRows();

            if (ticket == null)
            {
                customerNameText.text = string.Empty;
                timeRemainingText.text = string.Empty;
                dishImage.enabled = false;
                sideImage.gameObject.SetActive(false);
                drinkImage.gameObject.SetActive(false);
                return;
            }

            customerNameText.text = ticket.CustomerName;

            var main = ticket.RequiredItems.FirstOrDefault(item => item.Category == FoodCategory.Main);
            SetDishImage(main);

            // Cleared above and rebuilt in the ticket's own order every time —
            // this list is dynamic (0..N rows), not a fixed pool of slots.
            foreach (var modification in ticket.Modifications)
            {
                var row = Instantiate(modificationRowTemplate, modificationsListParent);
                row.gameObject.SetActive(true);
                row.SetModification(modification.Config.Icon, owner.DirectionSpriteFor(modification.IsAddition), owner.ModificationBoxColorFor(ticket.PatienceType));
                modificationRows.Add(row);
            }

            var side = ticket.RequiredItems.FirstOrDefault(item => item.Category == FoodCategory.Side);
            SetOptionalImage(sideImage, side);

            var drink = ticket.RequiredItems.FirstOrDefault(item => item.Category == FoodCategory.Drink);
            SetOptionalImage(drinkImage, drink);

            RefreshTimer(ticket);
        }

        private void RefreshTimer(Ticket ticket)
        {
            timeRemainingText.text = TicketCardFormatting.FormatRemainingTime(ticket.RemainingSeconds);
        }

        private void SetDishImage(FoodItemConfig main)
        {
            // Always the BASE sprite, never the board's SpriteLayers — GDD Section
            // 3.2: the card's main image is the unmodified dish photo, modifications
            // are shown separately as the list below.
            if (main != null && main.Sprite != null)
            {
                dishImage.enabled = true;
                dishImage.sprite = main.Sprite;
            }
            else
            {
                dishImage.enabled = false;
            }
        }

        private static void SetOptionalImage(Image image, FoodItemConfig config)
        {
            if (config != null && config.Sprite != null)
            {
                image.gameObject.SetActive(true);
                image.sprite = config.Sprite;
            }
            else
            {
                image.gameObject.SetActive(false);
            }
        }

        private void ClearModificationRows()
        {
            foreach (var row in modificationRows)
            {
                Destroy(row.gameObject);
            }
            modificationRows.Clear();
        }
    }
}
