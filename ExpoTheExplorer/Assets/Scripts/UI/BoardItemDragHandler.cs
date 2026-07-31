using DG.Tweening;
using ExpoTheExplorer.Bootstrap;
using ExpoTheExplorer.Core;
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
    public class BoardItemDragHandler : MonoBehaviour, IBeginDragHandler, IDragHandler, IEndDragHandler
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

        public void Configure(BoardGrid board, Camera dragCamera, BoardView boardView, GameManager gameManager, DragFeelSettings dragFeel)
        {
            this.board = board;
            this.dragCamera = dragCamera;
            this.boardView = boardView;
            this.gameManager = gameManager;
            this.dragFeel = dragFeel;
        }

        public void SetCell(int x, int y, BoardItem item)
        {
            cellX = x;
            cellY = y;
            CurrentItem = item;
            currentTraySlotIndex = null;
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
            homePosition = transform.position;
            homeScale = transform.localScale;

            transform.DOKill();
            transform.DOScale(homeScale * dragFeel.pickupScaleMultiplier, dragFeel.pickupScaleDuration).SetEase(Ease.OutBack);

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

            // Disabled for the duration of the drag — this item's own
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

        public void OnEndDrag(PointerEventData eventData)
        {
            if (CurrentItem == null) return;

            IsDragging = false;

            transform.DOKill();
            transform.localScale = homeScale;

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

            if (ownCollider != null) ownCollider.enabled = true;

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
                // object as needed — only the board side (if any) still owns
                // a pooled cell that needs releasing.
                if (wasOnBoard)
                {
                    boardView.ReleaseContainer(cellX, cellY);
                    board.RemoveItem(cellX, cellY);
                }
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

                board.TryPlaceItem(CurrentItem, newX, newY);
                Destroy(gameObject);
            }
            else if (!wasOnBoard)
            {
                board.RequestSpawn(CurrentItem);
                Destroy(gameObject);
            }
            else
            {
                transform.position = homePosition;
            }
        }

        // Called by WorldTrayView.OnDrop once TrayManager has accepted this
        // item and the tray still needs it displayed (batch not yet
        // resolved). Releases this container from BoardView's per-cell pool
        // first if it came from the board — otherwise the next item spawned
        // into that exact cell would find its "empty" pooled container
        // already reparented into the tray and rip it back out.
        public void PlaceInSlot(Transform slotTransform, int slotIndex)
        {
            if (slotTransform == null) return;

            if (!pickupSourceTraySlotIndex.HasValue)
            {
                boardView.ReleaseContainer(cellX, cellY);
            }

            currentTraySlotIndex = slotIndex;
            transform.SetParent(slotTransform, false);
            transform.localPosition = Vector3.zero;
        }

        // Called by WorldTrayView.OnDrop when this exact drop just resolved
        // the tray's batch check (delivered, or scattered along with the rest
        // of the tray) — if it came from the board it still owns that cell's
        // pooled container and must release it before being destroyed, same
        // as PlaceInSlot does for items that DO end up sitting in a slot.
        public void ReleaseAndDestroy()
        {
            if (!pickupSourceTraySlotIndex.HasValue)
            {
                boardView.ReleaseContainer(cellX, cellY);
            }

            Destroy(gameObject);
        }
    }
}
