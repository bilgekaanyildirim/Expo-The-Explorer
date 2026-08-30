using ExpoTheExplorer.Data;
using ExpoTheExplorer.Simulation;
using ExpoTheExplorer.Systems.DaySystem;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace ExpoTheExplorer.Tests.EditMode
{
    public class SimulationRunnerTests
    {
        [Test]
        public void RunOne_SameSeedAndDay_ReturnsSameResult()
        {
            var day = LoadDay("day_01");
            var options = CreateOptions(seed: 1234);

            var first = SimulationRunner.RunOne(day, options);
            var second = SimulationRunner.RunOne(day, options.CloneForSeed(1234));

            Assert.AreEqual(first.EndReason, second.EndReason);
            Assert.AreEqual(first.TicketsDelivered, second.TicketsDelivered);
            Assert.AreEqual(first.LivesLost, second.LivesLost);
            Assert.AreEqual(first.Timeouts, second.Timeouts);
            Assert.AreEqual(first.Revenue, second.Revenue);
            Assert.AreEqual(first.StarCount, second.StarCount);
            Assert.AreEqual(first.PeakBoardOccupancy, second.PeakBoardOccupancy);
            Assert.AreEqual(first.PeakPendingSpawns, second.PeakPendingSpawns);
            Assert.AreEqual(first.ZeroCompletableRounds, second.ZeroCompletableRounds);
            Assert.AreEqual(first.DurationSeconds, second.DurationSeconds, 0.0001f);
            Assert.AreEqual(first.StarScore, second.StarScore, 0.0001f);
        }

        [Test]
        public void RunOne_AuthoredDay_ProducesBoundedResult()
        {
            var day = LoadDay("day_01");
            var result = SimulationRunner.RunOne(day, CreateOptions(seed: 1000));

            Assert.AreNotEqual(SimulationEndReason.InvalidSetup, result.EndReason, result.Message);
            Assert.LessOrEqual(result.TicketsDelivered, day.TicketsRequiredForDay);
            Assert.GreaterOrEqual(result.LivesLost, 0);
            Assert.LessOrEqual(result.LivesLost, ExpoTheExplorer.Core.GameState.DefaultStartingLives);
            Assert.Greater(result.BoardCellCount, 0);
            Assert.LessOrEqual(result.PeakBoardOccupancy, result.BoardCellCount);
        }

        private static DayDefinition LoadDay(string fileName)
        {
            var catalog = AssetDatabase.LoadAssetAtPath<FoodCatalog>("Assets/Data/FoodData/FoodCatalog.asset");
            var text = AssetDatabase.LoadAssetAtPath<TextAsset>($"Assets/Resources/Days/{fileName}.json");
            Assert.NotNull(catalog);
            Assert.NotNull(text);

            var parsed = DayCatalogParser.ParseAll(new[] { new DayJsonFile(fileName, text.text) }, catalog);
            Assert.AreEqual(1, parsed.Count);
            return parsed[0];
        }

        private static SimulationOptions CreateOptions(int seed)
        {
            return new SimulationOptions
            {
                Seed = seed,
                StepSeconds = 0.1f,
                ActionDelaySeconds = 0.25f,
                MaxVirtualSeconds = 1000f,
                GameConfig = AssetDatabase.LoadAssetAtPath<GameConfig>("Assets/Data/GameConfig.asset"),
                TicketGenerationConfig = AssetDatabase.LoadAssetAtPath<TicketGenerationConfig>("Assets/Data/TicketGenerationConfig.asset"),
                EconomyConfig = AssetDatabase.LoadAssetAtPath<EconomyConfig>("Assets/Data/EconomyConfig.asset"),
                StarScoreConfig = AssetDatabase.LoadAssetAtPath<StarScoreConfig>("Assets/Data/StarScoreConfig.asset"),
            };
        }
    }
}
