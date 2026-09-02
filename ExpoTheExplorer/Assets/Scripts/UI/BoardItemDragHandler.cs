using System;
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

        // Optional, and null-checked at both call sites: a scene that never wired a
        // HapticsBinder drags exactly as it always did, silently. Handed down through
        // Configure like every other dependency here rather than looked up — a runtime
        // search for a scene reference is ruled out project-wide.
        private HapticsBinder haptics;
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

        // True for the rest of a gesture whose press was refused (a popup is up, or the
        // tutorial's forced move does not allow this cell). It exists because refusing in
        // OnPointerDown alone DOES NOT STOP A DRAG: UGUI still calls OnBeginDrag/OnDrag for
        // the same gesture, and their own `CurrentItem == null` guards pass, so the item
        // was picked up and flown around anyway -- with none of the pickup bookkeeping
        // done. Every handler in the gesture now checks this flag, which is what makes a
        // refusal actually mean "you cannot move this".
        private bool pickupRefused;

        private Vector3 homePosition;

        // Initialised to ONE rather than left at the default zero, because zero is not a
        // truthful statement about anything: every board container rests at scale one by
        // construction (BoardView.RefreshCell tweens both its fly-in and its plain pop-in
        // to exactly that, and nothing else ever writes a container's scale). The default
        // mattered -- see SettleForAutoCollect below for what reading it unset cost.
        private Vector3 homeScale = Vector3.one;

        // The scale and hitbox this container has while it lives on the BOARD, snapshotted
        // by Configure at creation — before any tray has had a chance to shrink it. They are
        // captured rather than written as constants because BoardView owns both facts (scale
        // one by construction, collider exactly one cell) and a second copy here would be a
        // second authority on them. What they answer is the question homeScale cannot: a tray
        // item's homeScale is correctly its TRAY scale (a tap that never became a drag, and a
        // snap-back, both leave it sitting in its slot), so neither the drag's grow target nor
        // the restore on the way back to the board can be read off it.
        private Vector3 boardScale = Vector3.one;
        private Vector2 boardHitSize;

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

        // The refusal above, made readable by the DROP TARGET -- and it has to be, because
        // refusing here cannot stop the tray from being offered this item. UGUI assigns
        // eventData.pointerDrag AFTER it has dispatched pointerDown, so nothing this class
        // does inside OnPointerDown can un-nominate it as the gesture's drag object; a tray
        // the finger is released over then reads that same pointerDrag in its own OnDrop and
        // finds a perfectly valid handler holding a perfectly valid item.
        //
        // That is how the first Day's tutorial could be walked straight past: pressing a
        // hotdog the forced move did not name refused the pickup correctly -- the item never
        // lifted and never followed the finger -- and releasing over the step's own target
        // tray delivered it anyway. The wrong-order scatter that followed then emptied the
        // cell the NEXT step pointed at, so the tutorial aborted itself on the way out.
        //
        // Read only on the finger's path (WorldTrayView's fromPlayerDrag), never Auto-
        // Collect's: this flag describes the last GESTURE and outlives it, so a stale true
        // from a press made under a popup would otherwise make an item permanently
        // un-collectable by a powerup that never asked a finger for anything.
        public bool WasPickupRefused => pickupRefused;

        public void Configure(BoardGrid board, Camera dragCamera, BoardView boardView, GameManager gameManager, DragFeelSettings dragFeel, BoardAnimationConfig animConfig, HapticsBinder haptics)
        {
            this.board = board;
            this.dragCamera = dragCamera;
            this.boardView = boardView;
            this.gameManager = gameManager;
            this.dragFeel = dragFeel;
            this.animConfig = animConfig;
            this.haptics = haptics;

            boardScale = transform.localScale;
            if (ownCollider == null) ownCollider = GetComponent<Collider2D>();
            if (ownCollider is BoxCollider2D boxCollider) boardHitSize = boxCollider.size;
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
            // The tutorial gate applies to BOARD cells only: cellX/cellY are stale leftovers
            // while an item sits in a tray (see currentTraySlotIndex), so asking about them
            // there would refuse a pickup based on a coordinate that means nothing.
            var refusedByTutorial = !currentTraySlotIndex.HasValue && !gameManager.IsBoardPickupAllowed(cellX, cellY);
            pickupRefused = CurrentItem == null || gameManager.State.IsAwaitingContinue || refusedByTutorial;
            if (pickupRefused) return;

            ApplyPickupVisuals(eventData);

            // After the gate, not before: a press the Continue popup swallowed produced
            // no visual reaction either, and a buzz with nothing on screen to explain it
            // reads as the game being broken rather than as input being blocked.
            haptics?.Request(HapticMoment.ItemPickup);
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

            // An item picked OUT OF A TRAY is sitting at its shrunken tray scale, and it grows
            // toward the BOARD scale rather than that one: it is on its way to a board cell or
            // to another tray, and a tray-sized item under the finger is both hard to see and
            // a lie about what it will be when it lands. A board item is already at boardScale,
            // so this reads as no change at all on the ordinary path. homeScale stays what it
            // was snapshotted as — the two readers below (a tap that never dragged, and the
            // snap-back) both put the item back where it came from, which for a tray item is
            // its slot, at tray scale.
            var dragScale = currentTraySlotIndex.HasValue ? boardScale : homeScale;
            scaleTween = transform.DOScale(dragScale * dragFeel.pickupScaleMultiplier, dragFeel.pickupScaleDuration).SetEase(Ease.OutBack);

            // The tray widened this hitbox in world terms and shrank the transform under it;
            // back at board scale it has to be a cell again, or the item would drag around a
            // hitbox stretched to whatever the tray needed.
            if (currentTraySlotIndex.HasValue && ownCollider is BoxCollider2D pickedUpCollider)
            {
                pickedUpCollider.size = boardHitSize;
            }

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
            if (pickupRefused || CurrentItem == null) return;

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
            if (pickupRefused || CurrentItem == null || dragCamera == null) return;

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
            // A refused press applied no pickup visuals, so there is nothing to undo here.
            if (pickupRefused || CurrentItem == null || IsDragging) return;

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
            // Nothing was ever picked up, so there is no teardown to run -- and running it
            // would be actively wrong: the snap-back branch at the bottom tweens toward a
            // homePosition this gesture never captured.
            if (pickupRefused || CurrentItem == null) return;

            IsDragging = false;

            // Targeted kill, not a blanket transform.DOKill() — in the
            // direct-hit tray-drop case, OnDrop (and the PlaceInSlot fly-in
            // tween it starts) already ran just before UGUI calls OnEndDrag,
            // and a blanket kill here would cancel that tween before it
            // even got to play. Skipped entirely when this drop was the one
            // that delivered (started by OnDrop too) — PlaceInSlotAndDeliver
            // has already done exactly this, and the item is on a one-way
            // trip from here: settle into the slot, then out with the tray.
            // WasAcceptedByTray joins deliverySuccessInProgress here for the same reason and
            // one turn later in the sequence: on a DIRECT tray hit, OnDrop -> PlaceInSlot ->
            // SeatInSlot has already run by the time UGUI calls this, and SeatInSlot now
            // starts a SCALE tween as well as a position one. Undoing the pickup grow at this
            // point would write over the seat's own target the frame before it takes effect --
            // once the seat owns the scale, the pickup teardown must not touch it. The
            // fallback path below (hoveredTray.TryAcceptDrop, for a drop the pointer raycast
            // missed) is unaffected: it runs AFTER this, so the reset lands first and the seat
            // tween still starts from the item's board scale.
            if (!deliverySuccessInProgress && !WasAcceptedByTray)
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

            // Also skipped when this drop delivered — the item is on its way
            // out (settling into its slot, then lifting and fading with the
            // tray) and must not become draggable again for any of it: the
            // model already emptied that tray and gave the slot to the next
            // ticket, so a re-pickup here would be an item nothing owns.
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
            // The tutorial refuses this branch outright for the whole of a step, and falls
            // through to the snap-back below. Without it the step could be made
            // uncompletable in one gesture: parking the item on any other empty cell moved
            // it off the ONE cell the pickup gate permits, so it could never be picked up
            // again and the day was stuck with nothing draggable and no tray accepting.
            // Snapping back is not extra work here -- that branch already tweens to
            // homePosition and touches BoardGrid not at all, so the item returns to its
            // starting cell in the view and never left it in the model.
            if (gameManager.IsBoardRelocationAllowed()
                && boardView.TryGetCellAt(transform.position, out var newX, out var newY)
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

                // The item is down where the player aimed it. D-071 left this branch
                // SILENT on the reasoning that a relocation is the drop working rather
                // than failing -- a correct observation with the wrong conclusion, since
                // dropping into a tray is also the drop working and that has always
                // buzzed. What the silence actually produced was half a gesture: the
                // pickup answered and the release did not.
                //
                // Fired for BOTH origins this branch covers, board-to-board and
                // tray-back-to-board, because ItemPickup does not care where the item
                // came from either. Splitting them would make the same gesture feel
                // finished or unfinished for a reason the player cannot see -- and if
                // board shuffling turns out to be noisy on a device, `wasOnBoard` is
                // already computed right above and narrowing it is one line.
                haptics?.Request(HapticMoment.ItemPlacedOnBoard);

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

                // Rejected: the player aimed this somewhere and it did not take.
                haptics?.Request(HapticMoment.DropRejected);

                Destroy(gameObject);
            }
            else
            {
                // GDD Section 5: "...snap-back animation on invalid drop."
                positionTween = transform.DOMove(homePosition, animConfig.SnapBackDuration).SetEase(Ease.OutQuad);

                // The two rejection branches buzz and the relocation branch above does
                // NOT, which is the distinction worth keeping: moving an item to another
                // empty cell is the drop working, not failing.
                //
                // OnPointerUp's settle-back is deliberately silent too, even though its
                // own comment calls it "the same feel as an invalid drop". That path is a
                // tap that never became a drag, and ItemPickup has already fired on the
                // press -- buzzing again on release would make every stray tap a double.
                haptics?.Request(HapticMoment.DropRejected);
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

        // What a POINTERLESS pickup has to do before this item may be handed to a tray --
        // Auto-Collect's entry point, called once per move by AutoCollectRunner. It is
        // ApplyPickupVisuals' first three lines and nothing else: the grow, the hover
        // offset and the sorting boost all belong to a finger and are deliberately absent.
        //
        // IT EXISTS BECAUSE homeScale WAS NEVER WRITTEN ON THIS PATH (D-113). That field is
        // assigned in exactly one place, ApplyPickupVisuals, reached only from
        // OnPointerDown -- and Auto-Collect raises no pointer event at all. So the item
        // that COMPLETED an order reached PlaceInSlotAndDeliver's `localScale = homeScale`
        // holding an unwritten field, was set to scale ZERO, and spent the entire delivery
        // animation invisible before being destroyed. Only that one item: PlaceInSlot, the
        // ordinary non-completing drop, never touches the scale, which is exactly the
        // asymmetry the user reported -- the rest of the tray looked right.
        //
        // THE DOKill(true) IS THE OTHER HALF, and COMPLETING rather than killing is the
        // same reasoning ApplyPickupVisuals sets out above: a board item can still be in
        // its fly-in when the powerup takes it, and completing that tween leaves it at its
        // own cell at full scale -- which is both a trustworthy snapshot and precisely
        // where a finger would have grabbed it from. Killed instead, it would be seated
        // from wherever it happened to be mid-flight; left running, BoardView's DOJump and
        // the tray's settle tween would write the same transform at once. D-112 doubled
        // the powerup's flights, which doubled that window too.
        public void SettleForAutoCollect()
        {
            transform.DOKill(true);

            homePosition = transform.position;
            homeScale = transform.localScale;
        }

        // Called by WorldTrayView.OnDrop once TrayManager has accepted this
        // item and the tray still needs it displayed (batch not yet
        // resolved). DetachFromBoard above already released this cell's
        // pooled container (if it came from the board) before TryAddItem's
        // batch check ran, so there's nothing left to release here.
        //
        // travelMultiplier stretches the settle tween for an item AUTO-COLLECT moved
        // (D-112). Defaulted to 1, so every finger drop is unchanged and the number only
        // exists on the path that asks for it.
        public void PlaceInSlot(Transform slotTransform, int slotIndex, float seatScale, float travelMultiplier = 1f)
        {
            if (slotTransform == null) return;

            // Only the drops that leave the item SITTING there buzz. The drop that
            // RESOLVES the batch goes to ReleaseAndDestroy or PlaceInSlotAndDeliver
            // below instead, and neither buzzes: that drop is the one that publishes
            // OrderDelivered or costs a life, and both of those outrank this tick in
            // the same frame anyway.
            haptics?.Request(HapticMoment.ItemDroppedInTray);

            SeatInSlot(slotTransform, slotIndex, travelMultiplier, seatScale);
        }

        // Called by WorldTrayView.TryAcceptDrop for the drop that just COMPLETED an
        // order. This item flies into its slot exactly like any other accepted drop,
        // and onSettled -- the tray's delivery animation -- runs when that tween
        // lands, not before. Until this existed the delivery started the instant the
        // batch resolved and this item was never seated at all: it grew and lifted
        // from wherever the finger let it go, while the tray flew away underneath it
        // (decisions.md D-100).
        //
        // The wait is the TWEEN's own completion rather than a DelayedCall on
        // TraySettleDuration, so the animation and the hand-off cannot disagree about
        // how long the item actually takes to arrive.
        //
        // deliverySuccessInProgress is set here for the reason
        // PlayDeliverySuccessAndDestroy used to set it, plus one more: OnEndDrag runs
        // right after this and would otherwise re-enable the collider, leaving the item
        // grabbable during the wait -- on a tray TrayManager has already emptied and
        // handed to the next ticket, so picking it back up would drag an item the model
        // no longer knows about.
        public void PlaceInSlotAndDeliver(
            Transform slotTransform, int slotIndex, Action onSettled, float seatScale, float travelMultiplier = 1f)
        {
            deliverySuccessInProgress = true;
            if (ownCollider != null) ownCollider.enabled = false;

            // OnEndDrag's own scale reset is skipped while deliverySuccessInProgress is
            // set, so the pickup grow is undone here instead. This lands the item at its
            // pre-pickup scale, which is where the seat tween below then starts from on
            // its way down to the tray's size -- so the delivering item shrinks into its
            // slot exactly like every other item in the tray, rather than sitting there
            // at board size for the whole delivery.
            scaleTween?.Kill();
            transform.localScale = homeScale;

            if (slotTransform == null)
            {
                // No slot to fly to (every slot occupied is impossible for a resolving
                // drop, but a missing Inspector reference is not) -- the delivery still
                // has to happen, it just gets nothing to wait for.
                onSettled?.Invoke();
                return;
            }

            SeatInSlot(slotTransform, slotIndex, travelMultiplier, seatScale).OnComplete(() => onSettled?.Invoke());
        }

        // Reparent without letting the item jump to the slot's local zero
        // instantly — restoring its world position right after SetParent keeps
        // it exactly where it visually was, so the settle tween has an actual
        // distance to travel instead of the item just appearing already-seated.
        private Tween SeatInSlot(Transform slotTransform, int slotIndex, float travelMultiplier, float seatScale)
        {
            currentTraySlotIndex = slotIndex;

            var worldPos = transform.position;
            transform.SetParent(slotTransform, false);
            transform.position = worldPos;
            positionTween = transform
                .DOLocalMove(Vector3.zero, animConfig.TraySettleDuration * travelMultiplier)
                .SetEase(Ease.OutBack);

            // The scale half of the same move, and the reason a tray stopped being a pile:
            // an item arrives at its board size, which is very nearly the size of the whole
            // tray (one cell is ~1.09 world units against a 1.48-unit tray), so three of them
            // could never sit apart no matter where the slots were. It rides the position
            // tween's own duration so the item shrinks INTO its slot as one gesture.
            //
            // Not a fixed multiple of boardScale: the caller measured this against the tray's
            // real world size, which is what keeps three items inside a tray whose sprite does
            // not change with the aspect ratio while the board's cells do.
            transform.DOScale(boardScale * seatScale, animConfig.TraySettleDuration * travelMultiplier)
                .SetEase(Ease.OutQuad);

            // The hitbox is a full CELL while the art inside it is only OverallScale of one
            // (0.85 on nineteen of the twenty-one foods) -- the padding BoardItem applies so a
            // board item does not touch its cell's edges. On the board that slack is free,
            // since neighbouring cells are a cell apart. In a tray it is not: the seat scale
            // now divides that padding out so the ART fills its authored size, which leaves a
            // cell-sized collider standing ~18% proud of its own item and reaching into the
            // one beside it. So the same factor is applied here, and the target lands on
            // exactly the item the player can see.
            //
            // The multiplier on top is 1, deliberately. The tray is packed edge to edge, so a
            // target bigger than its item necessarily reaches into the next item's, and two
            // overlapping targets hand the gesture to whichever is nearest the camera rather
            // than to the one under the finger -- the exact defect this whole feature exists
            // to remove. Nothing is given up for it: at 0.52-0.87 world units these are
            // 100-167px targets on a 1080-wide phone. Undone by ApplyPickupVisuals on the way
            // back out, which restores the full cell a board item wants.
            if (ownCollider is BoxCollider2D seatedCollider)
            {
                var overallScale = CurrentItem?.Config != null ? CurrentItem.Config.OverallScale : 1f;
                if (overallScale <= 0f) overallScale = 1f;
                seatedCollider.size = boardHitSize * (overallScale * animConfig.TrayItemHitMultiplier);
            }

            return positionTween;
        }

        // Called by WorldTrayView when this exact drop resolved the tray's batch
        // check the WRONG way — it shakes in place with the rest of the tray and
        // then goes, its contents already scattered back onto the board by the
        // model. DetachFromBoard released this cell's pooled container (if it came
        // from the board) before that batch check ran, so this is just cleanup of
        // the GameObject itself.
        //
        // The successful delivery does NOT come here: that item is seated into a
        // slot by PlaceInSlotAndDeliver above, which makes it a child of the tray,
        // and the tray's own delivery sequence lifts, fades and destroys it along
        // with everything else it is carrying.
        public void ReleaseAndDestroy()
        {
            Destroy(gameObject);
        }
    }
}
