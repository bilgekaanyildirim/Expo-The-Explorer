using System;
using System.IO;
using System.Linq;
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

        [Test]
        public void Renumber_AfterADeleteInTheMiddle_ClosesTheGap()
        {
            DayFileIO.Save(tempFolder, CreateDayJson(0));
            DayFileIO.Save(tempFolder, CreateDayJson(1, ticketsRequiredForDay: 11));
            DayFileIO.Save(tempFolder, CreateDayJson(2, ticketsRequiredForDay: 22));
            DayFileIO.Delete(tempFolder, 1);

            var moved = DayFileIO.Renumber(tempFolder);

            var loaded = DayFileIO.LoadAll(tempFolder);
            Assert.AreEqual(new[] { 0, 1 }, loaded.Select(f => f.Json.runtime.dayIndex).ToArray());
            CollectionAssert.AreEqual(new[] { (2, 1) }, moved);

            // The Day that moved is the one that WAS Day 2, not a renamed shell of the deleted one.
            Assert.AreEqual(22, loaded[1].Json.runtime.ticketsRequiredForDay);

            // Name and content agree: the file is day_01.json and says dayIndex 1, and nothing is
            // left behind at the old path.
            Assert.IsTrue(File.Exists(DayFileIO.GetFilePath(tempFolder, 1)));
            Assert.IsFalse(File.Exists(DayFileIO.GetFilePath(tempFolder, 2)));
        }

        [Test]
        public void Renumber_ManyGaps_CompactsEveryDayToItsPosition()
        {
            foreach (var dayIndex in new[] { 0, 3, 4, 9, 20 })
            {
                DayFileIO.Save(tempFolder, CreateDayJson(dayIndex, ticketsRequiredForDay: dayIndex + 100));
            }

            DayFileIO.Renumber(tempFolder);

            var loaded = DayFileIO.LoadAll(tempFolder);
            Assert.AreEqual(new[] { 0, 1, 2, 3, 4 }, loaded.Select(f => f.Json.runtime.dayIndex).ToArray());

            // Order is preserved, and no Day was overwritten by another on the way down -- the
            // ticketsRequiredForDay values still identify which Day each one used to be.
            Assert.AreEqual(new[] { 100, 103, 104, 109, 120 },
                loaded.Select(f => f.Json.runtime.ticketsRequiredForDay).ToArray());
        }

        [Test]
        public void Renumber_AlreadyContiguous_ChangesNothing()
        {
            DayFileIO.Save(tempFolder, CreateDayJson(0));
            DayFileIO.Save(tempFolder, CreateDayJson(1));

            Assert.IsEmpty(DayFileIO.Renumber(tempFolder));
            Assert.AreEqual(new[] { 0, 1 }, DayFileIO.LoadAll(tempFolder).Select(f => f.Json.runtime.dayIndex).ToArray());
        }

        [Test]
        public void Renumber_EmptyFolder_DoesNotThrow()
        {
            Assert.IsEmpty(DayFileIO.Renumber(tempFolder));
        }

        // ticketsRequiredForDay is used as a per-Day fingerprint throughout these: it is what makes
        // the difference between "the Days ended up in the right order" and "a Day was overwritten
        // by another on the way past it" visible to an assertion.
        private void SaveDays(params int[] dayIndices)
        {
            foreach (var dayIndex in dayIndices)
            {
                DayFileIO.Save(tempFolder, CreateDayJson(dayIndex, ticketsRequiredForDay: 100 + dayIndex));
            }
        }

        private int[] Fingerprints() =>
            DayFileIO.LoadAll(tempFolder).Select(f => f.Json.runtime.ticketsRequiredForDay).ToArray();

        [Test]
        public void Move_ADayDown_LandsAtThatPositionAndTheDaysItPassedShiftUp()
        {
            SaveDays(0, 1, 2, 3);

            DayFileIO.Move(tempFolder, fromDayIndex: 0, toPosition: 2);

            // Was 0,1,2,3 -> the old Day 0 now sits third, and 1 and 2 each moved up one.
            Assert.AreEqual(new[] { 101, 102, 100, 103 }, Fingerprints());
            Assert.AreEqual(new[] { 0, 1, 2, 3 },
                DayFileIO.LoadAll(tempFolder).Select(f => f.Json.runtime.dayIndex).ToArray());
        }

        [Test]
        public void Move_ADayUp_LandsAtThatPositionAndTheDaysItPassedShiftDown()
        {
            SaveDays(0, 1, 2, 3);

            DayFileIO.Move(tempFolder, fromDayIndex: 3, toPosition: 0);

            Assert.AreEqual(new[] { 103, 100, 101, 102 }, Fingerprints());
        }

        [Test]
        public void Move_OverAGappedFolder_ReordersAndClosesTheGapsInOnePass()
        {
            SaveDays(0, 5, 9);

            DayFileIO.Move(tempFolder, fromDayIndex: 9, toPosition: 0);

            // The move and the compaction are the same rewrite, so the holes go with it.
            Assert.AreEqual(new[] { 109, 100, 105 }, Fingerprints());
            Assert.AreEqual(new[] { 0, 1, 2 },
                DayFileIO.LoadAll(tempFolder).Select(f => f.Json.runtime.dayIndex).ToArray());
            Assert.IsFalse(File.Exists(DayFileIO.GetFilePath(tempFolder, 5)));
            Assert.IsFalse(File.Exists(DayFileIO.GetFilePath(tempFolder, 9)));
        }

        [Test]
        public void Move_ToTheSamePosition_ChangesNothing()
        {
            SaveDays(0, 1, 2);

            Assert.IsEmpty(DayFileIO.Move(tempFolder, fromDayIndex: 1, toPosition: 1));
            Assert.AreEqual(new[] { 100, 101, 102 }, Fingerprints());
        }

        [Test]
        public void Move_APositionPastTheEnd_ClampsToLastRatherThanLosingTheDay()
        {
            SaveDays(0, 1, 2);

            DayFileIO.Move(tempFolder, fromDayIndex: 0, toPosition: 99);

            Assert.AreEqual(new[] { 101, 102, 100 }, Fingerprints());
        }

        [Test]
        public void Move_ADayIndexThatHasNoFile_LeavesEveryDayWhereItWas()
        {
            SaveDays(0, 1, 2);

            Assert.IsEmpty(DayFileIO.Move(tempFolder, fromDayIndex: 42, toPosition: 0));
            Assert.AreEqual(new[] { 100, 101, 102 }, Fingerprints());
        }
    }
}
