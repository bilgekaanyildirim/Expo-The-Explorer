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
            var first = new DayDefinition(0, 1, new List<ResolvedTicketEntry>(), new List<ResolvedBoardSpawnEntry>());
            var second = new DayDefinition(1, 2, new List<ResolvedTicketEntry>(), new List<ResolvedBoardSpawnEntry>());
            var catalog = new List<DayDefinition> { first, second };

            Assert.AreSame(second, DayCatalogNavigator.GetDayAt(catalog, 1));
        }

        [Test]
        public void GetDayAt_NegativeIndex_ReturnsNull()
        {
            var catalog = new List<DayDefinition> { new(0, 1, new List<ResolvedTicketEntry>(), new List<ResolvedBoardSpawnEntry>()) };

            Assert.IsNull(DayCatalogNavigator.GetDayAt(catalog, -1));
        }

        [Test]
        public void GetDayAt_IndexEqualToCount_ReturnsNull()
        {
            var catalog = new List<DayDefinition> { new(0, 1, new List<ResolvedTicketEntry>(), new List<ResolvedBoardSpawnEntry>()) };

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

    }
}
