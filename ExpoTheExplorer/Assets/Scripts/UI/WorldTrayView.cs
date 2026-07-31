using System.Collections.Generic;
using ExpoTheExplorer.Bootstrap;
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

        private bool isValid;
        private int lastKnownCount = -1;

        private void Awake()
        {
            isValid = ValidateReferences();
            if (highlightVisual != null) highlightVisual.SetActive(false);
        }

        // GameManager.Awake() builds TrayManager, but Unity doesn't guarantee
        // Awake() order between separate root objects — reading gameManager.TrayManager
        // here instead of in Awake() ensures GameManager has already run (Unity
        // always finishes every object's Awake() before any Start() runs).
        private void Start()
        {
            if (isValid) lastKnownCount = gameManager.TrayManager.GetContents(slotIndex).Count;
        }

        private bool ValidateReferences()
        {
            var missing = new List<string>();
            if (gameManager == null) missing.Add(nameof(gameManager));
            if (mainDishSlot == null) missing.Add(nameof(mainDishSlot));
            if (sideSlot == null) missing.Add(nameof(sideSlot));
            if (drinkSlot == null) missing.Add(nameof(drinkSlot));

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
            var accepted = gameManager.TrayManager.TryAddItem(slotIndex, item);
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
                ClearAllSlotVisuals();
                dragHandler.ReleaseAndDestroy();
            }
            else
            {
                dragHandler.PlaceInSlot(SlotFor(item.Config.Category), slotIndex);
            }

            lastKnownCount = newCount;
            return true;
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

        private static void ClearSlot(Transform slot)
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

                Destroy(child.gameObject);
            }
        }
    }
}
