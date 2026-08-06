using System;
using System.Collections.Generic;
using ExpoTheExplorer.Data;

namespace ExpoTheExplorer.Core
{
    // Raw grid mechanics only — capacity, cell occupancy, and the "queue a spawn
    // request when the board is full, place it the moment a cell frees up" rule
    // from GDD Section 4. Deciding WHAT to spawn (required pool / noise pool) is
    // out of scope here — that's the future Systems/BoardDistribution module,
    // which only ever calls RequestSpawn on this grid.
    public class BoardGrid
    {
        private readonly BoardItem[,] cells;
        private readonly Queue<BoardItem> pendingSpawns = new();

        public int Width { get; }
        public int Height { get; }
        public int CellCount => Width * Height;
        public int OccupiedCellCount { get; private set; }
        public bool IsFull => OccupiedCellCount >= CellCount;
        public int PendingSpawnCount => pendingSpawns.Count;

        // Payload is just the coordinate — subscribers call ItemAt(x, y) to read
        // the current state, so this stays a thin notification, not a second
        // source of truth. Mirrors the EventBus<T> pattern already used by
        // GameState.TicketDelivered/TicketCancelled.
        public EventBus<(int X, int Y)> CellChanged { get; } = new();

        public BoardGrid(GameConfig config)
        {
            Width = config.BoardWidth;
            Height = config.BoardHeight;
            cells = new BoardItem[Width, Height];
        }

        public bool IsInBounds(int x, int y) => x >= 0 && x < Width && y >= 0 && y < Height;

        public bool IsCellEmpty(int x, int y) => IsInBounds(x, y) && cells[x, y] == null;

        public BoardItem ItemAt(int x, int y) => IsInBounds(x, y) ? cells[x, y] : null;

        public bool TryGetFirstEmptyCell(out int x, out int y)
        {
            for (var scanY = 0; scanY < Height; scanY++)
            {
                for (var scanX = 0; scanX < Width; scanX++)
                {
                    if (cells[scanX, scanY] != null) continue;
                    x = scanX;
                    y = scanY;
                    return true;
                }
            }

            x = -1;
            y = -1;
            return false;
        }

        public bool TryPlaceItem(BoardItem item, int x, int y)
        {
            if (!IsCellEmpty(x, y)) return false;

            cells[x, y] = item;
            OccupiedCellCount++;
            CellChanged.Publish((x, y));
            return true;
        }

        // A caller-supplied random source picks uniformly among ALL empty cells
        // (not just the first found) so spawned items don't visibly cluster in
        // scan order — omit it (or pass null) to fall back to the deterministic
        // first-empty-cell behavior, which existing callers/tests still rely on.
        public bool RequestSpawn(BoardItem item, Random random = null)
        {
            if (TryGetEmptyCell(random, out var x, out var y))
            {
                return TryPlaceItem(item, x, y);
            }

            pendingSpawns.Enqueue(item);
            return false;
        }

        private bool TryGetEmptyCell(Random random, out int x, out int y)
        {
            if (random == null) return TryGetFirstEmptyCell(out x, out y);

            var emptyCells = new List<(int X, int Y)>();
            for (var scanY = 0; scanY < Height; scanY++)
            {
                for (var scanX = 0; scanX < Width; scanX++)
                {
                    if (cells[scanX, scanY] == null) emptyCells.Add((scanX, scanY));
                }
            }

            if (emptyCells.Count == 0)
            {
                x = -1;
                y = -1;
                return false;
            }

            (x, y) = emptyCells[random.Next(emptyCells.Count)];
            return true;
        }

        // Full day-reset primitive (GameManager.RetryDay). Drains pendingSpawns
        // FIRST -- RemoveItem backfills the cell it just emptied from that
        // queue the instant it's non-empty, so clearing it after the loop
        // instead would let cells silently refill mid-loop and this would
        // never actually empty the board. Reuses RemoveItem per cell (rather
        // than a direct array reset) so CellChanged still fires per cell for
        // any board-rendering view to stay in sync.
        public void Clear()
        {
            pendingSpawns.Clear();

            for (var x = 0; x < Width; x++)
            {
                for (var y = 0; y < Height; y++)
                {
                    if (cells[x, y] != null)
                    {
                        RemoveItem(x, y);
                    }
                }
            }
        }

        public BoardItem RemoveItem(int x, int y)
        {
            if (!IsInBounds(x, y)) return null;

            var removed = cells[x, y];
            if (removed == null) return null;

            cells[x, y] = null;
            OccupiedCellCount--;

            if (pendingSpawns.Count > 0)
            {
                TryPlaceItem(pendingSpawns.Dequeue(), x, y); // publishes CellChanged itself
            }
            else
            {
                CellChanged.Publish((x, y));
            }

            return removed;
        }
    }
}
