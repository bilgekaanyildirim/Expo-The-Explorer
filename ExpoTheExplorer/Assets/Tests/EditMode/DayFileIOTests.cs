using System;
using System.IO;
using ExpoTheExplorer.Editor;
using ExpoTheExplorer.Systems.DaySystem;
using NUnit.Framework;

namespace ExpoTheExplorer.Tests.EditMode
{
    public class DayFileIOTests
    {
        private string tempFolder;

        [SetUp]
        public void SetUp()
        {
            tempFolder = Path.Combine(Path.GetTempPath(), "DayFileIOTests_" + Guid.NewGuid());
        }

        [TearDown]
        public void TearDown()
        {
            if (Directory.Exists(tempFolder))
            {
                Directory.Delete(tempFolder, recursive: true);
            }
        }

        private static DayJson CreateDayJson(int dayIndex, int ticketsRequiredForDay = 1)
        {
            return new DayJson
            {
                runtime = new DayRuntimeJson
                {
                    dayIndex = dayIndex,
                    ticketsRequiredForDay = ticketsRequiredForDay,
                    ticketSequence = Array.Empty<TicketEntryJson>(),
                    boardTimeline = Array.Empty<BoardSpawnEntryJson>(),
                },
                editorMeta = new DayEditorMetaJson(),
            };
        }

        [Test]
        public void Save_ThenLoadAll_RoundTripsCorrectly()
        {
            DayFileIO.Save(tempFolder, CreateDayJson(3, 7));

            var loaded = DayFileIO.LoadAll(tempFolder);

            Assert.AreEqual(1, loaded.Count);
            Assert.AreEqual(3, loaded[0].Json.runtime.dayIndex);
            Assert.AreEqual(7, loaded[0].Json.runtime.ticketsRequiredForDay);
        }

        [Test]
        public void Save_TwiceWithChangedDayIndex_DoesNotRemoveThePreviousFile()
        {
            DayFileIO.Save(tempFolder, CreateDayJson(1));
            DayFileIO.Save(tempFolder, CreateDayJson(2));

            Assert.IsTrue(File.Exists(DayFileIO.GetFilePath(tempFolder, 1)),
                "DayFileIO.Save isn't responsible for cleaning up a renamed Day's old file -- that's DayEditorWindow.OnSaveRequested's job.");
            Assert.IsTrue(File.Exists(DayFileIO.GetFilePath(tempFolder, 2)));
        }

        [Test]
        public void Delete_RemovesFile()
        {
            DayFileIO.Save(tempFolder, CreateDayJson(5));

            DayFileIO.Delete(tempFolder, 5);

            Assert.IsEmpty(DayFileIO.LoadAll(tempFolder));
        }

        [Test]
        public void LoadAll_EmptyOrMissingFolder_ReturnsEmptyList()
        {
            Assert.IsEmpty(DayFileIO.LoadAll(tempFolder));
        }
    }
}
