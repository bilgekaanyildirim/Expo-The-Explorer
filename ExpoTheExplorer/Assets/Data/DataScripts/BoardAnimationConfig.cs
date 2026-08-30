using UnityEngine;

namespace ExpoTheExplorer.Data
{
    [CreateAssetMenu(fileName = "BoardAnimationConfig", menuName = "ExpoTheExplorer/Data/Board Animation Config")]
    public class BoardAnimationConfig : ScriptableObject
    {
        [Header("Drag End")]
        [Tooltip("Duration (seconds) of the ease-back tween when an item is dropped somewhere invalid.")]
        [SerializeField] private float snapBackDuration = 0.25f;
        [Tooltip("Duration (seconds) of the settle tween when an item is dropped into a tray slot.")]
        [SerializeField] private float traySettleDuration = 0.2f;

        [Header("Board")]
        [Tooltip("Duration (seconds) of the pop-in scale tween when an item newly appears on the board.")]
        [SerializeField] private float popInDuration = 0.2f;
        [Tooltip("Arc height of the fly-in from BoardView's Starting Point, as a multiple of one cell size (0 = straight line, no arc).")]
        [SerializeField] private float popInJumpPower = 0.5f;

        [Header("Auto-Collect")]
        [Tooltip("How much longer an item takes to fly when AUTO-COLLECT is what moved it, as a multiple of the two ordinary travel durations above — Tray Settle Duration on the way into a tray, Pop In Duration on the way back out to the board. One press can move up to six items at once and they all set off together, so the same speed a finger gets reads as everything scattering at once. 1 = exactly as fast as a hand-dragged item. It scales ONLY the powerup's own moves: a drag, a spawn, a wrong-order scatter and a timeout are untouched.")]
        [SerializeField] private float autoCollectTravelMultiplier = 2f;

        [Header("Tray")]
        [Tooltip("Duration (seconds) of the scale-down tween before a resolved tray slot's items are destroyed.")]
        [SerializeField] private float slotClearDuration = 0.15f;

        // A tray is a fixed-size world sprite (TrayNoPers.png is 1.48 x 1.09 units at PPU
        // 1500, inner surface ~1.18 x 0.80) while a board item is cellSize across, and
        // cellSize is derived from the camera — 1.09 at 9:16, 1.46 at 3:4. So a tray item's
        // size is authored in WORLD UNITS and divided by cellSize at drop time, never as a
        // multiple of the board size: a multiple tuned on a phone overflows the tray on a
        // tablet. These are layout rather than animation numbers, kept here because it is the
        // config both WorldTrayView and BoardItemDragHandler already hold.
        // Why three sizes and not one: equal items cannot be made big. Three equal squares are
        // capped by width in a row and by HEIGHT in any two-row shape — a tray is barely twice
        // an item tall — so rearranging equal items (a triangle was tried) buys about 1%.
        // Standing the main dish at the left over the tray's full height and stacking the
        // other two beside it is what breaks that cap.
        //
        // THE SIZES ARE NOT BOUNDING SQUARES, and assuming they were is what made the first
        // pass too small. ApplyFittedScale fits a sprite by its LARGER side, so this number is
        // the item's long edge and the short edge falls out of the art's own aspect. Measured
        // off the shipped sprites:
        //   main   Hotdog 1.47 w/h, Burger 1.06  -> height is 0.68..0.94 of this number
        //   side   Fries 1.09, cookies ~1.05     -> near square, height ~0.95 of it
        //   drink  Cola/milkshakes ~0.57, Beer 0.75 -> TALL AND NARROW, width ~0.57..0.75 of it
        // So the column is 1.95 x S tall (side 0.95 + drink 1.00) rather than 2 x S, and only
        // S wide — the drink never reaches the column's edge, the side sets it.
        //
        // THE TRAY IS 1.789 x 1.319 WORLD UNITS, and that size is authored where a sprite's
        // world size belongs — TrayNoPers.png's Pixels Per Unit (2218px / 1240 = 1.789), not a
        // scale on the TrayArea transform. A transform scale would have been the quick way to
        // the same picture and is exactly the wrong one here: every number below says WORLD
        // UNITS, and a parent scale silently multiplies all of them. That is the same class of
        // hidden multiplier as OverallScale, which cost this task three rounds of retuning.
        // At scale 1 the tooltips are true and the Inspector is measurable.
        //
        // Solving the packing against a usable 1.694 x 1.233 (food overlapping the tray's rim
        // reads as food ON a tray, so the inner light panel is not the boundary):
        //   column = 1.233 / 1.95 = 0.63, main = 1.694 - 0.63 = 1.05.
        // The BURGER caps the main, not the hotdog — at 1.05 it stands 0.99 tall against 1.233
        // of room, while sizing for the hotdog's flatter 1.47 would have pushed the burger off
        // the tray. Slot CENTRES to match, on TrayArea.prefab's three children:
        // main (-0.32, 0), side (0.53, 0.32), drink (0.53, -0.29).
        [Tooltip("The MAIN DISH's long edge while it sits in a tray, in WORLD UNITS — not a multiple of the board's cell size. It is the hero of a ticket and gets the tray's whole left side. 1.05 is what is left after the side/drink column, and it is capped by the tallest main (the burger, 1.06 w/h), not by the flattest.")]
        [SerializeField, Min(0.01f)] private float trayMainDishWorldSize = 1.05f;
        [Tooltip("The SIDE's long edge while it sits in a tray, in WORLD UNITS. The side is near-square, so it is the item that sets the right-hand column's WIDTH and it is half of what sets the column's height. Raising it past ~0.63 pushes the drink off the bottom of the tray.")]
        [SerializeField, Min(0.01f)] private float traySideWorldSize = 0.63f;
        [Tooltip("The DRINK's long edge while it sits in a tray, in WORLD UNITS — its HEIGHT, since every drink sprite is taller than it is wide (~0.57 w/h). That is why a drink looks narrow at the same number the side looks full: the number is doing what it says. To make a cup visually chunkier, widen the art itself; raising this only makes it taller.")]
        [SerializeField, Min(0.01f)] private float trayDrinkWorldSize = 0.63f;
        [Tooltip("The touch target of a tray-seated item, as a multiple of its own size above. 1 = exactly the item. The packing has no gaps in it, so anything above 1 reaches into the NEIGHBOURING item's target — and two overlapping targets hand the gesture to whichever is nearest the camera instead of to the one the finger is on, which is the defect this whole feature exists to fix. At 0.40-0.78 world units a tray item is already 77-150px on a 1080-wide phone, so there is nothing to buy here.")]
        [SerializeField, Min(0.1f)] private float trayItemHitMultiplier = 1f;

        [Header("Wrong Order")]
        [Tooltip("Duration (seconds) the tray and its contents shake before scattering back to the board on a wrong order.")]
        [SerializeField] private float scatterShakeDuration = 0.5f;
        [Tooltip("Shake strength (world units) for the wrong-order pre-scatter shake.")]
        [SerializeField] private float scatterShakeStrength = 0.3f;
        [Tooltip("Number of full left-right oscillations during the pre-scatter shake.")]
        [SerializeField] private float scatterShakeFrequency = 8f;
        [Tooltip("How much more (multiplier on ScatterShakeStrength) the tray's contents shake compared to the tray itself.")]
        [SerializeField] private float scatterShakeItemMultiplier = 1.2f;

        // What a cleared item does on its way out (D-120). NOT a return of the old design in
        // which Noise Clear dimmed the board's noise for a few seconds -- the item is gone
        // from the model the instant the powerup runs, and what falls is a corpse. These
        // numbers buy legibility, not gameplay: nothing waits for them and the day never
        // pauses.
        [Header("Noise Clear")]
        [Tooltip("How far (world units) a cleared item falls before it is destroyed. It should comfortably clear the bottom of the board.")]
        [SerializeField] private float noiseClearFallDistance = 6f;
        [Tooltip("Duration (seconds) of that fall.")]
        [SerializeField] private float noiseClearFallDuration = 0.55f;
        [Tooltip("Duration (seconds) of the fade that runs alongside the fall. Shorter than the fall leaves the item invisible before it lands.")]
        [SerializeField] private float noiseClearFadeDuration = 0.45f;
        [Tooltip("Extra delay (seconds) added per item, so a sweep reads as a cascade rather than one frame of everything dropping. 0 drops them all together.")]
        [SerializeField] private float noiseClearStagger = 0.03f;

        [Header("Delivery Success")]
        [Tooltip("Scale multiplier the tray (and the just-delivered item) grows to on a successful delivery.")]
        [SerializeField] private float deliveryGrowScale = 1.15f;
        [Tooltip("Duration (seconds) of that grow-up.")]
        [SerializeField] private float deliveryGrowDuration = 0.2f;
        [Tooltip("World-space distance the tray and its contents lift upward while fading out, after the grow.")]
        [SerializeField] private float deliveryLiftDistance = 1.5f;
        [Tooltip("Duration (seconds) of the lift-and-fade-out.")]
        [SerializeField] private float deliveryFadeDuration = 0.35f;
        [Tooltip("Duration (seconds) of the next tray growing back in from below (DeliveryLiftDistance below rest position) once the previous one finishes lifting off.")]
        [SerializeField] private float deliveryReentryDuration = 0.25f;

        [Header("Ticket Card")]
        [Tooltip("How far the ticket card slides (RectTransform units) while fading out, once its tray starts lifting off after a successful delivery.")]
        [SerializeField] private float ticketExitLiftDistance = 150f;
        [Tooltip("Duration (seconds) of the ticket card's exit slide-and-fade.")]
        [SerializeField] private float ticketExitDuration = 0.3f;
        [Tooltip("How far above rest position (RectTransform units) the next ticket's card starts before dropping in.")]
        [SerializeField] private float ticketEntryDropDistance = 150f;
        [Tooltip("Duration (seconds) of the next ticket card's drop-in-and-fade.")]
        [SerializeField] private float ticketEntryDuration = 0.3f;
        [Tooltip("Seconds for ONE full on-off cycle of the danger flash a ticket card runs once its remaining time drops into EconomyConfig's Critical Ratio (the instant the timer bar turns red). The card is lit for the first half and rested for the second — a square wave, so this is the only speed knob. WHICH two colours it flashes between lives on TicketCardVisualsConfig. 0 turns the flash off and leaves the danger image showing solid instead — deliberately not a fast strobe.")]
        [SerializeField, Min(0f)] private float ticketDangerBlinkPeriod = 0.6f;

        [Header("Life Lost")]
        [Tooltip("How far the broken heart rises over the tray that cost a life, in RectTransform units — the hearts are canvas Images, so this is the same unit as the ticket card's own 150-unit exit lift, NOT world units.")]
        [SerializeField] private float lifeLostHeartRiseDistance = 120f;
        [Tooltip("Duration (seconds) of that rise-and-fade. Floored in code at a tenth of a second, so a 0 here is a very quick heart rather than an invisible one.")]
        [SerializeField, Min(0f)] private float lifeLostHeartDuration = 1f;

        [Header("Result Popups")]
        [Tooltip("Seconds the Day Complete popup waits after the day's last delivery before it appears. It exists so the delivery that FINISHED the day is watched instead of covered: the item settling into its slot, the tray growing, lifting and fading, the ticket card sliding away. Add those up (TraySettleDuration + DeliveryGrowDuration + DeliveryFadeDuration + DeliveryReentryDuration) and keep this comfortably above the total. 0 brings the popup up instantly, the way it used to.")]
        [SerializeField, Min(0f)] private float dayCompletePopupDelay = 2.5f;
        [Tooltip("Seconds the Game Over popup waits after the last life is lost before it appears, so the failure is seen rather than covered: the broken heart rising off the tray, and — when the last life went to a wrong order — that tray's shake and its contents scattering back to the board. Keep this above LifeLostHeartDuration AND above ScatterShakeDuration plus the board fly-in. The day is already held for the whole wait (the clock is stopped and the board refuses every pickup), so nothing is playable behind it. 0 brings the popup up instantly, which puts the scatter back underneath it.")]
        [SerializeField, Min(0f)] private float gameOverPopupDelay = 2.5f;

        [Header("Tutorial Spotlight")]
        [Tooltip("How dark the scene goes behind the tutorial's one lit hotdog and one lit tray. 0 = no dimming at all, 1 = solid black. The lit pair and the target tray's ticket card are unaffected -- this is the opacity of the black sheet everything ELSE sits behind.")]
        [SerializeField, Range(0f, 1f)] private float tutorialDimOpacity = 0.72f;
        [Tooltip("Opacity of the ghost hotdog that loops from the lit item to the lit tray. It is a copy of the real item, so 1 would make it indistinguishable from the one the player is meant to grab.")]
        [SerializeField, Range(0f, 1f)] private float tutorialGhostOpacity = 0.55f;
        [Tooltip("Seconds the ghost takes to travel from the item to the tray, once per loop.")]
        [SerializeField, Min(0.05f)] private float tutorialGhostTravelDuration = 0.9f;
        [Tooltip("Seconds of stillness between one ghost arriving and the next one setting off. 0 makes it a continuous stream rather than a repeated gesture.")]
        [SerializeField, Min(0f)] private float tutorialGhostLoopPause = 0.45f;

        // A PREFAB REFERENCE ON A CONFIG OF NUMBERS, and it earns its place here rather than
        // on a scene object (D-122). TutorialSpotlightView is created by WorldTrayView, which
        // is a SCENE component with three instances -- a prefab field there would be three
        // drags that must never disagree. This asset is already handed to every one of them,
        // so the reference costs zero drags, and an asset->asset reference is not the scene
        // lookup this project bans.
        //
        // It also sits exactly where the two fields it REPLACED were: tutorialMessageScreenHeight
        // and tutorialMessageFontSize, deleted 2026-08-28. The prefab carries its own anchors
        // and its own font size, so keeping either would have been a second authority over the
        // same look. Three literals went with them -- the plate's colour, its height (fontSize
        // times three, which clipped the moment a message wrapped to two lines) and its
        // paddings -- none of which an author could reach.
        //
        // IT CARRIES THE ARROWS TOO SINCE D-126, and the rename came with them: the two
        // modification arrows were still being drawn from literals and a generated texture, so
        // an author could not touch them. NO [FormerlySerializedAs] on the rename, deliberately
        // -- carrying the old value forward would leave this pointing at the arrow-less prefab
        // and the arrows would simply never appear, which is the silent failure a clean break
        // avoids. The field goes empty, and the view names the menu step that fills it.
        //
        // Seeded and wired by ExpoTheExplorer > Tutorial > Build Powerup Popups. Unset, a step
        // simply shows no message and no arrows and says so once in the console; the lesson
        // loses its hints and the day stays playable.
        [Tooltip("The prefab a tutorial step draws its message and its two modification arrows from. The message's Canvas must be Screen Space - Overlay — on the game's own canvas it is drawn and then buried (decisions.md D-086).")]
        [SerializeField] private GameObject tutorialStepHintsPrefab;

        [Tooltip("Seconds the arrows and the message take to fade in when a step begins. They appear AFTER the dim so the eye lands on the lit pair first rather than on text.")]
        [SerializeField, Min(0f)] private float tutorialHintFadeDuration = 0.35f;

        public float SnapBackDuration => snapBackDuration;
        public float TraySettleDuration => traySettleDuration;
        public float PopInDuration => popInDuration;
        public float PopInJumpPower => popInJumpPower;
        public float AutoCollectTravelMultiplier => autoCollectTravelMultiplier;
        public float SlotClearDuration => slotClearDuration;
        public float TrayItemHitMultiplier => trayItemHitMultiplier;

        // Keyed on the slot an item ACTUALLY LANDS IN, not on the item's own category —
        // WorldTrayView overflows a second Drink into whichever slot is free, and an item
        // drawn at the main dish's 0.78 while sitting in the drink's corner would hang off
        // the tray. The caller passes the placement category for that reason.
        public float TrayItemWorldSizeFor(FoodCategory category) => category switch
        {
            FoodCategory.Main => trayMainDishWorldSize,
            FoodCategory.Side => traySideWorldSize,
            FoodCategory.Drink => trayDrinkWorldSize,
            _ => traySideWorldSize,
        };
        public float ScatterShakeDuration => scatterShakeDuration;
        public float ScatterShakeStrength => scatterShakeStrength;
        public float ScatterShakeFrequency => scatterShakeFrequency;
        public float ScatterShakeItemMultiplier => scatterShakeItemMultiplier;
        public float NoiseClearFallDistance => noiseClearFallDistance;
        public float NoiseClearFallDuration => noiseClearFallDuration;
        public float NoiseClearFadeDuration => noiseClearFadeDuration;
        public float NoiseClearStagger => noiseClearStagger;
        public float DeliveryGrowScale => deliveryGrowScale;
        public float DeliveryGrowDuration => deliveryGrowDuration;
        public float DeliveryLiftDistance => deliveryLiftDistance;
        public float DeliveryFadeDuration => deliveryFadeDuration;
        public float DeliveryReentryDuration => deliveryReentryDuration;
        public float TicketExitLiftDistance => ticketExitLiftDistance;
        public float TicketExitDuration => ticketExitDuration;
        public float TicketEntryDropDistance => ticketEntryDropDistance;
        public float TicketEntryDuration => ticketEntryDuration;
        public float TicketDangerBlinkPeriod => ticketDangerBlinkPeriod;
        public float LifeLostHeartRiseDistance => lifeLostHeartRiseDistance;
        public float LifeLostHeartDuration => lifeLostHeartDuration;
        public float DayCompletePopupDelay => dayCompletePopupDelay;
        public float GameOverPopupDelay => gameOverPopupDelay;
        public float TutorialDimOpacity => tutorialDimOpacity;
        public float TutorialGhostOpacity => tutorialGhostOpacity;
        public float TutorialGhostTravelDuration => tutorialGhostTravelDuration;
        public float TutorialGhostLoopPause => tutorialGhostLoopPause;
        public GameObject TutorialStepHintsPrefab => tutorialStepHintsPrefab;
        public float TutorialHintFadeDuration => tutorialHintFadeDuration;
    }
}
