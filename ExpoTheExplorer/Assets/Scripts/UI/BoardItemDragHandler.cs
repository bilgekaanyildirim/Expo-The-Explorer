using DG.Tweening;
using ExpoTheExplorer.Bootstrap;
using ExpoTheExplorer.Core;
using ExpoTheExplorer.Data;
using UnityEngine;
using UnityEngine.EventSystems;

namespace ExpoTheExplorer.UI
{
    // Lets the player pick a composited item up off the board (or back out of
    // a tray) and drag it toward a WorldTrayView drop target or another empty
    // board cell (GDD Section 5). Added in code by BoardView to each item
    // container, alongside a BoxCollider2D — relies on a Physics2DRaycaster on
    // the scene camera so EventSystem routes pointer/drag events to this
    // world-space object exactly like it already does for Canvas UI
    // (GraphicRaycaster).
    //
    // Never touches BoardGrid during the drag itself (only on a successful
    // drop) — this avoids fighting BoardView's own reactive CellChanged/
    // RefreshCell cycle, which would otherwise immediately hide this exact
    // container the moment the model changed underneath it.
    public class BoardItemDragHandler : MonoBehaviour, IPointerDownHandler, IPointerUpHandler, IBeginDragHandler, IDragHandler, IEndDragHandler
    {
        // Resolved (world-unit) drag-feel tuning, computed once by BoardView
        // from its own cellSize-relative Inspector fields and handed down
        // through Configure. Intentionally a plain serializable struct rather
        // than a ScriptableObject — this is view-local presentation tuning
        // (how a drag *feels*), not shared gameplay-balance data, matching
        // BoardView's own screenFillFraction/cellPadding/cellColor fields.
        [System.Serializable]
        public struct DragFeelSettings
        {
            public float offsetDistance;
            public float followMultiplierUp;
            public float followMultiplierDown;
            public float followMultiplierHorizontal;
            public float pickupScaleMultiplier;
            public float pickupScaleDuration;
        }

        private const int DragSortingBoost = 1000;

        private BoardGrid board;
        private Camera dragCamera;
        private BoardView boardView;
        private GameManager gameManager;
        private DragFeelSettings dragFeel;
        private BoardAnimationConfig animConfig;
        private Collider2D ownCollider;
        private int cellX;
        private int cellY;

        // Non-null while this item is currently sitting in a tray slot rather
        // than a board cell — set by PlaceInSlot, cleared by SetCell (a board
        // spawn). Snapshotted into pickupSourceTraySlotIndex at the start of
        // each drag so the rest of that drag's lifecycle knows whether
        // cellX/cellY are meaningful board coordinates or stale leftovers.
        private int? currentTraySlotIndex;
        private int? pickupSourceTraySlotIndex;

        private Vector3 homePosition;
        private Vector3 homeScale;
        private float lastFingerX;
        private float lastFingerY;
        private WorldTrayView hoveredTray;
        private Tween scaleTween;
        private Tween positionTween;
        private bool deliverySuccessInProgress;
        private SpriteRenderer[] layerRenderers;
        private int[] homeSortingOrders;

        public BoardItem CurrentItem { get; private set; }

        // True for the whole duration of a drag — WorldTrayView's poll+diff
        // (which clears a tray slot's visuals whenever its item count drops,
        // to catch an external timeout scatter) must skip whatever is
        // currently being dragged, otherwise picking an item back up out of a
        // tray gets it destroyed the very next frame (its removal from
        // TrayManager already dropped that slot's count before the drag even
        // finishes).
        public bool IsDragging { get; private set; }

        // Set by whichever WorldTrayView.OnDrop accepts this item — read back
        // in OnEndDrag (which UGUI calls right after OnDrop) to decide whether
        // to finalize the pickup or snap back to the board.
        public bool WasAcceptedByTray { get; set; }

        public void Configure(BoardGrid board, Camera dragCamera, BoardView boardView, GameManager gameManager, DragFeelSettings dragFeel, BoardAnimationConfig animConfig)
        {
            this.board = board;
            this.dragCamera = dragCamera;
            this.boardView = boardView;
            this.gameManager = gameManager;
            this.dragFeel = dragFeel;
            this.animConfig = animConfig;
        }

        public void SetCell(int x, int y, BoardItem item)
        {
            cellX = x;
            cellY = y;
            CurrentItem = item;
            currentTraySlotIndex = null;
        }

        // Fires the instant the finger/cursor presses down on the item —
        // this is where all the pickup feedback (scale pop, snap-to-finger,
        // collider disable, sorting boost, tray-hover check) now lives, so
        // the player sees a reaction on touch rather than only once UGUI's
        // drag threshold is crossed. OnBeginDrag below always fires after
        // this for the same gesture (UGUI captures pointerPress before ever
        // considering a drag candidate), so there's nothing to guard here.
        // The IsAwaitingContinue check is the single gate for the whole
        // gesture lifecycle: a gesture never picked up here never sets
        // IsDragging, so OnBeginDrag/OnDrag/OnEndDrag no-op via their own
        // existing CurrentItem checks -- this is the one place a Game
        // Over/Continue popup needs to block board input from.
        public void OnPointerDown(PointerEventData eventData)
        {
            if (CurrentItem == null || gameManager.State.IsAwaitingContinue) return;

            ApplyPickupVisuals(eventData);
        }

        // A blanket kill here is safe (and intended) — grabbing the item is
        // a deliberate takeover of anything currently animating it (a
        // leftover pop-in, snap-back, or tray-settle tween), unlike
        // OnEndDrag below where a targeted kill is needed instead.
        //
        // COMPLETING that kill, and doing it BEFORE the home snapshot, is
        // what makes the snapshot trustworthy. Every tween that can be live
        // on a board item at pickup time (fly-in, snap-back, tray-settle)
        // ends at the item's real resting place at full scale, so completing
        // them means homePosition/homeScale describe where this item belongs
        // rather than wherever it happened to be mid-flight. The case that
        // forced this: a wrong-order scatter starts its items' fly-in with a
        // delay (BoardView.RefreshCell, held for the tray's pre-scatter
        // shake), so for that whole delay the container sits parked on the
        // tray at localScale ZERO. Snapshotting first and killing after
        // captured that zero as homeScale and cancelled the tween that would
        // have grown it — the item then dragged invisibly and OnEndDrag's
        // own `localScale = homeScale` re-applied the zero, so it stayed
        // invisible after landing in a tray slot even though TrayManager had
        // correctly counted it.
        private void ApplyPickupVisuals(PointerEventData eventData)
        {
            transform.DOKill(true);

            homePosition = transform.position;
            homeScale = transform.localScale;

            scaleTween = transform.DOScale(homeScale * dragFeel.pickupScaleMultiplier, dragFeel.pickupScaleDuration).SetEase(Ease.OutBack);

            // Snap straight to the resting hover position (finger + offset)
            // the moment it's picked up, and remember the finger's starting
            // height so OnDrag's very first call has a real previous-frame
            // value to diff against instead of a spurious huge jump.
            if (dragCamera != null)
            {
                var fingerWorldPos = ComputeFingerWorldPos(eventData.position);
                lastFingerX = fingerWorldPos.x;
                lastFingerY = fingerWorldPos.y;
                transform.position = new Vector3(fingerWorldPos.x, fingerWorldPos.y + dragFeel.offsetDistance, fingerWorldPos.z);
                UpdateHoveredTray();
            }

            // Disabled from the moment it's picked up — this item's own
            // collider follows the pointer exactly, so left enabled it would
            // sit directly on top of whatever drop target is underneath and
            // "steal" the raycast hit (Physics2DRaycaster only checks the
            // nearest hit's hierarchy for IDropHandler, not every overlapping
            // collider), making every drop silently fail.
            if (ownCollider == null) ownCollider = GetComponent<Collider2D>();
            if (ownCollider != null) ownCollider.enabled = false;

            layerRenderers = GetComponentsInChildren<SpriteRenderer>();
            homeSortingOrders = new int[layerRenderers.Length];
            for (var i = 0; i < layerRenderers.Length; i++)
            {
                homeSortingOrders[i] = layerRenderers[i].sortingOrder;
                layerRenderers[i].sortingOrder += DragSortingBoost;
            }
        }

        public void OnBeginDrag(PointerEventData eventData)
        {
            if (CurrentItem == null) return;

            IsDragging = true;

            // Picking this item back up out of a tray removes it from that
            // slot's tracked contents immediately — otherwise TrayManager
            // would still count it there even while it's being dragged away.
            pickupSourceTraySlotIndex = currentTraySlotIndex;
            if (pickupSourceTraySlotIndex.HasValue)
            {
                gameManager.TrayManager.RemoveItem(pickupSourceTraySlotIndex.Value, CurrentItem);
                currentTraySlotIndex = null;
            }

            WasAcceptedByTray = false;
        }

        public void OnDrag(PointerEventData eventData)
        {
            if (CurrentItem == null || dragCamera == null) return;

            var fingerWorldPos = ComputeFingerWorldPos(eventData.position);

            // The item's own movement is the finger's frame-to-frame delta
            // scaled by an up/down multiplier — pushing up moves it faster
            // than the finger (the gap stretches beyond offsetDistance), and
            // pulling down brings it back down faster too, but never past
            // the offset floor: once the floor clamps a frame, the item is
            // effectively pinned there (1:1 with the finger) until the
            // finger reverses upward and the gap can grow again.
            var fingerDeltaY = fingerWorldPos.y - lastFingerY;
            var verticalMultiplier = fingerDeltaY >= 0f ? dragFeel.followMultiplierUp : dragFeel.followMultiplierDown;
            var newY = transform.position.y + fingerDeltaY * verticalMultiplier;
            newY = Mathf.Max(newY, fingerWorldPos.y + dragFeel.offsetDistance);

            // Horizontal movement uses the same finger-delta-times-multiplier
            // feel, symmetric left/right — there's no "offset floor" concept
            // on this axis (that's specifically about staying above the
            // finger vertically), so no clamp is applied here.
            var fingerDeltaX = fingerWorldPos.x - lastFingerX;
            var newX = transform.position.x + fingerDeltaX * dragFeel.followMultiplierHorizontal;

            transform.position = new Vector3(newX, newY, fingerWorldPos.z);
            lastFingerX = fingerWorldPos.x;
            lastFingerY = fingerWorldPos.y;
            UpdateHoveredTray();
        }

        // Round-tripping through WorldToScreenPoint gives the correct
        // orthographic screen-space depth to feed back into
        // ScreenToWorldPoint, so the item stays at its original Z.
        private Vector3 ComputeFingerWorldPos(Vector2 screenPosition)
        {
            var screenDepth = dragCamera.WorldToScreenPoint(homePosition).z;
            var screenPoint = new Vector3(screenPosition.x, screenPosition.y, screenDepth);
            return dragCamera.ScreenToWorldPoint(screenPoint);
        }

        private static readonly Collider2D[] TrayOverlapBuffer = new Collider2D[8];

        // Drives the tray highlight (GDD Section 5 drop-zone feedback) and
        // the OnEndDrag drop fallback below off the item's own displayed
        // position rather than the pointer — with the drag-feel hover
        // offset, the item can be sitting right on top of a tray while the
        // finger itself is still outside its hitbox, and the highlight
        // should reflect what the player actually sees the item on top of.
        private void UpdateHoveredTray()
        {
            // OverlapPoint alone would return whichever single collider
            // happens to be first — often an item already resting in a
            // tray slot rather than the tray's own hitbox underneath it —
            // so every overlapping collider at this point is checked and
            // the tray always wins if it's among them, regardless of what
            // else is sitting there.
            WorldTrayView tray = null;
            var hitCount = Physics2D.OverlapPoint(transform.position, new ContactFilter2D().NoFilter(), TrayOverlapBuffer);
            for (var i = 0; i < hitCount; i++)
            {
                if (TrayOverlapBuffer[i].TryGetComponent<WorldTrayView>(out var trayView))
                {
                    tray = trayView;
                    break;
                }
            }

            if (tray == hoveredTray) return;

            if (hoveredTray != null) hoveredTray.SetHighlighted(false);
            hoveredTray = tray;
            if (hoveredTray != null) hoveredTray.SetHighlighted(true);
        }

        // Fires when the finger/cursor lifts. If OnBeginDrag never fired for
        // this gesture (a tap that never crossed the drag threshold),
        // IsDragging is still false here — undo the pickup visuals
        // OnPointerDown applied and let the item settle back down, same feel
        // as an invalid drop. If a real drag did happen, OnEndDrag (which
        // UGUI calls for the same release) already owns the full teardown,
        // so there's nothing left to do here.
        public void OnPointerUp(PointerEventData eventData)
        {
            if (CurrentItem == null || IsDragging) return;

            scaleTween?.Kill();
            transform.localScale = homeScale;

            if (ownCollider != null) ownCollider.enabled = true;

            if (layerRenderers != null)
            {
                for (var i = 0; i < layerRenderers.Length; i++)
                {
                    layerRenderers[i].sortingOrder = homeSortingOrders[i];
                }
            }

            if (hoveredTray != null)
            {
                hoveredTray.SetHighlighted(false);
                hoveredTray = null;
            }

            positionTween = transform.DOMove(homePosition, animConfig.SnapBackDuration).SetEase(Ease.OutQuad);
        }

        public void OnEndDrag(PointerEventData eventData)
        {
            if (CurrentItem == null) return;

            IsDragging = false;

            // Targeted kill, not a blanket transform.DOKill() — in the
            // direct-hit tray-drop case, OnDrop (and the PlaceInSlot fly-in
            // tween it starts) already ran just before UGUI calls OnEndDrag,
            // and a blanket kill here would cancel that tween before it
            // even got to play. Skipped entirely when a delivery-success
            // grow/lift/fade is already underway (started by OnDrop too) —
            // this would otherwise instantly snap the scale back to
            // homeScale mid-animation.
            if (!deliverySuccessInProgress)
            {
                scaleTween?.Kill();
                transform.localScale = homeScale;
            }

            // hoveredTray already reflects whichever tray the item's own
            // displayed position was last over (kept in sync every
            // OnBeginDrag/OnDrag call via UpdateHoveredTray) — reused here
            // as a drop fallback for when the drag-feel hover offset put
            // the item over a tray while the pointer itself (what OnDrop's
            // own raycast checks) was still below its hitbox.
            if (!WasAcceptedByTray && hoveredTray != null)
            {
                hoveredTray.TryAcceptDrop(this);
            }

            if (hoveredTray != null)
            {
                hoveredTray.SetHighlighted(false);
                hoveredTray = null;
            }

            // Also skipped during a delivery-success animation — the item
            // is on its way out (lifting/fading), it shouldn't become
            // draggable again for however long that takes.
            if (!deliverySuccessInProgress && ownCollider != null) ownCollider.enabled = true;

            if (layerRenderers != null)
            {
                for (var i = 0; i < layerRenderers.Length; i++)
                {
                    layerRenderers[i].sortingOrder = homeSortingOrders[i];
                }
            }

            var wasOnBoard = !pickupSourceTraySlotIndex.HasValue;

            if (WasAcceptedByTray)
            {
                // WorldTrayView.OnDrop already reparented/destroyed this
                // object as needed. The board side (if any) was already
                // detached too — DetachFromBoard ran synchronously inside
                // TrayManager.TryAddItem, before its own batch check, so a
                // delivery that call triggers sees an accurate board state
                // instead of this item still sitting in its old cell.
                return;
            }

            // Dropped somewhere other than a tray — either move it to another
            // empty board cell, or (if it came from a tray) scatter it back
            // onto the board the same way a wrong delivery/timeout already
            // does; anything else just snaps back to where it was.
            if (boardView.TryGetCellAt(transform.position, out var newX, out var newY)
                && board.IsCellEmpty(newX, newY)
                && (!wasOnBoard || newX != cellX || newY != cellY))
            {
                if (wasOnBoard)
                {
                    boardView.ReleaseContainer(cellX, cellY);
                    board.RemoveItem(cellX, cellY);
                }

                // This is a relocation of an item that already existed
                // (board-to-board, or a tray pickup landing back on the
                // board), not a genuinely new appearance — skip the
                // Starting Point fly-in BoardView would otherwise give it.
                boardView.BeginFlyInOverride(null);
                board.TryPlaceItem(CurrentItem, newX, newY);
                boardView.EndFlyInOverride();
                Destroy(gameObject);
            }
            else if (!wasOnBoard)
            {
                // A tray pickup dropped somewhere invalid — it still needs
                // to land on the board (same as a wrong-delivery scatter),
                // but flying in from Starting Point would look wrong for an
                // item the player was just holding; fly in from wherever it
                // last visually was instead.
                boardView.BeginFlyInOverride(transform.position);
                board.RequestSpawn(CurrentItem);
                boardView.EndFlyInOverride();
                Destroy(gameObject);
            }
            else
            {
                // GDD Section 5: "...snap-back animation on invalid drop."
                positionTween = transform.DOMove(homePosition, animConfig.SnapBackDuration).SetEase(Ease.OutQuad);
            }
        }

        // Called by TrayManager.TryAddItem (via WorldTrayView.TryAcceptDrop)
        // the instant this item is accepted into a tray slot — before that
        // same call's own batch check, which a full tray resolves
        // synchronously (delivered, or scattered) and, on a delivery,
        // cascades into BoardDistributor's required-pool re-check. Detaching
        // here rather than later (this used to happen back in OnEndDrag,
        // after TryAcceptDrop had already returned) means that re-check sees
        // an accurate board — not this item still occupying its old cell —
        // so a food/modification combo another active ticket also needs
        // doesn't wrongly read as "already present" and go unreplenished.
        // A no-op if this item came from another tray slot rather than the
        // board (nothing to detach there).
        public void DetachFromBoard()
        {
            if (pickupSourceTraySlotIndex.HasValue) return;

            boardView.ReleaseContainer(cellX, cellY);
            board.RemoveItem(cellX, cellY);
        }

        // Called by WorldTrayView.OnDrop once TrayManager has accepted this
        // item and the tray still needs it displayed (batch not yet
        // resolved). DetachFromBoard above already released this cell's
        // pooled container (if it came from the board) before TryAddItem's
        // batch check ran, so there's nothing left to release here.
        public void PlaceInSlot(Transform slotTransform, int slotIndex)
        {
            if (slotTransform == null) return;

            currentTraySlotIndex = slotIndex;

            // Reparent without letting the item jump to the slot's local
            // zero instantly — restoring its world position right after
            // SetParent keeps it exactly where it visually was, so the
            // settle tween below has an actual distance to travel instead
            // of the item just appearing already-seated.
            var worldPos = transform.position;
            transform.SetParent(slotTransform, false);
            transform.position = worldPos;
            positionTween = transform.DOLocalMove(Vector3.zero, animConfig.TraySettleDuration).SetEase(Ease.OutBack);
        }

        // Called by WorldTrayView.OnDrop when this exact drop just resolved
        // the tray's batch check (delivered, or scattered along with the rest
        // of the tray) — DetachFromBoard already released this cell's pooled
        // container (if it came from the board) before that batch check ran,
        // so this is just cleanup of the GameObject itself.
        public void ReleaseAndDestroy()
        {
            Destroy(gameObject);
        }

        // Called by WorldTrayView.TryAcceptDrop instead of ReleaseAndDestroy
        // when this exact drop was the one that completed a successful
        // delivery — this item was never placed into a slot, so it's still
        // sitting wherever the drag left it (DetachFromBoard already
        // released its board-side pooled container, before TryAddItem's
        // batch check ran). Grows then lifts-and-fades in sync with the
        // tray's own delivery animation (WorldTrayView.PlayDeliverySuccess)
        // instead of just vanishing. OnEndDrag's own scale reset / collider
        // re-enable check deliverySuccessInProgress and stay hands-off for
        // the rest of this object's short remaining lifetime.
        public void PlayDeliverySuccessAndDestroy(BoardAnimationConfig config)
        {
            deliverySuccessInProgress = true;
            if (ownCollider != null) ownCollider.enabled = false;

            transform.DOKill();
            var sequence = DOTween.Sequence();
            sequence.Append(transform.DOScale(homeScale * config.DeliveryGrowScale, config.DeliveryGrowDuration).SetEase(Ease.OutQuad));
            sequence.Append(transform.DOMoveY(transform.position.y + config.DeliveryLiftDistance, config.DeliveryFadeDuration).SetEase(Ease.InQuad));

            if (layerRenderers != null)
            {
                foreach (var layerRenderer in layerRenderers)
                {
                    if (layerRenderer != null) sequence.Join(layerRenderer.DOFade(0f, config.DeliveryFadeDuration));
                }
            }

            sequence.OnComplete(() => Destroy(gameObject));
        }
    }
}
