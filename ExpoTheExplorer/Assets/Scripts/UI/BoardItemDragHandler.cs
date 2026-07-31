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
        private const int DragSortingBoost = 1000;

        private BoardGrid board;
        private Camera dragCamera;
        private BoardView boardView;
        private GameManager gameManager;
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

        public void Configure(BoardGrid board, Camera dragCamera, BoardView boardView, GameManager gameManager)
        {
            this.board = board;
            this.dragCamera = dragCamera;
            this.boardView = boardView;
            this.gameManager = gameManager;
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

            // Round-tripping through WorldToScreenPoint gives the correct
            // orthographic screen-space depth to feed back into
            // ScreenToWorldPoint, so the item stays at its original Z.
            var screenDepth = dragCamera.WorldToScreenPoint(homePosition).z;
            var screenPoint = new Vector3(eventData.position.x, eventData.position.y, screenDepth);
            transform.position = dragCamera.ScreenToWorldPoint(screenPoint);
        }

        public void OnEndDrag(PointerEventData eventData)
        {
            if (CurrentItem == null) return;

            IsDragging = false;

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
