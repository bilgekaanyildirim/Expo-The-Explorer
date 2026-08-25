using System.Collections.Generic;
using DG.Tweening;
using ExpoTheExplorer.Bootstrap;
using ExpoTheExplorer.Core;
using ExpoTheExplorer.Data;
using UnityEngine;

namespace ExpoTheExplorer.UI
{
    // Reactive, read-only view of GameState.Board — no game logic here. Builds a
    // background square per cell once, then updates only the cells BoardGrid
    // reports as changed via CellChanged (no per-frame polling).
    public class BoardView : MonoBehaviour
    {
        [SerializeField] private GameManager gameManager;
        [SerializeField] private Camera targetCamera;
        [SerializeField, Range(0f, 1f)] private float screenFillFraction = 0.92f;
        [SerializeField, Range(0f, 1f)] private float cellPadding = 0f;
        [Tooltip("Board colors (cell checkerboard, placeholder item tint, frame) -- shared with the Day Editor's board preview via the same asset, so both stay in sync by construction instead of duplicating these values in two places.")]
        [SerializeField] private BoardVisualsConfig visualsConfig;

        [Header("Frame")]
        [Tooltip("Frame thickness as a fraction of cell size, added around the whole board on every side. 0 = no frame.")]
        [SerializeField, Range(0f, 1f)] private float frameThicknessFraction = 0.08f;

        [Header("Drag Feel")]
        [Tooltip("Shared tuning for the pickup/hover/follow feel of dragged board items.")]
        [SerializeField] private DragFeelConfig dragFeelConfig;
        [Tooltip("Shared tuning for board/tray animation durations (snap-back, tray settle, pop-in, slot clear).")]
        [SerializeField] private BoardAnimationConfig animConfig;
        [Tooltip("Optional — if set, newly-appearing items fly in from this point (scaling up as they travel) instead of just popping in at their destination cell.")]
        [SerializeField] private Transform startingPoint;

        [Header("Haptics")]
        [Tooltip("Optional. The scene's HapticsBinder, handed down to every board item so picking one up and dropping it into a tray can be felt. Unwired means no drag haptics and nothing else changes.")]
        [SerializeField] private HapticsBinder haptics;

        private BoardGrid board;
        private Sprite placeholderSprite;
        private Transform[,] itemContainers;
        private List<SpriteRenderer>[,] itemLayerPools;
        private Transform itemsParent;
        private float cellSize;
        private Vector2 boardOrigin;
        private Camera resolvedCamera;
        private BoardItemDragHandler.DragFeelSettings dragFeel;

        private void Start()
        {
            board = gameManager.State.Board;
            placeholderSprite = CreatePlaceholderSprite();
            itemContainers = new Transform[board.Width, board.Height];
            itemLayerPools = new List<SpriteRenderer>[board.Width, board.Height];

            FitToCamera();

            // Resolved once cellSize is known (post-FitToCamera) so the drag
            // feel stays proportional to the board's on-screen size instead
            // of being a fixed world-unit constant tied to one aspect ratio.
            dragFeel = new BoardItemDragHandler.DragFeelSettings
            {
                offsetDistance = dragFeelConfig.OffsetFraction * cellSize,
                followMultiplierUp = dragFeelConfig.FollowMultiplierUp,
                followMultiplierDown = dragFeelConfig.FollowMultiplierDown,
                followMultiplierHorizontal = dragFeelConfig.FollowMultiplierHorizontal,
                pickupScaleMultiplier = dragFeelConfig.PickupScaleMultiplier,
                pickupScaleDuration = dragFeelConfig.PickupScaleDuration,
            };

            BuildFrame();
            BuildBackground();
            itemsParent = new GameObject("Items").transform;
            itemsParent.SetParent(transform, false);

            // Subscribe before the initial sync so nothing spawned between this
            // object's Awake and Start (script execution order isn't guaranteed)
            // can be missed.
            board.CellChanged.Subscribe(OnCellChanged);

            for (var y = 0; y < board.Height; y++)
            {
                for (var x = 0; x < board.Width; x++)
                {
                    RefreshCell(x, y);
                }
            }
        }

        private void OnDestroy()
        {
            board?.CellChanged.Unsubscribe(OnCellChanged);
        }

        private void OnCellChanged((int X, int Y) coords) => RefreshCell(coords.X, coords.Y);

        // Called by BoardItemDragHandler.PlaceInSlot when an item is permanently
        // reparented into a tray slot — that container no longer belongs to this
        // cell's pool, so a future spawn into (x, y) must build a fresh one
        // instead of reactivating/repositioning the one now sitting in the tray.
        public void ReleaseContainer(int x, int y)
        {
            itemContainers[x, y] = null;
        }

        // The seam the Auto-Collect powerup needs (.claude/powerup-plan.md Adım 6), and it
        // is deliberately the SAME object a finger would have grabbed. That powerup places
        // items by calling WorldTrayView.TryAcceptDrop with this handler, which is the
        // ordinary drop path -- detach from the board, reparent into the tray slot, let
        // TrayManager run its batch check. The alternative, writing straight into the data
        // model, would leave the tray visually EMPTY: a tray's contents are drawn entirely
        // by the dragged object being reparented, and nothing rebuilds them from state.
        //
        // Returns false for an empty or not-yet-built cell rather than throwing, because
        // the caller is walking a board that its own previous move may have changed.
        public bool TryGetDragHandler(int x, int y, out BoardItemDragHandler handler)
        {
            handler = null;

            // board is assigned in Start, and a powerup press cannot reach this before
            // then -- but a null here would be a NullReferenceException inside a button
            // handler, which is the one place it is least readable.
            if (board == null || itemContainers == null || !board.IsInBounds(x, y)) return false;

            var container = itemContainers[x, y];

            // An inactive container is a cell whose item was removed: RefreshCell switches
            // the object off rather than destroying it, so "there is a container" and
            // "there is an item" are different questions.
            if (container == null || !container.gameObject.activeSelf) return false;

            handler = container.GetComponent<BoardItemDragHandler>();
            return handler != null && handler.CurrentItem != null;
        }

        // Brackets whatever call below is expected to trigger RefreshCell for
        // a "new appearance" — a board-to-board move (BoardItemDragHandler),
        // a tray pickup dropped somewhere invalid (BoardItemDragHandler), or
        // a wrong-order scatter (WorldTrayView, around TrayManager.
        // TryAddItem) — so that appearance uses the given origin instead of
        // Starting Point. origin == null means "no fly-in at all" (a plain
        // relocation, not a new appearance); a real Vector3 means "fly in
        // from here instead" (the item's last position, or the tray's
        // position). Not one-shot/per-cell: a single scatter can trigger
        // several RequestSpawn calls (one per item) before the caller ends
        // the bracket, and all of them should use the same origin.
        private bool flyInOverrideActive;
        private Vector3? flyInOverrideOrigin;
        private float flyInOverrideDelay;

        // delay lets a wrong-order scatter hold the newly-appeared item
        // invisible (still scale zero at its origin) for as long as
        // WorldTrayView's own pre-scatter shake takes, so the two stay in
        // sync instead of the board item popping in while the tray is still
        // visibly shaking with the (about to vanish) old items.
        public void BeginFlyInOverride(Vector3? origin, float delay = 0f)
        {
            flyInOverrideActive = true;
            flyInOverrideOrigin = origin;
            flyInOverrideDelay = delay;
        }

        public void EndFlyInOverride()
        {
            flyInOverrideActive = false;
            flyInOverrideOrigin = null;
            flyInOverrideDelay = 0f;
        }

        // Inverse of CellPosition — used by BoardItemDragHandler to figure out
        // which board cell (if any) a drag was released over, so an item can
        // be moved to another empty cell instead of only ever going to a tray
        // or snapping back home. boardOrigin/cellSize are expressed in this
        // object's LOCAL space (CellPosition's result is used as a
        // localPosition under itemsParent, which sits at zero offset from
        // this transform) — converting the incoming world position through
        // InverseTransformPoint first keeps this correct no matter where the
        // BoardView GameObject itself has been moved/rotated/scaled in the scene.
        public bool TryGetCellAt(Vector3 worldPosition, out int x, out int y)
        {
            var localPosition = transform.InverseTransformPoint(worldPosition);
            x = Mathf.RoundToInt((localPosition.x - boardOrigin.x) / cellSize);
            y = Mathf.RoundToInt((localPosition.y - boardOrigin.y) / cellSize);
            return board.IsInBounds(x, y);
        }

        // Cell size/origin aren't designer-tunable magic numbers — they're derived
        // from the camera's orthographic viewport so the board always fits the
        // screen regardless of aspect ratio (mobile portrait vs. editor window).
        private void FitToCamera()
        {
            var cam = targetCamera != null ? targetCamera : Camera.main;
            if (cam == null) return;

            resolvedCamera = cam;

            var visibleHeight = 2f * cam.orthographicSize;
            var visibleWidth = visibleHeight * cam.aspect;

            cellSize = Mathf.Min(
                visibleWidth * screenFillFraction / board.Width,
                visibleHeight * screenFillFraction / board.Height);

            boardOrigin = new Vector2(
                cam.transform.position.x - (board.Width - 1) * cellSize / 2f,
                cam.transform.position.y - (board.Height - 1) * cellSize / 2f);
        }

        // A single oversized sprite behind the cells, extending frameThicknessFraction * cellSize
        // past the board's edge on every side — reads as a border/frame around the grid.
        private void BuildFrame()
        {
            if (frameThicknessFraction <= 0f) return;

            var frameObject = new GameObject("Frame");
            frameObject.transform.SetParent(transform, false);

            var centerX = boardOrigin.x + (board.Width - 1) * cellSize / 2f;
            var centerY = boardOrigin.y + (board.Height - 1) * cellSize / 2f;
            frameObject.transform.localPosition = new Vector3(centerX, centerY, 0.05f);

            var renderer = frameObject.AddComponent<SpriteRenderer>();
            renderer.sprite = placeholderSprite;
            renderer.color = visualsConfig.FrameColor;
            renderer.sortingOrder = -1;

            var thickness = frameThicknessFraction * cellSize;
            frameObject.transform.localScale = new Vector3(
                board.Width * cellSize + thickness * 2f,
                board.Height * cellSize + thickness * 2f,
                1f);
        }

        private void BuildBackground()
        {
            var cellsParent = new GameObject("Cells").transform;
            cellsParent.SetParent(transform, false);

            for (var y = 0; y < board.Height; y++)
            {
                for (var x = 0; x < board.Width; x++)
                {
                    var cellObject = new GameObject($"Cell_{x}_{y}");
                    cellObject.transform.SetParent(cellsParent, false);
                    cellObject.transform.localPosition = CellPosition(x, y, 0f);

                    var renderer = cellObject.AddComponent<SpriteRenderer>();
                    renderer.sprite = placeholderSprite;
                    renderer.color = (x + y) % 2 == 0 ? visualsConfig.CellColorA : visualsConfig.CellColorB;
                    renderer.sortingOrder = 0;
                    ApplyFittedScale(cellObject.transform, placeholderSprite);
                }
            }
        }

        private void RefreshCell(int x, int y)
        {
            var item = board.ItemAt(x, y);
            var container = itemContainers[x, y];

            if (item == null)
            {
                if (container != null) container.gameObject.SetActive(false);
                return;
            }

            // Computed before container gets created/reactivated below — a
            // brand-new container is active-by-default the instant it's
            // made, so "was this inactive" has to be captured first or a
            // fresh spawn would never look newly-appearing.
            var isNewlyAppearing = container == null || !container.gameObject.activeSelf;

            if (container == null)
            {
                var itemObject = new GameObject($"Item_{x}_{y}");
                itemObject.transform.SetParent(itemsParent, false);
                itemObject.transform.localPosition = CellPosition(x, y, -0.1f);

                // Generous hitbox (GDD Section 5 — mobile drop targets should be
                // larger than the visual bounds) sized to the whole cell, plus the
                // drag handler that lets this item be picked up toward a tray.
                var collider = itemObject.AddComponent<BoxCollider2D>();
                collider.size = new Vector2(cellSize, cellSize);

                var dragHandler = itemObject.AddComponent<BoardItemDragHandler>();
                dragHandler.Configure(board, resolvedCamera, this, gameManager, dragFeel, animConfig, haptics);

                container = itemObject.transform;
                itemContainers[x, y] = container;
                itemLayerPools[x, y] = new List<SpriteRenderer>();
            }

            container.gameObject.SetActive(true);
            container.GetComponent<BoardItemDragHandler>().SetCell(x, y, item);

            if (isNewlyAppearing)
            {
                // Covers a brand-new spawn, a wrong-delivery/timeout scatter
                // back onto the board, and a board-to-board move's
                // destination cell — all three go through this exact same
                // "container was inactive/nonexistent, now showing an item"
                // path. Which origin (if any) the fly-in should use depends
                // on which of those this actually is: a genuinely new item
                // defaults to Starting Point; a caller-specified override
                // (BeginFlyInOverride) takes priority when active — either a
                // specific origin (last drag position, tray position) or
                // null (a relocation, no fly-in — plain in-place pop-in).
                container.DOKill();
                Vector3? flyInOrigin = flyInOverrideActive
                    ? flyInOverrideOrigin
                    : (startingPoint != null ? startingPoint.position : null);
                var flyInDelay = flyInOverrideActive ? flyInOverrideDelay : 0f;

                if (flyInOrigin.HasValue)
                {
                    // The destination is recomputed from the cell, never read
                    // off container.position. A reused container is only
                    // still standing on its cell if every tween that ever
                    // moved it ran to completion, and an interrupted fly-in
                    // (the player grabbing an item mid-flight) leaves it
                    // stranded wherever it was killed — trusting the
                    // transform there would bake that stranded spot in as
                    // this cell's destination from then on. Captured after
                    // the reset and before flyInOrigin overwrites it below,
                    // then animated back to, so the item visually flies in
                    // from flyInOrigin and grows into place rather than just
                    // popping in place.
                    container.localPosition = CellPosition(x, y, -0.1f);
                    var destinationWorldPos = container.position;
                    container.position = flyInOrigin.Value;
                    container.localScale = Vector3.zero;
                    // DOJump arcs the path itself (a real parabola, not just
                    // an eased straight line) — jumpPower is the arc's peak
                    // height, scaled by cellSize so it holds up across
                    // different board/camera sizes; 1 jump = a single arc,
                    // not a bounced/repeating hop. flyInDelay (only nonzero
                    // for a wrong-order scatter) keeps the container
                    // invisible at its origin until WorldTrayView's own
                    // pre-scatter shake finishes, so the two stay in sync.
                    container.DOJump(destinationWorldPos, animConfig.PopInJumpPower * cellSize, 1, animConfig.PopInDuration).SetEase(Ease.OutQuad).SetDelay(flyInDelay);
                    container.DOScale(Vector3.one, animConfig.PopInDuration).SetEase(Ease.OutBack).SetDelay(flyInDelay);

                    // For the length of a delayed fly-in (only ever a
                    // wrong-order scatter) this container is active, at
                    // scale zero, parked on the tray it came from — and with
                    // its collider live that is a stack of invisible but
                    // fully grabbable items sitting exactly where the
                    // player's finger just released. Held out of the raycast
                    // until it actually starts appearing; the drag handler
                    // owns this flag from the moment a real drag begins, and
                    // the two never overlap because no drag can start while
                    // it's off.
                    if (flyInDelay > 0f && container.TryGetComponent<Collider2D>(out var itemCollider))
                    {
                        itemCollider.enabled = false;
                        DOVirtual.DelayedCall(flyInDelay, () =>
                        {
                            if (itemCollider != null) itemCollider.enabled = true;
                        });
                    }
                }
                else
                {
                    container.localScale = Vector3.zero;
                    container.DOScale(Vector3.one, animConfig.PopInDuration).SetEase(Ease.OutBack);
                }
            }

            var resolvedLayers = item.ResolvedLayers;
            var pool = itemLayerPools[x, y];

            // ResolvedLayers falls back to Config.Sprite internally, so this only
            // stays empty for a genuinely unconfigured FoodItemConfig (no layers,
            // no base sprite) — show one placeholder layer for that case.
            if (resolvedLayers.Count == 0)
            {
                var placeholderRenderer = GetPooledLayerRenderer(pool, container, 0);
                placeholderRenderer.sprite = placeholderSprite;
                placeholderRenderer.color = visualsConfig.PlaceholderItemColor;
                placeholderRenderer.transform.localPosition = Vector3.zero;
                ApplyFittedScale(placeholderRenderer.transform, placeholderSprite);
                DeactivateUnusedLayers(pool, 1);
                return;
            }

            for (var i = 0; i < resolvedLayers.Count; i++)
            {
                var resolved = resolvedLayers[i];
                var layerRenderer = GetPooledLayerRenderer(pool, container, i);

                if (resolved.Sprite != null)
                {
                    layerRenderer.sprite = resolved.Sprite;
                    layerRenderer.color = Color.white;
                }
                else
                {
                    layerRenderer.sprite = placeholderSprite;
                    layerRenderer.color = visualsConfig.PlaceholderItemColor;
                }

                layerRenderer.transform.localPosition = new Vector3(resolved.Offset.x * cellSize, resolved.Offset.y * cellSize, 0f);
                ApplyFittedScale(layerRenderer.transform, layerRenderer.sprite, resolved.Scale);
            }

            DeactivateUnusedLayers(pool, resolvedLayers.Count);
        }

        private static SpriteRenderer GetPooledLayerRenderer(List<SpriteRenderer> pool, Transform container, int index)
        {
            if (index < pool.Count)
            {
                var existing = pool[index];
                existing.gameObject.SetActive(true);
                return existing;
            }

            var layerObject = new GameObject($"Layer_{index}");
            layerObject.transform.SetParent(container, false);

            var renderer = layerObject.AddComponent<SpriteRenderer>();
            renderer.sortingOrder = 1 + index;
            pool.Add(renderer);
            return renderer;
        }

        private static void DeactivateUnusedLayers(List<SpriteRenderer> pool, int usedCount)
        {
            for (var i = usedCount; i < pool.Count; i++)
            {
                pool[i].gameObject.SetActive(false);
            }
        }

        // Real imported sprites can be any native size (e.g. ~10 world units at
        // 1000px/100 PPU) while the procedural placeholder is exactly 1 unit —
        // scale each sprite individually so it fits inside a cell instead of
        // assuming every sprite is already cell-sized. additionalScale is each
        // SpriteLayer's own relative-size tuning on top of that fit (independently
        // cropped ingredient art doesn't share a canvas, so "fit to cell" alone
        // would make a thin cheese slice as big as the whole bun).
        private void ApplyFittedScale(Transform target, Sprite spriteToFit, float additionalScale = 1f)
        {
            var targetSize = cellSize * (1f - cellPadding);
            var nativeSize = spriteToFit.bounds.size;
            var scale = Mathf.Min(targetSize / nativeSize.x, targetSize / nativeSize.y);
            target.localScale = Vector3.one * (scale * additionalScale);
        }

        private Vector3 CellPosition(int x, int y, float z)
        {
            return new Vector3(boardOrigin.x + x * cellSize, boardOrigin.y + y * cellSize, z);
        }

        private static Sprite CreatePlaceholderSprite()
        {
            const int size = 4;
            var texture = new Texture2D(size, size);
            var pixels = new Color[size * size];
            for (var i = 0; i < pixels.Length; i++) pixels[i] = Color.white;
            texture.SetPixels(pixels);
            texture.Apply();
            return Sprite.Create(texture, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), size);
        }
    }
}
