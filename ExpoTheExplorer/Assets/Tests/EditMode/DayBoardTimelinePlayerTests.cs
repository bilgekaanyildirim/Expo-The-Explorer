using System.Collections.Generic;
using ExpoTheExplorer.Core;
using ExpoTheExplorer.Data;
using ExpoTheExplorer.Systems.DaySystem;
using NUnit.Framework;
using UnityEngine;

namespace ExpoTheExplorer.Tests.EditMode
{
    public class DayBoardTimelinePlayerTests
    {
        private readonly List<Object> spawned = new();

        [TearDown]
        public void TearDown()
        {
            foreach (var o in spawned) Object.DestroyImmediate(o);
            spawned.Clear();
        }

        private FoodItemConfig CreateFood()
        {
            var food = ScriptableObject.CreateInstance<FoodItemConfig>();
            spawned.Add(food);
            return food;
        }

        private BoardGrid CreateGrid()
        {
            var config = ScriptableObject.CreateInstance<GameConfig>();
            spawned.Add(config);
            return new BoardGrid(config);
        }

        private ResolvedBoardSpawnEntry CreateEntry(int triggerStepIndex, FoodItemConfig item, bool useExactCell = false, int x = 0, int y = 0)
        {
            return new ResolvedBoardSpawnEntry(triggerStepIndex, item, new List<Modification>(), useExactCell, x, y);
        }

        [Test]
        public void ApplyForStep_MatchingExactCellEntry_PlacesAtThatCell()
        {
            var board = CreateGrid();
            var food = CreateFood();
            var entries = new List<ResolvedBoardSpawnEntry> { CreateEntry(0, food, useExactCell: true, x: 2, y: 3) };

            DayBoardTimelinePlayer.ApplyForStep(board, entries, 0);

            Assert.AreSame(food, board.ItemAt(2, 3)?.Config);
            Assert.AreEqual(1, board.OccupiedCellCount);
        }

        [Test]
        public void ApplyForStep_MatchingEntryWithoutExactCell_PlacesInFirstEmptyCell()
        {
            var board = CreateGrid();
            board.TryGetFirstEmptyCell(out var expectedX, out var expectedY);
            var food = CreateFood();
            var entries = new List<ResolvedBoardSpawnEntry> { CreateEntry(0, food, useExactCell: false) };

            DayBoardTimelinePlayer.ApplyForStep(board, entries, 0);

            Assert.AreSame(food, board.ItemAt(expectedX, expectedY)?.Config);
            Assert.AreEqual(1, board.OccupiedCellCount);
        }

        [Test]
        public void ApplyForStep_ExactCellOccupied_FallsBackToRequestSpawn()
        {
            var board = CreateGrid();
            var occupant = CreateFood();
            board.TryPlaceItem(new BoardItem(occupant, new List<Modification>()), 0, 0);

            var food = CreateFood();
            var entries = new List<ResolvedBoardSpawnEntry> { CreateEntry(0, food, useExactCell: true, x: 0, y: 0) };

            DayBoardTimelinePlayer.ApplyForStep(board, entries, 0);

            Assert.AreSame(occupant, board.ItemAt(0, 0)?.Config);
            Assert.AreEqual(2, board.OccupiedCellCount);
        }

        [Test]
        public void ApplyForStep_NonMatchingStepIndex_DoesNothing()
        {
            var board = CreateGrid();
            var food = CreateFood();
            var entries = new List<ResolvedBoardSpawnEntry> { CreateEntry(5, food) };

            DayBoardTimelinePlayer.ApplyForStep(board, entries, 0);

            Assert.AreEqual(0, board.OccupiedCellCount);
        }

        [Test]
        public void ApplyForStep_MultipleMatchingEntries_AppliesAll()
        {
            var board = CreateGrid();
            var entries = new List<ResolvedBoardSpawnEntry>
            {
                CreateEntry(0, CreateFood(), useExactCell: true, x: 0, y: 0),
                CreateEntry(0, CreateFood(), useExactCell: true, x: 1, y: 0),
                CreateEntry(1, CreateFood(), useExactCell: true, x: 2, y: 0),
            };

            DayBoardTimelinePlayer.ApplyForStep(board, entries, 0);

            Assert.AreEqual(2, board.OccupiedCellCount);
            Assert.IsNotNull(board.ItemAt(0, 0));
            Assert.IsNotNull(board.ItemAt(1, 0));
            Assert.IsNull(board.ItemAt(2, 0));
        }

        // --- HasEntriesForStep -------------------------------------------------------
        //
        // GameManager asks this about Day Start (-1) to decide whether the opening ticket
        // fill distributes at all, so a wrong "yes" is an opening with the distribution
        // suppressed and nothing on the board. It must therefore answer for exactly the
        // entries ApplyForStep would place -- which is what these cases pin down.

        [Test]
        public void HasEntriesForStep_MatchingEntry_IsTrue()
        {
            var entries = new List<ResolvedBoardSpawnEntry> { CreateEntry(-1, CreateFood()) };

            Assert.IsTrue(DayBoardTimelinePlayer.HasEntriesForStep(entries, -1));
        }

        [Test]
        public void HasEntriesForStep_OnlyEntriesForOtherSteps_IsFalse()
        {
            var entries = new List<ResolvedBoardSpawnEntry> { CreateEntry(0, CreateFood()), CreateEntry(1, CreateFood()) };

            Assert.IsFalse(DayBoardTimelinePlayer.HasEntriesForStep(entries, -1));
        }

        // A row the designer added but has not filled in yet places nothing, so it must not
        // report as an authored board either -- that is the exact combination that would
        // silence the opening distribution and leave the player an empty board.
        [Test]
        public void HasEntriesForStep_EntryWithNoItemYet_IsFalse()
        {
            var entries = new List<ResolvedBoardSpawnEntry> { CreateEntry(-1, null) };

            Assert.IsFalse(DayBoardTimelinePlayer.HasEntriesForStep(entries, -1));
            Assert.AreEqual(0, ApplyToFreshBoard(entries, -1), "ApplyForStep must agree: an unfilled row places nothing.");
        }

        [Test]
        public void HasEntriesForStep_EmptyOrNullTimeline_IsFalse()
        {
            Assert.IsFalse(DayBoardTimelinePlayer.HasEntriesForStep(new List<ResolvedBoardSpawnEntry>(), -1));
            Assert.IsFalse(DayBoardTimelinePlayer.HasEntriesForStep(null, -1));
        }

        private int ApplyToFreshBoard(List<ResolvedBoardSpawnEntry> entries, long stepIndex)
        {
            var board = CreateGrid();
            DayBoardTimelinePlayer.ApplyForStep(board, entries, stepIndex);
            return board.OccupiedCellCount;
        }
    }
}
