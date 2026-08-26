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

        // The spotlight THIS tray raised, while it is the tutorial's target. Held so it can
        // be dismissed synchronously at the step boundary rather than left to Unity's
        // end-of-frame Destroy -- see DismissTutorialSpotlight.
        private TutorialSpotlightView tutorialSpotlight;

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

            // Subscribe, then sync -- the same shape BoardView uses for CellChanged, and
            // for the same reason: the FIRST arm happens in GameManager.Awake, before any
            // Start could have subscribed, while a RETRY re-arms mid-scene long after every
            // Start has run. One of the two would be missed by either half alone.
            gameManager.TutorialStepChanged += OnTutorialStepChanged;
            OnTutorialStepChanged();

            gameManager.State.TicketDelivered.Subscribe(OnTicketDelivered);
            gameManager.State.TraySlotScatterBegin.Subscribe(OnTraySlotScatterBegin);
            gameManager.State.TraySlotScatterEnd.Subscribe(OnTraySlotScatterEnd);
        }

        private void OnDestroy()
        {
            if (!isValid) return;
            gameManager.TutorialStepChanged -= OnTutorialStepChanged;
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

        // Only the ONE tray the Day named raises the spotlight, and it does so because it is
        // the only object already holding both of the ghost's endpoints -- a serialized
        // boardView for the source cell, and its own transform for the destination. That is
        // what lets the whole effect exist with no new scene object and nothing to wire.
        // Runs on every step boundary, in every tray -- so the tray that just stopped being
        // the target tears its spotlight down and the tray that just became one raises the
        // next. A tray that is neither does nothing, which is why this needs no coordination
        // between the three of them.
        private void OnTutorialStepChanged()
        {
            DismissTutorialSpotlight();

            if (!IsTutorialTarget()) return;

            // Deferred by one tween tick: BoardView builds its item containers in its OWN
            // Start, and Unity does not order Starts between root objects, so on a scene's
            // first day the source item may genuinely not exist at this instant. Later steps
            // and retries run long after that, where the delay is simply harmless.
            DOVirtual.DelayedCall(0f, RaiseTutorialSpotlight).SetLink(gameObject);
        }

        // Torn down through Dismiss rather than plain Destroy so the sorting orders it lifted
        // are put back SYNCHRONOUSLY. Unity defers Destroy to the end of the frame while the
        // next step's spotlight is built immediately, so anything else would restore the old
        // step's renderers after the new step had already lifted its own.
        private void DismissTutorialSpotlight()
        {
            if (tutorialSpotlight == null) return;

            tutorialSpotlight.Dismiss();
            tutorialSpotlight = null;
        }

        // Asked of the director rather than worked out from Current's fields (D-098). Reading
        // TargetTraySlotIndex here was wrong in a way that looked right: a step that names NO
        // tray -- the powerup panel -- leaves that field unset, and unset is 0, so tray 0
        // claimed the panel as its own forced move and went looking for an item at the equally
        // unset cell (0,0). SpotlightTraySlotIndex answers -1 for every such step.
        private bool IsTutorialTarget()
        {
            var tutorial = gameManager.Tutorial;
            return tutorial != null && tutorial.SpotlightTraySlotIndex == slotIndex;
        }

        private void RaiseTutorialSpotlight()
        {
            // Re-checked rather than trusted from OnTutorialStepChanged: a frame passed, and
            // a step boundary, a retry or a scene teardown can have landed in between.
            if (this == null || !IsTutorialTarget()) return;

            var tutorial = gameManager.Tutorial;
            var step = tutorial.Current;
            if (!boardView.TryGetDragHandler(step.SourceX, step.SourceY, out var sourceHandler))
            {
                // GameManager already refused to start a step whose cell is empty, so reaching
                // here means the MODEL has an item the VIEW never built a container for. That
                // is a BoardView problem rather than a content one, hence a different sentence
                // than the step guard's.
                Debug.LogError(
                    $"{nameof(WorldTrayView)}: the tutorial step's source cell ({step.SourceX}, {step.SourceY}) " +
                    "has an item in the board model but no rendered container, so no ghost can be shown. The step " +
                    "is still enforced -- it just has nothing to point at.", this);
                return;
            }

            var dimmedCards = new List<RectTransform>();
            for (var i = 0; i < GameState.TicketSlotCount; i++)
            {
                // Every card BUT this tray's own: the target's ticket is what tells the
                // player which order the forced move is filling, so it stays lit. It needs
                // nothing done to it -- Canvas UI already composites above the world-space
                // dim, so staying bright is the default and only the others are covered.
                if (i == slotIndex) continue;

                var card = ticketCardsView.GetCard(i);
                if (card != null && card.transform is RectTransform cardRect) dimmedCards.Add(cardRect);
            }

            tutorialSpotlight = TutorialSpotlightView.Create(
                tutorial,
                animConfig,
                sourceHandler.transform,
                sourceHandler.CurrentItem,
                transform,
                new[] { transform },
                dimmedCards,
                ticketCardView);
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

        // Where a second item of the same category should overflow to when
        // its own slot is already occupied (e.g. a wrong-order drop landing
        // a second Drink before the tray's batch check resolves) — picked
        // by hand per category rather than a generic rotation, so it's not
        // symmetric (every category prefers Side first, then whichever of
        // Main/Drink isn't itself).
        private static readonly Dictionary<FoodCategory, FoodCategory[]> OverflowOrder = new()
        {
            { FoodCategory.Side, new[] { FoodCategory.Drink, FoodCategory.Main } },
            { FoodCategory.Drink, new[] { FoodCategory.Side, FoodCategory.Main } },
            { FoodCategory.Main, new[] { FoodCategory.Side, FoodCategory.Drink } },
        };

        // Prefers the item's own category slot; if it's already occupied,
        // falls back through OverflowOrder to the next empty slot so two
        // items dropped into the tray before it's full never render stacked
        // on top of each other. At most 3 items ever sit in a tray before
        // TrayManager's batch check clears it (RequiredItems.Count maxes at
        // 3, matching the 3 physical slots), so an empty slot is always
        // found before this needs to fall back to the (occupied) preferred
        // slot as a last resort.
        private Transform ResolvePlacementSlot(FoodCategory category)
        {
            var preferred = SlotFor(category);
            if (preferred != null && preferred.childCount == 0) return preferred;

            foreach (var fallback in OverflowOrder[category])
            {
                var slot = SlotFor(fallback);
                if (slot != null && slot.childCount == 0) return slot;
            }

            return preferred;
        }

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

            // The tutorial's second gate. Refusing HERE rather than inside TrayManager is
            // deliberate: a false return is already this method's "not accepted" answer, so
            // the item snaps back exactly as it does for any other rejected drop, and
            // TraySystem never learns that tutorials exist.
            if (!gameManager.IsTrayDropAllowed(slotIndex)) return false;

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

            // The forced move has landed, so this step is over and the next one (if any)
            // takes over. Announced on ACCEPTANCE rather than on delivery: what a step asks
            // for is "put that item in that tray", and tying it to the payout would leave the
            // scene dark through the whole delivery animation -- or forever, for a step whose
            // order needs more than one item.
            //
            // No spotlight teardown is needed here: this publishes StepChanged, which every
            // tray answers by dismissing its own spotlight synchronously, and only THEN (a
            // tween tick later) does the new target raise the next one. One mechanism for the
            // boundary rather than a second copy of it on this path.
            gameManager.Tutorial?.NotifyTrayAccepted(slotIndex);

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
                dragHandler.PlaceInSlot(ResolvePlacementSlot(item.Config.Category), slotIndex);
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

            ticketCardView.SetTrayAnimating(true);
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

                transform.DOScale(restScale, animConfig.DeliveryReentryDuration).SetEase(Ease.OutBack)
                    .OnComplete(() => ticketCardView.SetTrayAnimating(false));
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
            ticketCardView.SetTrayAnimating(true);
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

            // Only the items already SEATED in the slots can be picked back
            // up while this shake runs, so only they get a drag handler to
            // check against below (see IsHeldByPlayer). finalItem is left
            // with a null one deliberately: its OnEndDrag hasn't run yet when
            // this is reached through OnDrop, so IsDragging can still read
            // true, but it is being released rather than held and it has to
            // shake along with the rest.
            var itemHandlers = new BoardItemDragHandler[itemTransforms.Count + 1];
            for (var i = 0; i < itemTransforms.Count; i++)
            {
                itemHandlers[i] = itemTransforms[i].GetComponent<BoardItemDragHandler>();
            }

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
                        if (!IsShakeable(itemTransforms[i], itemHandlers[i])) continue;
                        SetWorldX(itemTransforms[i], itemBaseX[i] + itemOffset);
                    }
                })
                .OnComplete(() =>
                {
                    SetWorldX(transform, trayBaseX);
                    for (var i = 0; i < itemTransforms.Count; i++)
                    {
                        if (!IsShakeable(itemTransforms[i], itemHandlers[i])) continue;
                        SetWorldX(itemTransforms[i], itemBaseX[i]);
                    }

                    if (wrongVisual != null) wrongVisual.SetActive(false);
                    ClearAllSlotVisuals();
                    finalItem.ReleaseAndDestroy();

                    // ClearAllSlotVisuals' own scale-to-zero tweens still run
                    // for animConfig.SlotClearDuration after this point — every
                    // slot uses the same fixed duration, so a single delayed
                    // call here covers all of them without needing to track
                    // each one's own OnComplete.
                    DOVirtual.DelayedCall(animConfig.SlotClearDuration, () => ticketCardView.SetTrayAnimating(false));
                });
        }

        // A tray item the player has picked back up mid-shake belongs to the
        // drag, not to this shake — BoardItemDragHandler is the single writer
        // of a held item's transform, and this shake fighting it over the X
        // axis is exactly what stranded one: OnDrag set X from the finger,
        // the next OnUpdate here overwrote it back toward the tray, and the
        // final restore snapped it there for good, while Y kept following
        // normally. Since the item also hovers a cell above the finger by
        // design, the player saw it stuck up and to the LEFT of the cursor.
        // Rechecked every frame rather than filtered once up front, because
        // the pickup happens DURING the shake — the same reason ClearSlot
        // checks IsDragging on its own destroy pass. The null check covers an
        // item that was dragged out and destroyed before the shake ended.
        private static bool IsShakeable(Transform itemTransform, BoardItemDragHandler handler)
        {
            if (itemTransform == null) return false;
            return handler == null || !handler.IsDragging;
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

            if (currentCount == 0)
            {
                ticketCardView.SetTrayAnimating(true);
                ClearAllSlotVisuals();
                DOVirtual.DelayedCall(animConfig.SlotClearDuration, () => ticketCardView.SetTrayAnimating(false));
            }

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
