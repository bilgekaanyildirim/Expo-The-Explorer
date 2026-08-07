using System.Collections.Generic;
using ExpoTheExplorer.Systems.DaySystem;
using NUnit.Framework;

namespace ExpoTheExplorer.Tests.EditMode
{
    public class DayCatalogNavigatorTests
    {
        [Test]
        public void GetDayAt_ValidIndex_ReturnsThatDay()
        {
            var first = new DayDefinition(0, 1, new List<ResolvedTicketEntry>(), new List<ResolvedBoardSpawnEntry>(), null);
            var second = new DayDefinition(1, 2, new List<ResolvedTicketEntry>(), new List<ResolvedBoardSpawnEntry>(), null);
            var catalog = new List<DayDefinition> { first, second };

            Assert.AreSame(second, DayCatalogNavigator.GetDayAt(catalog, 1));
        }

        [Test]
        public void GetDayAt_NegativeIndex_ReturnsNull()
        {
            var catalog = new List<DayDefinition> { new(0, 1, new List<ResolvedTicketEntry>(), new List<ResolvedBoardSpawnEntry>(), null) };

            Assert.IsNull(DayCatalogNavigator.GetDayAt(catalog, -1));
        }

        [Test]
        public void GetDayAt_IndexEqualToCount_ReturnsNull()
        {
            var catalog = new List<DayDefinition> { new(0, 1, new List<ResolvedTicketEntry>(), new List<ResolvedBoardSpawnEntry>(), null) };

            Assert.IsNull(DayCatalogNavigator.GetDayAt(catalog, 1));
        }

        [Test]
        public void GetDayAt_EmptyCatalog_ReturnsNull()
        {
            Assert.IsNull(DayCatalogNavigator.GetDayAt(new List<DayDefinition>(), 0));
        }

        [Test]
        public void GetDayAt_NullCatalog_ReturnsNull()
        {
            Assert.IsNull(DayCatalogNavigator.GetDayAt(null, 0));
        }

        [Test]
        public void GetEffectiveDay_NotRetrying_ReturnsBaseDay()
        {
            var variant = new DayDefinition(0, 1, new List<ResolvedTicketEntry>(), new List<ResolvedBoardSpawnEntry>(), null);
            var baseDay = new DayDefinition(0, 5, new List<ResolvedTicketEntry>(), new List<ResolvedBoardSpawnEntry>(), variant);

            Assert.AreSame(baseDay, DayCatalogNavigator.GetEffectiveDay(baseDay, isRetryAttempt: false));
        }

        [Test]
        public void GetEffectiveDay_RetryingWithVariant_ReturnsVariant()
        {
            var variant = new DayDefinition(0, 1, new List<ResolvedTicketEntry>(), new List<ResolvedBoardSpawnEntry>(), null);
            var baseDay = new DayDefinition(0, 5, new List<ResolvedTicketEntry>(), new List<ResolvedBoardSpawnEntry>(), variant);

            Assert.AreSame(variant, DayCatalogNavigator.GetEffectiveDay(baseDay, isRetryAttempt: true));
        }

        [Test]
        public void GetEffectiveDay_RetryingWithoutVariant_FallsBackToBaseDay()
        {
            var baseDay = new DayDefinition(0, 5, new List<ResolvedTicketEntry>(), new List<ResolvedBoardSpawnEntry>(), null);

            Assert.AreSame(baseDay, DayCatalogNavigator.GetEffectiveDay(baseDay, isRetryAttempt: true));
        }
    }
}
