using ExpoTheExplorer.Core;
using UnityEngine;
using UnityEngine.EventSystems;

namespace ExpoTheExplorer.UI
{
    // Lets the player pick a composited item up off the board and drag it
    // toward a WorldTrayView drop target (GDD Section 5). Added in code by
    // BoardView to each item container, alongside a BoxCollider2D — relies on
    // a Physics2DRaycaster on the scene camera so EventSystem routes
    // pointer/drag events to this world-space object exactly like it already
    // does for Canvas UI (GraphicRaycaster).
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
        private Collider2D ownCollider;
        private int cellX;
        private int cellY;
        private Vector3 homePosition;
        private SpriteRenderer[] layerRenderers;
        private int[] homeSortingOrders;

        public BoardItem CurrentItem { get; private set; }

        // Set by whichever WorldTrayView.OnDrop accepts this item — read back
        // in OnEndDrag (which UGUI calls right after OnDrop) to decide whether
        // to finalize the pickup or snap back to the board.
        public bool WasAcceptedByTray { get; set; }

        public void Configure(BoardGrid board, Camera dragCamera, BoardView boardView)
        {
            this.board = board;
            this.dragCamera = dragCamera;
            this.boardView = boardView;
        }

        public void SetCell(int x, int y, BoardItem item)
        {
            cellX = x;
            cellY = y;
            CurrentItem = item;
        }

        public void OnBeginDrag(PointerEventData eventData)
        {
            if (CurrentItem == null) return;

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

            if (ownCollider != null) ownCollider.enabled = true;

            if (layerRenderers != null)
            {
                for (var i = 0; i < layerRenderers.Length; i++)
                {
                    layerRenderers[i].sortingOrder = homeSortingOrders[i];
                }
            }

            if (WasAcceptedByTray)
            {
                board.RemoveItem(cellX, cellY);
            }
            else
            {
                transform.position = homePosition;
            }
        }

        // Called by WorldTrayView.OnDrop once TrayManager has accepted this
        // item and the tray still needs it displayed (batch not yet
        // resolved). Both the tray slot and this object now live in the same
        // world-space coordinate system, so no screen/world conversion is
        // needed — just reparent and zero the local position. Releases this
        // container from BoardView's per-cell pool first — otherwise the next
        // item spawned into this exact board cell would find its "empty"
        // pooled container already reparented into the tray and rip it back out.
        public void PlaceInSlot(Transform slotTransform)
        {
            if (slotTransform == null) return;

            boardView.ReleaseContainer(cellX, cellY);
            transform.SetParent(slotTransform, false);
            transform.localPosition = Vector3.zero;
        }

        // Called by WorldTrayView.OnDrop when this exact drop just resolved
        // the tray's batch check (delivered, or scattered along with the rest
        // of the tray) — this item was never placed into a slot, so it still
        // owns its board cell's pooled container and must release that before
        // being destroyed, same as PlaceInSlot does for items that DO end up
        // sitting in a slot.
        public void ReleaseAndDestroy()
        {
            boardView.ReleaseContainer(cellX, cellY);
            Destroy(gameObject);
        }
    }
}
