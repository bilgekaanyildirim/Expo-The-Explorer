using System.Collections.Generic;
using System.Linq;
using DG.Tweening;
using ExpoTheExplorer.Bootstrap;
using ExpoTheExplorer.Core;
using ExpoTheExplorer.Data;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace ExpoTheExplorer.UI
{
    // One ticket slot's card. Pure "bind" component — every visual piece is a
    // reference wired up in the Inspector (the Canvas/card hierarchy is built by
    // hand in the Editor, not procedurally), so this script only ever changes
    // .sprite/.text/.SetActive on those existing references. Never creates or
    // destroys structural GameObjects except cloning the two authored templates,
    // modificationRowTemplate and timerDividerTemplate — both are counts only the
    // ticket knows (its modifications, its time limit), so neither can be laid out
    // in the Editor ahead of time.
    //
    // Polls its slot every frame instead of binding to GameState.TicketDelivered/
    // TicketCancelled: those events fire BEFORE TicketSlotManager reassigns the
    // slot, and FillEmptySlots (initial population) never fires anything at all —
    // neither event can tell this view what's actually in the slot right now.
    // The same per-frame check drives the timer bar, which has no event at
    // all since RemainingSeconds is mutated directly every frame.
    public class TicketCardView : MonoBehaviour
    {
        // Safety valve, not a design number: authored data decides how many ticks a
        // bar gets (limit / segment seconds), and this only stops a nonsense pair —
        // a huge limit against a tiny segment — from spawning thousands of objects.
        private const int MaxTimerDividers = 64;

        [SerializeField] private Image background;
        [SerializeField] private TMP_Text customerNameText;
        [Tooltip("Filled (Horizontal, Origin Left) Image drained by the ticket's remaining time. Sits inside the timer bar's track, which draws the outline around it.")]
        [SerializeField] private Image timerFillImage;
        [Tooltip("Inactive divider tick, cloned once per segment boundary. Must be a sibling AFTER the fill so the ticks draw over it, and anchored to the track's left edge with a vertical stretch — only its horizontal offset is moved. Keep its width equal to the track sprite's outline, or the ticks read as a different line weight.")]
        [SerializeField] private RectTransform timerDividerTemplate;
        [Tooltip("OPTIONAL. A single Image that blinks hard on and off while this ticket's remaining time is inside the danger zone — the same instant the timer bar turns red. Its colours are never touched, so author it as loud as you like. Toggled through Image.enabled, not SetActive, so it keeps its layout footprint and never slides the card's other rows: author it as ONE Image drawn over the card (its own children would not be hidden with it), outside any layout group. Left empty, the ticket's paper still flashes and only the icon is missing.")]
        [SerializeField] private Image dangerImage;
        [SerializeField] private Image dishImage;
        [SerializeField] private Transform modificationsListParent;
        [SerializeField] private ModificationSlotView modificationRowTemplate;
        [SerializeField] private Image sideImage;
        [SerializeField] private Image drinkImage;
        [Tooltip("Needed to fade the whole card (including dynamically-created modification rows) as one unit during the delivery-success exit/entry transition.")]
        [SerializeField] private CanvasGroup canvasGroup;

        private GameManager gameManager;
        private int slotIndex;
        private TicketCardsView owner;
        private BoardAnimationConfig animConfig;
        private Ticket cachedTicket;
        private bool isValid;
        private bool transitionInProgress;
        private bool trayAnimating;
        private bool dangerPulsing;
        private Color authoredBackgroundColor = Color.white;
        private RectTransform rectTransform;
        private readonly List<ModificationSlotView> modificationRows = new();
        private readonly List<RectTransform> timerDividers = new();

        // True while either this card's own delivery transition or its
        // paired WorldTrayView's delivery/scatter animation is playing —
        // TrayFillCounterView reads this to hide the "x/y" readout for the
        // whole window instead of just this card's own slide/fade.
        public bool IsAnimating => transitionInProgress || trayAnimating;

        // Called by WorldTrayView at the start/end of its own tray
        // animations (delivery grow/lift/reentry, wrong-order shake/scatter,
        // timeout scatter) — this card has no way to observe those on its
        // own since they're driven entirely from the sibling world-space
        // tray object, not from any GameState event.
        public void SetTrayAnimating(bool animating)
        {
            trayAnimating = animating;
        }

        public void Initialize(GameManager gameManager, int slotIndex, TicketCardsView owner, BoardAnimationConfig animConfig)
        {
            this.gameManager = gameManager;
            this.slotIndex = slotIndex;
            this.owner = owner;
            this.animConfig = animConfig;

            isValid = ValidateReferences();
            if (!isValid) return;

            rectTransform = (RectTransform)transform;

            // Read once, here, and never again: from the first danger flash onward the
            // live value is a flash colour, so anything asking later would learn the
            // alarm's colour rather than the card's. This is what the paper returns to
            // when a ticket leaves the danger zone or the slot changes hands.
            authoredBackgroundColor = background.color;

            // The container itself must stay active — only the template row
            // inside it (and the clones built from it) toggle. Prefab authoring
            // sometimes leaves this off after hiding the two sample rows in the
            // Editor, which silently hides every real modification row too.
            modificationsListParent.gameObject.SetActive(true);
            modificationRowTemplate.gameObject.SetActive(false);
            timerDividerTemplate.gameObject.SetActive(false);

            // The one optional reference on this card, and the only one whose absence
            // ValidateReferences deliberately does NOT fail on: that would set isValid
            // false and take the WHOLE card down — an invisible ticket — over a missing
            // decoration the card has a working degraded mode without (the paper still
            // flashes).
            // But an unwired one is a feature that is simply not there with nothing on
            // screen to say why, so it says so here instead: once per card, at build
            // time, naming the prefab to wire rather than going quiet.
            if (dangerImage == null)
            {
                Debug.LogWarning($"{nameof(TicketCardView)} on '{name}' has no {nameof(dangerImage)} wired — a ticket running out of time will still flash its paper, but no danger icon will appear. Wire it on the TicketCard prefab.", this);
            }

            RebuildContent(null);
        }

        // Every field here is wired by hand in the Editor (no procedural
        // fallback) — a missing one should fail loudly with a clear pointer to
        // which GameObject/field, not a bare NullReferenceException three
        // frames deep in Unity's own Instantiate code.
        private bool ValidateReferences()
        {
            var missing = new List<string>();
            if (background == null) missing.Add(nameof(background));
            if (customerNameText == null) missing.Add(nameof(customerNameText));
            if (timerFillImage == null) missing.Add(nameof(timerFillImage));
            if (timerDividerTemplate == null) missing.Add(nameof(timerDividerTemplate));
            if (dishImage == null) missing.Add(nameof(dishImage));
            if (modificationsListParent == null) missing.Add(nameof(modificationsListParent));
            if (modificationRowTemplate == null) missing.Add(nameof(modificationRowTemplate));
            if (sideImage == null) missing.Add(nameof(sideImage));
            if (drinkImage == null) missing.Add(nameof(drinkImage));
            if (canvasGroup == null) missing.Add(nameof(canvasGroup));

            if (missing.Count == 0) return true;

            Debug.LogError($"{nameof(TicketCardView)} on '{name}' is missing Inspector reference(s): {string.Join(", ", missing)}. Check the TicketCard prefab.", this);
            return false;
        }

        private void Update()
        {
            // Suppressed for the duration of PlayDeliveryTransition below —
            // that method already knows the ticket changed (it caused it)
            // and handles the rebuild itself once its exit animation
            // finishes; letting this poll rebuild instantly the moment the
            // reference changes would skip straight past the whole
            // exit/entry animation.
            if (!isValid || transitionInProgress) return;

            var ticket = gameManager.State.TicketSlots[slotIndex];
            if (!ReferenceEquals(ticket, cachedTicket))
            {
                cachedTicket = ticket;
                RebuildContent(ticket);
            }

            if (ticket != null)
            {
                RefreshTimer(ticket);
            }
        }

        // Called by WorldTrayView.TryAcceptDrop the instant it knows a
        // delivery just happened — must run in the very same frame as the
        // model's ticket reassignment (TicketSlotManager.DeliverTicket,
        // synchronous inside TrayManager.TryAddItem), otherwise Update()'s
        // own poll would catch the changed ticket reference on the very
        // next frame and instantly rebuild to the new ticket well before
        // PlayDeliveryTransition (deferred until the tray's lift phase
        // starts, ~DeliveryGrowDuration seconds later) gets a chance to
        // play its exit animation on the still-old content.
        public void SuppressPollUntilDeliveryTransition()
        {
            if (!isValid) return;
            transitionInProgress = true;
        }

        // Called by WorldTrayView.PlayDeliverySuccess (via TicketCardsView.
        // GetCard) exactly when the tray for this slot starts its own lift
        // phase after a successful delivery — slides this (still the OLD
        // ticket's) card up while fading out, then swaps to whatever ticket
        // is now actually in this slot (already reassigned synchronously by
        // the time this runs) and drops the new card in from above.
        public void PlayDeliveryTransition()
        {
            if (!isValid) return;

            transitionInProgress = true;
            rectTransform.DOKill();
            canvasGroup.DOKill();

            // Read fresh, not cached — cardsParent is a HorizontalLayoutGroup
            // (TicketCardsView), so "rest position" isn't a fixed constant:
            // it depends on every card's current size, which can shift
            // (e.g. a differing modification-row count changes this card's
            // own preferred size) — a value captured once back at
            // Initialize could already be stale by the time any particular
            // delivery happens.
            var exitFromPos = rectTransform.anchoredPosition;

            var sequence = DOTween.Sequence();
            sequence.Append(rectTransform.DOAnchorPosY(exitFromPos.y + animConfig.TicketExitLiftDistance, animConfig.TicketExitDuration).SetEase(Ease.InQuad));
            sequence.Join(canvasGroup.DOFade(0f, animConfig.TicketExitDuration));
            sequence.AppendCallback(() =>
            {
                var newTicket = gameManager.State.TicketSlots[slotIndex];
                cachedTicket = newTicket;
                RebuildContent(newTicket);

                // RebuildContent can change this card's own size (a
                // different modification-row count) — force the parent
                // layout group to react to that now, so the position read
                // right after is authoritative for the NEW ticket's content
                // instead of trusting a pre-rebuild value.
                if (rectTransform.parent is RectTransform parentRect)
                {
                    LayoutRebuilder.ForceRebuildLayoutImmediate(parentRect);
                }

                var settledPos = rectTransform.anchoredPosition;
                rectTransform.anchoredPosition = new Vector2(settledPos.x, settledPos.y + animConfig.TicketEntryDropDistance);
                canvasGroup.alpha = 0f;

                rectTransform.DOAnchorPos(settledPos, animConfig.TicketEntryDuration).SetEase(Ease.OutBack)
                    .OnComplete(() => transitionInProgress = false);

                // Nothing left to hand this slot (the day's last ticket was the
                // one just delivered) means there is no card to drop in — fade
                // back to the hidden state RebuildContent(null) just set, not to
                // 1, or the entry animation would reveal an empty card. The
                // position tween still runs so the card lands on its layout
                // position while invisible, ready for the next ticket.
                canvasGroup.DOFade(newTicket != null ? 1f : 0f, animConfig.TicketEntryDuration);
            });
        }

        private void RebuildContent(Ticket ticket)
        {
            // Handed back BEFORE the new sprite goes on, so a card that was mid-flash
            // when its ticket left — delivered, cancelled, or timed out — does not
            // hand that frame's alarm colour to whatever fills the slot next.
            ResetDangerPulse();

            background.sprite = owner.TicketSpriteFor(ticket?.PatienceType ?? PatienceType.Normal);

            ClearModificationRows();

            if (ticket == null)
            {
                // An empty slot shows nothing at all — not even the card body,
                // which would otherwise sit there as a blank ticket. Hidden
                // through the CanvasGroup rather than SetActive(false) because
                // cardsParent is a HorizontalLayoutGroup: alpha is invisible to
                // layout, so this card keeps its exact footprint and the other
                // two stay put, whereas deactivating the object would drop it
                // out of the layout and slide them across.
                canvasGroup.alpha = 0f;

                customerNameText.text = string.Empty;
                timerFillImage.fillAmount = 0f;
                ClearTimerDividers();
                dishImage.enabled = false;
                sideImage.gameObject.SetActive(false);
                drinkImage.gameObject.SetActive(false);
                return;
            }

            canvasGroup.alpha = 1f;

            customerNameText.text = ticket.CustomerName;

            var main = ticket.RequiredItems.FirstOrDefault(item => item.Category == FoodCategory.Main);
            SetDishImage(main);

            // Cleared above and rebuilt in the ticket's own order every time —
            // this list is dynamic (0..N rows), not a fixed pool of slots.
            foreach (var modification in ticket.Modifications)
            {
                var row = Instantiate(modificationRowTemplate, modificationsListParent);
                row.gameObject.SetActive(true);
                row.SetModification(modification.Config.Icon, owner.DirectionSpriteFor(modification.IsAddition), owner.ModificationBoxColorFor(ticket.PatienceType));
                modificationRows.Add(row);
            }

            var side = ticket.RequiredItems.FirstOrDefault(item => item.Category == FoodCategory.Side);
            SetOptionalImage(sideImage, side);

            var drink = ticket.RequiredItems.FirstOrDefault(item => item.Category == FoodCategory.Drink);
            SetOptionalImage(drinkImage, drink);

            RebuildTimerDividers(ticket);
            RefreshTimer(ticket);
        }

        // Runs every frame, so both writes are deliberately left to Image's own
        // setters: fillAmount and color each early-out on an unchanged value, so a
        // card only dirties its graphic on the frames the bar actually moves.
        private void RefreshTimer(Ticket ticket)
        {
            var remainingRatio = ticket.TimeLimitSeconds > 0f
                ? Mathf.Clamp01(ticket.RemainingSeconds / ticket.TimeLimitSeconds)
                : 0f;

            timerFillImage.fillAmount = remainingRatio;
            timerFillImage.color = owner.TimerFillColorFor(remainingRatio);
            ApplyDangerPulse(owner.IsInDangerZone(remainingRatio));
        }

        // The danger flash rides the timer read above rather than running as its own
        // looping tween, and that is a lifetime decision, not a style one: the two
        // things it writes — the paper's colour and the icon's enabled flag — are both
        // owned by RebuildContent as well, which runs whenever the slot's ticket
        // changes. A loop tween would have to be killed on every one of those
        // hand-offs, and PlayDeliveryTransition already kills and re-tweens this card;
        // computing the flash here instead means Update's existing silence for the
        // whole transition (transitionInProgress) is all the coordination needed.
        //
        // Phase comes from Time.time, not a per-card accumulator, so all three cards
        // flash together: three alarms out of step read as clutter, one rhythm reads
        // as an alarm.
        private void ApplyDangerPulse(bool inDanger)
        {
            if (!inDanger)
            {
                // Guarded rather than written every frame: this is the state a ticket
                // spends most of its life in, and the paper's colour belongs to whoever
                // set it last. Only a flash that actually ran has something to give
                // back. Reachable for a live ticket only if its clock ever moves UP —
                // no mechanic does that today (a Time Reset powerup would) — but the
                // restore is two lines and its absence would strand a card red.
                if (dangerPulsing) ResetDangerPulse();
                return;
            }

            dangerPulsing = true;

            // A SQUARE wave, not a ramp: lit for the first half of the period, rested
            // for the second. A smoothly interpolated flash reads as fading, which is
            // exactly what the alpha version got wrong — an alarm switches.
            //
            // A zero period means "no flash", not "flash infinitely fast". Clamping it
            // to some minimum would turn a 0 left in the asset into a ~20Hz strobe —
            // unreadable, and the kind of flashing that is a genuine accessibility
            // hazard. Lit-and-steady is the honest reading of "no cycle authored".
            var period = animConfig.TicketDangerBlinkPeriod;
            var lit = period <= 0f || Mathf.Repeat(Time.time, period) < period * 0.5f;

            // The PAPER is what changes colour; the customer name, the dish photo, the
            // modification rows and the timer bar printed on it all stay fully opaque.
            // That is the whole reason this is a tint and not the CanvasGroup alpha it
            // started as: dimming the card took the very numbers the player is reading
            // down with it, at the one moment they are worth reading.
            background.color = owner.DangerFlashColorFor(lit);

            // Hard on/off, so the icon keeps its own artwork at full saturation instead
            // of being faded through a half-transparent version of itself. It shares the
            // paper's phase deliberately: both halves then say one thing — DANGER, or
            // back to normal — rather than trading places and reading as motion.
            if (dangerImage != null) dangerImage.enabled = lit;
        }

        // The paper goes back to the colour the PREFAB authored, not to the flash's
        // own rest colour: those are two different things. The rest colour is half of
        // a choreography (white by default, which is why the two usually look alike),
        // while authoredBackgroundColor is the card's own tint, and a card that came
        // out of the danger zone owes the player the second one. Captured once at
        // Initialize, because after the first flash the live value is no longer it.
        private void ResetDangerPulse()
        {
            dangerPulsing = false;

            if (dangerImage != null) dangerImage.enabled = false;
            background.color = authoredBackgroundColor;
        }

        // The bar spans the ticket's WHOLE time limit, so a tick belongs at
        // boundary/limit along it, measured from the track's left edge.
        //
        // Deliberately NOT an even split of the bar into n pieces: a limit that
        // isn't a whole multiple of the segment length (nothing authored today —
        // 45/90/150 against 15) has to end with one shorter final section, because
        // spreading the ticks evenly would put them on the wrong seconds.
        private void RebuildTimerDividers(Ticket ticket)
        {
            ClearTimerDividers();

            var segmentSeconds = owner.TimerSegmentSeconds;
            var timeLimit = ticket.TimeLimitSeconds;
            if (segmentSeconds <= 0f || timeLimit <= 0f) return;

            var track = (RectTransform)timerDividerTemplate.parent;

            // Read straight off the track's own sizeDelta (fixed anchors, no layout
            // group driving it), so this is already right on the frame the card is
            // built — nothing here has to wait for a layout pass.
            var trackWidth = track.rect.width;

            for (var section = 1; section * segmentSeconds < timeLimit && timerDividers.Count < MaxTimerDividers; section++)
            {
                var divider = Instantiate(timerDividerTemplate, track);
                divider.gameObject.SetActive(true);

                // Rounded to whole units instead of left on the exact fraction. A
                // tick that lands mid-unit gets smeared across two pixels, and since
                // every boundary falls on a different fraction each one would smear
                // by a different amount and read as a different line weight. The
                // template is already anchored to the track's left edge, so only the
                // offset moves.
                var normalized = section * segmentSeconds / timeLimit;
                divider.anchoredPosition = new Vector2(
                    Mathf.Round(normalized * trackWidth), divider.anchoredPosition.y);

                timerDividers.Add(divider);
            }
        }

        private void SetDishImage(FoodItemConfig main)
        {
            // Always the BASE sprite, never the board's SpriteLayers — GDD Section
            // 3.2: the card's main image is the unmodified dish photo, modifications
            // are shown separately as the list below.
            if (main != null && main.Sprite != null)
            {
                dishImage.enabled = true;
                dishImage.sprite = main.Sprite;
            }
            else
            {
                dishImage.enabled = false;
            }
        }

        private static void SetOptionalImage(Image image, FoodItemConfig config)
        {
            if (config != null && config.Sprite != null)
            {
                image.gameObject.SetActive(true);
                image.sprite = config.Sprite;
            }
            else
            {
                image.gameObject.SetActive(false);
            }
        }

        private void ClearModificationRows()
        {
            foreach (var row in modificationRows)
            {
                Destroy(row.gameObject);
            }
            modificationRows.Clear();
        }

        private void ClearTimerDividers()
        {
            foreach (var divider in timerDividers)
            {
                Destroy(divider.gameObject);
            }
            timerDividers.Clear();
        }
    }
}
