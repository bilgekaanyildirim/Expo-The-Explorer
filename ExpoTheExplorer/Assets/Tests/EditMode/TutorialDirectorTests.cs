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

        // The fraction of its own limit a ticket has to fall to before the deferred lesson
        // arms. 1/3 is what PowerupConfig ships, and it is where the timer bar turns red.
        private const float TriggerRatio = 1f / 3f;

        // Mirrors day_00: a plain first move, then a modification-teaching second one.
        private static TutorialDirector CreateTwoStepDirector() => new(new List<TutorialStep>
        {
            TutorialStep.ForcedMove(StepOneX, StepOneY, StepOneTray, string.Empty, false),
            TutorialStep.ForcedMove(StepTwoX, StepTwoY, StepTwoTray, "Modifications matter.", true),
        });

        // The tray a D-165 step takes an item OUT of, and the one it puts it into. Distinct
        // from StepOneTray/StepTwoTray so a test cannot pass by coincidence.
        private const int SourceTray = 1;
        private const int DestinationTray = 0;

        // Mirrors day_03: a plain move off the board, then a move out of another tray.
        private static TutorialDirector CreateTrayMoveDirector() => new(new List<TutorialStep>
        {
            TutorialStep.ForcedMove(StepOneX, StepOneY, DestinationTray, string.Empty, false),
            TutorialStep.TrayMove(SourceTray, DestinationTray, "You can use the drink from the other tray."),
        });

        // Mirrors Day 0 as it is built at runtime: the Day file's two moves, then the powerup
        // steps GameManager appends from PowerupConfig's schedule.
        private static TutorialDirector CreateDirectorEndingInIntro() => new(new List<TutorialStep>
        {
            TutorialStep.ForcedMove(StepOneX, StepOneY, StepOneTray, string.Empty, false),
            TutorialStep.ForcedMove(StepTwoX, StepTwoY, StepTwoTray, "Modifications matter.", true),
            TutorialStep.PowerupIntro(TutorialPowerup.AutoCollect, string.Empty),
        });

        private static TutorialDirector CreateIntroOnlyDirector() => new(new List<TutorialStep>
        {
            TutorialStep.PowerupIntro(TutorialPowerup.AutoCollect, string.Empty),
        });

        // The shape a powerup taught at a Day's start produces: panel, then a press asked for
        // straight away with the day still frozen.
        private static TutorialDirector CreateImmediatePowerupDirector() => new(new List<TutorialStep>
        {
            TutorialStep.PowerupIntro(TutorialPowerup.NoiseClear, string.Empty),
            TutorialStep.PowerupUse(TutorialPowerup.NoiseClear, TutorialTrigger.Immediate, 0f, "Try it now."),
        });

        // The shape Time Reset produces: panel at the Day's start, then a press that waits for
        // a ticket to run down.
        private static TutorialDirector CreateDeferredPowerupDirector() => new(new List<TutorialStep>
        {
            TutorialStep.PowerupIntro(TutorialPowerup.TimeReset, string.Empty),
            TutorialStep.PowerupUse(TutorialPowerup.TimeReset, TutorialTrigger.TicketPatienceBelow, TriggerRatio, "Press Time Reset."),
        });

        [Test]
        public void DuringAForcedMove_NoTrayCanBeTakenFrom()
        {
            var director = CreateTwoStepDirector();

            // THE HOLE D-165 CLOSED, pinned as a test because it was invisible: before the
            // tray gate existed the drag handler simply never asked about a seated item, so
            // while this forced move ran the player could still empty any tray on screen --
            // including the one the step was filling.
            Assert.IsFalse(director.IsTrayPickupAllowed(0));
            Assert.IsFalse(director.IsTrayPickupAllowed(1));
            Assert.IsFalse(director.IsTrayPickupAllowed(2));
        }

        [Test]
        public void DuringATrayMove_OnlyItsSourceTrayCanBeTakenFrom()
        {
            var director = CreateTrayMoveDirector();
            director.NotifyTrayAccepted(DestinationTray);

            Assert.IsTrue(director.IsTrayPickupAllowed(SourceTray));
            Assert.IsFalse(director.IsTrayPickupAllowed(DestinationTray));
            Assert.IsFalse(director.IsTrayPickupAllowed(2));
        }

        [Test]
        public void ATrayMove_RefusesTheWholeBoard()
        {
            var director = CreateTrayMoveDirector();
            director.NotifyTrayAccepted(DestinationTray);

            // The item being moved is not on the board, so every cell is off limits -- the
            // same stance the powerup kinds take, and for the same reason: anything else the
            // player could pick up fills a tray this step is not about.
            Assert.IsFalse(director.IsPickupAllowed(StepOneX, StepOneY));
            Assert.IsFalse(director.IsPickupAllowed(StepTwoX, StepTwoY));
            Assert.IsFalse(director.IsBoardRelocationAllowed());
        }

        [Test]
        public void ATrayMove_OnlyItsTargetTrayAcceptsADrop_AndCompletingItEndsTheTutorial()
        {
            var director = CreateTrayMoveDirector();
            director.NotifyTrayAccepted(DestinationTray);

            Assert.IsFalse(director.IsTrayDropAllowed(SourceTray));
            Assert.IsTrue(director.IsTrayDropAllowed(DestinationTray));

            // Dropping back into the tray it came from must not finish it.
            director.NotifyTrayAccepted(SourceTray);
            Assert.IsTrue(director.IsActive);

            director.NotifyTrayAccepted(DestinationTray);
            Assert.IsFalse(director.IsActive);

            // And every gate is open again, trays included.
            Assert.IsTrue(director.IsTrayPickupAllowed(SourceTray));
            Assert.IsTrue(director.IsBoardRelocationAllowed());
        }

        [Test]
        public void ATrayMove_PointsItsGhostFromOneTrayToTheOther()
        {
            var director = CreateTrayMoveDirector();

            // While the FORCED move runs the ghost still comes off the board, so the tray
            // source answers the sentinel rather than a plausible "tray 0".
            Assert.AreEqual(DestinationTray, director.SpotlightTraySlotIndex);
            Assert.AreEqual(-1, director.SpotlightSourceTraySlotIndex);

            director.NotifyTrayAccepted(DestinationTray);

            Assert.AreEqual(DestinationTray, director.SpotlightTraySlotIndex);
            Assert.AreEqual(SourceTray, director.SpotlightSourceTraySlotIndex);
        }

        [Test]
        public void WhenNoTutorialStepIsArmed_EveryTrayCanBeTakenFrom()
        {
            var director = CreateTrayMoveDirector();
            director.NotifyTrayAccepted(DestinationTray);
            director.NotifyTrayAccepted(DestinationTray);

            Assert.IsFalse(director.IsActive);
            Assert.IsTrue(director.IsTrayPickupAllowed(0));
            Assert.IsTrue(director.IsTrayPickupAllowed(1));
            Assert.IsTrue(director.IsTrayPickupAllowed(2));
        }

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

        // Asks what IsHoldingForReading means -- "this step is a panel" -- and no longer what
        // it used to imply. It stopped being the day clock's question at D-097: the clock now
        // holds for the whole tutorial, forced moves included, so a forced move being no
        // reading step says nothing about whether time is running.
        [Test]
        public void AForcedMove_IsNotAReadingStep()
        {
            var director = CreateTwoStepDirector();

            Assert.IsFalse(director.IsHoldingForReading);
        }

        // The trap D-098 closed, pinned in place. A panel step authors no cell and no tray, so
        // those fields are UNSET -- and unset is 0, which is a real tray, and specifically the
        // very tray day_00's first move names. Nothing about the step itself distinguishes the
        // two; only the Kind does. This is why the answer lives here rather than at each call
        // site: WorldTrayView asked the fields directly and had tray 0 raise a ghost from cell
        // (0,0) over the powerup panel.
        [Test]
        public void AnIntroStep_PointsAtNoTray_ThoughItsUnsetFieldReadsAsTrayZero()
        {
            var director = CreateIntroOnlyDirector();

            Assert.AreEqual(0, director.Current.TargetTraySlotIndex, "The unset field really is 0 -- that IS the trap.");
            Assert.AreEqual(StepOneTray, director.Current.TargetTraySlotIndex, "And 0 is a tray a real step uses.");
            Assert.AreEqual(-1, director.SpotlightTraySlotIndex, "No tray may claim a step that names none.");
        }

        [Test]
        public void AForcedMove_PointsAtItsOwnTray_AndTheSpotlightFollowsTheBoundary()
        {
            var director = CreateTwoStepDirector();

            Assert.AreEqual(StepOneTray, director.SpotlightTraySlotIndex);

            director.NotifyTrayAccepted(StepOneTray);

            Assert.AreEqual(StepTwoTray, director.SpotlightTraySlotIndex);
        }

        // Same shape as the gates: once it is over, it points at nothing -- so a tray needs no
        // "was there a tutorial" branch to know it has no spotlight to raise.
        [Test]
        public void AFinishedTutorial_PointsAtNoTray()
        {
            var director = CreateTwoStepDirector();
            director.NotifyTrayAccepted(StepOneTray);
            director.NotifyTrayAccepted(StepTwoTray);

            Assert.IsFalse(director.IsActive);
            Assert.AreEqual(-1, director.SpotlightTraySlotIndex);
        }

        [Test]
        public void AnAbortedTutorial_PointsAtNoTray()
        {
            var director = CreateTwoStepDirector();

            director.Abort();

            Assert.AreEqual(-1, director.SpotlightTraySlotIndex, "An abort must take the spotlight's claim with it.");
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

        // ---- D-115: armed vs merely active, and the forced press --------------------------

        // The distinction the whole deferred lesson rests on. Every step written before D-115
        // arms the instant it becomes current, so the two answers agree everywhere except on
        // a waiting step -- which is exactly what makes this worth pinning: if a refactor ever
        // collapses them again, it will look correct until Time Reset's Day.
        [Test]
        public void AStepThatArmsImmediately_IsBothActiveAndArmed()
        {
            var director = CreateTwoStepDirector();

            Assert.IsTrue(director.IsActive);
            Assert.IsTrue(director.IsArmed);
            Assert.IsFalse(director.IsAwaitingTrigger);
        }

        // The deadlock this design exists to avoid: the clock gate reads IsArmed, so a step
        // waiting for a ticket to run down must leave it false -- otherwise the day freezes
        // while waiting for the day to advance.
        [Test]
        public void ADeferredUseStep_IsActiveButNotArmed_AndLeavesEveryGateOpen()
        {
            var director = CreateDeferredPowerupDirector();
            director.NotifyReadingFinished();

            Assert.IsTrue(director.IsActive, "The lesson is not finished -- the press is still to come.");
            Assert.IsFalse(director.IsArmed, "But nothing is being asked for yet, so nothing is frozen.");
            Assert.IsTrue(director.IsAwaitingTrigger);

            Assert.IsTrue(director.IsPickupAllowed(4, 3), "The day runs normally while the step waits.");
            Assert.IsTrue(director.IsTrayDropAllowed(1));
            Assert.IsTrue(director.IsBoardRelocationAllowed());
            Assert.IsTrue(director.IsPowerupUseAllowed(TutorialPowerup.AutoCollect), "Including the other powerups.");
            Assert.IsNull(director.RequiredPowerup, "Nothing is being asked for, so nothing is spotlit.");
        }

        [Test]
        public void ADeferredUseStep_ArmsWhenATicketReachesTheThreshold()
        {
            var director = CreateDeferredPowerupDirector();
            director.NotifyReadingFinished();

            var changes = 0;
            director.StepChanged += () => changes++;

            director.NotifyTicketPatienceRatio(0.9f, slotIndex: 0);
            director.NotifyTicketPatienceRatio(TriggerRatio + 0.01f, slotIndex: 0);
            Assert.IsFalse(director.IsArmed, "Above the threshold is not at it.");
            Assert.AreEqual(0, changes, "A step that did not arm announces nothing.");

            director.NotifyTicketPatienceRatio(TriggerRatio, slotIndex: 0);

            Assert.IsTrue(director.IsArmed, "At the threshold, the lesson takes over.");
            Assert.AreEqual(1, changes, "Arming is a change of what the tutorial is asking for, so it is announced.");
            Assert.AreEqual(TutorialPowerup.TimeReset, director.RequiredPowerup);
        }

        // Once armed, the day is held exactly as it is for every other step -- which is the
        // half of the user's description that says the clock stops again when the ticket runs
        // down.
        [Test]
        public void AnArmedUseStep_RefusesTheBoardAndEveryOtherPowerup()
        {
            var director = CreateDeferredPowerupDirector();
            director.NotifyReadingFinished();
            director.NotifyTicketPatienceRatio(0.1f, slotIndex: 0);

            Assert.IsFalse(director.IsPickupAllowed(4, 3));
            Assert.IsFalse(director.IsTrayDropAllowed(1));
            Assert.IsFalse(director.IsBoardRelocationAllowed());
            Assert.IsTrue(director.IsPowerupUseAllowed(TutorialPowerup.TimeReset), "The one it asked for.");
            Assert.IsFalse(director.IsPowerupUseAllowed(TutorialPowerup.AutoCollect));
            Assert.IsFalse(director.IsPowerupUseAllowed(TutorialPowerup.NoiseClear));
        }

        [Test]
        public void PressingTheRequiredPowerup_FinishesTheLessonAndReleasesEverything()
        {
            var director = CreateDeferredPowerupDirector();
            director.NotifyReadingFinished();
            director.NotifyTicketPatienceRatio(0f, slotIndex: 0);

            director.NotifyPowerupUsed(TutorialPowerup.TimeReset);

            Assert.IsFalse(director.IsActive);
            Assert.IsFalse(director.IsArmed);
            Assert.IsNull(director.RequiredPowerup);
            Assert.IsTrue(director.IsPickupAllowed(4, 3), "The day is handed back.");
            Assert.IsTrue(director.IsPowerupUseAllowed(TutorialPowerup.AutoCollect));
        }

        // The guard that matches NotifyTrayAccepted's slot check: the gate above and this call
        // sit either side of the bar's own spend, and a caller that forgets the gate must not
        // be able to advance the lesson with the wrong button.
        [Test]
        public void PressingAnotherPowerup_DoesNotFinishTheLesson()
        {
            var director = CreateDeferredPowerupDirector();
            director.NotifyReadingFinished();
            director.NotifyTicketPatienceRatio(0f, slotIndex: 0);

            director.NotifyPowerupUsed(TutorialPowerup.AutoCollect);

            Assert.IsTrue(director.IsArmed, "Still waiting for the right one.");
            Assert.AreEqual(TutorialPowerup.TimeReset, director.RequiredPowerup);
        }

        // A powerup taught at the Day's start: the press follows the panel with no gap, so the
        // clock never restarts between the two.
        [Test]
        public void AnImmediateUseStep_ArmsAsSoonAsThePanelIsDismissed()
        {
            var director = CreateImmediatePowerupDirector();

            Assert.AreEqual(TutorialPowerup.NoiseClear, director.IntroducedPowerup);
            Assert.IsNull(director.RequiredPowerup, "The panel is up; nothing is being pressed yet.");

            director.NotifyReadingFinished();

            Assert.IsTrue(director.IsArmed, "No trigger to wait for, so the day stays frozen.");
            Assert.IsFalse(director.IsAwaitingTrigger);
            Assert.IsNull(director.IntroducedPowerup, "The panel is gone.");
            Assert.AreEqual(TutorialPowerup.NoiseClear, director.RequiredPowerup);
        }

        // A patience report must not arm a step that named no trigger, or every panel in the
        // list would advance itself the first time a ticket got low.
        [Test]
        public void APatienceReport_DoesNothingToAStepThatNamedNoTrigger()
        {
            var director = CreateImmediatePowerupDirector();
            var changes = 0;
            director.StepChanged += () => changes++;

            director.NotifyTicketPatienceRatio(0f, slotIndex: 0);

            Assert.AreEqual(0, changes);
            Assert.AreEqual(TutorialPowerup.NoiseClear, director.IntroducedPowerup, "Still on the panel.");
        }

        // The slot that armed the step is remembered, and it is remembered AS OF THE ARMING.
        // The lesson dims the screen down to that one card (D-146), so a later report from a
        // ticket that has since become the most urgent must not move the spotlight -- by then
        // the player is already looking at the card the lesson lit.
        [Test]
        public void ADeferredUseStep_RemembersTheSlotThatArmedIt()
        {
            var director = CreateDeferredPowerupDirector();
            director.NotifyReadingFinished();

            Assert.AreEqual(-1, director.TriggeringTicketSlotIndex, "Nothing has tripped the step yet.");

            director.NotifyTicketPatienceRatio(TriggerRatio, slotIndex: 2);

            Assert.IsTrue(director.IsArmed);
            Assert.AreEqual(2, director.TriggeringTicketSlotIndex, "The slot handed over with the arming report is the one the lesson lights.");

            director.NotifyTicketPatienceRatio(0f, slotIndex: 1);

            Assert.AreEqual(2, director.TriggeringTicketSlotIndex,
                "A ticket that becomes more urgent AFTER the step armed does not steal the spotlight.");
        }

        // Arming happens once. A second report below the threshold must not re-announce a step
        // that is already asking for its press -- the bar would tear its frame down and build
        // an identical one every frame.
        [Test]
        public void FurtherPatienceReports_AfterArming_AnnounceNothing()
        {
            var director = CreateDeferredPowerupDirector();
            director.NotifyReadingFinished();
            director.NotifyTicketPatienceRatio(0.2f, slotIndex: 0);

            var changes = 0;
            director.StepChanged += () => changes++;

            director.NotifyTicketPatienceRatio(0.1f, slotIndex: 0);
            director.NotifyTicketPatienceRatio(0f, slotIndex: 0);

            Assert.AreEqual(0, changes);
        }

        // The message belongs to the step, so the spotlight prints authored words rather than
        // assembling its own -- and a step that is not armed has nothing to say.
        [Test]
        public void TheCurrentMessage_IsTheArmedStepsOwn()
        {
            var director = CreateDeferredPowerupDirector();
            director.NotifyReadingFinished();

            Assert.AreEqual(string.Empty, director.CurrentMessage, "A waiting step is not saying anything yet.");

            director.NotifyTicketPatienceRatio(0f, slotIndex: 0);

            Assert.AreEqual("Press Time Reset.", director.CurrentMessage);
        }

        // Aborting out of a WAITING step has to work too: the day can end with a deferred
        // lesson still unarmed, and an abort must not leave RequiredPowerup pointing at a
        // button nobody is being asked to press.
        [Test]
        public void AbortingAWaitingStep_EndsTheTutorial()
        {
            var director = CreateDeferredPowerupDirector();
            director.NotifyReadingFinished();

            director.Abort();

            Assert.IsFalse(director.IsActive);
            Assert.IsFalse(director.IsAwaitingTrigger);
            Assert.IsNull(director.RequiredPowerup);
            Assert.IsNull(director.IntroducedPowerup);
        }

        // A finished tutorial permits every powerup, the same property the board gates have --
        // so the bar needs no "was there a tutorial" branch.
        [Test]
        public void AFinishedTutorial_AllowsEveryPowerup()
        {
            var director = CreateTwoStepDirector();
            director.NotifyTrayAccepted(StepOneTray);
            director.NotifyTrayAccepted(StepTwoTray);

            Assert.IsTrue(director.IsPowerupUseAllowed(TutorialPowerup.AutoCollect));
            Assert.IsTrue(director.IsPowerupUseAllowed(TutorialPowerup.TimeReset));
            Assert.IsTrue(director.IsPowerupUseAllowed(TutorialPowerup.NoiseClear));
        }

        // A forced MOVE refuses every powerup, which is the rule that stops Auto-Collect from
        // sweeping the very item the ghost is pointing at.
        [Test]
        public void AForcedMove_RefusesEveryPowerup()
        {
            var director = CreateTwoStepDirector();

            Assert.IsFalse(director.IsPowerupUseAllowed(TutorialPowerup.AutoCollect));
            Assert.IsFalse(director.IsPowerupUseAllowed(TutorialPowerup.TimeReset));
        }

        // The panel step's own refusal, for the same reason the board is refused behind it:
        // spending a charge while reading is a way to waste one on nothing.
        [Test]
        public void AnIntroStep_RefusesEveryPowerupIncludingItsOwn()
        {
            var director = CreateImmediatePowerupDirector();

            Assert.IsFalse(director.IsPowerupUseAllowed(TutorialPowerup.NoiseClear));
            Assert.IsFalse(director.IsPowerupUseAllowed(TutorialPowerup.AutoCollect));
        }
    }
}
