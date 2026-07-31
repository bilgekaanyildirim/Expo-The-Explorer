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
        public void RequestSpawn_WithoutRandom_PlacesAtFirstEmptyCell()
        {
            var grid = new BoardGrid(config);

            grid.RequestSpawn(CreateItem());

            Assert.IsNotNull(grid.ItemAt(0, 0));
        }

        [Test]
        public void RequestSpawn_WithRandomSource_PlacesAtVariousEmptyCells_NotAlwaysTheFirst()
        {
            var landedPositions = new HashSet<(int X, int Y)>();
            for (var seed = 0; seed < 30; seed++)
            {
                var grid = new BoardGrid(config);
                grid.RequestSpawn(CreateItem(), new System.Random(seed));

                for (var y = 0; y < grid.Height; y++)
                {
                    for (var x = 0; x < grid.Width; x++)
                    {
                        if (grid.ItemAt(x, y) != null) landedPositions.Add((x, y));
                    }
                }
            }

            Assert.Greater(landedPositions.Count, 1, "Expected spawns across many seeds to land on more than one cell.");
        }

        [Test]
        public void RequestSpawn_WithRandomSource_WhenGridIsFull_QueuesInsteadOfPlacing()
        {
            var grid = new BoardGrid(config);
            FillGrid(grid);

            var placed = grid.RequestSpawn(CreateItem(), new System.Random(1));

            Assert.IsFalse(placed);
            Assert.AreEqual(1, grid.PendingSpawnCount);
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

        [Test]
        public void TryPlaceItem_OnEmptyCell_PublishesCellChanged()
        {
            var grid = new BoardGrid(config);
            var item = CreateItem();
            (int X, int Y)? published = null;
            grid.CellChanged.Subscribe(coords => published = coords);

            grid.TryPlaceItem(item, 2, 3);

            Assert.AreEqual((2, 3), published);
        }

        [Test]
        public void RemoveItem_PublishesCellChanged()
        {
            var grid = new BoardGrid(config);
            grid.TryPlaceItem(CreateItem(), 1, 1);
            (int X, int Y)? published = null;
            grid.CellChanged.Subscribe(coords => published = coords);

            grid.RemoveItem(1, 1);

            Assert.AreEqual((1, 1), published);
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
