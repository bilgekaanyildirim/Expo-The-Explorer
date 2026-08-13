using System.Collections.Generic;
using System.Linq;
using ExpoTheExplorer.Core;
using ExpoTheExplorer.Data;
using ExpoTheExplorer.Systems.DaySystem;
using UnityEditor;
using UnityEngine;

namespace ExpoTheExplorer.Editor
{
    // Replays the same deterministic playback the game actually uses
    // (DayBoardTimelinePlayer.ApplyForStep, on a throwaway BoardGrid) to show
    // exactly where each BoardTimeline entry lands -- including UseExactCell =
    // false ones, whose landing cell isn't stored anywhere and would otherwise
    // have to be guessed. TriggerStepIndex is a ticket's ArrivalSequence (its
    // position in TicketSequence), -1 = Day Start (see GameManager.cs).
    public static class DayEditorBoardTimelinePreview
    {
        private const float CellSize = 52f;
        private const float CellSpacing = 2f;
        private const float LabelHeight = 16f;
        private const float ColumnSpacing = 10f;
        private const float DividerWidth = 1f;
        private static readonly Color DividerColor = new(0.5f, 0.5f, 0.5f, 0.6f);
        // Used only when visualsConfig isn't assigned -- BoardVisualsConfig is the
        // real source of truth for cell colors (shared with BoardView), this is
        // just a "something's missing" placeholder, not a second real palette.
        private static readonly Color FallbackCellColorA = new(0.5f, 0.5f, 0.5f, 0.25f);
        private static readonly Color FallbackCellColorB = new(0.4f, 0.4f, 0.4f, 0.25f);
        private static readonly Color NewThisStepBorder = new(0.3f, 0.9f, 0.4f, 1f);
        private static readonly Color EmptiedThisStepBorder = new(0.9f, 0.3f, 0.3f, 1f);

        public static void DrawGrid(GameConfig gameConfig, BoardVisualsConfig visualsConfig, List<DayEditorBoardSpawnEntry> boardTimeline, List<DayEditorTicketEntry> ticketSequence, ref Vector2 scrollPos)
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
            var resolvedTickets = ticketSequence.Select(e => e.ToResolved()).ToList();
            var board = new BoardGrid(gameConfig);
            var gridHeight = board.Height * (CellSize + CellSpacing) + LabelHeight;
            var warnings = new List<string>();

            // One cumulative pass -- board state carries over from one step's grid to
            // the next (Day Start, then each ticket's arrival in order), so every
            // step's snapshot is drawn right after its own ApplyForStep call instead
            // of re-replaying the whole prefix from scratch per step.
            scrollPos = EditorGUILayout.BeginScrollView(scrollPos, false, false, GUILayout.Height(gridHeight + 20));
            GUILayout.BeginHorizontal();
            DrawStepColumn(board, resolvedTimeline, null, -1, "Day Start", cellColorA, cellColorB, warnings);
            for (var step = 0; step < resolvedTickets.Count; step++)
            {
                DrawColumnDivider(gridHeight);
                // Same round-robin idealization DayContentGenerator.GenerateCore uses to
                // keep its own simulation's board from growing forever: the ticket that
                // occupied this same slot (TicketSlotCount steps ago) is assumed delivered
                // -- and its items removed from the board -- right before this step's
                // ticket arrives. Not a prediction of real player pacing (see
                // DayContentGenerator.cs), just the same "ideal playthrough" this preview
                // should visualize consistently with what Generate/Regenerate already assume.
                var outgoingIndex = step - GameState.TicketSlotCount;
                var outgoing = outgoingIndex >= 0 ? resolvedTickets[outgoingIndex] : null;
                DrawStepColumn(board, resolvedTimeline, outgoing, step, $"Step {step}", cellColorA, cellColorB, warnings);
            }
            GUILayout.EndHorizontal();
            EditorGUILayout.EndScrollView();

            if (board.PendingSpawnCount > 0)
            {
                EditorGUILayout.HelpBox($"{board.PendingSpawnCount} spawn(s) still waiting for an empty cell by the end of the day (board full).", MessageType.Warning);
            }

            if (warnings.Count > 0)
            {
                EditorGUILayout.HelpBox("Some simulated deliveries couldn't find a matching item on the board:\n\n" + string.Join("\n", warnings), MessageType.Warning);
            }
        }

        // Applies one step onto the running board (first the simulated delivery/removal
        // of the outgoing ticket's items, if any, then the step's own arrivals) and draws
        // the result as a labeled mini-grid. Cells newly filled this step get the green
        // border; cells newly emptied get the red one -- scanning left to right shows
        // both where items land and where they leave.
        private static void DrawStepColumn(BoardGrid board, List<ResolvedBoardSpawnEntry> resolvedTimeline, ResolvedTicketEntry outgoing, long step, string label, Color cellColorA, Color cellColorB, List<string> warnings)
        {
            var before = new HashSet<(int X, int Y)>();
            CollectOccupied(board, before);

            if (outgoing != null)
            {
                DayContentGenerator.RemoveTicketItemsFromBoard(board, outgoing.RequiredItems, outgoing.Modifications, warnings, (int)step);
            }
            DayBoardTimelinePlayer.ApplyForStep(board, resolvedTimeline, step);

            var after = new HashSet<(int X, int Y)>();
            CollectOccupied(board, after);
            var newlyFilled = new HashSet<(int X, int Y)>(after);
            newlyFilled.ExceptWith(before);
            var newlyEmptied = new HashSet<(int X, int Y)>(before);
            newlyEmptied.ExceptWith(after);

            GUILayout.BeginVertical(GUILayout.Width(board.Width * (CellSize + CellSpacing)));
            GUILayout.Label(label, EditorStyles.miniBoldLabel);
            DrawGridCells(board, newlyFilled, newlyEmptied, cellColorA, cellColorB);
            GUILayout.EndVertical();
        }

        // A thin vertical rule with breathing room on both sides, so adjacent step
        // columns read as separate boards instead of touching edge to edge.
        private static void DrawColumnDivider(float height)
        {
            GUILayout.Space(ColumnSpacing);
            var lineRect = GUILayoutUtility.GetRect(DividerWidth, height, GUILayout.ExpandHeight(false));
            EditorGUI.DrawRect(lineRect, DividerColor);
            GUILayout.Space(ColumnSpacing);
        }

        private static void DrawGridCells(BoardGrid board, HashSet<(int X, int Y)> newlyFilled, HashSet<(int X, int Y)> newlyEmptied, Color cellColorA, Color cellColorB)
        {
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
                    var item = board.ItemAt(x, y);
                    EditorGUI.DrawRect(cellRect, (x + y) % 2 == 0 ? cellColorA : cellColorB);
                    if (item != null)
                    {
                        var iconRect = new Rect(cellRect.x + 3, cellRect.y + 3, cellRect.width - 6, cellRect.height - 6);
                        foreach (var layer in item.ResolvedLayers)
                        {
                            DayEditorSpriteGUI.DrawLayeredSprite(iconRect, layer.Sprite, layer.Offset, layer.Scale);
                        }
                    }
                    if (newlyFilled.Contains((x, y)))
                    {
                        DrawCellBorder(cellRect, NewThisStepBorder);
                    }
                    else if (newlyEmptied.Contains((x, y)))
                    {
                        DrawCellBorder(cellRect, EmptiedThisStepBorder);
                    }
                }
            }
        }

        private static void CollectOccupied(BoardGrid board, HashSet<(int X, int Y)> set)
        {
            for (var x = 0; x < board.Width; x++)
            {
                for (var y = 0; y < board.Height; y++)
                {
                    if (board.ItemAt(x, y) != null) set.Add((x, y));
                }
            }
        }

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
