using System;
using System.Collections.Generic;

namespace ExpoTheExplorer.Systems.Tutorial
{
    // The forced opening, as rules: while a step is running, exactly one board cell can be
    // picked up and exactly one tray will accept what comes off it. Everything else is
    // refused until the player makes that move, and then the next step takes over.
    //
    // It knows NOTHING about the game it is gating. Plain numbers and strings come in; two
    // questions, a current step and a completion go out. There is no BoardItem here, no
    // Ticket, no tray contents, and this assembly references nothing at all -- which is what
    // lets every rule below be tested with no MonoBehaviour, no scene and no Unity. The same
    // shape PowerupSystem uses to avoid pointing at the board, the trays and the tickets at
    // once (see blueprint.md).
    //
    // It became a step LIST in D-083, when a second step was authored. It is still not a
    // tutorial framework: a list and an index, no interface, no state machine, no per-step
    // subclass. What a step means is decided by whoever reads Current, not by this class.
    public class TutorialDirector
    {
        private readonly IReadOnlyList<TutorialStep> steps;
        private int index;

        // The step the player is being held on, or null once they are all done (and for a
        // tutorial that was aborted). Every gate below is derived from this one field, so
        // there is no second place that can disagree about which step is running.
        public TutorialStep Current => index >= 0 && index < steps.Count ? steps[index] : null;

        public bool IsActive => Current != null;

        // Fires whenever Current changes -- including to null at the end. The trays listen so
        // that whichever one is the new target can raise the spotlight; a single Completed
        // event would not have been enough once there was more than one step, because the
        // interesting moment is "the step changed", not only "they are all finished".
        //
        // Deliberately a plain event rather than the project's EventBus: this type is meant
        // to reference nothing.
        public event Action StepChanged;

        public TutorialDirector(IReadOnlyList<TutorialStep> steps)
        {
            this.steps = steps ?? Array.Empty<TutorialStep>();
        }

        // Both gates answer TRUE once the tutorial is over, which is what makes the call
        // sites read as an ordinary condition instead of a tutorial special case -- a
        // finished tutorial permits everything, exactly like no tutorial at all.
        // An intro step refuses EVERYTHING rather than falling back to its unused cell and
        // tray fields: it is a panel to be read, and anything reachable behind it is a way
        // to lose a life while reading. The clock is stopped for every kind of step alike
        // (D-097) -- GameManager gates its tick on IsActive, not on the kind.
        public bool IsPickupAllowed(int x, int y)
        {
            var step = Current;
            if (step == null) return true;
            if (step.Kind != TutorialStepKind.ForcedMove) return false;
            return x == step.SourceX && y == step.SourceY;
        }

        public bool IsTrayDropAllowed(int slotIndex)
        {
            var step = Current;
            if (step == null) return true;
            if (step.Kind != TutorialStepKind.ForcedMove) return false;
            return slotIndex == step.TargetTraySlotIndex;
        }

        // True exactly while a step is asking the player to READ rather than to act. It used
        // to be the day clock's question too; since D-097 the clock holds for the whole
        // tutorial (IsActive), and this one is left with its other reader: the powerup bar,
        // which builds and tears down the intro panel from it. It stays a property of the
        // step list rather than a Kind comparison at that call site, so "which kinds are a
        // panel" is answered in one place.
        public bool IsHoldingForReading => Current != null && Current.Kind == TutorialStepKind.PowerupIntro;

        // The tray the current step points its ghost at, or -1 when nothing points at one --
        // no step running, or a step that names no tray. This exists because of a trap in the
        // step's own shape (D-098): a panel step leaves the cell and tray fields UNSET, and
        // unset is 0, not "absent". A caller that reads TargetTraySlotIndex before asking
        // about the Kind is told "tray 0, cell (0,0)" by a step that named neither -- which is
        // exactly how the powerup panel came to have tray 0 raise a spotlight over it.
        //
        // Answered here rather than by a Kind comparison at the call site for the reason
        // IsHoldingForReading is: which kinds have a tray is a property of the step list. The
        // sentinel cannot collide with a real answer -- slot indices are 0..TicketSlotCount-1,
        // and DayValidator already refuses an authored index outside that range.
        public int SpotlightTraySlotIndex =>
            Current != null && Current.Kind == TutorialStepKind.ForcedMove ? Current.TargetTraySlotIndex : -1;

        // Whether an item may be PARKED on the board -- moved from one cell to another
        // instead of going to a tray. False for the whole of a step, and that is not a
        // nicety: the pickup gate permits exactly one cell, so an item parked anywhere else
        // could never be picked up again, leaving the step uncompletable and the day stuck.
        //
        // Deliberately its own question rather than IsPickupAllowed(destination). The two
        // happen to agree on today's content, but they ask different things -- "may this
        // cell be picked up from" and "may an item be put down at all" -- and a single
        // predicate serving both would be true by coincidence rather than by rule.
        public bool IsBoardRelocationAllowed() => !IsActive;

        // Called by the tray that accepted an item. The slot check is not paranoia: the drop
        // gate and this call sit either side of TrayManager's own accept, and a future caller
        // that forgets the gate must not be able to advance the tutorial by dropping into the
        // wrong tray. Ignoring a non-matching slot also makes it idempotent for the second
        // item of a multi-item order.
        public void NotifyTrayAccepted(int slotIndex)
        {
            var step = Current;
            if (step == null || step.Kind != TutorialStepKind.ForcedMove) return;
            if (slotIndex != step.TargetTraySlotIndex) return;

            index++;
            StepChanged?.Invoke();
        }

        // The intro panel's own way of finishing, kept separate from NotifyTrayAccepted so
        // neither kind of step can be completed by the other's gesture -- a stray tray drop
        // must not skip a panel the player has not read, and dismissing a panel must not
        // stand in for a move.
        public void NotifyReadingFinished()
        {
            if (!IsHoldingForReading) return;

            index++;
            StepChanged?.Invoke();
        }

        // Ends the tutorial wherever it stands, opening every gate. This is the escape hatch
        // for a step that has become impossible -- its source cell is empty when its turn
        // comes -- and it exists because the alternative is a player staring at a board that
        // refuses every touch. The caller logs why; this class only knows that it stops.
        public void Abort()
        {
            if (!IsActive) return;

            index = steps.Count;
            StepChanged?.Invoke();
        }
    }

    // One forced move. Plain data, and deliberately this system's OWN type rather than
    // DaySystem's ResolvedTutorialStep: taking that one would have put a reference on this
    // assembly's empty list, which is the one thing that keeps these rules testable without
    // a Day catalog. GameManager translates at the boundary, the same way it already hands
    // PowerupManager a Func rather than the systems an effect touches.
    // Mirrors DaySystem's TutorialStepKind. A mirror rather than a shared type for the same
    // reason TutorialStep itself is one: referencing DaySystem would be the first entry on
    // this assembly's empty reference list, and GameManager already translates at that
    // boundary. Written out rather than cast, so adding a kind upstream without teaching
    // this system about it is a compile error rather than a silent misread.
    public enum TutorialStepKind
    {
        ForcedMove = 0,
        PowerupIntro = 1,
    }

    public class TutorialStep
    {
        public TutorialStepKind Kind { get; }
        public int SourceX { get; }
        public int SourceY { get; }
        public int TargetTraySlotIndex { get; }
        public string Message { get; }
        public bool HighlightModification { get; }

        public TutorialStep(TutorialStepKind kind, int sourceX, int sourceY, int targetTraySlotIndex, string message, bool highlightModification)
        {
            Kind = kind;
            SourceX = sourceX;
            SourceY = sourceY;
            TargetTraySlotIndex = targetTraySlotIndex;
            Message = message ?? string.Empty;
            HighlightModification = highlightModification;
        }
    }
}
