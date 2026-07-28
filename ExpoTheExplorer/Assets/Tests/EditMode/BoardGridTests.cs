using System.Collections.Generic;
using ExpoTheExplorer.Core;
using ExpoTheExplorer.Data;
using NUnit.Framework;
using UnityEngine;

namespace ExpoTheExplorer.Tests.EditMode
{
    public class BoardGridTests
    {
        private GameConfig config;
        private readonly List<FoodItemConfig> spawnedConfigs = new();

        [SetUp]
        public void SetUp()
        {
            config = ScriptableObject.CreateInstance<GameConfig>();
        }

        [TearDown]
        public void TearDown()
        {
            Object.DestroyImmediate(config);
            foreach (var foodConfig in spawnedConfigs)
            {
                Object.DestroyImmediate(foodConfig);
            }
            spawnedConfigs.Clear();
        }

        private BoardItem CreateItem()
        {
            var foodConfig = ScriptableObject.CreateInstance<FoodItemConfig>();
            spawnedConfigs.Add(foodConfig);
            return new BoardItem(foodConfig, new List<Modification>());
        }

        [Test]
        public void NewBoardGrid_SizeMatchesConfig_AndStartsEmpty()
        {
            var grid = new BoardGrid(config);

            Assert.AreEqual(config.BoardWidth, grid.Width);
            Assert.AreEqual(config.BoardHeight, grid.Height);
            Assert.AreEqual(0, grid.OccupiedCellCount);
            Assert.IsFalse(grid.IsFull);
        }

        [Test]
        public void TryPlaceItem_OnEmptyCell_Succeeds()
        {
            var grid = new BoardGrid(config);
            var item = CreateItem();

            var placed = grid.TryPlaceItem(item, 0, 0);

            Assert.IsTrue(placed);
            Assert.AreSame(item, grid.ItemAt(0, 0));
            Assert.IsFalse(grid.IsCellEmpty(0, 0));
            Assert.AreEqual(1, grid.OccupiedCellCount);
        }

        [Test]
        public void TryPlaceItem_OnOccupiedCell_Fails()
        {
            var grid = new BoardGrid(config);
            var first = CreateItem();
            var second = CreateItem();
            grid.TryPlaceItem(first, 0, 0);

            var placed = grid.TryPlaceItem(second, 0, 0);

            Assert.IsFalse(placed);
            Assert.AreSame(first, grid.ItemAt(0, 0));
        }

        [Test]
        public void RequestSpawn_WhenGridIsFull_QueuesInsteadOfPlacing()
        {
            var grid = new BoardGrid(config);
            FillGrid(grid);

            var overflowItem = CreateItem();
            var placed = grid.RequestSpawn(overflowItem);

            Assert.IsFalse(placed);
            Assert.AreEqual(1, grid.PendingSpawnCount);
            Assert.IsTrue(grid.IsFull);
        }

        [Test]
        public void RemoveItem_DrainsQueuedSpawnIntoFreedCell()
        {
            var grid = new BoardGrid(config);
            FillGrid(grid);
            var queuedItem = CreateItem();
            grid.RequestSpawn(queuedItem);

            var removed = grid.RemoveItem(0, 0);

            Assert.IsNotNull(removed);
            Assert.AreSame(queuedItem, grid.ItemAt(0, 0));
            Assert.AreEqual(0, grid.PendingSpawnCount);
            Assert.IsTrue(grid.IsFull);
        }

        [Test]
        public void TryGetFirstEmptyCell_ReturnsFirstAvailableCellInScanOrder()
        {
            var grid = new BoardGrid(config);
            grid.TryPlaceItem(CreateItem(), 0, 0);
            grid.TryPlaceItem(CreateItem(), 1, 0);

            var found = grid.TryGetFirstEmptyCell(out var x, out var y);

            Assert.IsTrue(found);
            Assert.AreEqual(2, x);
            Assert.AreEqual(0, y);

            FillGrid(grid);
            Assert.IsFalse(grid.TryGetFirstEmptyCell(out _, out _));
        }

        private void FillGrid(BoardGrid grid)
        {
            for (var y = 0; y < grid.Height; y++)
            {
                for (var x = 0; x < grid.Width; x++)
                {
                    grid.TryPlaceItem(CreateItem(), x, y);
                }
            }
        }
    }
}
