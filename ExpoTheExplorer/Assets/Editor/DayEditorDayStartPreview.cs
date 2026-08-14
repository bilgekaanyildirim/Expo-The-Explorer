using System.Collections.Generic;
using System.Linq;
using ExpoTheExplorer.Core;
using ExpoTheExplorer.Data;
using ExpoTheExplorer.Systems.DaySystem;
using UnityEditor;
using UnityEngine;

namespace ExpoTheExplorer.Editor
{
    // Single static grid showing exactly what TriggerStepIndex == -1 (Day Start) entries
    // produce -- reuses DayBoardTimelinePlayer.ApplyForStep(..., -1), the same primitive
    // GameManager.ApplyDayStartBoardPreSeed calls at runtime, so this is never out of sync
    // with what the live game actually shows the player at the start of a Day. Unlike the
    // old per-step spawn+delivery simulation strip (removed, see decisions.md D-002), there
    // is no ticket sequence dependency here -- Day Start doesn't depend on tickets at all.
    //
    // Interaction mirrors DayEditorTicketCardPreview's card strip: left-click-drag relocates
    // an occupied cell's item (only starts if the cell actually has one), right-click selects
    // a cell (toggling it back off if it's already selected) for DayEditorModel's item/
    // modifications editor panel -- the two gestures are independent, same as the ticket
    // strip's drag-vs-right-click split.
    public static class DayEditorDayStartPreview
    {
        private const float CellSize = 52f;
        private const float CellSpacing = 2f;
        private const float DragThreshold = 10f;
        // Used only when visualsConfig isn't assigned -- BoardVisualsConfig is the real
        // source of truth for cell colors (shared with BoardView), this is just a
        // "something's missing" placeholder, not a second real palette.
        private static readonly Color FallbackCellColorA = new(0.5f, 0.5f, 0.5f, 0.25f);
        private static readonly Color FallbackCellColorB = new(0.4f, 0.4f, 0.4f, 0.25f);
        private static readonly Color SelectedCellBorder = new(0.95f, 0.85f, 0.2f, 1f);
        private static readonly Color DragTargetBorder = new(0.3f, 0.9f, 0.4f, 1f);

        // selectedX/selectedY and draggedX/draggedY/dragStartMousePos are UI state owned by
        // the caller (same convention as DayEditorTicketCardPreview's scrollPos/selectedIndex/
        // draggedIndex) -- static fields here would be shared across every open Day tab.
        public static void DrawGrid(
            GameConfig gameConfig, BoardVisualsConfig visualsConfig, List<DayEditorBoardSpawnEntry> boardTimeline,
            ref int selectedX, ref int selectedY, ref int draggedX, ref int draggedY, ref Vector2 dragStartMousePos)
        {
            if (gameConfig == null)
            {
                EditorGUILayout.HelpBox("Game Config not assigned (toolbar above) -- can't preview the board.", MessageType.Info);
                return;
            }

            if (visualsConfig == null)
            {
                EditorGUILayout.HelpBox("Board Visuals Config not assigned (toolbar above) -- cell colors will show as placeholders.", MessageType.Info);
            }

            var cellColorA = visualsConfig != null ? visualsConfig.CellColorA : FallbackCellColorA;
            var cellColorB = visualsConfig != null ? visualsConfig.CellColorB : FallbackCellColorB;

            var resolvedTimeline = boardTimeline.Select(e => e.ToResolved()).ToList();
            var board = new BoardGrid(gameConfig);
            DayBoardTimelinePlayer.ApplyForStep(board, resolvedTimeline, -1);

            var controlId = GUIUtility.GetControlID(FocusType.Passive);
            var isDragging = draggedX >= 0 && (Event.current.mousePosition - dragStartMousePos).sqrMagnitude > DragThreshold * DragThreshold;

            // ExpandWidth(false) keeps this at its exact pixel size -- without it, GetRect
            // stretches to fill the whole horizontal layout group, pushing whatever's drawn
            // next to it (DayEditorModel's cell editor panel) far off to the right.
            var gridRect = GUILayoutUtility.GetRect(
                board.Width * (CellSize + CellSpacing), board.Height * (CellSize + CellSpacing), GUILayout.ExpandWidth(false));

            var dragTargetCell = isDragging ? CellAtMousePosition(gridRect, board, Event.current.mousePosition) : (X: -1, Y: -1);

            // Row 0 draws at the bottom, matching BoardView.CellPosition's world-space
            // y = origin.y + y*cellSize (Unity Y-up) -- keeps the preview's row order
            // visually consistent with how the board actually looks in-game.
            for (var x = 0; x < board.Width; x++)
            {
                for (var y = 0; y < board.Height; y++)
                {
                    var cellRect = new Rect(
                        gridRect.x + x * (CellSize + CellSpacing),
                        gridRect.y + (board.Height - 1 - y) * (CellSize + CellSpacing),
                        CellSize, CellSize);

                    // Same (x+y)%2 checkerboard BoardView.BuildBackground uses -- the
                    // background doesn't change with occupancy in the real game either,
                    // items just render on top of it.
                    EditorGUI.DrawRect(cellRect, (x + y) % 2 == 0 ? cellColorA : cellColorB);

                    var item = board.ItemAt(x, y);
                    if (item != null)
                    {
                        var isBeingDragged = isDragging && x == draggedX && y == draggedY;
                        var previousColor = GUI.color;
                        if (isBeingDragged) GUI.color = new Color(1f, 1f, 1f, 0.35f) * previousColor; // faded in place -- its ghost follows the cursor instead

                        var iconRect = new Rect(cellRect.x + 3, cellRect.y + 3, cellRect.width - 6, cellRect.height - 6);
                        foreach (var layer in item.ResolvedLayers)
                        {
                            DayEditorSpriteGUI.DrawLayeredSprite(iconRect, layer.Sprite, layer.Offset, layer.Scale);
                        }

                        if (isBeingDragged) GUI.color = previousColor;
                    }

                    if (x == selectedX && y == selectedY)
                    {
                        DrawCellBorder(cellRect, SelectedCellBorder);
                    }
                    else if (isDragging && x == dragTargetCell.X && y == dragTargetCell.Y)
                    {
                        DrawCellBorder(cellRect, DragTargetBorder);
                    }

                    if (Event.current.type == EventType.MouseDown && Event.current.button == 0 && cellRect.Contains(Event.current.mousePosition) && item != null)
                    {
                        draggedX = x;
                        draggedY = y;
                        dragStartMousePos = Event.current.mousePosition;
                        GUIUtility.hotControl = controlId;
                        Event.current.Use();
                    }

                    // Right-click toggles selection for the item/modifications editor panel,
                    // independent of the left-click/drag gesture above -- works on an empty
                    // cell too (that's how a brand-new cell gets its first item picked).
                    if (Event.current.type == EventType.MouseDown && Event.current.button == 1 && cellRect.Contains(Event.current.mousePosition))
                    {
                        var alreadySelected = x == selectedX && y == selectedY;
                        selectedX = alreadySelected ? -1 : x;
                        selectedY = alreadySelected ? -1 : y;
                        GUI.changed = true;
                        Event.current.Use();
                    }
                }
            }

            // MouseUp decides, after the fact, whether this gesture was a click or a drag --
            // GUIUtility.hotControl keeps routing it here even if the cursor left the grid
            // mid-gesture, same as DayEditorTicketCardPreview's own drag.
            if (draggedX >= 0 && GUIUtility.hotControl == controlId && Event.current.type == EventType.MouseUp && Event.current.button == 0)
            {
                if (isDragging && (dragTargetCell.X != draggedX || dragTargetCell.Y != draggedY))
                {
                    MoveOrSwap(boardTimeline, draggedX, draggedY, dragTargetCell.X, dragTargetCell.Y);
                }
                // A plain click (never exceeded the threshold) does nothing -- left-click's
                // only job here is the drag gesture; selection is right-click's job.
                draggedX = -1;
                draggedY = -1;
                GUIUtility.hotControl = 0;
                GUI.changed = true;
                Event.current.Use();
            }

            // draggedX may have just been reset above (the MouseUp block that commits a move
            // or a plain click both clear it within this same event) -- isDragging alone is
            // stale at that point, so it's re-checked here too before drawing the ghost.
            if (isDragging && draggedX >= 0)
            {
                var draggedItem = board.ItemAt(draggedX, draggedY);
                if (draggedItem != null)
                {
                    var ghostRect = new Rect(
                        Event.current.mousePosition.x - CellSize / 2f, Event.current.mousePosition.y - CellSize / 2f, CellSize, CellSize);
                    var previousColor = GUI.color;
                    GUI.color = new Color(1f, 1f, 1f, 0.85f) * previousColor;
                    foreach (var layer in draggedItem.ResolvedLayers)
                    {
                        DayEditorSpriteGUI.DrawLayeredSprite(ghostRect, layer.Sprite, layer.Offset, layer.Scale);
                    }
                    GUI.color = previousColor;
                }
            }

            if (board.PendingSpawnCount > 0)
            {
                EditorGUILayout.HelpBox($"{board.PendingSpawnCount} spawn(s) still waiting for an empty cell (board full).", MessageType.Warning);
            }
        }

        private static (int X, int Y) CellAtMousePosition(Rect gridRect, BoardGrid board, Vector2 mousePosition)
        {
            var col = Mathf.FloorToInt((mousePosition.x - gridRect.x) / (CellSize + CellSpacing));
            var rowFromTop = Mathf.FloorToInt((mousePosition.y - gridRect.y) / (CellSize + CellSpacing));
            var x = Mathf.Clamp(col, 0, board.Width - 1);
            var y = Mathf.Clamp(board.Height - 1 - rowFromTop, 0, board.Height - 1);
            return (x, y);
        }

        // Moves the entry at (fromX, fromY) to (toX, toY). If the target cell is already
        // occupied, the two entries swap positions instead of one silently overwriting/
        // losing the other.
        private static void MoveOrSwap(List<DayEditorBoardSpawnEntry> boardTimeline, int fromX, int fromY, int toX, int toY)
        {
            var source = FindEntryAt(boardTimeline, fromX, fromY);
            if (source == null) return; // shouldn't happen -- a drag only ever starts on an occupied cell

            var target = FindEntryAt(boardTimeline, toX, toY);
            source.X = toX;
            source.Y = toY;
            if (target != null)
            {
                target.X = fromX;
                target.Y = fromY;
            }
        }

        private static DayEditorBoardSpawnEntry FindEntryAt(List<DayEditorBoardSpawnEntry> boardTimeline, int x, int y) =>
            boardTimeline.FirstOrDefault(e => e.TriggerStepIndex == -1 && e.UseExactCell && e.X == x && e.Y == y);

        private static void DrawCellBorder(Rect rect, Color color)
        {
            const float t = 2f;
            EditorGUI.DrawRect(new Rect(rect.x, rect.y, rect.width, t), color);
            EditorGUI.DrawRect(new Rect(rect.x, rect.yMax - t, rect.width, t), color);
            EditorGUI.DrawRect(new Rect(rect.x, rect.y, t, rect.height), color);
            EditorGUI.DrawRect(new Rect(rect.xMax - t, rect.y, t, rect.height), color);
        }
    }
}
