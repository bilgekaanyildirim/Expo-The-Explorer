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
    public static class DayEditorDayStartPreview
    {
        private const float CellSize = 52f;
        private const float CellSpacing = 2f;
        // Used only when visualsConfig isn't assigned -- BoardVisualsConfig is the real
        // source of truth for cell colors (shared with BoardView), this is just a
        // "something's missing" placeholder, not a second real palette.
        private static readonly Color FallbackCellColorA = new(0.5f, 0.5f, 0.5f, 0.25f);
        private static readonly Color FallbackCellColorB = new(0.4f, 0.4f, 0.4f, 0.25f);

        public static void DrawGrid(GameConfig gameConfig, BoardVisualsConfig visualsConfig, List<DayEditorBoardSpawnEntry> boardTimeline)
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

            var gridRect = GUILayoutUtility.GetRect(board.Width * (CellSize + CellSpacing), board.Height * (CellSize + CellSpacing));

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
                        var iconRect = new Rect(cellRect.x + 3, cellRect.y + 3, cellRect.width - 6, cellRect.height - 6);
                        foreach (var layer in item.ResolvedLayers)
                        {
                            DayEditorSpriteGUI.DrawLayeredSprite(iconRect, layer.Sprite, layer.Offset, layer.Scale);
                        }
                    }
                }
            }

            if (board.PendingSpawnCount > 0)
            {
                EditorGUILayout.HelpBox($"{board.PendingSpawnCount} spawn(s) still waiting for an empty cell (board full).", MessageType.Warning);
            }
        }
    }
}
