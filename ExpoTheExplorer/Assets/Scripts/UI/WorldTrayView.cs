using System.Collections.Generic;
using DG.Tweening;
using ExpoTheExplorer.Bootstrap;
using ExpoTheExplorer.Core;
using ExpoTheExplorer.Data;
using UnityEngine;
using UnityEngine.EventSystems;

namespace ExpoTheExplorer.UI
{
    // World-space tray (GDD Section 5, 5.1 — the tray IS the drop area itself,
    // not a separate preview) — an extension of the board's own rendering
    // approach rather than Canvas UI, so the dragged item's real GameObject can
    // be reparented straight into it (BoardItemDragHandler.PlaceInSlot). A
    // Screen Space - Overlay Canvas always composites on top of everything the
    // camera renders, so a SpriteRenderer parented under a Canvas RectTransform
    // could never appear correctly there — being a pure world-space object here
    // sidesteps that entirely, and also makes future animation (item flying
    // into place) a plain Transform tween instead of a Canvas conversion.
    //
    // A standalone scene object (one per ticket slot), not instantiated per
    // TicketCard — slotIndex is set by hand in the Inspector. Single drop
    // hitbox for the whole tray (BoxCollider2D added by hand in the Editor) —
    // which of mainDishSlot/sideSlot/drinkSlot an accepted item belongs to is
    // resolved from its own FoodCategory, not from where exactly it was dropped.
    public class WorldTrayView : MonoBehaviour, IDropHandler
    {
        [SerializeField] private GameManager gameManager;
        [SerializeField] private int slotIndex;
        [SerializeField] private Transform mainDishSlot;
        [SerializeField] private Transform sideSlot;
        [SerializeField] private Transform drinkSlot;
        [Tooltip("Optional — shown while a valid drag is hovering over this tray (GDD Section 5 drop-zone highlight).")]
        [SerializeField] private GameObject highlightVisual;
        [Tooltip("Optional — shown for the duration of the wrong-order pre-scatter shake.")]
        [SerializeField] private GameObject wrongVisual;
        [Tooltip("Shared tuning for board/tray animation durations (snap-back, tray settle, pop-in, slot clear).")]
        [SerializeField] private BoardAnimationConfig animConfig;
        [Tooltip("Needed so a wrong-order scatter can tell BoardView to fly those items in from this tray instead of Starting Point.")]
        [SerializeField] private BoardView boardView;
        [Tooltip("Needed to look up this slot's TicketCardView so its own exit/entry animation can sync with a successful delivery's lift-off.")]
        [SerializeField] private TicketCardsView ticketCardsView;

        private bool isValid;
        private int lastKnownCount = -1;
        private bool justDelivered;
        private Vector3 restScale;
        private Vector3 restPosition;
        private TicketCardView ticketCardView;
        private bool inDropCall;

        private void Awake()
        {
            isValid = ValidateReferences();
            if (highlightVisual != null) highlightVisual.SetActive(false);
            if (wrongVisual != null) wrongVisual.SetActive(false);
        }

        // GameManager.Awake() builds TrayManager, but Unity doesn't guarantee
        // Awake() order between separate root objects — reading gameManager.TrayManager
        // here instead of in Awake() ensures GameManager has already run (Unity
        // always finishes every object's Awake() before any Start() runs).
        private void Start()
        {
            if (!isValid) return;

            lastKnownCount = gameManager.TrayManager.GetContents(slotIndex).Count;
            restScale = transform.localScale;
            restPosition = transform.position;
            // TicketCardsView.Awake() instantiates its cards at runtime, so
            // this can't be wired by hand in the Editor — resolved here
            // instead, safe because Unity finishes every object's Awake()
            // before any Start() runs.
            ticketCardView = ticketCardsView.GetCard(slotIndex);
            gameManager.State.TicketDelivered.Subscribe(OnTicketDelivered);
            gameManager.State.TraySlotScatterBegin.Subscribe(OnTraySlotScatterBegin);
            gameManager.State.TraySlotScatterEnd.Subscribe(OnTraySlotScatterEnd);
        }

        private void OnDestroy()
        {
            if (!isValid) return;
            gameManager.State.TicketDelivered.Unsubscribe(OnTicketDelivered);
            gameManager.State.TraySlotScatterBegin.Unsubscribe(OnTraySlotScatterBegin);
            gameManager.State.TraySlotScatterEnd.Unsubscribe(OnTraySlotScatterEnd);
        }

        // TrayManager.TryAddItem calls deliverTicket (-> TicketSlotManager.
        // DeliverTicket -> this publish) synchronously before returning, so
        // by the time TryAcceptDrop's call to TryAddItem below returns,
        // justDelivered is already correctly set for this exact drop if it
        // was the one that completed a successful delivery on this slot.
        private void OnTicketDelivered((int SlotIndex, Ticket Ticket) delivery)
        {
            if (delivery.SlotIndex == slotIndex) justDelivered = true;
        }

        // These bracket exactly TrayManager.ScatterBackToBoard's own
        // RequestSpawn calls for THIS slot (a wrong order or a timeout,
        // never a delivery) — narrower than wrapping the whole TryAddItem
        // call, which on a successful delivery also cascades into the
        // *next* ticket's unrelated required-item spawn and would wrongly
        // tag that as flying in from this tray too.
        //
        // inDropCall distinguishes which of those two this actually is: only
        // a wrong order (inDropCall true, set around TryAcceptDrop's own
        // TryAddItem call below) delays the scattered items' board pop-in to
        // match PlayWrongOrderShakeThenScatter's shake — a timeout scatter
        // (inDropCall false, triggered from GameManager.OnTicketAssigned
        // instead) isn't preceded by any shake, so it keeps appearing
        // immediately.
        private void OnTraySlotScatterBegin(int scatteringSlotIndex)
        {
            if (scatteringSlotIndex != slotIndex) return;
            var delay = inDropCall ? animConfig.ScatterShakeDuration : 0f;
            boardView.BeginFlyInOverride(transform.position, delay);
        }

        private void OnTraySlotScatterEnd(int scatteringSlotIndex)
        {
            if (scatteringSlotIndex == slotIndex) boardView.EndFlyInOverride();
        }

        private bool ValidateReferences()
        {
            var missing = new List<string>();
            if (gameManager == null) missing.Add(nameof(gameManager));
            if (mainDishSlot == null) missing.Add(nameof(mainDishSlot));
            if (sideSlot == null) missing.Add(nameof(sideSlot));
            if (drinkSlot == null) missing.Add(nameof(drinkSlot));
            if (boardView == null) missing.Add(nameof(boardView));
            if (ticketCardsView == null) missing.Add(nameof(ticketCardsView));

            if (missing.Count == 0) return true;

            Debug.LogError($"{nameof(WorldTrayView)} on '{name}' is missing Inspector reference(s): {string.Join(", ", missing)}.", this);
            return false;
        }

        private Transform SlotFor(FoodCategory category) => category switch
        {
            FoodCategory.Main => mainDishSlot,
            FoodCategory.Side => sideSlot,
            FoodCategory.Drink => drinkSlot,
            _ => null,
        };

        public void OnDrop(PointerEventData eventData)
        {
            if (!isValid) return;

            var dragHandler = eventData.pointerDrag != null
                ? eventData.pointerDrag.GetComponent<BoardItemDragHandler>()
                : null;
            if (dragHandler == null) return;

            TryAcceptDrop(dragHandler);
        }

        // Also called directly by BoardItemDragHandler.OnEndDrag as a fallback:
        // the drag-feel hover offset means the item can visually be sitting
        // right on top of this tray while the actual pointer (what OnDrop's
        // own raycast above checks) is still below it, outside this
        // collider — that fallback finds this tray from the item's own
        // displayed position instead and accepts the drop the same way.
        public bool TryAcceptDrop(BoardItemDragHandler dragHandler)
        {
            if (!isValid || dragHandler == null || dragHandler.CurrentItem == null) return false;

            var item = dragHandler.CurrentItem;
            justDelivered = false;

            // A wrong order's scatter (if this call causes one) is handled
            // by OnTraySlotScatterBegin/End above, triggered from inside
            // TrayManager.ScatterBackToBoard itself — narrower than
            // bracketing this whole call, which on a successful delivery
            // also cascades into the next ticket's unrelated spawn.
            // inDropCall tells that handler this scatter (if any) came from
            // a manual drop rather than a timeout, so it knows to delay the
            // board-side pop-in to match the shake below.
            inDropCall = true;
            var accepted = gameManager.TrayManager.TryAddItem(slotIndex, item, dragHandler.DetachFromBoard);
            inDropCall = false;

            dragHandler.WasAcceptedByTray = accepted;
            if (!accepted) return false;

            var newCount = gameManager.TrayManager.GetContents(slotIndex).Count;
            if (newCount == 0)
            {
                // This drop just completed the tray and TrayManager already ran
                // the batch check (delivered or scattered) — every item that
                // was sitting in our slots (from earlier drops on this same
                // ticket) is stale now, and this drop's own item was never
                // placed into a slot, so it needs cleanup here instead.
                if (justDelivered)
                {
                    // Must happen in this exact frame, before PlayDeliverySuccess
                    // (whose own PlayDeliveryTransition trigger is deferred to
                    // the tray's lift phase) — otherwise TicketCardView.Update's
                    // poll catches the already-reassigned ticket on the very
                    // next frame and rebuilds instantly, well before the exit
                    // animation would even start.
                    ticketCardView.SuppressPollUntilDeliveryTransition();
                    PlayDeliverySuccess(dragHandler);
                }
                else
                {
                    PlayWrongOrderShakeThenScatter(dragHandler);
                }
            }
            else
            {
                dragHandler.PlaceInSlot(SlotFor(item.Config.Category), slotIndex);
            }

            lastKnownCount = newCount;
            return true;
        }

        // Successful delivery only (a wrong-order scatter still uses the
        // plain ClearAllSlotVisuals shrink-and-destroy above) — grows the
        // whole tray (background + every slot's contents, since they're all
        // descendants of this transform) as if lifting toward the camera,
        // then moves it up while every SpriteRenderer underneath fades out
        // together, before resetting back to normal for the next ticket.
        // The just-delivered item itself was never parented under a slot,
        // so it gets the identical treatment on its own transform in
        // parallel (BoardItemDragHandler.PlayDeliverySuccessAndDestroy).
        private void PlayDeliverySuccess(BoardItemDragHandler finalItem)
        {
            var renderers = GetComponentsInChildren<SpriteRenderer>(true);

            transform.DOKill();
            var sequence = DOTween.Sequence();
            sequence.Append(transform.DOScale(restScale * animConfig.DeliveryGrowScale, animConfig.DeliveryGrowDuration).SetEase(Ease.OutQuad));
            // Fires exactly when the grow finishes and the lift begins —
            // the ticket card's own exit (slide + fade) starts in sync with
            // the tray actually starting to move up, not the grow before it.
            sequence.InsertCallback(animConfig.DeliveryGrowDuration, () => ticketCardView.PlayDeliveryTransition());
            sequence.Append(transform.DOMoveY(transform.position.y + animConfig.DeliveryLiftDistance, animConfig.DeliveryFadeDuration).SetEase(Ease.InQuad));
            foreach (var renderer in renderers)
            {
                if (renderer != null) sequence.Join(renderer.DOFade(0f, animConfig.DeliveryFadeDuration));
            }

            sequence.OnComplete(() =>
            {
                DestroySlotChildrenImmediate(mainDishSlot);
                DestroySlotChildrenImmediate(sideSlot);
                DestroySlotChildrenImmediate(drinkSlot);

                // "New tray" re-entrance: grows and fades in right at rest
                // position (no movement) instead of sliding up from below or
                // just snapping back for the next ticket. Re-fetched here
                // (rather than reusing the captured `renderers`) since those
                // included the now-destroyed slot items — this only picks up
                // the tray's own remaining renderers (background sprite etc.).
                var remainingRenderers = GetComponentsInChildren<SpriteRenderer>(true);

                transform.DOKill();
                transform.position = restPosition;
                transform.localScale = Vector3.zero;
                foreach (var renderer in remainingRenderers)
                {
                    var color = renderer.color;
                    color.a = 0f;
                    renderer.color = color;
                }

                transform.DOScale(restScale, animConfig.DeliveryReentryDuration).SetEase(Ease.OutBack);
                foreach (var renderer in remainingRenderers)
                {
                    renderer.DOFade(1f, animConfig.DeliveryReentryDuration);
                }
            });

            finalItem.PlayDeliverySuccessAndDestroy(animConfig);
        }

        // Wrong order: the tray (and everything sitting in it, since
        // they're all descendants) plus the just-dropped final item shake
        // in place for ScatterShakeDuration before actually vanishing — the
        // model already placed the scattered items on their board cells
        // synchronously inside TryAddItem above, but BoardView.RefreshCell
        // is holding their pop-in invisible for this same duration (the
        // delay passed through OnTraySlotScatterBegin), so nothing shows up
        // on the board until this shake finishes and hands off to it.
        private void PlayWrongOrderShakeThenScatter(BoardItemDragHandler finalItem)
        {
            transform.DOKill();
            finalItem.transform.DOKill();
            if (wrongVisual != null) wrongVisual.SetActive(true);

            // Every currently-visible slot item plus the just-dropped final
            // item — all shake horizontally in sync with the tray, but at
            // ScatterShakeItemMultiplier times its strength (e.g. tray moves
            // 1 unit right, these move 1.2). Driven from one shared
            // oscillation instead of independent DOShakePosition calls per
            // object, which would each roll their own random pattern and
            // drift out of sync with each other.
            var itemTransforms = new List<Transform>();
            CollectSlotChildren(mainDishSlot, itemTransforms);
            CollectSlotChildren(sideSlot, itemTransforms);
            CollectSlotChildren(drinkSlot, itemTransforms);
            itemTransforms.Add(finalItem.transform);
            foreach (var t in itemTransforms) t.DOKill();

            var trayBaseX = transform.position.x;
            var itemBaseX = new float[itemTransforms.Count];
            for (var i = 0; i < itemTransforms.Count; i++) itemBaseX[i] = itemTransforms[i].position.x;

            var progress = 0f;
            DOTween.To(() => progress, p => progress = p, 1f, animConfig.ScatterShakeDuration)
                .SetEase(Ease.Linear)
                .OnUpdate(() =>
                {
                    // Decaying sine wave — horizontal only, settles to zero
                    // by the end instead of cutting off abruptly.
                    var decay = 1f - progress;
                    var wave = Mathf.Sin(progress * animConfig.ScatterShakeDuration * animConfig.ScatterShakeFrequency * Mathf.PI * 2f) * decay;

                    SetWorldX(transform, trayBaseX + wave * animConfig.ScatterShakeStrength);
                    var itemOffset = wave * animConfig.ScatterShakeStrength * animConfig.ScatterShakeItemMultiplier;
                    for (var i = 0; i < itemTransforms.Count; i++)
                    {
                        SetWorldX(itemTransforms[i], itemBaseX[i] + itemOffset);
                    }
                })
                .OnComplete(() =>
                {
                    SetWorldX(transform, trayBaseX);
                    for (var i = 0; i < itemTransforms.Count; i++) SetWorldX(itemTransforms[i], itemBaseX[i]);

                    if (wrongVisual != null) wrongVisual.SetActive(false);
                    ClearAllSlotVisuals();
                    finalItem.ReleaseAndDestroy();
                });
        }

        private static void SetWorldX(Transform t, float x)
        {
            var pos = t.position;
            pos.x = x;
            t.position = pos;
        }

        private static void CollectSlotChildren(Transform slot, List<Transform> into)
        {
            if (slot == null) return;
            for (var i = 0; i < slot.childCount; i++) into.Add(slot.GetChild(i));
        }

        private static void DestroySlotChildrenImmediate(Transform slot)
        {
            if (slot == null) return;
            for (var i = slot.childCount - 1; i >= 0; i--)
            {
                Destroy(slot.GetChild(i).gameObject);
            }
        }

        // Called by BoardItemDragHandler.UpdateHoveredTray, driven off the
        // dragged item's own displayed position rather than pointer
        // enter/exit events — with the drag-feel hover offset, the pointer
        // and the item can be over different things, and the highlight
        // should reflect what the player actually sees the item on top of.
        public void SetHighlighted(bool active)
        {
            if (highlightVisual != null) highlightVisual.SetActive(active);
        }

        private void Update()
        {
            if (!isValid) return;

            // Catches the one case OnDrop doesn't cover: a timeout scattering a
            // partially-filled tray back to the board (TrayManager.OnTicketAssigned),
            // which happens with no direct event this view is subscribed to —
            // same poll+diff approach TicketCardView already uses for its timer.
            // OnDrop keeps lastKnownCount in sync for its own changes, so this
            // only ever fires for that external case.
            //
            // Only a drop to exactly zero means the WHOLE tray was cleared
            // externally (OnTicketAssigned/TryAddItem's batch-resolve paths
            // both always empty the slot completely) — a single manual
            // pickup (BoardItemDragHandler.OnBeginDrag removing just one item)
            // decrements by exactly one and must NOT wipe the other items
            // still legitimately sitting in this tray.
            var currentCount = gameManager.TrayManager.GetContents(slotIndex).Count;
            if (currentCount == lastKnownCount) return;

            if (currentCount == 0) ClearAllSlotVisuals();

            lastKnownCount = currentCount;
        }

        private void ClearAllSlotVisuals()
        {
            ClearSlot(mainDishSlot);
            ClearSlot(sideSlot);
            ClearSlot(drinkSlot);
        }

        private void ClearSlot(Transform slot)
        {
            if (slot == null) return;
            for (var i = slot.childCount - 1; i >= 0; i--)
            {
                var child = slot.GetChild(i);

                // Picking an item back up out of this tray already removed it
                // from TrayManager's count (BoardItemDragHandler.OnBeginDrag),
                // which looks identical to an external clear from here — skip
                // it while it's actively being dragged so it doesn't get
                // destroyed out from under the player mid-drag.
                var dragHandler = child.GetComponent<BoardItemDragHandler>();
                if (dragHandler != null && dragHandler.IsDragging) continue;

                var childTransform = child;
                childTransform.DOKill();
                childTransform.DOScale(Vector3.zero, animConfig.SlotClearDuration)
                    .SetEase(Ease.InBack)
                    .OnComplete(() => Destroy(childTransform.gameObject));
            }
        }
    }
}
