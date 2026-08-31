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
    //
    // D-115 (2026-08-27) SPLIT "a step is running" FROM "everything is frozen", and that is
    // the one structural idea in this file. Until then IsActive meant both, which is fine
    // while every step arms the instant it becomes current. The powerup that buys time has
    // to be taught at a moment when somebody is short of time, so its forced press waits for
    // an active ticket to run down -- and a step that freezes the clock while waiting for
    // the clock to run is a deadlock. So a step can now be CURRENT but not yet ARMED: the
    // gates below stay open, the day runs, and only the trigger closes them.
    public class TutorialDirector
    {
        private readonly IReadOnlyList<TutorialStep> steps;
        private int index;

        // Whether the CURRENT step has had its trigger fire. Recomputed at every index
        // change and never anywhere else, so "armed" cannot drift out of step with which
        // step is current -- the bug a second bool set by hand at each call site would be.
        private bool armed;

        // Which ticket slot tripped the current step's deferred trigger, or -1. Written in
        // exactly one place -- the moment the step arms -- and cleared with `armed` at every
        // index change, for the same reason that flag is: two fields that describe one step
        // must move together or a later step inherits an earlier one's answer.
        private int triggeringTicketSlotIndex = -1;

        // The step the player is being held on, or null once they are all done (and for a
        // tutorial that was aborted). Every gate below is derived from this one field, so
        // there is no second place that can disagree about which step is running.
        public TutorialStep Current => index >= 0 && index < steps.Count ? steps[index] : null;

        // The tutorial has not finished. NOT the gate question -- see IsArmed. This is what
        // "there is still teaching to come" means, and it stays true while a deferred step
        // waits for its moment.
        public bool IsActive => Current != null;

        // The gate question, and the freeze question: a step is current AND its trigger has
        // fired. Every refusal below reads this rather than IsActive, which is what lets a
        // waiting step leave the day completely alone.
        public bool IsArmed => Current != null && armed;

        // A step is current but its moment has not come. The day runs normally here; this
        // exists so a caller can tell "waiting to teach" from "finished teaching", which
        // IsActive alone cannot answer.
        public bool IsAwaitingTrigger => Current != null && !armed;

        // Fires whenever Current changes -- including to null at the end -- and whenever a
        // waiting step ARMS, which is a change of the same kind even though the index did
        // not move. The trays listen so that whichever one is the new target can raise the
        // spotlight; the powerup bar listens so it can put its panel up and take it down.
        //
        // Deliberately a plain event rather than the project's EventBus: this type is meant
        // to reference nothing.
        public event Action StepChanged;

        public TutorialDirector(IReadOnlyList<TutorialStep> steps)
        {
            this.steps = steps ?? Array.Empty<TutorialStep>();
            armed = ArmsImmediately(Current);
        }

        // Both gates answer TRUE once the tutorial is over, which is what makes the call
        // sites read as an ordinary condition instead of a tutorial special case -- a
        // finished tutorial permits everything, exactly like no tutorial at all. A step that
        // is merely WAITING permits everything for the same reason: nothing is being taught
        // yet, so nothing is being refused.
        // An intro step refuses EVERYTHING rather than falling back to its unused cell and
        // tray fields: it is a panel to be read, and anything reachable behind it is a way
        // to lose a life while reading. A forced-use step refuses the board for the same
        // reason -- the player is being asked to press one button, not to play.
        public bool IsPickupAllowed(int x, int y)
        {
            if (!IsArmed) return true;

            var step = Current;
            if (step.Kind != TutorialStepKind.ForcedMove) return false;
            return x == step.SourceX && y == step.SourceY;
        }

        public bool IsTrayDropAllowed(int slotIndex)
        {
            if (!IsArmed) return true;

            var step = Current;
            if (step.Kind != TutorialStepKind.ForcedMove) return false;
            return slotIndex == step.TargetTraySlotIndex;
        }

        // True exactly while a step is asking the player to READ rather than to act. It used
        // to be the day clock's question too; since D-097 the clock holds for the whole
        // tutorial, and since D-115 that means IsArmed rather than IsActive. This one is
        // left with its other reader: the powerup bar, which builds and tears down the intro
        // panel from it. It stays a property of the step list rather than a Kind comparison
        // at that call site, so "which kinds are a panel" is answered in one place.
        public bool IsHoldingForReading => IsArmed && Current.Kind == TutorialStepKind.PowerupIntro;

        // The tray the current step points its ghost at, or -1 when nothing points at one --
        // no step running, a step still waiting, or a step that names no tray. This exists
        // because of a trap in the step's own shape (D-098): a panel step leaves the cell and
        // tray fields UNSET, and unset is 0, not "absent". A caller that reads
        // TargetTraySlotIndex before asking about the Kind is told "tray 0, cell (0,0)" by a
        // step that named neither -- which is exactly how the powerup panel came to have tray
        // 0 raise a spotlight over it.
        //
        // Answered here rather than by a Kind comparison at the call site for the reason
        // IsHoldingForReading is: which kinds have a tray is a property of the step list. The
        // sentinel cannot collide with a real answer -- slot indices are 0..TicketSlotCount-1,
        // and DayValidator already refuses an authored index outside that range.
        public int SpotlightTraySlotIndex =>
            IsArmed && Current.Kind == TutorialStepKind.ForcedMove ? Current.TargetTraySlotIndex : -1;

        // The ticket slot whose clock tripped the current step's deferred trigger, or -1 for
        // every other step -- the same sentinel and the same reasoning as the tray index
        // above, including why it is answered here rather than by a Trigger comparison at the
        // call site.
        //
        // IT IS THE ONE THING THIS CLASS REMEMBERS ABOUT A TICKET, and it remembers it as a
        // number it was handed rather than by looking at anything: the caller decides what
        // "closest to running out" means, exactly as it decides what a board cell is. That is
        // the property the comment on NotifyTicketPatienceRatio protects, and an int index is
        // no more a ticket than TargetTraySlotIndex is a tray.
        //
        // Its consumer is the lesson's own staging (D-146): the deferred press dims the screen
        // and this says which card must stay lit, because that card -- its clock, its
        // exclamation, its bar -- IS the reason the powerup is being pressed.
        public int TriggeringTicketSlotIndex =>
            IsArmed && Current.Trigger == TutorialTrigger.TicketPatienceBelow ? triggeringTicketSlotIndex : -1;

        // The powerup whose panel is on screen, or null. Nullable rather than a sentinel
        // value for the reason the tray index is NOT: an enum has no spare member to spend
        // on absence, and inventing one would put "None" into every switch in the project.
        public TutorialPowerup? IntroducedPowerup =>
            IsArmed && Current.Kind == TutorialStepKind.PowerupIntro ? Current.Powerup : null;

        // The powerup the player is being made to press, or null. The spotlight reads this
        // to know which button to light, and the message beside it comes from the same step.
        public TutorialPowerup? RequiredPowerup =>
            IsArmed && Current.Kind == TutorialStepKind.PowerupUse ? Current.Powerup : null;

        // The sentence to show beside whatever is being taught right now, or empty. Taken
        // from the step rather than assembled by the view, because the words are content and
        // this is the object that holds the authored ones.
        public string CurrentMessage => IsArmed ? Current.Message : string.Empty;

        // Whether a powerup may be pressed at all. The third gate, and the one that makes a
        // forced press meaningful: while the player is being asked for Time Reset, pressing
        // Auto-Collect is refused the way a wrong tray is: the step names exactly one
        // acceptable action and everything else waits.
        //
        // Note what this does NOT do: it never refuses a press while the tutorial is merely
        // waiting or finished. A day is fully playable in both.
        public bool IsPowerupUseAllowed(TutorialPowerup powerup)
        {
            if (!IsArmed) return true;

            var step = Current;
            if (step.Kind != TutorialStepKind.PowerupUse) return false;
            return step.Powerup == powerup;
        }

        // Whether an item may be PARKED on the board -- moved from one cell to another
        // instead of going to a tray. False for the whole of an armed step, and that is not
        // a nicety: the pickup gate permits exactly one cell, so an item parked anywhere else
        // could never be picked up again, leaving the step uncompletable and the day stuck.
        //
        // Deliberately its own question rather than IsPickupAllowed(destination). The two
        // happen to agree on today's content, but they ask different things -- "may this
        // cell be picked up from" and "may an item be put down at all" -- and a single
        // predicate serving both would be true by coincidence rather than by rule.
        public bool IsBoardRelocationAllowed() => !IsArmed;

        // Called by the tray that accepted an item. The slot check is not paranoia: the drop
        // gate and this call sit either side of TrayManager's own accept, and a future caller
        // that forgets the gate must not be able to advance the tutorial by dropping into the
        // wrong tray. Ignoring a non-matching slot also makes it idempotent for the second
        // item of a multi-item order.
        public void NotifyTrayAccepted(int slotIndex)
        {
            if (!IsArmed) return;

            var step = Current;
            if (step.Kind != TutorialStepKind.ForcedMove) return;
            if (slotIndex != step.TargetTraySlotIndex) return;

            Advance();
        }

        // The intro panel's own way of finishing, kept separate from NotifyTrayAccepted so
        // neither kind of step can be completed by the other's gesture -- a stray tray drop
        // must not skip a panel the player has not read, and dismissing a panel must not
        // stand in for a move.
        public void NotifyReadingFinished()
        {
            if (!IsHoldingForReading) return;

            Advance();
        }

        // The forced press, and it completes the step on the PRESS rather than on the effect
        // having done something. GDD 5.2 says a powerup with no work to do costs no charge,
        // so an effect can legitimately decline -- and a step that waited for a successful
        // effect would leave the player pressing a button that refuses to advance, with no
        // way to find out why. The powerup check is the same guard NotifyTrayAccepted's slot
        // check is: the gate above and this call sit either side of the bar's own spend.
        public void NotifyPowerupUsed(TutorialPowerup powerup)
        {
            if (!IsArmed) return;

            var step = Current;
            if (step.Kind != TutorialStepKind.PowerupUse) return;
            if (step.Powerup != powerup) return;

            Advance();
        }

        // The deferred trigger, fed as a plain fraction so this class still knows nothing
        // about tickets -- the caller decides what "how close to running out" means and
        // hands over a number, exactly as it hands over cell coordinates without this class
        // knowing what a BoardItem is.
        //
        // Called every tick with the LOWEST ratio among the active tickets, so a step arms on
        // the first ticket to reach the threshold rather than on some particular one. Cheap
        // enough to poll: a comparison against a float, refused on the first line for every
        // step that is already armed, which is all of them but one.
        //
        // `slotIndex` is WHOSE ratio that was, and it is required rather than defaulted
        // (D-146). A default would compile at every call site and cost the lesson its
        // highlight the first time somebody added a caller and forgot -- the same trap D-135
        // refused when it made `givingUpOnAttempt` mandatory. It is stored, never consulted:
        // this class still decides nothing about tickets.
        public void NotifyTicketPatienceRatio(float lowestRemainingRatio, int slotIndex)
        {
            var step = Current;
            if (step == null || armed) return;
            if (step.Trigger != TutorialTrigger.TicketPatienceBelow) return;
            if (lowestRemainingRatio > step.TriggerPatienceRatio) return;

            // Recorded with the arming rather than on every tick, so it is the slot that
            // ACTUALLY tripped the step and not whichever one happens to be lowest later --
            // by the time the player presses the powerup, a different ticket may well be
            // closest to running out.
            triggeringTicketSlotIndex = slotIndex;

            // Not an index change, but a change of exactly the kind every listener cares
            // about: what the tutorial is asking for right now is different from a moment
            // ago. The views cannot tell the two apart and should not have to.
            armed = true;
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
            armed = false;
            StepChanged?.Invoke();
        }

        // The single place the index moves, which is what keeps `armed` honest: every
        // advance re-asks whether the new step wants the day frozen straight away, and no
        // caller is in a position to forget.
        private void Advance()
        {
            index++;
            armed = ArmsImmediately(Current);

            // Cleared here rather than left to the property's Trigger check, which would also
            // hide it: two deferred steps in a row would otherwise let the second one inherit
            // the first one's ticket for the whole of its wait.
            triggeringTicketSlotIndex = -1;

            StepChanged?.Invoke();
        }

        // A finished tutorial is not armed, and neither is a step that named a trigger. Every
        // other step arms the moment it becomes current, which is what every step did before
        // D-115 and what all but one of them still does.
        private static bool ArmsImmediately(TutorialStep step) =>
            step != null && step.Trigger == TutorialTrigger.Immediate;
    }

    // One forced move. Plain data, and deliberately this system's OWN type rather than
    // DaySystem's ResolvedTutorialStep: taking that one would have put a reference on this
    // assembly's empty list, which is the one thing that keeps these rules testable without
    // a Day catalog. GameManager translates at the boundary, the same way it already hands
    // PowerupManager a Func rather than the systems an effect touches.
    // Mirrors DaySystem's TutorialStepKind for the ForcedMove half. A mirror rather than a
    // shared type for the same reason TutorialStep itself is one: referencing DaySystem
    // would be the first entry on this assembly's reference list, and GameManager already
    // translates at that boundary. Written out rather than cast, so adding a kind upstream
    // without teaching this system about it is a compile error rather than a silent misread.
    //
    // The two powerup kinds have NO counterpart in DaySystem since D-115 -- a Day file
    // cannot author them any more. They are built by GameManager from PowerupConfig's
    // schedule, which is what leaves the Day files owning forced moves and nothing else.
    public enum TutorialStepKind
    {
        ForcedMove = 0,
        PowerupIntro = 1,
        PowerupUse = 2,
    }

    // Mirrors Data's PowerupType, and mirrors it for the reason above rather than out of
    // preference: naming the real enum would put ExpoTheExplorer.Data on this assembly's
    // reference list. Three values that a design change would have to alter in both places
    // is the price of an assembly that references nothing, and it is the same price the
    // kind enum above already pays.
    public enum TutorialPowerup
    {
        AutoCollect = 0,
        TimeReset = 1,
        NoiseClear = 2,
    }

    // What has to happen before a step closes the gates. Mirrors Data's
    // PowerupTutorialTrigger; Immediate is the name for "nothing has to happen", which is
    // every forced move, every panel, and the two powerups taught at a day's start.
    public enum TutorialTrigger
    {
        Immediate = 0,
        TicketPatienceBelow = 1,
    }

    public class TutorialStep
    {
        public TutorialStepKind Kind { get; }
        public int SourceX { get; }
        public int SourceY { get; }
        public int TargetTraySlotIndex { get; }

        // Empty rather than null for an unauthored message, so every reader can ask
        // string.IsNullOrEmpty and none of them can dereference it.
        public string Message { get; }

        public bool HighlightModification { get; }

        // Meaningful only for the two powerup kinds. Unset is AutoCollect rather than
        // "absent" -- an enum has no absent -- which is exactly why the director never reads
        // it without checking Kind first, the same trap SpotlightTraySlotIndex documents.
        public TutorialPowerup Powerup { get; }

        public TutorialTrigger Trigger { get; }

        // Meaningful only for TicketPatienceBelow. A fraction of a ticket's own time limit,
        // so it scales across patience types for free -- the same reasoning the tip tiers
        // use for their own thresholds (CLAUDE.md, Tip Tiers).
        public float TriggerPatienceRatio { get; }

        // The three named ways to build a step. Named factories rather than one constructor
        // with nine arguments because the three shapes share almost no fields: a forced move
        // has a cell and no powerup, a panel has a powerup and no cell, and passing zero for
        // everything a shape does not use is how (0,0)/tray-0 became a real answer for a step
        // that named neither.
        public static TutorialStep ForcedMove(int sourceX, int sourceY, int targetTraySlotIndex, string message, bool highlightModification) =>
            new(TutorialStepKind.ForcedMove, sourceX, sourceY, targetTraySlotIndex, message, highlightModification,
                default, TutorialTrigger.Immediate, 0f);

        // A panel always arms immediately: it is shown at the start of the Day that
        // introduces its powerup, and the whole point is that the day stops to say it.
        public static TutorialStep PowerupIntro(TutorialPowerup powerup, string message) =>
            new(TutorialStepKind.PowerupIntro, 0, 0, 0, message, false, powerup, TutorialTrigger.Immediate, 0f);

        public static TutorialStep PowerupUse(TutorialPowerup powerup, TutorialTrigger trigger, float triggerPatienceRatio, string message) =>
            new(TutorialStepKind.PowerupUse, 0, 0, 0, message, false, powerup, trigger, triggerPatienceRatio);

        private TutorialStep(
            TutorialStepKind kind,
            int sourceX,
            int sourceY,
            int targetTraySlotIndex,
            string message,
            bool highlightModification,
            TutorialPowerup powerup,
            TutorialTrigger trigger,
            float triggerPatienceRatio)
        {
            Kind = kind;
            SourceX = sourceX;
            SourceY = sourceY;
            TargetTraySlotIndex = targetTraySlotIndex;
            Message = message ?? string.Empty;
            HighlightModification = highlightModification;
            Powerup = powerup;
            Trigger = trigger;
            TriggerPatienceRatio = triggerPatienceRatio;
        }
    }
}
