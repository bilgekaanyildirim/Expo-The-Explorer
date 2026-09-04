using System.Collections;
using System.Collections.Generic;
using DG.Tweening;
using ExpoTheExplorer.Bootstrap;
using ExpoTheExplorer.Core;
using ExpoTheExplorer.Data;
using UnityEngine;
using UnityEngine.EventSystems;

namespace ExpoTheExplorer.UI
{
    // World-space tray (GDD Section 5, 5.1 — the tray IS the drop area itself,
    // not a separate preview) — an extension of the board's own rendering
    // approach rather than Canvas UI, so the dragged item's real GameObject can
    // be reparented straight into it (BoardItemDragHandler.PlaceInSlot). A
    // Screen Space - Overlay Canvas always composites on top of everything the
    // camera renders, so a SpriteRenderer parented under a Canvas RectTransform
    // could never appear correctly there — being a pure world-space object here
    // sidesteps that entirely, and also makes future animation (item flying
    // into place) a plain Transform tween instead of a Canvas conversion.
    //
    // A standalone scene object (one per ticket slot), not instantiated per
    // TicketCard — slotIndex is set by hand in the Inspector. Single drop
    // hitbox for the whole tray (BoxCollider2D added by hand in the Editor) —
    // which of mainDishSlot/sideSlot/drinkSlot an accepted item belongs to is
    // resolved from its own FoodCategory, not from where exactly it was dropped.
    public class WorldTrayView : MonoBehaviour, IDropHandler
    {
        [SerializeField] private GameManager gameManager;
        [SerializeField] private int slotIndex;
        [SerializeField] private Transform mainDishSlot;
        [SerializeField] private Transform sideSlot;
        [SerializeField] private Transform drinkSlot;
        [Tooltip("Optional — shown while a valid drag is hovering over this tray (GDD Section 5 drop-zone highlight).")]
        [SerializeField] private GameObject highlightVisual;
        [Tooltip("Optional — shown for the duration of the wrong-order pre-scatter shake.")]
        [SerializeField] private GameObject wrongVisual;
        [Tooltip("Shared tuning for board/tray animation durations (snap-back, tray settle, pop-in, slot clear).")]
        [SerializeField] private BoardAnimationConfig animConfig;
        [Tooltip("Needed so a wrong-order scatter can tell BoardView to fly those items in from this tray instead of Starting Point.")]
        [SerializeField] private BoardView boardView;
        [Tooltip("Needed to look up this slot's TicketCardView so its own exit/entry animation can sync with a successful delivery's lift-off.")]
        [SerializeField] private TicketCardsView ticketCardsView;

        // The three authored tray layouts, one per ticket size. A tray belongs to a TICKET
        // (D-129) and so does the shape it stands in: a one-item order gets one centred
        // slot, not one filled slot and two empty holes.
        //
        // These are PREFAB references, and that is the whole reason this works at all. The
        // scene's three trays carry references that only exist in a scene (gameManager,
        // boardView, ticketCardsView) plus a per-instance slotIndex, they register
        // themselves with GameManager in Awake, and AutoCollectRunner, TutorialDirector and
        // BoardItemDragHandler all hold live references to the instance -- so swapping the
        // OBJECT for a different prefab on every ticket would mean re-establishing all of
        // that in the middle of a delivery cascade. Nothing is instantiated or destroyed
        // here. The tray keeps its identity and ADOPTS the chosen prefab's layout, which is
        // the only thing the three prefabs actually differ in (TrayArea2Item deletes
        // DrinkSlot and centres SideSlot on y; TrayArea1Item deletes both and centres
        // MainDishSlot on x; sprite, collider and rest scale are identical in all three).
        //
        // Typed as WorldTrayView rather than GameObject so the layout is read off the
        // prefab's OWN mainDishSlot/sideSlot/drinkSlot fields -- the variants null the slots
        // they remove, so "does this size have a Side slot" is already authored there and
        // nothing has to be looked up by name.
        // THE THREE-ITEM LAYOUT IS NOT ONE OF THESE FIELDS. It is this tray's own authored
        // state, snapshotted in Awake (authoredMain/Side/Drink below) — because the scene's
        // trays ARE TrayArea3Item instances, so the full layout is already standing right
        // here. A `threeItemLayout` field existed for exactly one commit and was the bug
        // D-167a fixes: Unity REMAPS a prefab-internal reference to the instance when it
        // instantiates, so a field dragged to "the prefab I am editing" arrives in the scene
        // pointing at ITSELF, and restoring the big layout became a self-copy that changed
        // nothing.
        [Tooltip("Optional — the tray layout for a one-item ticket (TrayArea1Item). Unwired, a one-item order simply gets the full three-slot tray.")]
        [SerializeField] private WorldTrayView oneItemLayout;
        [Tooltip("Optional — the tray layout for a two-item ticket (TrayArea2Item). Unwired, a two-item order simply gets the full three-slot tray.")]
        [SerializeField] private WorldTrayView twoItemLayout;


        // This tray's drop target. Taken from its own GameObject rather than serialized: it is
        // the collider that makes OnDrop fire, so it is never anywhere else, and a slot nobody
        // filled would silently disable the whole tray. See RefreshTutorialPressThrough.
        private Collider2D dropCollider;

        private bool isValid;
        private int lastKnownCount = -1;
        private bool justDelivered;
        private Vector3 restScale;
        private Vector3 restPosition;
        private TicketCardView ticketCardView;
        private bool inDropCall;

        // True from the moment a drop delivers until that delivery's animation has
        // emptied the slots — the item's settle flight plus the tray's grow/lift/fade.
        // TryAcceptDrop refuses everything for that window because the MODEL is already
        // a ticket ahead: TrayManager emptied this tray and handed the slot to the next
        // order the instant the batch resolved, so an item accepted now would be seated
        // into slots DestroySlotChildrenImmediate is about to wipe — counted by
        // TrayManager and invisible to the player. Auto-Collect never trips this; it
        // already stops on the slot it delivered (AutoCollectRunner's ReferenceEquals
        // check).
        private bool deliveryInProgress;

        // The item whose settle flight the delivery is waiting on, held for exactly one
        // reason: that flight's OnComplete is what starts the delivery, and DOTween KILLS
        // a tween whose target GameObject is destroyed rather than completing it. A day
        // reset landing in that gap would leave deliveryInProgress stuck true and this
        // tray refusing every drop for the rest of the session — Update below watches for
        // it. Nothing is ever called on this reference; only its liveness is read.
        private BoardItemDragHandler deliveringItem;

        // The spotlight THIS tray raised, while it is the tutorial's target. Held so it can
        // be dismissed synchronously at the step boundary rather than left to Unity's
        // end-of-frame Destroy -- see DismissTutorialSpotlight.
        private TutorialSpotlightView tutorialSpotlight;

        // Whether this tray is currently drawn at all. A tray belongs to a TICKET, not to
        // the scene: once the Day's sequence is exhausted this slot is handed null forever
        // (TicketSlotManager.AssignTicket) and TicketCardView.RebuildContent(null) takes its
        // card down to alpha 0 -- but the tray underneath used to grow straight back in and
        // sit there full-size beneath an invisible ticket, waiting for an order that was
        // never coming. It now leaves with the card, and comes back only when a ticket does.
        private bool trayShown = true;

        // Mirrors what this tray last told its card through SetTrayAnimating -- the same
        // fact, kept locally so RefreshTrayPresence can wait for the tray's own
        // grow/lift/fade or slot-clear to FINISH before taking it away. Written in exactly
        // one place (SetTrayAnimating below) so the two cannot drift apart.
        private bool trayAnimating;

        // The ticket this tray's slots are currently ARRANGED for -- not the one it is
        // holding items for. Held so RefreshTrayLayout can tell "same order, nothing to do"
        // from "a new order, possibly a different size", and so the two callers of
        // ApplyTicketLayout can both be unconditional without either doing the work twice.
        // Compared by reference: a slot handed a second ticket that happens to want the same
        // three foods is still a different order, and re-applying an identical layout costs
        // three SetActive calls that change nothing.
        private Ticket layoutTicket;

        // One slot's authored arrangement: whether this layout HAS the slot at all, and
        // where it sits when it does. Read either off a layout prefab's own slot transform
        // (a variant nulls the slots it deletes, so a missing slot answers false) or off
        // this tray's own slots in Awake.
        private readonly struct SlotLayout
        {
            public SlotLayout(Transform slot)
            {
                Present = slot != null && slot.gameObject.activeSelf;
                LocalPosition = slot != null ? slot.localPosition : Vector3.zero;
            }

            public bool Present { get; }
            public Vector3 LocalPosition { get; }
        }

        // This tray's layout AS AUTHORED, captured in Awake before any ticket can rearrange
        // it — the three-item layout, and the answer whenever a smaller layout is unwired.
        // Snapshotted rather than read back from the object on demand, which is the entire
        // fix in D-167a: once a two-item order has moved SideSlot and switched DrinkSlot
        // off, the object no longer remembers what it started as, and a slot's Transform
        // reference stays perfectly valid while its GameObject is inactive — so "read my own
        // slots" would answer with the small tray's arrangement and call it the big one.
        private SlotLayout authoredMain;
        private SlotLayout authoredSide;
        private SlotLayout authoredDrink;

        private bool HasTicket => gameManager.State.TicketSlots[slotIndex] != null;

        // Every animation boundary in this view goes through here rather than calling the
        // card directly, so the local flag and the card's are always set as one.
        private void SetTrayAnimating(bool animating)
        {
            trayAnimating = animating;
            ticketCardView.SetTrayAnimating(animating);
        }

        private void Awake()
        {
            isValid = ValidateReferences();
            dropCollider = GetComponent<Collider2D>();

            // Before anything else in this class runs, and that is the point: the first
            // ApplyTicketLayout can only happen in Start, so what is captured here is what
            // the prefab authored.
            authoredMain = new SlotLayout(mainDishSlot);
            authoredSide = new SlotLayout(sideSlot);
            authoredDrink = new SlotLayout(drinkSlot);

            if (highlightVisual != null) highlightVisual.SetActive(false);
            if (wrongVisual != null) wrongVisual.SetActive(false);

            // In Awake rather than Start, and safe here even though the note below explains
            // why most of this class is not: RegisterTray only writes into an array that is a
            // FIELD INITIALIZER on GameManager, so it exists from the moment that object is
            // constructed -- no Awake of its own has to have run. Registering this early is
            // what makes a tray reachable by another tray's first spotlight, whatever order
            // Unity starts the three of them in.
            if (isValid) gameManager.RegisterTray(slotIndex, this);
        }

        // GameManager.Awake() builds TrayManager, but Unity doesn't guarantee
        // Awake() order between separate root objects — reading gameManager.TrayManager
        // here instead of in Awake() ensures GameManager has already run (Unity
        // always finishes every object's Awake() before any Start() runs).
        private void Start()
        {
            if (!isValid) return;

            lastKnownCount = gameManager.TrayManager.GetContents(slotIndex).Count;
            restScale = transform.localScale;
            restPosition = transform.position;
            // TicketCardsView.Awake() instantiates its cards at runtime, so
            // this can't be wired by hand in the Editor — resolved here
            // instead, safe because Unity finishes every object's Awake()
            // before any Start() runs.
            ticketCardView = ticketCardsView.GetCard(slotIndex);

            // The same Awake ordering makes this the REAL answer rather than a
            // not-yet-started day's: GameManager.Awake ends with
            // TicketSlotManager.FillEmptySlots, so the three slots already hold
            // their first tickets. A slot that somehow begins empty starts hidden
            // with no animation at all — there is nothing to animate away from,
            // and RefreshTrayPresence would otherwise play a departure for a tray
            // the player never saw arrive.
            trayShown = HasTicket;
            if (!trayShown) ApplyHiddenState();

            // The opening ticket's layout, applied before anything can be seated in it --
            // ahead of ScheduleTrayPreSeed below, which is what puts a Day-authored item
            // into this tray a tween tick from now. Same Awake ordering as trayShown above
            // makes this the real first ticket rather than a not-yet-started day's null.
            ApplyTicketLayout();

            // Subscribe, then sync -- the same shape BoardView uses for CellChanged, and
            // for the same reason: the FIRST arm happens in GameManager.Awake, before any
            // Start could have subscribed, while a RETRY re-arms mid-scene long after every
            // Start has run. One of the two would be missed by either half alone.
            gameManager.TutorialStepChanged += OnTutorialStepChanged;
            OnTutorialStepChanged();

            gameManager.State.TicketDelivered.Subscribe(OnTicketDelivered);
            gameManager.State.TraySlotScatterBegin.Subscribe(OnTraySlotScatterBegin);
            gameManager.State.TraySlotScatterEnd.Subscribe(OnTraySlotScatterEnd);

            // Same subscribe-then-sync shape as the tutorial hook above, and the same two
            // halves: a retry or a day advance re-publishes DaySessionStarted mid-scene, but
            // the FIRST one fired inside GameManager.Awake before this Start could subscribe.
            // TelemetryBinder documents the identical gap for the identical event.
            gameManager.State.DaySessionStarted.Subscribe(OnDaySessionStarted);
            ScheduleTrayPreSeed();
        }

        private void OnDestroy()
        {
            if (!isValid) return;
            gameManager.TutorialStepChanged -= OnTutorialStepChanged;
            gameManager.State.TicketDelivered.Unsubscribe(OnTicketDelivered);
            gameManager.State.TraySlotScatterBegin.Unsubscribe(OnTraySlotScatterBegin);
            gameManager.State.TraySlotScatterEnd.Unsubscribe(OnTraySlotScatterEnd);
            gameManager.State.DaySessionStarted.Unsubscribe(OnDaySessionStarted);
        }

        private void OnDaySessionStarted(int _) => ScheduleTrayPreSeed();

        // Lets a press through to the item this tray is HOLDING, by standing out of its way
        // (D-165d). The tray wins that press otherwise, and the reason is a mismatch nothing
        // about either object makes visible:
        //
        // Physics2DRaycaster reads the sort key off the SpriteRenderer on the COLLIDER'S OWN
        // GameObject. The tray root has one; a board item's container does NOT -- its sprites
        // are children, one level down -- so the raycaster scores the item at a flat 0 no
        // matter what its layers are set to. Normally that still wins, because the tray's own
        // sprite is authored at -2. But the tutorial dim lifts a lit tray by +600, and 598
        // beats 0, so every item inside a lit tray stops being pressable. Measured, not
        // guessed: the press log reported TrayArea at 598 and Item_0_0 at 0, same distance.
        //
        // Only the SOURCE tray of an armed tray move stands aside, and only while that step
        // runs. It costs nothing: a tray's collider exists to be a DROP target, and the step's
        // own drop gate already refuses this tray -- the item is going to the OTHER one.
        private void RefreshTutorialPressThrough()
        {
            if (dropCollider == null) return;

            var tutorial = gameManager.Tutorial;
            var standAside = tutorial != null && tutorial.SpotlightSourceTraySlotIndex == slotIndex;

            dropCollider.enabled = !standAside;
        }

        // DEFERRED BY ONE TWEEN TICK, and both reasons are load-bearing -- this is not the
        // "harmless delay" the spotlight's identical call can afford (D-165).
        //
        // 1. The day-start sequence CLEARS this tray after publishing the event that gets us
        //    here: TicketSlotManager's opening fill publishes TicketAssigned three times and
        //    GameManager.OnTicketAssigned answers each with TrayManager.OnTicketAssigned,
        //    which empties that slot's tray. A seed placed synchronously would be wiped a
        //    few statements later, by design rather than by accident.
        // 2. BoardView builds its item containers in its OWN Start, which Unity does not
        //    order against GameManager.Awake or against this Start. The seeding needs a
        //    rendered drag handler, so it cannot run before that.
        //
        // The visible cost is one frame of an empty tray at Day Start, behind a held clock
        // (the tutorial holds it for its whole run, and an item-intro popup holds it too), so
        // there is no frame in which the player could see the tray fill.
        private void ScheduleTrayPreSeed()
        {
            DOVirtual.DelayedCall(0f, ApplyTrayPreSeed).SetLink(gameObject);
        }

        // Seats whatever this Day authored into THIS tray. Each tray seeds only its own slot,
        // which is what keeps this free of any cross-tray coordination -- the same reason
        // OnTutorialStepChanged needs none.
        private void ApplyTrayPreSeed()
        {
            if (this == null || !isValid) return;

            var seeds = gameManager.TrayPreSeedForCurrentDay;
            if (seeds == null) return;

            // A tray that already holds something is NOT seeded on top of. This runs again on
            // every retry and day advance, and a retry that arrived while the tray still held
            // the previous attempt's items would otherwise stack a second copy -- and the
            // count TrayManager keeps is what decides when an order is complete.
            if (gameManager.TrayManager.GetContents(slotIndex).Count > 0) return;

            foreach (var seed in seeds)
            {
                if (seed == null || seed.TraySlotIndex != slotIndex || seed.Item == null) continue;

                SeatOneItem(new BoardItem(seed.Item, seed.Modifications));
            }
        }

        // Goes onto the board and straight back off it, which looks like a detour and is the
        // opposite: it is what lets this reuse the ONE path that puts an item in a tray. A
        // drag handler is built by BoardView for a board cell and by nothing else, and the
        // alternative -- writing into TrayManager directly -- gives a tray that counts an
        // item it does not draw.
        //
        // NOTHING RENDERS BETWEEN THE TWO HALVES. BoardGrid publishes CellChanged
        // synchronously (EventBus.Publish is a plain invoke), so the container exists by the
        // time TryPlaceItem returns, and the seat happens before this method does -- all
        // inside one frame, with no Update and no render in between. The fly-in is suppressed
        // for the same reason: the item is not appearing on the board, it is passing through.
        private void SeatOneItem(BoardItem item)
        {
            var board = gameManager.State.Board;
            if (!board.TryGetFirstEmptyCell(out var x, out var y))
            {
                Debug.LogError(
                    $"{nameof(WorldTrayView)} on '{name}': Day {gameManager.State.CurrentDayIndex} seeds an item into " +
                    $"tray {slotIndex}, but the board has no free cell to build it through, so that tray opens " +
                    "empty. A tutorial step that expects it will abort.", this);
                return;
            }

            boardView.BeginFlyInOverride(null);
            try
            {
                if (!board.TryPlaceItem(item, x, y)) return;
                if (!boardView.TryGetDragHandler(x, y, out var handler)) return;

                // The pickup half of a gesture no finger performs -- the same call
                // AutoCollectRunner makes before handing an item to a tray, and for the same
                // two reasons (D-113): it LANDS the fly-in this placement just started, so the
                // board's tween and the tray's seat are never writing this transform at once,
                // and it records the resting scale that the delivering-item path later reads.
                // Skipping it was a straight omission from the established pointerless path.
                handler.SettleForAutoCollect();

                TryAcceptPreSeedDrop(handler);
            }
            finally
            {
                boardView.EndFlyInOverride();
            }
        }

        // TrayManager.TryAddItem calls deliverTicket (-> TicketSlotManager.
        // DeliverTicket -> this publish) synchronously before returning, so
        // by the time TryAcceptDrop's call to TryAddItem below returns,
        // justDelivered is already correctly set for this exact drop if it
        // was the one that completed a successful delivery on this slot.
        private void OnTicketDelivered((int SlotIndex, Ticket Ticket) delivery)
        {
            if (delivery.SlotIndex == slotIndex) justDelivered = true;
        }

        // These bracket exactly TrayManager.ScatterBackToBoard's own
        // RequestSpawn calls for THIS slot (a wrong order or a timeout,
        // never a delivery) — narrower than wrapping the whole TryAddItem
        // call, which on a successful delivery also cascades into the
        // *next* ticket's unrelated required-item spawn and would wrongly
        // tag that as flying in from this tray too.
        //
        // inDropCall distinguishes which of those two this actually is: only
        // a wrong order (inDropCall true, set around TryAcceptDrop's own
        // TryAddItem call below) delays the scattered items' board pop-in to
        // match PlayWrongOrderShakeThenScatter's shake — a timeout scatter
        // (inDropCall false, triggered from GameManager.OnTicketAssigned
        // instead) isn't preceded by any shake, so it keeps appearing
        // immediately.
        private void OnTraySlotScatterBegin(int scatteringSlotIndex)
        {
            if (scatteringSlotIndex != slotIndex) return;
            var delay = inDropCall ? animConfig.ScatterShakeDuration : 0f;
            boardView.BeginFlyInOverride(transform.position, delay);
        }

        private void OnTraySlotScatterEnd(int scatteringSlotIndex)
        {
            if (scatteringSlotIndex == slotIndex) boardView.EndFlyInOverride();
        }

        // Only the ONE tray the Day named raises the spotlight, and it does so because it is
        // the only object already holding both of the ghost's endpoints -- a serialized
        // boardView for the source cell, and its own transform for the destination. That is
        // what lets the whole effect exist with no new scene object and nothing to wire.
        // Runs on every step boundary, in every tray -- so the tray that just stopped being
        // the target tears its spotlight down and the tray that just became one raises the
        // next. A tray that is neither does nothing, which is why this needs no coordination
        // between the three of them.
        private void OnTutorialStepChanged()
        {
            DismissTutorialSpotlight();

            // BEFORE the target check, deliberately: the tray that has to stand aside is the
            // step's SOURCE, which is by definition not its target, so anything after that
            // early return would never run on the one tray this concerns.
            RefreshTutorialPressThrough();

            if (!IsTutorialTarget()) return;

            // Deferred to the NEXT FRAME, and it used to be one tween tick. Two reasons now
            // ride on it, and only the first was known when this was written:
            //
            // 1. BoardView builds its item containers in its OWN Start, and Unity does not
            //    order Starts between root objects, so on a scene's first day the source item
            //    may genuinely not exist at this instant.
            // 2. THE STEP BEFORE THIS ONE IS STILL BEING TORN DOWN. DismissTutorialSpotlight
            //    ran a line ago and TutorialDim.Restore removes a lifted card's Canvas with
            //    Object.Destroy -- which Unity defers to the END OF THE FRAME. A raise in the
            //    same frame therefore finds that Canvas still on the card, and TutorialDim.Lift
            //    treats "already has a Canvas" as "already lifted" and does nothing. The stale
            //    Canvas is then destroyed and the card drops behind the dim sheet: lit on the
            //    first step, dark on every step after it. A tween tick could land in the same
            //    frame (DOTween's update and the EventSystem's both run in Update, and the
            //    order between them is fixed for a build, which is why this reproduced every
            //    time); a frame boundary cannot.
            //
            // A coroutine rather than DOVirtual for exactly that reason -- "next frame" is the
            // guarantee being bought, and yield return null is the one construct that spells
            // it. Unity stops it if this object dies, which is what SetLink was doing.
            if (spotlightRaiseRoutine != null) StopCoroutine(spotlightRaiseRoutine);
            spotlightRaiseRoutine = StartCoroutine(RaiseTutorialSpotlightNextFrame());
        }

        // Torn down through Dismiss rather than plain Destroy so the sorting orders it lifted
        // are put back SYNCHRONOUSLY. Unity defers Destroy to the end of the frame while the
        // next step's spotlight is built immediately, so anything else would restore the old
        // step's renderers after the new step had already lifted its own.
        private void DismissTutorialSpotlight()
        {
            if (tutorialSpotlight == null) return;

            tutorialSpotlight.Dismiss();
            tutorialSpotlight = null;
        }

        // Asked of the director rather than worked out from Current's fields (D-098). Reading
        // TargetTraySlotIndex here was wrong in a way that looked right: a step that names NO
        // tray -- the powerup panel -- leaves that field unset, and unset is 0, so tray 0
        // claimed the panel as its own forced move and went looking for an item at the equally
        // unset cell (0,0). SpotlightTraySlotIndex answers -1 for every such step.
        private bool IsTutorialTarget()
        {
            var tutorial = gameManager.Tutorial;
            return tutorial != null && tutorial.SpotlightTraySlotIndex == slotIndex;
        }

        private Coroutine spotlightRaiseRoutine;

        private IEnumerator RaiseTutorialSpotlightNextFrame()
        {
            yield return null;

            spotlightRaiseRoutine = null;
            RaiseTutorialSpotlight();
        }

        private void RaiseTutorialSpotlight()
        {
            // Re-checked rather than trusted from OnTutorialStepChanged: a frame passed, and
            // a step boundary, a retry or a scene teardown can have landed in between.
            if (this == null || !IsTutorialTarget()) return;

            var tutorial = gameManager.Tutorial;
            var step = tutorial.Current;

            // Which of the two sources this step has is asked of the DIRECTOR, not worked out
            // from the step's fields, for the reason IsTutorialTarget already gives (D-098):
            // an unset source is 0, which is a real tray and a real cell, so a reader that
            // guesses is told a plausible lie. -1 means "this ghost starts on the board".
            var sourceTraySlotIndex = tutorial.SpotlightSourceTraySlotIndex;
            var sourceHandler = sourceTraySlotIndex >= 0
                ? FindSeatedItem(sourceTraySlotIndex)
                : ResolveBoardSourceHandler(step.SourceX, step.SourceY);

            if (sourceHandler == null) return;

            // A TRAY MOVE DIMS NO CARD AT ALL, and that is a rule about what the step teaches
            // rather than a concession. A forced move points at ONE order and the dim's job is
            // to say which -- every other card is noise. A tray move says the opposite thing:
            // an item can leave one order and go to another, so the player has to be able to
            // read the orders to see why this one is moving.
            //
            // A CARD IS NOT LIT BY BEING LEFT ALONE, which is what the first attempt at this
            // assumed and why it did nothing (D-165f). The cards ride InGameCanvas, a Screen
            // Space - CAMERA canvas at sortingOrder -1, so the dim sheet covers them; a curtain
            // is a SECOND layer of dark on top of that. Being readable takes an explicit lift,
            // so every card that must stay bright goes in litCards, and dimmedCards keeps
            // meaning "darker still than the sheet".
            var dimmedCards = new List<RectTransform>();
            var litCards = new List<TicketCardView>();

            for (var i = 0; i < GameState.TicketSlotCount; i++)
            {
                var card = ticketCardsView.GetCard(i);
                if (card == null) continue;

                // This tray's own card is lit by Build itself (the modification arrow points
                // into it), so it needs no entry on either list.
                if (i == slotIndex) continue;

                // A TRAY MOVE LIGHTS BOTH ORDERS IT TOUCHES and dims the rest -- the source
                // tray's card joins the target's, because the step teaches that an item can
                // leave ONE order for ANOTHER and the player has to read both to see the
                // trade. A third order is not part of that sentence, so it stays dark, exactly
                // as it does under a forced move.
                if (i == sourceTraySlotIndex) litCards.Add(card);
                else if (card.transform is RectTransform cardRect) dimmedCards.Add(cardRect);
            }

            // Both ENDS of a tray-to-tray move are lit, not just this one. A forced move's
            // source is a board item, which the dim lifts on its own; a tray move's source is
            // an item sitting INSIDE another tray, and lifting the item while leaving its tray
            // dark leaves it floating over a hole. The player is being told "take it from
            // there", so there has to be a there.
            var sourceTray = sourceTraySlotIndex >= 0 ? gameManager.TrayForSlot(sourceTraySlotIndex) : null;
            var litExtras = sourceTray != null && sourceTray != this
                ? new[] { transform, sourceTray.transform }
                : new[] { transform };

            tutorialSpotlight = TutorialSpotlightView.Create(
                tutorial,
                animConfig,
                sourceHandler.transform,
                sourceHandler.CurrentItem,
                transform,
                litExtras,
                dimmedCards,
                ticketCardView,
                litCards);
        }

        // The board half of a step's source, kept as its own method only so the two halves
        // read alike at the call site. Unchanged behaviour, including the error.
        //
        // Takes the two coordinates rather than the step, so this file still names no type
        // from the Tutorial assembly -- it reaches that system only through GameManager,
        // which is the one object holding both sides.
        private BoardItemDragHandler ResolveBoardSourceHandler(int sourceX, int sourceY)
        {
            if (boardView.TryGetDragHandler(sourceX, sourceY, out var handler)) return handler;

            // GameManager already refused to start a step whose cell is empty, so reaching
            // here means the MODEL has an item the VIEW never built a container for. That
            // is a BoardView problem rather than a content one, hence a different sentence
            // than the step guard's.
            Debug.LogError(
                $"{nameof(WorldTrayView)}: the tutorial step's source cell ({sourceX}, {sourceY}) " +
                "has an item in the board model but no rendered container, so no ghost can be shown. The step " +
                "is still enforced -- it just has nothing to point at.", this);
            return null;
        }

        // The tray half: the item currently seated in ANOTHER tray, which is where a tray-move
        // step's ghost starts (D-165).
        //
        // It reads that tray's own children rather than TrayManager's contents, because what
        // the ghost needs is a Transform to copy a sprite stack from, and a tray's contents
        // ARE its children -- the same fact ClearSlot and ReturnItemsToBoard already walk.
        // The `trays` array is what makes the other tray reachable at all; unwired, the step
        // still runs and only the ghost is missing, which is the correct way for a missing
        // Inspector reference to fail here.
        private BoardItemDragHandler FindSeatedItem(int sourceTraySlotIndex)
        {
            var sourceTray = gameManager.TrayForSlot(sourceTraySlotIndex);
            if (sourceTray != null) return sourceTray.FirstSeatedItem();

            // Nothing to wire and nothing to fix in a scene: every tray registers itself, so
            // reaching this means that tray has not started yet or this scene has none.
            Debug.LogError(
                $"{nameof(WorldTrayView)} on '{name}': this tray is the target of a tray-to-tray tutorial step " +
                $"whose item sits in tray {sourceTraySlotIndex}, but no tray has registered for that slot, so " +
                "no ghost can be shown. The step is still enforced -- it just has nothing to point at.", this);
            return null;
        }

        // The item currently seated in THIS tray, or null when it holds none.
        //
        // Walks the three SLOTS rather than this object's own children, which is the whole
        // correction: SeatInSlot parents an item to mainDishSlot/sideSlot/drinkSlot, one level
        // below the tray, so a direct-children scan finds nothing at all -- and did, which is
        // why the first build of the tray-to-tray ghost never appeared. ReturnItemsToBoard and
        // ClearSlot walk the same three for the same reason.
        public BoardItemDragHandler FirstSeatedItem()
        {
            return SeatedIn(mainDishSlot) ?? SeatedIn(sideSlot) ?? SeatedIn(drinkSlot);

            static BoardItemDragHandler SeatedIn(Transform slot)
            {
                if (slot == null) return null;

                foreach (Transform child in slot)
                {
                    var handler = child.GetComponent<BoardItemDragHandler>();

                    // A child mid-drag has already been taken out of this tray's count by
                    // OnBeginDrag, so it is not something the ghost should point at -- the
                    // same check ClearSlot makes, for the same reason.
                    if (handler == null || handler.IsDragging) continue;

                    return handler;
                }

                return null;
            }
        }

        // Puts an item BACK into this tray after a drop that had nowhere legal to go (D-165c).
        // Not the pre-seed path even though both bypass the player-drag gates: this one
        // travels, because the player DID move it and watching it fly home is what says the
        // move was refused rather than silently undone.
        public bool TryAcceptReturnDrop(BoardItemDragHandler dragHandler) =>
            TryAcceptDrop(dragHandler, 1f, fromPlayerDrag: false);

        private bool ValidateReferences()
        {
            var missing = new List<string>();
            if (gameManager == null) missing.Add(nameof(gameManager));
            if (mainDishSlot == null) missing.Add(nameof(mainDishSlot));
            if (sideSlot == null) missing.Add(nameof(sideSlot));
            if (drinkSlot == null) missing.Add(nameof(drinkSlot));
            if (boardView == null) missing.Add(nameof(boardView));
            if (ticketCardsView == null) missing.Add(nameof(ticketCardsView));

            if (missing.Count == 0) return true;

            Debug.LogError($"{nameof(WorldTrayView)} on '{name}' is missing Inspector reference(s): {string.Join(", ", missing)}.", this);
            return false;
        }

        // A slot this tray does not currently HAVE answers null, exactly as an unwired one
        // does, and that is what makes the smaller layouts safe with no change to the
        // overflow chain below: ResolvePlacementCategory already skips a null slot, so an
        // item can never be seated into a slot that is switched off and invisible. Deciding
        // it here rather than at each reader is the point -- SlotFor is what "this tray has
        // a Side" means, and two definitions of that is how an item ends up parented to
        // something nobody can see.
        private Transform SlotFor(FoodCategory category)
        {
            var slot = category switch
            {
                FoodCategory.Main => mainDishSlot,
                FoodCategory.Side => sideSlot,
                FoodCategory.Drink => drinkSlot,
                _ => null,
            };

            return slot != null && slot.gameObject.activeSelf ? slot : null;
        }

        // Arranges the three slots the way the layout prefab for this ticket's item count
        // authored them: a slot the prefab deleted is switched off, a slot it kept is
        // switched on and moved to the position it holds THERE. The tray object itself is
        // untouched -- see the layout fields for why nothing is instantiated.
        //
        // Reference-compared against the last ticket applied, so this is a no-op on every
        // call but the ones that matter, and both callers can be unconditional.
        //
        // A null ticket is left alone deliberately: a slot with no order is a tray on its
        // way out (D-129), and rearranging one mid-departure would rearrange something the
        // player is watching shrink for no reason. The next ticket brings its own layout.
        private void ApplyTicketLayout()
        {
            var ticket = gameManager.State.TicketSlots[slotIndex];
            if (ticket == null || ReferenceEquals(ticket, layoutTicket)) return;

            var source = LayoutSourceFor(ticket.RequiredItems.Count);
            layoutTicket = ticket;

            ApplySlotLayout(mainDishSlot, source != null ? new SlotLayout(source.mainDishSlot) : authoredMain);
            ApplySlotLayout(sideSlot, source != null ? new SlotLayout(source.sideSlot) : authoredSide);
            ApplySlotLayout(drinkSlot, source != null ? new SlotLayout(source.drinkSlot) : authoredDrink);
        }

        // Null means "this tray's own authored layout", which is the right answer twice
        // over: for a three-item order, because these trays ARE TrayArea3Item instances, and
        // for a smaller order whose field nobody dragged, because a forgotten drag should
        // cost a three-slot tray on a one-item order and never a tray still wearing the
        // PREVIOUS order's shape. An item count outside 1..3 cannot happen (a ticket always
        // has a Main and at most one Side and one Drink) and lands on the same answer.
        //
        // A field pointing at this very component is treated as unwired rather than obeyed.
        // That is not defensiveness about a typo: dragging the prefab you are editing into
        // one of these fields produces a prefab-INTERNAL reference, which Unity silently
        // remaps to the instance, and obeying it would copy every slot's position onto
        // itself while reading a switched-off slot's still-valid Transform as "present".
        private WorldTrayView LayoutSourceFor(int requiredItemCount)
        {
            var source = requiredItemCount switch
            {
                1 => oneItemLayout,
                2 => twoItemLayout,
                _ => null,
            };

            return source == this ? null : source;
        }

        // Presence AND position are applied together, because in these prefabs they are one
        // authoring act: TrayArea2Item does not merely delete DrinkSlot, it then re-centres
        // SideSlot in the room that freed up.
        private static void ApplySlotLayout(Transform slot, SlotLayout layout)
        {
            if (slot == null) return;

            slot.gameObject.SetActive(layout.Present);
            if (layout.Present) slot.localPosition = layout.LocalPosition;
        }

        // Where a second item of the same category should overflow to when
        // its own slot is already occupied (e.g. a wrong-order drop landing
        // a second Drink before the tray's batch check resolves) — picked
        // by hand per category rather than a generic rotation, so it's not
        // symmetric (every category prefers Side first, then whichever of
        // Main/Drink isn't itself).
        private static readonly Dictionary<FoodCategory, FoodCategory[]> OverflowOrder = new()
        {
            { FoodCategory.Side, new[] { FoodCategory.Drink, FoodCategory.Main } },
            { FoodCategory.Drink, new[] { FoodCategory.Side, FoodCategory.Main } },
            { FoodCategory.Main, new[] { FoodCategory.Side, FoodCategory.Drink } },
        };

        // Prefers the item's own category slot; if it's already occupied,
        // falls back through OverflowOrder to the next empty slot so two
        // items dropped into the tray before it's full never render stacked
        // on top of each other. At most 3 items ever sit in a tray before
        // TrayManager's batch check clears it (RequiredItems.Count maxes at
        // 3, matching the 3 physical slots), so an empty slot is always
        // found before this needs to fall back to the (occupied) preferred
        // slot as a last resort.
        // Returns the CATEGORY whose slot this item lands in, which is not always its own.
        // Split out from ResolvePlacementSlot because the destination now decides two things
        // rather than one -- where the item goes AND how big it is drawn (a tray's three
        // places are three different sizes) -- and answering them from two separate walks of
        // the same fallback chain is how they would eventually disagree.
        private FoodCategory ResolvePlacementCategory(FoodCategory category)
        {
            var preferred = SlotFor(category);
            if (preferred != null && preferred.childCount == 0) return category;

            foreach (var fallback in OverflowOrder[category])
            {
                var slot = SlotFor(fallback);
                if (slot != null && slot.childCount == 0) return fallback;
            }

            // Every slot occupied. Falling back to the item's own is the original last
            // resort and still the right one -- but only if this tray HAS that slot: since
            // a smaller layout switches slots off, the own-category answer can now be one
            // that does not exist, and returning it would hand SeatInSlot a null parent and
            // put the item nowhere. Unreachable in practice (a tray never holds more items
            // than its ticket needs, and a layout always has that many slots), which is
            // exactly why it is worth being cheap and total here rather than trusting the
            // arithmetic to hold through the next content change.
            if (SlotFor(category) != null) return category;

            foreach (var fallback in OverflowOrder[category])
            {
                if (SlotFor(fallback) != null) return fallback;
            }

            return category;
        }

        private Transform ResolvePlacementSlot(FoodCategory category) => SlotFor(ResolvePlacementCategory(category));

        // How much an accepted item has to shrink to be a TRAY item rather than a board one,
        // for the slot it is actually landing in (the three are three different sizes).
        //
        // The two sizes live in different spaces and that is the whole reason this exists. A
        // tray is a fixed world sprite -- 1.48 x 1.09 units, inner surface about 1.18 wide --
        // while a board item is one cell across and BoardView derives a cell from the CAMERA
        // (1.09 units at 9:16, 1.46 at 3:4). So an item arrives at very nearly the size of the
        // entire tray, three of them want 3.3 units of a 1.48-unit surface, and they pile up
        // no matter where the slots sit. Nothing had ever changed a tray item's scale: the
        // seat tween moved the item and left its size alone.
        //
        // Dividing an ABSOLUTE authored size by the live cell size is what makes the answer
        // survive an aspect-ratio change. A plain multiplier of the board size tuned on a
        // phone would overflow the tray on a tablet, where cells grow and the tray does not.
        //
        // Falls back to 1 -- the item's own board scale, i.e. today's behaviour -- if the
        // board has not sized itself yet. That is a drop before BoardView's Start, which
        // cannot happen from a finger; taking the un-shrunk item beats dividing by zero.
        // Dividing by the item's OWN OverallScale as well as by the cell is what makes the
        // authored number honest. A board item is not drawn at one cell: BoardItem multiplies
        // every resolved layer by Config.OverallScale, which is 0.85 on nineteen of the
        // twenty-one foods (0.8 and 0.9 on the other two) -- padding so an item does not touch
        // its cell's edges. Ignoring it made every tray item 10-15% smaller than the size that
        // asked for it (fries authored at 0.52 arriving at 0.44, the hotdog 0.78 rather than
        // 0.87), which reads on screen as exactly what it is: gaps nobody asked for.
        //
        // Cancelling that padding is right for a TRAY and would be wrong for the board -- a
        // tray has three fixed places and the art should fill them, a board cell wants the
        // breathing room -- and the board is untouched. It also makes the two off-spec foods
        // sit at the same size as everything else in a tray, which is what a fixed slot wants.
        private float ResolveSeatScale(FoodCategory placementCategory, BoardItem item)
        {
            var cellSize = boardView != null ? boardView.CellSize : 0f;
            if (cellSize <= 0f) return 1f;

            var overallScale = item?.Config != null ? item.Config.OverallScale : 1f;
            if (overallScale <= 0f) overallScale = 1f;

            return animConfig.TrayItemWorldSizeFor(placementCategory) / (cellSize * overallScale);
        }

        public void OnDrop(PointerEventData eventData)
        {
            if (!isValid) return;

            var dragHandler = eventData.pointerDrag != null
                ? eventData.pointerDrag.GetComponent<BoardItemDragHandler>()
                : null;
            if (dragHandler == null) return;

            TryAcceptDrop(dragHandler);
        }

        // Also called directly by BoardItemDragHandler.OnEndDrag as a fallback:
        // the drag-feel hover offset means the item can visually be sitting
        // right on top of this tray while the actual pointer (what OnDrop's
        // own raycast above checks) is still below it, outside this
        // collider — that fallback finds this tray from the item's own
        // displayed position instead and accepts the drop the same way.
        public bool TryAcceptDrop(BoardItemDragHandler dragHandler) =>
            TryAcceptDrop(dragHandler, 1f, fromPlayerDrag: true);

        // Auto-Collect's way in (D-112). Identical to a finger's drop in every respect but
        // the tween's LENGTH -- same acceptance, same batch check, same delivery. The
        // multiplier is read here rather than passed in by AutoCollectRunner because this
        // view already has the config serialized on it, so the powerup needs no reference
        // of its own and there is nothing new to drag in the Inspector.
        //
        // ...AND IT DOES NOT ASK THE TUTORIAL (D-115, found in the first play-test). The
        // gate below exists to constrain the PLAYER'S FINGER; this path is a powerup's own
        // machinery. While the tutorial is asking the player to press Auto-Collect, the
        // armed step refuses every tray -- so the effect it is forcing found nothing it could
        // do, returned false, and the lesson advanced on the press having collected nothing.
        //
        // Safe by construction rather than by care: GameManager.CanUsePowerup already refuses
        // EVERY powerup during a forced move and behind a panel, so a powerup can only reach
        // this line while a PowerupUse step names it -- the one moment it has to work. There
        // is no reachable state where this bypass lets Auto-Collect sweep the item a ghost is
        // pointing at.
        public bool TryAcceptAutoCollectDrop(BoardItemDragHandler dragHandler) =>
            TryAcceptDrop(dragHandler, animConfig.AutoCollectTravelMultiplier, fromPlayerDrag: false);

        // The Day-Start seeding path (D-165): an item this Day authored into a tray, seated
        // through the SAME accept the other two go through, so TrayManager stays the single
        // writer of what a tray holds and nothing writes into a tray's contents directly --
        // the mistake BoardView.TryGetDragHandler's comment already warns about (a tray
        // written straight into the model draws as empty).
        //
        // Travel multiplier ZERO is the whole difference, and it is the point: this item was
        // never moved by anyone, it is simply already there when the player first looks. A
        // flight, even a fast one, would say something happened.
        //
        // fromPlayerDrag: false for the reason Auto-Collect passes it -- the seeding happens
        // while a tutorial step may already be armed, and the drop gate would (correctly)
        // refuse a drop into a tray that step does not name.
        public bool TryAcceptPreSeedDrop(BoardItemDragHandler dragHandler) =>
            TryAcceptDrop(dragHandler, 0f, fromPlayerDrag: false);

        private bool TryAcceptDrop(BoardItemDragHandler dragHandler, float travelMultiplier, bool fromPlayerDrag)
        {
            if (!isValid || dragHandler == null || dragHandler.CurrentItem == null) return false;

            // A tray in the middle of delivering takes nothing — see deliveryInProgress.
            // A false return is this method's ordinary "not accepted" answer, so the item
            // simply snaps back to the board and can be dropped again a moment later.
            if (deliveryInProgress) return false;

            // WAS THIS ITEM EVER PICKED UP? A refused press is not a pickup, and until this
            // line a tray took that item anyway: OnDrop reads eventData.pointerDrag, which
            // UGUI nominates AFTER pointerDown has already been refused, so the refusal
            // reached every method on the handler and none of the ones here. The finger
            // travelled with nothing visibly in it and the tray accepted a delivery at the
            // end of it -- which is how a tutorial step that permits exactly one cell could
            // be answered with any item on the board.
            //
            // It sits ABOVE the tray gate because it is the more basic question, and it is
            // asked only of a finger for the reason on WasPickupRefused: the flag describes a
            // gesture, and Auto-Collect makes none.
            if (fromPlayerDrag && dragHandler.WasPickupRefused) return false;

            // The tutorial's second gate. Refusing HERE rather than inside TrayManager is
            // deliberate: a false return is already this method's "not accepted" answer, so
            // the item snaps back exactly as it does for any other rejected drop, and
            // TraySystem never learns that tutorials exist.
            //
            // Asked only of a FINGER since D-115 -- see TryAcceptAutoCollectDrop above for
            // why a powerup's own machinery is not what this gate is for.
            if (fromPlayerDrag && !gameManager.IsTrayDropAllowed(slotIndex)) return false;

            var item = dragHandler.CurrentItem;
            justDelivered = false;

            // A wrong order's scatter (if this call causes one) is handled
            // by OnTraySlotScatterBegin/End above, triggered from inside
            // TrayManager.ScatterBackToBoard itself — narrower than
            // bracketing this whole call, which on a successful delivery
            // also cascades into the next ticket's unrelated spawn.
            // inDropCall tells that handler this scatter (if any) came from
            // a manual drop rather than a timeout, so it knows to delay the
            // board-side pop-in to match the shake below.
            inDropCall = true;
            var accepted = gameManager.TrayManager.TryAddItem(slotIndex, item, dragHandler.DetachFromBoard);
            inDropCall = false;

            dragHandler.WasAcceptedByTray = accepted;
            if (!accepted) return false;

            // The forced move has landed, so this step is over and the next one (if any)
            // takes over. Announced on ACCEPTANCE rather than on delivery: what a step asks
            // for is "put that item in that tray", and tying it to the payout would leave the
            // scene dark through the whole delivery animation -- or forever, for a step whose
            // order needs more than one item.
            //
            // No spotlight teardown is needed here: this publishes StepChanged, which every
            // tray answers by dismissing its own spotlight synchronously, and only THEN (a
            // tween tick later) does the new target raise the next one. One mechanism for the
            // boundary rather than a second copy of it on this path.
            gameManager.Tutorial?.NotifyTrayAccepted(slotIndex);

            var newCount = gameManager.TrayManager.GetContents(slotIndex).Count;

            // Resolved ONCE for both branches below. Where this item goes and how big it is
            // drawn are the same answer read twice, and the slots it walks are about to gain
            // a child, so asking again after the seat could give a different one.
            var placement = ResolvePlacementCategory(item.Config.Category);

            if (newCount == 0)
            {
                // This drop just completed the tray and TrayManager already ran
                // the batch check (delivered or scattered) — every item that
                // was sitting in our slots (from earlier drops on this same
                // ticket) is stale now, and so is this drop's own. The two
                // branches part company over that last one: a delivery seats it
                // with the others and takes the whole tray out together, a wrong
                // order shakes it where it landed and destroys it on its own.
                if (justDelivered)
                {
                    // Must happen in this exact frame, before PlayDeliverySuccess
                    // (whose own PlayDeliveryTransition trigger is deferred to
                    // the tray's lift phase) — otherwise TicketCardView.Update's
                    // poll catches the already-reassigned ticket on the very
                    // next frame and rebuilds instantly, well before the exit
                    // animation would even start. It is also what lets the wait
                    // below exist at all: the card holds the delivered ticket
                    // open until PlayDeliveryTransition, however long that takes.
                    ticketCardView.SuppressPollUntilDeliveryTransition();

                    // The item that completed the order flies into its slot first
                    // and the delivery starts when it LANDS (D-100). It used to
                    // start here, in this frame, which meant the one item the
                    // player had just dropped never travelled — it grew and lifted
                    // from wherever the finger let go while the tray left without
                    // it.
                    deliveryInProgress = true;
                    deliveringItem = dragHandler;
                    dragHandler.PlaceInSlotAndDeliver(
                        SlotFor(placement), slotIndex, PlayDeliverySuccess,
                        ResolveSeatScale(placement, item), travelMultiplier);
                }
                else
                {
                    PlayWrongOrderShakeThenScatter(dragHandler);
                }
            }
            else
            {
                dragHandler.PlaceInSlot(
                    SlotFor(placement), slotIndex, ResolveSeatScale(placement, item), travelMultiplier);
            }

            lastKnownCount = newCount;
            return true;
        }

        // Auto-Collect's RETURN path (D-110): items this tray's ticket never asked for go
        // back onto the board. WHICH items is decided in PowerupEffects.UnwantedTrayItems
        // (pure, and therefore testable) and handed here by instance -- this method only
        // carries it out, because this tray is the only object that can: a tray item's
        // GameObject is a child of one of these three slots, and anything that removed it
        // from TrayManager without also taking the object out would leave the item drawn in
        // a tray that no longer holds it.
        //
        // The move itself is the one BoardItemDragHandler.OnEndDrag already performs for a
        // tray pickup the player dropped somewhere invalid -- remove from the tray, spawn
        // back onto the board under a fly-in override so it travels FROM this tray rather
        // than from Starting Point, destroy the tray-side object. Reused rather than
        // reinvented so an item sent home by the powerup lands exactly like one sent home
        // by hand.
        //
        // Returns how many actually went back, which is not always what was asked for: an
        // item the player is holding mid-drag is left alone (see below).
        public int ReturnItemsToBoard(IReadOnlyList<BoardItem> items)
        {
            if (!isValid || items == null || items.Count == 0) return 0;

            // A tray mid-delivery has already been emptied in the MODEL and handed to the
            // next ticket; what is still drawn in it belongs to the delivery animation, not
            // to the player. Same window TryAcceptDrop refuses for.
            if (deliveryInProgress) return 0;

            var unwanted = new HashSet<BoardItem>(items);
            var returned = 0;

            returned += ReturnFromSlot(mainDishSlot, unwanted);
            returned += ReturnFromSlot(sideSlot, unwanted);
            returned += ReturnFromSlot(drinkSlot, unwanted);

            // Resynced rather than left to Update's poll. That poll reads a drop to exactly
            // zero as "the whole tray was cleared externally" and runs ClearAllSlotVisuals
            // on it -- correct for a timeout scatter, wrong here, where the objects are
            // already gone and the tray is simply emptier than it was.
            if (returned > 0) lastKnownCount = gameManager.TrayManager.GetContents(slotIndex).Count;

            return returned;
        }

        private int ReturnFromSlot(Transform slot, HashSet<BoardItem> unwanted)
        {
            if (slot == null) return 0;

            var returned = 0;

            // Backwards, because the children are destroyed as they are visited.
            for (var i = slot.childCount - 1; i >= 0; i--)
            {
                var child = slot.GetChild(i);
                var dragHandler = child.GetComponent<BoardItemDragHandler>();
                if (dragHandler == null || dragHandler.CurrentItem == null) continue;

                // Held by the player right now (a powerup pressed with a second finger).
                // OnBeginDrag already took this item out of TrayManager's count and that
                // handler is the single writer of its transform -- taking it away mid-drag
                // is the same mistake ClearSlot's own IsDragging check exists to avoid.
                if (dragHandler.IsDragging) continue;

                if (!unwanted.Contains(dragHandler.CurrentItem)) continue;

                var item = dragHandler.CurrentItem;
                gameManager.TrayManager.RemoveItem(slotIndex, item);

                // Flies in from where it was sitting in this tray. RequestSpawn rather than
                // TryPlaceItem at a chosen cell: the board picks, and on a momentarily full
                // board the item waits in the pending-spawn queue exactly like any other
                // returned item instead of being lost.
                // Slower than an ordinary appearance, for the same reason the collect side
                // is (D-112): one press can send several items home at once and they all
                // set off together. The multiplier rides on the same bracket as the origin,
                // which is what keeps every other board arrival at its authored speed.
                boardView.BeginFlyInOverride(
                    child.position, 0f, animConfig.AutoCollectTravelMultiplier);
                gameManager.State.Board.RequestSpawn(item);
                boardView.EndFlyInOverride();

                Destroy(child.gameObject);
                returned++;
            }

            return returned;
        }

        // Successful delivery only (a wrong-order scatter still uses the
        // plain ClearAllSlotVisuals shrink-and-destroy above) — grows the
        // whole tray (background + every slot's contents, since they're all
        // descendants of this transform) as if lifting toward the camera,
        // then moves it up while every SpriteRenderer underneath fades out
        // together, before resetting back to normal for the next ticket.
        //
        // Runs one settle tween AFTER the drop that delivered, handed here as
        // that tween's completion callback (D-100). The item that completed
        // the order is by then an ordinary child of a slot like every other
        // item in the tray, which is why nothing here treats it specially any
        // more: GetComponentsInChildren picks it up for the fade, the tray's
        // own scale/move carries it, and DestroySlotChildrenImmediate below
        // takes it with the rest. It used to animate in parallel on its own
        // transform, and doing that to a now-parented item would double both
        // the grow and the lift.
        private void PlayDeliverySuccess()
        {
            // A retry, a scene teardown or an abandoned day can land in the gap
            // between the drop and this callback. Destroying the item kills its
            // settle tween without completing it, so this is the belt to that
            // brace rather than the only guard.
            if (this == null || !isValid) return;

            var renderers = GetComponentsInChildren<SpriteRenderer>(true);

            SetTrayAnimating(true);
            transform.DOKill();
            var sequence = DOTween.Sequence();
            sequence.Append(transform.DOScale(restScale * animConfig.DeliveryGrowScale, animConfig.DeliveryGrowDuration).SetEase(Ease.OutQuad));
            // Fires exactly when the grow finishes and the lift begins —
            // the ticket card's own exit (slide + fade) starts in sync with
            // the tray actually starting to move up, not the grow before it.
            sequence.InsertCallback(animConfig.DeliveryGrowDuration, () => ticketCardView.PlayDeliveryTransition());
            sequence.Append(transform.DOMoveY(transform.position.y + animConfig.DeliveryLiftDistance, animConfig.DeliveryFadeDuration).SetEase(Ease.InQuad));
            foreach (var renderer in renderers)
            {
                if (renderer != null) sequence.Join(renderer.DOFade(0f, animConfig.DeliveryFadeDuration));
            }

            sequence.OnComplete(() =>
            {
                DestroySlotChildrenImmediate(mainDishSlot);
                DestroySlotChildrenImmediate(sideSlot);
                DestroySlotChildrenImmediate(drinkSlot);

                // Reopened the instant the slots are empty rather than when the
                // re-entrance finishes: from here on a newly accepted item can be
                // seated safely, and the tray fading back in is a good enough
                // place to land on.
                deliveryInProgress = false;
                deliveringItem = null;

                // The order that just left was the Day's last one for this slot, so
                // there is no next tray to bring in and the one that lifted off is
                // simply the last (D-129). Hiding costs NOTHING here and cuts nothing
                // short — the lift above has already faded every renderer to zero, so
                // "gone" is the state the tray is standing in at this exact instant.
                // ApplyHiddenState only puts it back at rest position and zero scale,
                // ready for a ticket that may still arrive on a retry or a new Day.
                if (!HasTicket)
                {
                    trayShown = false;
                    ApplyHiddenState();
                    SetTrayAnimating(false);
                    return;
                }

                // "New tray" re-entrance for the ticket that is now in this slot.
                PlayTrayEntrance();
            });
        }

        // A tray arriving for a ticket: grows and fades in right at rest position
        // (no movement) rather than sliding up from below or snapping into place.
        //
        // ONE animation with two callers, deliberately. It is the "new tray" beat
        // that has always followed a delivery, and it is also how a tray comes back
        // to a slot that was empty — a retry, a new Day, or any hand-off that fills
        // this slot again (D-129). A second entrance written for the second case
        // would be the same tweens with its own drift.
        //
        // Renderers are re-fetched on every call rather than captured once: after a
        // delivery the slot items this tray held have just been destroyed, and by
        // the next entrance it may hold different ones.
        private void PlayTrayEntrance()
        {
            // Before a single renderer is touched, so the tray that grows in is already the
            // right SHAPE for the order it is arriving for. This is the main path -- a
            // delivery's next ticket, a retry, a new Day -- and the poll in Update exists
            // only for the one case that never comes through here.
            ApplyTicketLayout();

            var renderers = GetComponentsInChildren<SpriteRenderer>(true);

            trayShown = true;
            SetTrayAnimating(true);
            ApplyHiddenState(renderers);

            transform.DOScale(restScale, animConfig.DeliveryReentryDuration).SetEase(Ease.OutBack)
                .OnComplete(() => SetTrayAnimating(false));
            foreach (var renderer in renderers)
            {
                renderer.DOFade(1f, animConfig.DeliveryReentryDuration);
            }
        }

        // A tray leaving a slot the Day has no more orders for, when it is still
        // standing there in full view — the timeout/external-clear path, where
        // nothing has faded it. The exact mirror of the entrance above (same
        // duration, the inverse ease) so arriving and leaving read as one gesture
        // rather than two unrelated effects.
        //
        // The delivery path does NOT come through here: its own lift has already
        // faded the tray out, and playing a second departure on top of that would
        // shrink an invisible object for no reason.
        private void PlayTrayExit()
        {
            var renderers = GetComponentsInChildren<SpriteRenderer>(true);

            trayShown = false;
            SetTrayAnimating(true);
            transform.DOKill();

            transform.DOScale(Vector3.zero, animConfig.DeliveryReentryDuration).SetEase(Ease.InBack)
                .OnComplete(() =>
                {
                    // Position is only put back once the shrink is over: moving it
                    // mid-tween would slide a still-visible tray across the screen.
                    transform.position = restPosition;
                    SetTrayAnimating(false);
                });
            foreach (var renderer in renderers)
            {
                renderer.DOFade(0f, animConfig.DeliveryReentryDuration);
            }
        }

        // Where a hidden tray waits: at rest position, zero scale, nothing drawn.
        // Also the state the entrance starts FROM, which is why both callers share
        // it — an entrance that began from a slightly different "gone" than the one
        // hiding leaves behind is exactly the kind of pop that is invisible in code.
        private void ApplyHiddenState(SpriteRenderer[] renderers = null)
        {
            renderers ??= GetComponentsInChildren<SpriteRenderer>(true);

            transform.DOKill();
            transform.position = restPosition;
            transform.localScale = Vector3.zero;

            foreach (var renderer in renderers)
            {
                var color = renderer.color;
                color.a = 0f;
                renderer.color = color;
            }
        }

        // Wrong order: the tray (and everything sitting in it, since
        // they're all descendants) plus the just-dropped final item shake
        // in place for ScatterShakeDuration before actually vanishing — the
        // model already placed the scattered items on their board cells
        // synchronously inside TryAddItem above, but BoardView.RefreshCell
        // is holding their pop-in invisible for this same duration (the
        // delay passed through OnTraySlotScatterBegin), so nothing shows up
        // on the board until this shake finishes and hands off to it.
        private void PlayWrongOrderShakeThenScatter(BoardItemDragHandler finalItem)
        {
            SetTrayAnimating(true);
            transform.DOKill();
            finalItem.transform.DOKill();
            if (wrongVisual != null) wrongVisual.SetActive(true);

            // Every currently-visible slot item plus the just-dropped final
            // item — all shake horizontally in sync with the tray, but at
            // ScatterShakeItemMultiplier times its strength (e.g. tray moves
            // 1 unit right, these move 1.2). Driven from one shared
            // oscillation instead of independent DOShakePosition calls per
            // object, which would each roll their own random pattern and
            // drift out of sync with each other.
            var itemTransforms = new List<Transform>();
            CollectSlotChildren(mainDishSlot, itemTransforms);
            CollectSlotChildren(sideSlot, itemTransforms);
            CollectSlotChildren(drinkSlot, itemTransforms);

            // Only the items already SEATED in the slots can be picked back
            // up while this shake runs, so only they get a drag handler to
            // check against below (see IsHeldByPlayer). finalItem is left
            // with a null one deliberately: its OnEndDrag hasn't run yet when
            // this is reached through OnDrop, so IsDragging can still read
            // true, but it is being released rather than held and it has to
            // shake along with the rest.
            var itemHandlers = new BoardItemDragHandler[itemTransforms.Count + 1];
            for (var i = 0; i < itemTransforms.Count; i++)
            {
                itemHandlers[i] = itemTransforms[i].GetComponent<BoardItemDragHandler>();
            }

            itemTransforms.Add(finalItem.transform);
            foreach (var t in itemTransforms) t.DOKill();

            var trayBaseX = transform.position.x;
            var itemBaseX = new float[itemTransforms.Count];
            for (var i = 0; i < itemTransforms.Count; i++) itemBaseX[i] = itemTransforms[i].position.x;

            var progress = 0f;
            DOTween.To(() => progress, p => progress = p, 1f, animConfig.ScatterShakeDuration)
                .SetEase(Ease.Linear)
                .OnUpdate(() =>
                {
                    // Decaying sine wave — horizontal only, settles to zero
                    // by the end instead of cutting off abruptly.
                    var decay = 1f - progress;
                    var wave = Mathf.Sin(progress * animConfig.ScatterShakeDuration * animConfig.ScatterShakeFrequency * Mathf.PI * 2f) * decay;

                    SetWorldX(transform, trayBaseX + wave * animConfig.ScatterShakeStrength);
                    var itemOffset = wave * animConfig.ScatterShakeStrength * animConfig.ScatterShakeItemMultiplier;
                    for (var i = 0; i < itemTransforms.Count; i++)
                    {
                        if (!IsShakeable(itemTransforms[i], itemHandlers[i])) continue;
                        SetWorldX(itemTransforms[i], itemBaseX[i] + itemOffset);
                    }
                })
                .OnComplete(() =>
                {
                    SetWorldX(transform, trayBaseX);
                    for (var i = 0; i < itemTransforms.Count; i++)
                    {
                        if (!IsShakeable(itemTransforms[i], itemHandlers[i])) continue;
                        SetWorldX(itemTransforms[i], itemBaseX[i]);
                    }

                    if (wrongVisual != null) wrongVisual.SetActive(false);
                    ClearAllSlotVisuals();
                    finalItem.ReleaseAndDestroy();

                    // ClearAllSlotVisuals' own scale-to-zero tweens still run
                    // for animConfig.SlotClearDuration after this point — every
                    // slot uses the same fixed duration, so a single delayed
                    // call here covers all of them without needing to track
                    // each one's own OnComplete.
                    DOVirtual.DelayedCall(animConfig.SlotClearDuration, () => SetTrayAnimating(false));
                });
        }

        // A tray item the player has picked back up mid-shake belongs to the
        // drag, not to this shake — BoardItemDragHandler is the single writer
        // of a held item's transform, and this shake fighting it over the X
        // axis is exactly what stranded one: OnDrag set X from the finger,
        // the next OnUpdate here overwrote it back toward the tray, and the
        // final restore snapped it there for good, while Y kept following
        // normally. Since the item also hovers a cell above the finger by
        // design, the player saw it stuck up and to the LEFT of the cursor.
        // Rechecked every frame rather than filtered once up front, because
        // the pickup happens DURING the shake — the same reason ClearSlot
        // checks IsDragging on its own destroy pass. The null check covers an
        // item that was dragged out and destroyed before the shake ended.
        private static bool IsShakeable(Transform itemTransform, BoardItemDragHandler handler)
        {
            if (itemTransform == null) return false;
            return handler == null || !handler.IsDragging;
        }

        private static void SetWorldX(Transform t, float x)
        {
            var pos = t.position;
            pos.x = x;
            t.position = pos;
        }

        private static void CollectSlotChildren(Transform slot, List<Transform> into)
        {
            if (slot == null) return;
            for (var i = 0; i < slot.childCount; i++) into.Add(slot.GetChild(i));
        }

        private static void DestroySlotChildrenImmediate(Transform slot)
        {
            if (slot == null) return;
            for (var i = slot.childCount - 1; i >= 0; i--)
            {
                Destroy(slot.GetChild(i).gameObject);
            }
        }

        // Called by BoardItemDragHandler.UpdateHoveredTray, driven off the
        // dragged item's own displayed position rather than pointer
        // enter/exit events — with the drag-feel hover offset, the pointer
        // and the item can be over different things, and the highlight
        // should reflect what the player actually sees the item on top of.
        public void SetHighlighted(bool active)
        {
            // A tray with no ticket is not drawn at all (D-129), and TrayManager
            // already refuses every item dropped on it — so lighting it up would
            // promise a drop zone that does not exist. Guarded here rather than in
            // the drag handler because this view is the one that knows: it owns the
            // highlight object AND the hidden state.
            if (!trayShown) active = false;

            if (highlightVisual != null) highlightVisual.SetActive(active);
        }

        private void Update()
        {
            if (!isValid) return;


            // The delivery's start signal rides on the delivering item's settle tween,
            // and that tween is killed rather than completed if the item is destroyed
            // first (a retry, an abandoned day). Releasing the guard here is what keeps
            // that from silently turning this tray into one that never accepts again.
            // A plain bool read on every other frame — the Unity liveness check behind
            // it only runs while a delivery is actually in flight.
            if (deliveryInProgress && deliveringItem == null) deliveryInProgress = false;

            RefreshClearedTray();
            RefreshTrayLayout();
            RefreshTrayPresence();
        }

        // The one ticket change PlayTrayEntrance does not cover: a TIMEOUT, where
        // TicketSlotManager cancels the order and hands this slot the next one immediately.
        // The tray never leaves and never comes back, so nothing plays an entrance, and
        // without this poll the tray would keep the departed order's shape for the whole of
        // the new one.
        //
        // Runs AFTER RefreshClearedTray, and that order is the safety: a timeout scatters
        // the tray's contents and RefreshClearedTray sets the animating flag for
        // SlotClearDuration, so the guards below hold the rearrangement until those items
        // are gone. Switching a slot off while an item is still parented to it would take
        // the item off screen with it -- the same "single writer of a visible object" hazard
        // ClearSlot and the wrong-order shake already step around.
        //
        // Cost: one array read and a reference compare per tray per frame (3 trays, memory
        // tier), the same shape and the same order of magnitude as the two polls beside it.
        private void RefreshTrayLayout()
        {
            if (deliveryInProgress || trayAnimating || lastKnownCount != 0) return;

            ApplyTicketLayout();
        }

        // Whether this tray is drawn follows one fact and one only: does its slot
        // hold a ticket (D-129). Read here rather than driven by an event, and that
        // is the whole design: the slot is emptied inside a SYNCHRONOUS cascade
        // (TrayManager.TryAddItem -> deliverTicket -> TicketSlotManager.AssignTicket)
        // that runs at the START of a delivery, so an event-driven hide would fire
        // while the tray is still growing and lifting — exactly what must not happen.
        // A poll can wait, and the two guards below are that wait: the tray leaves
        // only once its own animation has finished, never in the middle of one.
        //
        // Cost: one array read and a bool compare per tray per frame (3 trays,
        // arithmetic tier) — far below the threshold where the cheaper-but-blind
        // option would be worth it.
        private void RefreshTrayPresence()
        {
            if (deliveryInProgress || trayAnimating) return;

            var hasTicket = HasTicket;
            if (hasTicket == trayShown) return;

            if (hasTicket) PlayTrayEntrance();
            else PlayTrayExit();
        }

        // Catches the one case OnDrop doesn't cover: a timeout scattering a
        // partially-filled tray back to the board (TrayManager.OnTicketAssigned),
        // which happens with no direct event this view is subscribed to —
        // same poll+diff approach TicketCardView already uses for its timer.
        // OnDrop keeps lastKnownCount in sync for its own changes, so this
        // only ever fires for that external case.
        //
        // Only a drop to exactly zero means the WHOLE tray was cleared
        // externally (OnTicketAssigned/TryAddItem's batch-resolve paths
        // both always empty the slot completely) — a single manual
        // pickup (BoardItemDragHandler.OnBeginDrag removing just one item)
        // decrements by exactly one and must NOT wipe the other items
        // still legitimately sitting in this tray.
        //
        // Runs BEFORE RefreshTrayPresence in the frame a timeout empties the last
        // ticket, and that order is the "after the animation" rule in action: this
        // sets the animating flag for SlotClearDuration, so the presence check sees
        // a busy tray and holds its departure until the items have finished
        // clearing out of it (D-129).
        private void RefreshClearedTray()
        {
            var currentCount = gameManager.TrayManager.GetContents(slotIndex).Count;
            if (currentCount == lastKnownCount) return;

            if (currentCount == 0)
            {
                SetTrayAnimating(true);
                ClearAllSlotVisuals();
                DOVirtual.DelayedCall(animConfig.SlotClearDuration, () => SetTrayAnimating(false));
            }

            lastKnownCount = currentCount;
        }

        private void ClearAllSlotVisuals()
        {
            ClearSlot(mainDishSlot);
            ClearSlot(sideSlot);
            ClearSlot(drinkSlot);
        }

        private void ClearSlot(Transform slot)
        {
            if (slot == null) return;
            for (var i = slot.childCount - 1; i >= 0; i--)
            {
                var child = slot.GetChild(i);

                // Picking an item back up out of this tray already removed it
                // from TrayManager's count (BoardItemDragHandler.OnBeginDrag),
                // which looks identical to an external clear from here — skip
                // it while it's actively being dragged so it doesn't get
                // destroyed out from under the player mid-drag.
                var dragHandler = child.GetComponent<BoardItemDragHandler>();
                if (dragHandler != null && dragHandler.IsDragging) continue;

                var childTransform = child;
                childTransform.DOKill();
                childTransform.DOScale(Vector3.zero, animConfig.SlotClearDuration)
                    .SetEase(Ease.InBack)
                    .OnComplete(() => Destroy(childTransform.gameObject));
            }
        }
    }
}
