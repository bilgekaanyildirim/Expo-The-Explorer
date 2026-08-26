using System.Collections.Generic;
using ExpoTheExplorer.Systems.Tutorial;
using NUnit.Framework;

namespace ExpoTheExplorer.Tests.EditMode
{
    // No GameConfig, no ScriptableObject, no scene -- which is the point of the Tutorial
    // assembly referencing nothing. The rules that decide whether the player can touch the
    // board are reachable here directly.
    public class TutorialDirectorTests
    {
        private const int StepOneX = 1;
        private const int StepOneY = 2;
        private const int StepOneTray = 0;

        private const int StepTwoX = 2;
        private const int StepTwoY = 0;
        private const int StepTwoTray = 2;

        // Mirrors day_00: a plain first move, then a modification-teaching second one.
        private static TutorialDirector CreateTwoStepDirector() => new(new List<TutorialStep>
        {
            new(TutorialStepKind.ForcedMove, StepOneX, StepOneY, StepOneTray, string.Empty, false),
            new(TutorialStepKind.ForcedMove, StepTwoX, StepTwoY, StepTwoTray, "Modifications matter.", true),
        });

        // Mirrors day_00 in full: the two moves, then the powerup panel.
        private static TutorialDirector CreateDirectorEndingInIntro() => new(new List<TutorialStep>
        {
            new(TutorialStepKind.ForcedMove, StepOneX, StepOneY, StepOneTray, string.Empty, false),
            new(TutorialStepKind.ForcedMove, StepTwoX, StepTwoY, StepTwoTray, "Modifications matter.", true),
            new(TutorialStepKind.PowerupIntro, 0, 0, 0, string.Empty, false),
        });

        private static TutorialDirector CreateIntroOnlyDirector() => new(new List<TutorialStep>
        {
            new(TutorialStepKind.PowerupIntro, 0, 0, 0, string.Empty, false),
        });

        [Test]
        public void WhileAStepRuns_OnlyItsCellCanBePickedUp()
        {
            var director = CreateTwoStepDirector();

            Assert.IsTrue(director.IsPickupAllowed(StepOneX, StepOneY), "The current step's cell must be pickable.");
            Assert.IsFalse(director.IsPickupAllowed(StepOneX + 1, StepOneY), "A neighbouring cell must be refused.");
            Assert.IsFalse(director.IsPickupAllowed(StepTwoX, StepTwoY), "A LATER step's cell must be refused until its turn.");
        }

        [Test]
        public void WhileAStepRuns_OnlyItsTrayAcceptsADrop()
        {
            var director = CreateTwoStepDirector();

            Assert.IsTrue(director.IsTrayDropAllowed(StepOneTray));
            Assert.IsFalse(director.IsTrayDropAllowed(1));
            Assert.IsFalse(director.IsTrayDropAllowed(StepTwoTray), "The second step's tray must not accept during the first.");
        }

        // The whole point of the list: finishing one step hands the gates to the next rather
        // than opening them.
        [Test]
        public void FinishingAStep_MovesTheGatesToTheNextStep()
        {
            var director = CreateTwoStepDirector();

            director.NotifyTrayAccepted(StepOneTray);

            Assert.IsTrue(director.IsActive, "The tutorial must still be running after step one.");
            Assert.AreEqual(StepTwoX, director.Current.SourceX);
            Assert.AreEqual(StepTwoY, director.Current.SourceY);
            Assert.IsTrue(director.IsPickupAllowed(StepTwoX, StepTwoY));
            Assert.IsFalse(director.IsPickupAllowed(StepOneX, StepOneY), "The finished step's cell must lock again.");
            Assert.IsTrue(director.IsTrayDropAllowed(StepTwoTray));
            Assert.IsFalse(director.IsTrayDropAllowed(StepOneTray), "The finished step's tray must stop accepting.");
        }

        // The softlock this gate exists to prevent: the pickup gate permits exactly one
        // cell, so an item parked on any other cell could never be picked up again.
        [Test]
        public void WhileAStepRuns_AnItemCannotBeParkedElsewhereOnTheBoard()
        {
            var director = CreateTwoStepDirector();

            Assert.IsFalse(director.IsBoardRelocationAllowed(), "A board-to-board move must be refused during a step.");

            director.NotifyTrayAccepted(StepOneTray);
            Assert.IsFalse(director.IsBoardRelocationAllowed(), "Still refused during the second step.");
        }

        // The other direction, and the one that would break the whole game if this gate were
        // written backwards: outside a tutorial the board must behave normally.
        [Test]
        public void OnceEveryStepIsDone_ItemsCanBeMovedAroundTheBoardAgain()
        {
            var director = CreateTwoStepDirector();

            director.NotifyTrayAccepted(StepOneTray);
            director.NotifyTrayAccepted(StepTwoTray);

            Assert.IsTrue(director.IsBoardRelocationAllowed());
        }

        [Test]
        public void Abort_AlsoReleasesTheBoard()
        {
            var director = CreateTwoStepDirector();

            director.Abort();

            Assert.IsTrue(director.IsBoardRelocationAllowed(), "An aborted tutorial must not leave the board locked.");
        }

        [Test]
        public void TheLastStep_CarriesItsAuthoredHints()
        {
            var director = CreateTwoStepDirector();

            director.NotifyTrayAccepted(StepOneTray);

            Assert.IsTrue(director.Current.HighlightModification, "Step two is the one that teaches modifications.");
            Assert.AreEqual("Modifications matter.", director.Current.Message);
        }

        // The property both call sites depend on: a finished tutorial permits everything, so
        // it is indistinguishable from a Day that never had one.
        [Test]
        public void OnceEveryStepIsDone_EveryCellAndEveryTrayIsAllowedAgain()
        {
            var director = CreateTwoStepDirector();

            director.NotifyTrayAccepted(StepOneTray);
            director.NotifyTrayAccepted(StepTwoTray);

            Assert.IsFalse(director.IsActive);
            Assert.IsNull(director.Current);
            Assert.IsTrue(director.IsPickupAllowed(4, 3));
            Assert.IsTrue(director.IsTrayDropAllowed(1));
        }

        [Test]
        public void StepChanged_FiresOncePerBoundaryAndNotAfterTheEnd()
        {
            var director = CreateTwoStepDirector();
            var changes = 0;
            director.StepChanged += () => changes++;

            director.NotifyTrayAccepted(StepOneTray);
            director.NotifyTrayAccepted(StepTwoTray);
            director.NotifyTrayAccepted(StepTwoTray);

            Assert.AreEqual(2, changes, "Two boundaries, and a drop after the end must not fire a third.");
        }

        // The guard that keeps a step honest if a future caller ever forgets the drop gate:
        // advancing has to be the AUTHORED move, not any move.
        [Test]
        public void AcceptanceOnAnotherTray_DoesNotAdvance()
        {
            var director = CreateTwoStepDirector();
            var changes = 0;
            director.StepChanged += () => changes++;

            director.NotifyTrayAccepted(StepOneTray + 1);

            Assert.AreEqual(0, changes);
            Assert.AreEqual(StepOneX, director.Current.SourceX, "Still on step one.");
            Assert.IsFalse(director.IsPickupAllowed(4, 3), "The board must still be locked down.");
        }

        // The escape hatch for a step whose item is gone: everything opens at once, so the
        // player is never left facing a board that refuses every touch.
        [Test]
        public void Abort_EndsTheTutorialAndOpensEveryGate()
        {
            var director = CreateTwoStepDirector();
            var changes = 0;
            director.StepChanged += () => changes++;

            director.Abort();

            Assert.IsFalse(director.IsActive);
            Assert.IsNull(director.Current);
            Assert.AreEqual(1, changes, "Aborting is a step boundary and must be announced.");
            Assert.IsTrue(director.IsPickupAllowed(StepOneX, StepOneY));
            Assert.IsTrue(director.IsTrayDropAllowed(1));
        }

        // An intro step is read, not played, so nothing on the board is reachable behind it
        // -- otherwise the panel would be a way to lose a life while reading.
        [Test]
        public void DuringAnIntroStep_TheWholeBoardIsRefused()
        {
            var director = CreateIntroOnlyDirector();

            Assert.IsTrue(director.IsHoldingForReading);
            Assert.IsFalse(director.IsPickupAllowed(0, 0), "Not even the step's own unused cell may be picked up.");
            Assert.IsFalse(director.IsPickupAllowed(2, 1));
            Assert.IsFalse(director.IsTrayDropAllowed(0), "Not even tray 0, which its unused field names.");
            Assert.IsFalse(director.IsBoardRelocationAllowed());
        }

        // The two completions are deliberately separate: a stray tray drop must not skip a
        // panel the player has not read, and dismissing a panel must not stand in for a move.
        [Test]
        public void AnIntroStep_IsNotCompletedByATrayDrop()
        {
            var director = CreateIntroOnlyDirector();

            director.NotifyTrayAccepted(0);

            Assert.IsTrue(director.IsActive, "A tray drop must not advance a panel.");
            Assert.IsTrue(director.IsHoldingForReading);
        }

        [Test]
        public void AForcedMove_IsNotCompletedByDismissingAPanel()
        {
            var director = CreateTwoStepDirector();

            director.NotifyReadingFinished();

            Assert.AreEqual(StepOneX, director.Current.SourceX, "Still on the first move.");
        }

        [Test]
        public void DismissingThePanel_EndsTheTutorialAndReleasesEverything()
        {
            var director = CreateDirectorEndingInIntro();
            director.NotifyTrayAccepted(StepOneTray);
            director.NotifyTrayAccepted(StepTwoTray);

            Assert.IsTrue(director.IsHoldingForReading, "The panel follows the second move.");

            director.NotifyReadingFinished();

            Assert.IsFalse(director.IsActive);
            Assert.IsFalse(director.IsHoldingForReading, "The clock must start again.");
            Assert.IsTrue(director.IsPickupAllowed(4, 3));
            Assert.IsTrue(director.IsBoardRelocationAllowed());
        }

        // The clock only stops for a step that asks the player to read; a forced move is
        // played against a running day like any other moment.
        [Test]
        public void AForcedMove_DoesNotStopTheClock()
        {
            var director = CreateTwoStepDirector();

            Assert.IsFalse(director.IsHoldingForReading);
        }

        [Test]
        public void Abort_AfterTheTutorialIsOver_IsSilent()
        {
            var director = CreateTwoStepDirector();
            director.NotifyTrayAccepted(StepOneTray);
            director.NotifyTrayAccepted(StepTwoTray);

            var changes = 0;
            director.StepChanged += () => changes++;
            director.Abort();

            Assert.AreEqual(0, changes, "There is nothing left to abort, so nothing is announced.");
        }
    }
}
