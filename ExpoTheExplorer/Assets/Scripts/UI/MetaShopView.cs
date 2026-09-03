using System.Collections;
using System.Collections.Generic;
using DG.Tweening;
using ExpoTheExplorer.Bootstrap;
using ExpoTheExplorer.Core;
using ExpoTheExplorer.Data;
using ExpoTheExplorer.Session;
using ExpoTheExplorer.Systems.MetaSystem;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace ExpoTheExplorer.UI
{
    // The meta shop: a market button in the bottom-right corner, a panel it opens, a row
    // per prop still for sale, and — since Ş3 — a preview: tapping a row's BUY puts a
    // translucent ghost of the prop where it would stand and a confirm popup over it. The
    // popup's BUY spends (Ş4), and a purchase ends the whole flow: the shop goes back to
    // KAPALI so the screen ends on the prop rather than on the list (D-047).
    //
    // THREE WAYS OUT since D-108, and all three are the same transition: the market button
    // again, an X in the sheet's corner, and a tap on the full-screen backdrop the sheet now
    // sits inside. They differ only in where the finger lands -- every one calls SetOpen,
    // never SetActive, for the reason the next paragraph gives.
    //
    // THE STATE MACHINE LIVES HERE, all of it, on purpose. Two fields hold it: `isOpen`
    // (KAPALI/AÇIK) and `pendingItem` (ÖNİZLEME when non-null). Every transition goes
    // through `ApplyVisibility`, and every exit from a preview goes through `ClosePreview`
    // — so "when does the ghost go away" has exactly one answer. Splitting the panel, the
    // rows and the popup into separate components would answer that question three times,
    // which is how the first shop's ghost could survive the panel that summoned it (D-024).
    //
    // It ASKS the rules and owns none of them. Which props are offered comes from
    // MetaPurchase.ShopItems; whether one can be afforded will come from
    // MetaPurchase.Evaluate in Ş5. Which LOCATION is being shopped comes from
    // MetaGroundsView.ViewedLocation rather than being resolved here again, which is what
    // keeps the MetaCatalog reference in a single place -- two components free to point at
    // two catalogs would break D-015's single-authority rule without anything looking
    // wrong.
    //
    // This class will grow into the owner of the shop's whole state machine
    // (KAPALI/AÇIK/ÖNİZLEME), which is why the panel is toggled from HERE rather than by a
    // Button-to-SetActive wire in the Inspector: the ghost's lifetime is going to hang off
    // these same transitions, and a state machine half in the scene and half in code is one
    // that disagrees with itself. Today that machine has two states, which is the point of
    // building it in this order rather than after the rows exist.
    //
    // It reads nothing and writes nothing. The catalog, the wallet and the six purchase
    // verdicts stay where they were built and tested (MetaPurchase, D-019) -- this screen
    // will ASK them in Ş2, and it will still not own a rule.
    public class MetaShopView : MonoBehaviour
    {
        // Where the store button IS, for something that needs to point at it -- the
        // main-screen tutorial's arrow, and nothing else today. Read-only and deliberately
        // the RectTransform rather than the Button: a caller gets a position and a size, and
        // no way to press the store on the player's behalf or to restyle it.
        public RectTransform MarketButtonRect => marketButton != null ? (RectTransform)marketButton.transform : null;

        [Header("Bound by ExpoTheExplorer > Meta > Build Meta Shop")]
        [Tooltip("Opens and closes the panel. Drawn ON TOP of the panel on purpose, so it can still be tapped to close a panel that covers it.")]
        [SerializeField] private Button marketButton;

        [Tooltip("Everything the shop shows. Toggled with SetActive, so a closed shop costs nothing per frame. Since D-108 this is the full-screen BACKDROP, with the sheet as its child, so the block and the sheet come and go together.")]
        [SerializeField] private GameObject panel;

        // Both OPTIONAL, and both go through SetOpen rather than touching `panel` (D-108).
        // That is the rule this class was built around: visibility has exactly one owner,
        // because the first shop's ghost leaked precisely when it had more than one
        // (D-024/D-027). A button calling SetActive would skip ClosePreview and
        // CancelPendingReopen and reintroduce the same class of bug.
        [Tooltip("Optional. An X in the sheet's top-right corner. Closes the shop.")]
        [SerializeField] private Button closeButton;

        // NOT the same thing D-038 removed. That was a backdrop on the CONFIRM POPUP, taken
        // out at the user's request so CANCEL is the only way out of a preview; the preview
        // is untouched here. This one sits behind the LIST, and it needs no inside/outside
        // hit-test: the sheet is a child of the backdrop and carries its own raycast-target
        // Image, so UGUI gives a tap on the sheet to the sheet and only an outside tap
        // reaches this button.
        [Tooltip("Optional. A Button on the full-screen backdrop — tapping outside the sheet closes the shop. Set its Transition to None so the dim layer does not flash.")]
        [SerializeField] private Button backdropButton;

        // OPTIONAL, like comingSoonLabel and for the same reason: a shop built before this
        // existed has a null here and behaves exactly as it always did, so it stays out of
        // ValidateReferences. A missing badge costs a nudge; making it required would make
        // every already-built shop log an error over an ornament.
        //
        // A RectTransform rather than a GameObject, because the hop below moves
        // anchoredPosition -- and asking for the type the animation needs is what makes
        // "this must be a UI object" a thing the Inspector refuses rather than a thing that
        // fails at runtime.
        [Header("Can-buy badge")]
        [Tooltip("Optional. A marker under the market button, switched on whenever this location has a prop the player can actually afford, and switched off the moment that stops being true. Hops in place while it is up.")]
        [SerializeField] private RectTransform canBuyBadge;

        [Tooltip("How far the badge hops, in UI units.")]
        [SerializeField, Min(0f)] private float canBuyHopHeight = 16f;

        [Tooltip("How long ONE hop takes, up and back down.")]
        [SerializeField, Min(0.05f)] private float canBuyHopDuration = 0.34f;

        [Tooltip("The still moment between two hops. Hop plus rest is the period the player sees.")]
        [SerializeField, Min(0f)] private float canBuyHopRest = 1.15f;

        [Header("List")]
        [Tooltip("Who the player is: the wallet, the day they are on, and which props they already own. Read only — nothing here writes to it.")]
        [SerializeField] private SessionHost sessionHost;

        [Tooltip("Which location's props to list. Read from the grounds rather than resolved again here, so the catalog reference lives in exactly one place.")]
        [SerializeField] private MetaGroundsView grounds;

        [Tooltip("Optional. The scene's HapticsBinder, so a purchase going through can be felt. On the main screen that binder's GameManager field is left EMPTY — there is no GameState here.")]
        [SerializeField] private HapticsBinder haptics;

        [Tooltip("The rows' parent — the ScrollRect's content. Must stay ACTIVE: hiding it to hide the template hides every real row too.")]
        [SerializeField] private Transform rowsParent;

        [Tooltip("An INACTIVE row authored in the scene, cloned once per offer. Style this one and every row follows. Leave it disabled — it is a template, not the first row.")]
        [SerializeField] private MetaShopRowView rowTemplate;

        // OPTIONAL on purpose, unlike everything else this list needs. A shop built before
        // this existed has a null here and behaves exactly as it did -- an unfinished
        // location's shop opens empty. Putting it in ValidateReferences instead would make
        // every already-built shop log an error at Start over a label, which is a worse
        // failure than the one it would be reporting.
        [Tooltip("Optional. Shown INSTEAD of the list when the location being shopped has no props authored yet — the same state the Meta window warns about. Built by ExpoTheExplorer > Meta > Build Meta Shop; the words on it are yours to retype.")]
        [SerializeField] private GameObject comingSoonLabel;

        [Tooltip("Optional. Assets/Data/BoardAnimationConfig.asset — read for Popup Fade In Duration only. It covers the shop sheet AND the confirm popup. Unwired, both appear instantly, exactly as they did before the fade existed.")]
        [SerializeField] private BoardAnimationConfig animConfig;

        [Header("Confirm popup")]
        [Tooltip("The confirm popup's root. Active only while a purchase is being previewed.")]
        [SerializeField] private GameObject confirmPopup;

        // THE POPUP CATCHES ITS OWN OUTSIDE TAPS AGAIN, and this reverses D-038. That turn
        // deleted the authored backdrop at the user's request, on the reasoning that CANCEL
        // should be the only way out and that eating taps "costs nothing, because the
        // ScrollRect is already disabled". The second half was the mistake: the ScrollRect
        // is not the only thing under this popup. ConfirmPopup is a full-screen stretch with
        // NO Graphic on it, and the main screen's "Continue Day" button is a sibling of this
        // whole shop under the same Canvas -- so a tap beside the box hit nothing here, fell
        // through, and STARTED THE DAY out from under a purchase the player was still
        // deciding on. See CreateConfirmCatcher for why it is built rather than authored.
        [Tooltip("The popup's box — the part that MOVES. Positioned above the previewed prop each time, with a bottom-centre pivot, so its lower edge sits just over the ghost.")]
        [SerializeField] private RectTransform confirmBox;

        [Tooltip("Gap between the previewed prop and the popup's edge.")]
        [SerializeField, Min(0f)] private float confirmGap = 24f;

        // Both BUY colours live HERE, not on the row: the row is a binder that decides
        // nothing, and two components each holding this pair is a pair that can drift. They
        // are applied on every bind, so a row always shows one of the two rather than
        // sometimes inheriting whatever the template was authored with.
        [Header("BUY colours")]
        [Tooltip("A BUY the player can actually press.")]
        [SerializeField] private Color buyAffordableColor = new(0.20f, 0.42f, 0.28f);

        [Tooltip("A BUY the player cannot afford. The project's other \"you are about to lose something\" red, so the game does not end up with two different reds.")]
        [SerializeField] private Color buyUnaffordableColor = new(0.45f, 0.18f, 0.18f);

        [SerializeField] private TMP_Text confirmNameLabel;
        [SerializeField] private TMP_Text confirmPriceLabel;

        [Tooltip("Commits the purchase. In Ş3 it only logs — the wallet is wired in Ş4.")]
        [SerializeField] private Button confirmBuyButton;

        [Tooltip("Cancels the preview and brings the list back. The ghost goes with it (MS3).")]
        [SerializeField] private Button confirmCancelButton;

        // Not saved and not restored. Whether the shop was open is viewing state, the same
        // call K6-4 made for the viewed location: persisting it would mean a profile field
        // and a schema version for a preference nobody would miss.
        private bool isOpen;

        // The ONE piece of state that says "a purchase is being previewed". Non-null IS the
        // ÖNİZLEME state: the ghost is up, the popup is up, and the list is hidden. Keeping
        // it as the item rather than a bool means the popup and the Ş4 commit both read the
        // same thing, so they cannot come to disagree about WHICH prop is being bought.
        private MetaItemDefinition pendingItem;

        // The wait between an area-unlocking purchase and the shop coming back up (D-096).
        // HELD so it can be abandoned: every other transition of this state machine cancels
        // it, which is what stops a timer started a second ago from reopening a panel the
        // player has since closed with their own thumb.
        private Coroutine reopenRoutine;

        // The badge's hop, held so it can be killed. One sequence at a time and never two:
        // ShowCanBuyBadge is reached from three different events, and a second sequence
        // driving the same anchoredPosition would fight the first over a value neither owns
        // exclusively.
        private Sequence canBuyHop;

        // The badge's authored resting height, read ONCE at Start and put back whenever the
        // hop is killed. Captured before anything animates, so it is the position the scene
        // was saved with rather than wherever a hop happened to be interrupted -- reading it
        // at kill time instead would let the badge drift upward over a session.
        private float canBuyBadgeRestY;

        // The state whose SoftMoneyChanged this view is subscribed to, held so OnDestroy can
        // unsubscribe from the same object it subscribed to rather than walking the session
        // chain again -- by then the host may be half torn down.
        private GameState watchedState;

        // The transparent full-screen button under the confirm box. Held only so its
        // listener can be taken off in OnDestroy the way every other listener here is --
        // the object itself needs no lifetime management, because it is a CHILD of
        // confirmPopup and therefore goes on and off with the popup and dies with the scene.
        // That parenting is the whole trick: "when is the catcher up" is not a second
        // question this state machine has to answer, it is the same activeSelf the popup
        // already has.
        private Button confirmCatcher;

        // Keeps the popup off the very edge when the previewed prop sits at a corner of the
        // map. Not serialized: it is a "do not touch the screen border" constant, not a look.
        private const float ScreenMargin = 24f;

        private readonly List<MetaShopRowView> rows = new();

        // The CanvasGroup that fades the confirm popup's BUY, built on it rather than
        // authored -- the same call MetaShopRowView.EnsureBuyGroup and CreateConfirmCatcher
        // (D-159) make, for the same reason: everything on this screen lives in the SCENE,
        // so a serialized slot is a slot the next re-author can forget to fill, and the bug
        // returns with nothing reporting it.
        private CanvasGroup confirmBuyGroup;

        // "The player asked to go and earn the money for this prop." Raised when the confirm
        // popup's BUY is pressed on something they cannot afford; the payload is the prop
        // they were looking at, so a listener can say so.
        //
        // AN EVENT RATHER THAN A CALL, and the direction is the whole point (D-164). Starting
        // a day is MainScreenView's business -- it owns Play and the two gates that guard it
        // (the key gate D-069, the first-building gate D-090) -- but this view cannot hold a
        // reference to it: MainScreen -> Tutorial -> MetaSystem is an existing chain, so a
        // MainScreenView field here would close a cycle. Publishing leaves every arrow
        // pointing MainScreen -> MetaSystem, and it also means this screen never learns what
        // "go to the game" involves, which is what keeps those two gates un-duplicated.
        public EventBus<MetaItemDefinition> PlayRequested { get; } = new();

        private void Start()
        {
            // Hidden BEFORE validating, and with null checks of its own, because a missing
            // reference has to read as an error in the console rather than as the shop
            // covering the screen. This is what went wrong when the scene still held the
            // Ş1-era shop: validation returned early, so nothing was ever switched off and
            // the panel sat over the map with a market button that had no listener yet
            // (D-036). An incomplete shop should be invisible, not stuck open.
            if (panel != null) panel.SetActive(false);
            if (confirmPopup != null) confirmPopup.SetActive(false);

            // Same paragraph, same reason: an unfinished shop should be quiet, not nagging.
            // A badge left on by a scene saved with it visible would pulse over a market
            // button whose listener never got wired.
            //
            // The rest height is captured HERE, in the same breath, because this is the last
            // moment it is certainly the authored one -- and unlike the two SetActive calls
            // above it must happen even when validation then fails, or a later show would
            // hop the badge back to zero.
            if (canBuyBadge != null)
            {
                canBuyBadgeRestY = canBuyBadge.anchoredPosition.y;
                canBuyBadge.gameObject.SetActive(false);
            }

            if (!ValidateReferences()) return;

            // The container has to stay active whatever the scene was saved as, and only
            // the template inside it toggles. Authoring a list by hiding the container to
            // get the sample row out of the way silently hides every real row too --
            // TicketCardView records the same trap for its modification rows, which is
            // where this list's shape comes from.
            rowsParent.gameObject.SetActive(true);
            rowTemplate.gameObject.SetActive(false);

            // Closed on open, whatever the scene was saved as. A shop already up when the
            // screen appears drops the player into a purchase flow they never asked for and
            // hides the grounds they came to look at. The scene's checkbox is deliberately
            // not the authority, because it is easy to leave ticked after testing.
            SetOpen(false);

            marketButton.onClick.AddListener(OnMarketClicked);
            if (closeButton != null) closeButton.onClick.AddListener(OnCloseClicked);
            if (backdropButton != null) backdropButton.onClick.AddListener(OnCloseClicked);
            confirmBuyButton.onClick.AddListener(OnConfirmBuyClicked);
            confirmCancelButton.onClick.AddListener(ClosePreview);

            // Built here rather than in ShowPreview: it is one object for the life of the
            // screen, and creating it on the first preview would put a GameObject allocation
            // inside a tap the player is watching an animation land on. Safe at this point in
            // Start because confirmPopup is a REQUIRED reference -- ValidateReferences has
            // already returned by the time this line runs.
            confirmCatcher = CreateConfirmCatcher();
            if (confirmCatcher != null) confirmCatcher.onClick.AddListener(ClosePreview);

            // THE TWO EVENTS SetOpen CANNOT COVER. Every transition of this state machine
            // ends in ApplyVisibility, which re-asks the badge's question -- so the screen
            // opening, the shop opening and closing, a preview going up and coming down, and
            // the SetOpen(false) a purchase ends on are all already handled by being
            // transitions. What is left is the two ways the ANSWER changes while the machine
            // sits still:
            //
            //   money   -- the powerup shop sells on this same screen, and the debug menu
            //              grants here too. Subscribed to GameState rather than to either of
            //              them, because Wallet is the single writer and its state is where
            //              every spender already has to go through.
            //   location -- the player walks to another location while the panel is closed,
            //              which is precisely the window the badge is up for.
            //
            // Both are event-frequency and both are one bool's worth of work; neither adds
            // anything per frame.
            watchedState = sessionHost.Session?.State;
            watchedState?.SoftMoneyChanged.Subscribe(OnWatchedMoneyChanged);
            grounds.ViewedLocationChanged.Subscribe(OnViewedLocationChanged);

            // The badge's first answer. Start's SetOpen(false) above ran BEFORE these
            // subscriptions and asked it once already; this is not that call repeated for
            // safety, it is the one that runs after Refresh has had a chance to resolve the
            // grounds, since an unresolved ViewedLocation reads as "nothing to buy".
            RefreshCanBuyBadge();
        }

        private void OnDestroy()
        {
            if (marketButton != null) marketButton.onClick.RemoveListener(OnMarketClicked);
            if (closeButton != null) closeButton.onClick.RemoveListener(OnCloseClicked);
            if (backdropButton != null) backdropButton.onClick.RemoveListener(OnCloseClicked);
            if (confirmBuyButton != null) confirmBuyButton.onClick.RemoveListener(OnConfirmBuyClicked);
            if (confirmCancelButton != null) confirmCancelButton.onClick.RemoveListener(ClosePreview);
            if (confirmCatcher != null) confirmCatcher.onClick.RemoveListener(ClosePreview);

            watchedState?.SoftMoneyChanged.Unsubscribe(OnWatchedMoneyChanged);
            if (grounds != null) grounds.ViewedLocationChanged.Unsubscribe(OnViewedLocationChanged);

            // The tween outlives this component otherwise: DOTween holds the sequence, not
            // the object, and a scene change during a hop would tick it against a destroyed
            // RectTransform. SetLink covers the badge being destroyed; this covers THIS
            // component going away while the badge does not.
            KillCanBuyHop(restore: false);
        }

        // Both discard the payload: the badge's question is re-asked in full rather than
        // updated from what changed, because "can anything be bought" depends on the money
        // AND the location AND what is owned, and an update from one of the three is how two
        // answers to one question get started.
        private void OnWatchedMoneyChanged(int softMoney) => RefreshCanBuyBadge();

        private void OnViewedLocationChanged(MetaLocation location) => RefreshCanBuyBadge();

        private void OnMarketClicked() => SetOpen(!isOpen);

        // Both new exits share one handler and one method name, so the listener that goes on
        // in Start is the listener that comes off in OnDestroy. Unconditionally CLOSE rather
        // than toggle, unlike the market button: the X and the backdrop are only reachable
        // while the shop is up, so a toggle there could only ever mean close, and spelling it
        // as a toggle would invite a future caller to press it shut and open again.
        private void OnCloseClicked() => SetOpen(false);

        // The one place the panel's visibility changes, even now that there are only two
        // states. Every later transition -- a row's BUY opening the preview, the confirm
        // popup committing, cancelling back out -- has to leave the panel in a defined
        // state, and they will all come through here rather than each calling SetActive.
        // The first shop's ghost ended up with more than one way to leak precisely because
        // its visibility had more than one owner (D-024/D-027).
        private void SetOpen(bool open)
        {
            // The one door is also where a pending auto-reopen dies (D-096). Every caller of
            // this method is either the player working the shop or the purchase path itself,
            // and in both cases a timer left running from an earlier purchase would act on a
            // decision the player has already overtaken. The purchase path is unharmed
            // because it starts its wait AFTER its own SetOpen(false), and the wait clears
            // the handle before reopening, so this line cannot cancel the coroutine that is
            // calling it.
            CancelPendingReopen();

            isOpen = open;

            // Closing the shop from ÖNİZLEME goes through the one exit door, so the ghost
            // cannot survive the panel that summoned it. This is the leak the first shop
            // had (D-024) and the reason the machine has a single door at all.
            ClosePreview();

            ApplyVisibility();

            // Rebuilt on every open rather than once in Start: a purchase changes what
            // ShopItems answers (the bought prop drops out of the list), and opening the
            // panel is the moment that catches it. Never per frame -- this runs on a tap.
            if (open) RebuildRows();
            else ClearRows();
        }

        // The list and the popup are never both on screen. The panel is a bottom sheet
        // covering most of the map, and the ghost stands ON the map -- so previewing while
        // the list is up would hide the very thing being previewed, and the feature would
        // look broken rather than hidden. Two conditions, one place: `isOpen` is what the
        // market button toggles, `pendingItem` is what a row's BUY sets, and the panel's
        // activeSelf is neither of them on its own.
        private void ApplyVisibility()
        {
            var previewing = pendingItem != null;

            // Read BEFORE the SetActive calls, because "did this just come on?" is the whole
            // question and afterwards it is unanswerable. ApplyVisibility is called from three
            // places and re-runs while the shop is already open -- without this the sheet
            // would restart its fade from zero every time a row changed, which reads as the
            // shop flickering. Only a genuine off-to-on transition fades.
            var panelOpening = !panel.activeSelf && isOpen && !previewing;
            var confirmOpening = !confirmPopup.activeSelf && previewing;

            panel.SetActive(isOpen && !previewing);
            confirmPopup.SetActive(previewing);

            // Exactly one of these can be true, since the two are never on together (see
            // above). That matters: fading BOTH sides of the swap would cross-dissolve the
            // sheet into the confirm box and let the map show through the gap between them.
            if (animConfig != null)
            {
                if (panelOpening) PopupFade.In(panel, animConfig.PopupFadeInDuration);
                if (confirmOpening) PopupFade.In(confirmPopup, animConfig.PopupFadeInDuration);
            }

            // The badge rides along on the same door, which is what makes "returning to the
            // screen" and "a purchase just went through" cost no call sites of their own:
            // Start closes the shop, and so does the last line of a purchase. Cheap enough to
            // belong here -- a walk over one location's item list, on a tap.
            RefreshCanBuyBadge();
        }

        // Visible when the player could act on it and it is not in the way: something in this
        // location is affordable, and the sheet the button opens is not already up. Hiding it
        // while the shop is open is not tidiness -- the market button is drawn ON TOP of the
        // panel (see its field), so a badge that stayed would hop over the very list it was
        // pointing at, telling the player to go somewhere they already are.
        //
        // NOT cached. The answer depends on the balance, the location and the owned set, and
        // a cached bool would be a fourth place those three facts are known -- exactly the
        // kind of second copy that starts lying after the change nobody remembered to hook.
        private void RefreshCanBuyBadge()
        {
            if (canBuyBadge == null) return;

            SetCanBuyBadgeVisible(!isOpen && pendingItem == null && HasSomethingToBuy());
        }

        // Asks the rules, as everything on this screen does. A null session or an unresolved
        // location answers NO rather than being an error: both mean the screen has not
        // finished building, and a badge that flashed on during a load would be a promise
        // made before anything was known -- the same call RebuildRows makes one field over.
        private bool HasSomethingToBuy()
        {
            var session = sessionHost.Session;
            var location = grounds.ViewedLocation;
            if (session == null || location == null) return false;

            return MetaPurchase.HasAffordableOffer(
                location, session.OwnedMetaItemIds, session.State.CurrentDayIndex, session.State.SoftMoney);
        }

        // Idempotent on purpose, because the three triggers overlap freely -- a purchase
        // publishes a money change AND ends in a transition, so this runs twice in one frame
        // for one event. Asking whether the object is already active before touching the
        // tween is what keeps that from restarting the hop mid-air and turning a steady pulse
        // into a stutter.
        private void SetCanBuyBadgeVisible(bool visible)
        {
            if (visible == canBuyBadge.gameObject.activeSelf) return;

            if (!visible)
            {
                KillCanBuyHop(restore: true);
                canBuyBadge.gameObject.SetActive(false);
                return;
            }

            canBuyBadge.gameObject.SetActive(true);
            StartCanBuyHop();
        }

        // Hop, land, wait, hop again -- built as a sequence rather than a yoyo so the REST is
        // part of the loop. A yoyo of the same two tweens would bounce continuously, and a
        // badge that never stands still stops reading as "look here" and starts reading as
        // decoration.
        //
        // The two eases are the shape of a jump seen from the side: leaving the ground fast
        // and slowing at the top (OutQuad), then falling back with the opposite curve. Same
        // pair, and the same reason, as every other hop in this project.
        private void StartCanBuyHop()
        {
            KillCanBuyHop(restore: true);

            // A zero hop is a legitimate authoring choice -- the badge simply appears and
            // sits there -- so it is not a warning, but it must not become a sequence that
            // loops forever moving nothing.
            if (canBuyHopHeight <= 0f) return;

            var half = canBuyHopDuration * 0.5f;

            canBuyHop = DOTween.Sequence()
                .Append(canBuyBadge.DOAnchorPosY(canBuyBadgeRestY + canBuyHopHeight, half).SetEase(Ease.OutQuad))
                .Append(canBuyBadge.DOAnchorPosY(canBuyBadgeRestY, half).SetEase(Ease.InQuad))
                .AppendInterval(canBuyHopRest)
                .SetLoops(-1)
                .SetLink(canBuyBadge.gameObject);
        }

        // `restore` is the difference between hiding the badge and tearing it down. Hiding
        // puts it back on its authored line so the next show starts from the ground; teardown
        // must not touch a transform that may already be destroyed.
        private void KillCanBuyHop(bool restore)
        {
            canBuyHop?.Kill();
            canBuyHop = null;

            if (!restore || canBuyBadge == null) return;

            canBuyBadge.anchoredPosition = new Vector2(canBuyBadge.anchoredPosition.x, canBuyBadgeRestY);
        }

        // A row's BUY. Replaces whatever was being previewed rather than refusing -- tapping
        // a second row while the popup is up is a change of mind, not an error, and the old
        // ghost has to go with it.
        private void ShowPreview(MetaItemDefinition item)
        {
            pendingItem = item;

            grounds.ShowGhost(item);

            // Authored text stays in the scene; only the values come from the catalog. The
            // price is a bare number here for the same reason it is in the row: the HUD's
            // coin icon is this screen's unit.
            if (confirmNameLabel != null) confirmNameLabel.text = RowName(item);
            if (confirmPriceLabel != null) confirmPriceLabel.text = item.Price.ToString();

            // LIVE EVEN WHEN THE MONEY IS SHORT, and that is a reversal of Ş5 rather than a
            // slip back to what came before it (D-164). Ş5 closed this button because it was
            // dead -- it called Evaluate, got NotEnoughMoney, logged, and left the screen
            // exactly as it was, which is the worst kind of button. What changed is that
            // pressing it now DOES something: it takes the player to the day, to go and earn
            // the price. A disabled Button raises no onClick at all, so being pressable is
            // not a look here, it is the mechanism.
            var session = sessionHost.Session;
            var affordable = session != null && item.Price <= session.State.SoftMoney;
            confirmBuyButton.interactable = true;

            // Red, and faded, and still pressable. Ş5 leaned on the DISABLED tint to darken
            // the red for it; with the button live that tint is gone, so the fade has to be
            // applied directly or an unaffordable BUY would come out brighter than an
            // affordable one. Same pair of signals the row's BUY carries since D-163, and
            // deliberately the same amount -- the row and the popup are two views of one
            // refusal, and a player who saw the row dim expects the popup to agree.
            if (confirmBuyButton.targetGraphic != null)
            {
                confirmBuyButton.targetGraphic.color = affordable ? buyAffordableColor : buyUnaffordableColor;
            }

            EnsureConfirmBuyGroup();
            if (confirmBuyGroup != null)
            {
                confirmBuyGroup.alpha = affordable || rowTemplate == null ? 1f : rowTemplate.UnaffordableAlpha;
            }

            ApplyVisibility();

            // AFTER the ghost exists and the map has been framed around it, because the box
            // is placed against the ghost's on-screen edges and both of those move.
            PlaceConfirmBox();
        }

        // Over the prop rather than in the middle of the screen: the popup is about THAT
        // thing, and a centred box makes the player look away from the ghost to read what
        // they are buying. Recomputed on every preview -- a position cached from the first
        // prop would be wrong for the second.
        private void PlaceConfirmBox()
        {
            if (confirmBox == null) return;
            if (!grounds.TryGetGhostBounds(out var ghostTop, out var ghostBottom)) return;

            var parent = confirmBox.parent as RectTransform;
            if (parent == null) return;

            // Converted into the parent's own space rather than positioned in world units,
            // because the box's size is measured there: mixing the two would drift by
            // whatever the canvas scaler is doing on the current device.
            var topLocal = parent.InverseTransformPoint(ghostTop);
            var bottomLocal = parent.InverseTransformPoint(ghostBottom);
            var size = confirmBox.rect.size;
            var area = parent.rect;

            // Pivot is bottom-centre, so y IS the box's lower edge.
            var y = topLocal.y + confirmGap;
            if (y + size.y > area.yMax - ScreenMargin)
            {
                // The prop is near the top of the screen and there is no room over it, so
                // the box hangs underneath instead. Better than covering the very thing it
                // describes.
                y = bottomLocal.y - confirmGap - size.y;
            }

            confirmBox.localPosition = new Vector3(
                Mathf.Clamp(topLocal.x, area.xMin + size.x * 0.5f + ScreenMargin, area.xMax - size.x * 0.5f - ScreenMargin),
                Mathf.Clamp(y, area.yMin + ScreenMargin, area.yMax - size.y - ScreenMargin),
                0f);
        }

        // THE one exit from ÖNİZLEME. Cancel, tapping outside, another row, the market
        // button, closing the shop -- all of them arrive here, which is what makes "when
        // does the ghost go away" a question with a single answer. Safe to call when
        // nothing is being previewed, because every caller would otherwise need to check.
        // A transparent, full-screen button as the popup's FIRST child. Three properties of
        // that placement do all the work:
        //
        //   inside confirmPopup -- it is shown and hidden by the SetActive this state machine
        //                          already makes, so the catcher's visibility is not a second
        //                          thing to keep in step (D-024's lesson, applied to a
        //                          catcher rather than to a ghost);
        //   FIRST sibling       -- UGUI hit-tests children after their parent and later
        //                          siblings over earlier ones, so Box and its BUY and CANCEL
        //                          keep winning every tap that actually lands on them;
        //   stretched 0..1      -- ConfirmPopup is itself anchored 0..1 with a zero
        //                          sizeDelta, so this covers the whole canvas and nothing
        //                          behind the popup is reachable while it is up. "Continue
        //                          Day" is the one that mattered.
        //
        // BUILT, NOT AUTHORED, unlike nearly everything else on this screen, and D-038 is the
        // reason: the last full-screen backdrop here WAS a scene object, and a scene object is
        // precisely what got deleted. A serialized field would be a slot the scene does not
        // fill, and the bug would come back silently the next time this popup is re-authored.
        // MetaGroundsView.CreateSkipCatcher makes the same call for the same reason.
        //
        // The click goes to ClosePreview, the exit door that already exists: an outside tap is
        // a change of mind, which is exactly what CANCEL means, and a second way out with its
        // own idea of what to clean up is how a ghost survives the panel that summoned it.
        private Button CreateConfirmCatcher()
        {
            if (confirmPopup == null) return null;

            var catcher = new GameObject("ConfirmCatcher", typeof(RectTransform), typeof(Image), typeof(Button));
            var rect = (RectTransform)catcher.transform;
            rect.SetParent(confirmPopup.transform, worldPositionStays: false);
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
            rect.SetAsFirstSibling();

            var image = catcher.GetComponent<Image>();
            // Fully transparent and still hit-testable: an Image raycasts against its RECT,
            // not against pixel alpha, unless an alpha threshold is set -- and none is. A
            // visible dim was considered and rejected: the box is placed over the ghost so the
            // player can see WHERE the prop would stand, and dimming the map would hide the
            // very thing being previewed.
            image.color = new Color(0f, 0f, 0f, 0f);
            image.raycastTarget = true;

            var button = catcher.GetComponent<Button>();
            button.targetGraphic = image;
            button.transition = Selectable.Transition.None;

            return button;
        }

        // Built, not authored -- see confirmBuyGroup's own comment. Idempotent, and written
        // as an explicit == null rather than ??, which bypasses the operator
        // UnityEngine.Object overloads and is the standard way to end up holding a reference
        // that is "not null" and not alive.
        //
        // A CanvasGroup's defaults are what make it safe to add blind: interactable and
        // blocksRaycasts both start true, so the BUY keeps its tap -- which it must, since
        // the tap is now the whole point.
        private void EnsureConfirmBuyGroup()
        {
            if (confirmBuyGroup != null || confirmBuyButton == null) return;

            var existing = confirmBuyButton.GetComponent<CanvasGroup>();
            confirmBuyGroup = existing != null
                ? existing
                : confirmBuyButton.gameObject.AddComponent<CanvasGroup>();
        }

        private void ClosePreview()
        {
            if (pendingItem == null) return;

            pendingItem = null;
            grounds.ClearGhost();
            ApplyVisibility();
        }

        // The purchase (Ş4). This is the only place in the meta side that spends money, and
        // it owns no rule: MetaPurchase decides whether the buy is allowed, Wallet decides
        // whether the balance can carry it, and GameSession.Save decides what reaches disk.
        //
        // ATOMIC BY CONSTRUCTION, not by care taken here: the balance and the owned set live
        // on the same GameSession and Save() writes both together, so "the money went but
        // the item did not arrive" is not a state this code can express. That is why the
        // order below is safe to read literally.
        private void OnConfirmBuyClicked()
        {
            if (pendingItem == null) return;

            var session = sessionHost.Session;
            var location = grounds.ViewedLocation;
            if (session == null || location == null) return;

            var item = pendingItem;

            // Evaluated a SECOND time, having already been evaluated when the row was drawn.
            // The original reason was that the panel stayed open across several purchases, so
            // the row's balance could be stale by now; since D-047 closes the shop on every
            // purchase, that particular staleness is gone. The re-check STAYS anyway: it is
            // the guard that this screen never spends on a verdict it did not just ask for,
            // and it costs one function call on a tap. Deleting it would make the correctness
            // of a purchase depend on the shop's lifetime, which is exactly the coupling the
            // next change to that lifetime would break silently.
            var verdict = MetaPurchase.Evaluate(
                location, item, session.OwnedMetaItemIds, session.State.CurrentDayIndex, session.State.SoftMoney);

            // NO MONEY IS ANSWERED WITH THE WAY TO GET SOME (D-164). It is the one refusal
            // the player can act on immediately, and the answer to it is a day -- so this
            // press takes them there instead of buzzing at them. Every other verdict keeps
            // the buzz below: "already yours" and "that area is locked" are not things a
            // trip to the kitchen fixes.
            //
            // Branching on the VERDICT rather than re-comparing price against balance,
            // because MetaPurchase is the single authority on why a buy is refused and this
            // screen holding its own opinion is how the two come to disagree.
            //
            // Published rather than acted on: what "go and play" means -- the key gate, the
            // first-building gate, the scene load -- belongs to MainScreenView, and this view
            // deliberately does not know any of it. See PlayRequested for why the arrow
            // points that way and not the other.
            //
            // The preview is left OPEN and the ghost left standing. If nobody is listening
            // (the field is unwired on the main screen) the press falls back to exactly the
            // logged refusal it was before, never to a bypass; and if someone is, the scene
            // is about to be destroyed anyway.
            if (verdict == MetaPurchaseVerdict.NotEnoughMoney)
            {
                Debug.Log($"'{item.Id}' costs {item.Price} and the player has {session.State.SoftMoney}: sending them to play.", this);
                PlayRequested.Publish(item);
                return;
            }

            if (verdict != MetaPurchaseVerdict.Ok)
            {
                // Left open on purpose, with the reason in the console. Saying WHY on screen
                // is Ş5's job, and doing it here as well would put the appearance of a
                // verdict in two places.
                Debug.Log($"'{item.Id}' was not bought: {verdict}.", this);

                // One moment for every refusal rather than one per verdict: to the hand,
                // "not enough money", "area locked" and "already owned" are the same
                // answer -- no. Which one it was is Ş5's job to say on screen.
                haptics?.Request(HapticMoment.PurchaseRefused);
                return;
            }

            // The wallet is asked even though Evaluate just said the money is there, because
            // Wallet is the single writer of the balance and its answer is the authority.
            // These are not two answers to one question -- Evaluate is the decision, this is
            // the transaction, and only the second one can refuse on the real number.
            if (!session.Wallet.TrySpendSoftMoney(item.Price))
            {
                Debug.LogWarning(
                    $"'{item.Id}' passed {nameof(MetaPurchase)} but the wallet refused to spend {item.Price}. " +
                    "Nothing was bought and nothing was written.", this);

                // The wallet's own refusal, which Evaluate above should have caught first.
                // Unreachable in practice today; buzzing here anyway costs one line and
                // keeps the two refusal paths from feeling different if it ever is reached.
                haptics?.Request(HapticMoment.PurchaseRefused);
                return;
            }

            session.OwnedMetaItemIds.Add(MetaCatalog.OwnershipKey(location.Id, item.Id));

            // One write, both facts. A failure here leaves the session correct in memory and
            // the file behind by one purchase -- PlayerProfileStore logs why. There is no
            // partial write to recover from, because the profile is one file written whole.
            session.Save();

            // Here rather than on the tap: this is the first line at which the purchase is
            // a fact -- the verdict passed, the wallet actually spent, and the file has the
            // prop. A buzz on the tap would sometimes fire for a purchase that then failed.
            // Deliberately the lightest preset in the table, because the payoff the player
            // is waiting for is the prop hitting the ground a moment later.
            haptics?.Request(HapticMoment.PropPurchased);

            // THE GROUNDS ARE TOLD FIRST, AND THE ORDER OF THESE TWO CALLS IS LOAD-BEARING
            // (D-049). RefreshAfterPurchase claims the map's framing before anything can ask
            // for it back; SetOpen(false) below goes through ClosePreview and ClearGhost,
            // whose second half is exactly that request. Closing first would start the zoom
            // out before the grounds had heard about the purchase, and the prop would land on
            // a map already travelling away from it -- which is the bug this ordering exists
            // to prevent, found while wiring D-049 rather than reasoned about in advance.
            //
            // Both calls land in the same frame with no render between them, so nothing is
            // visibly half-done: the panel and the drop begin together.
            //
            // The prop is redrawn as OWNED (MetaResolver.IsActive answers differently now, so
            // it is drawn solid) and then SET DOWN -- it falls the last stretch into place and
            // the ground takes the hit. The shop passes WHAT was bought and nothing else: what
            // a purchase looks like on the grounds is the grounds' business, and this screen
            // stays a thing that spends money and asks two views to catch up.
            grounds.RefreshAfterPurchase(item);

            // The shop CLOSES on a purchase, all the way down to KAPALI (D-047, the user's
            // instruction). MS4 said the opposite -- the panel stayed open so several props
            // could be bought without tapping the market button between each one -- and that
            // was wrong about what the player wants to see next: the panel is a bottom sheet
            // over most of the map, so coming back to a list means the thing just bought is
            // hidden behind the list of things not bought yet. The payoff of a purchase is
            // the prop standing on the grounds, and the screen now ends on it.
            //
            // Through SetOpen rather than by clearing `isOpen` here, because that is the one
            // door: it takes the preview down, applies the visibility for both states at once,
            // and drops the rows. The ghost leaked in the first shop precisely because a
            // transition took a shortcut around this (D-024). Its ClearGhost call is now a
            // no-op on both halves -- the ghost was destroyed by the redraw above, and the
            // framing belongs to the placement until it lands -- and that is by design, not a
            // coincidence to lean on: the list is NOT rebuilt either, because a closed panel
            // has no rows and the next SetOpen(true) builds them against the balance as it
            // will be then rather than as it is now.
            SetOpen(false);

            // ...and comes STRAIGHT BACK for one kind of prop: the one that opens an area
            // (D-096, the user's instruction). This is not a second opinion about D-047 above
            // -- the shop still closes on every purchase, and the screen still ends on the
            // prop -- it is the one case where closing for good hides something that just
            // came into existence. MetaPurchase.ShopItems HIDES area-locked props entirely
            // (D-019 + the user's 2026-08-21 call), so buying the square is the only purchase
            // in the game whose payoff is partly a LIST: a handful of rows that were not there
            // a moment ago. Leaving them behind a market button the player has no reason to
            // tap again is what makes an area unlock feel like it bought nothing.
            //
            // Read from the catalog's own UnlocksArea flag rather than from a list of ids
            // here, so authoring a second expansion in the Meta window needs no code.
            //
            // Started AFTER SetOpen(false), which is what keeps the two from arguing: the
            // close is the shop's own exit path and must run whole before anything schedules
            // a return. The wait itself is in the coroutine below.
            if (item.UnlocksArea)
            {
                reopenRoutine = StartCoroutine(ReopenWhenTheGroundsSettle(location));
            }

            Debug.Log($"Bought '{item.Id}' for {item.Price}. Balance is now {session.State.SoftMoney}.", this);
        }

        // Waits out the purchase the player is watching, then puts the list back up.
        //
        // Polled rather than told, and the poll is the cheap half of this: one bool and one
        // tween query per frame, alive only for the second or so a placement lasts, at most
        // once per purchase. The expensive half would have been a completion callback on the
        // grounds -- see IsSettlingPurchase for why that shape was rejected. Nothing here
        // knows what the animation IS; it asks the grounds whether they are done.
        //
        // WAITS FOR THE WHOLE SETTLE, not just the drop: the prop lands, the ground shakes,
        // the map travels back out, and only then does the sheet come up. Reopening at the
        // earlier moment would slide a panel that covers most of the map over a map still
        // moving underneath it, which reads as the shop interrupting the payoff it was
        // supposed to be rewarding.
        private IEnumerator ReopenWhenTheGroundsSettle(MetaLocation boughtOn)
        {
            while (grounds.IsSettlingPurchase) yield return null;

            // Cleared BEFORE the reopen, because SetOpen cancels whatever this field points
            // at -- and what it points at right now is this coroutine.
            reopenRoutine = null;

            // ABANDONED rather than forced, if the player walked to another location while
            // the prop was landing. The rows would be rebuilt for wherever they are standing
            // now, so the shop would open somewhere they never asked for it -- and the tap
            // this exists to save them is not worth taking the screen away from something
            // they did with their own hands. The other two ways out -- opening the shop
            // themselves, or reopening it into a preview -- cannot reach this line at all,
            // because both go through SetOpen, which cancels this coroutine.
            if (grounds.ViewedLocation != boughtOn) yield break;

            SetOpen(true);
        }

        private void CancelPendingReopen()
        {
            if (reopenRoutine == null) return;

            StopCoroutine(reopenRoutine);
            reopenRoutine = null;
        }

        // The shop asks; it does not decide. Which props are offered comes from
        // MetaPurchase.ShopItems, where "unaffordable and area-gated STAY in the list, only
        // the already-owned drop out" is written down and tested (D-019). Ş2 draws that
        // answer as-is: every row looks the same and every BUY logs. Telling the player
        // WHY a row cannot be bought is Ş5's job, and doing it here early would mean two
        // places deciding what a verdict looks like.
        private void RebuildRows()
        {
            ClearRows();

            var session = sessionHost.Session;
            var location = grounds.ViewedLocation;
            if (session == null || location == null)
            {
                // Not an error: the grounds warn about an unreachable catalog themselves,
                // and a session that is still null means this ran before the scene finished
                // building. Either way an empty shop is the honest thing to show.
                return;
            }

            // ASKED BEFORE MetaPurchase, not after it, and that ordering is the feature. A
            // location with nothing authored in it yet is a fact about the CATALOG, and the
            // catalog answers it (`HasNoItems` -- the very condition MetaCatalogValidator
            // warns about in the Meta window). Deriving it from an empty offer list instead
            // would fold it together with the case where the player has simply bought
            // everything, and that player would be promised more props for a location that
            // is finished.
            //
            // No id and no location count appears here: whichever location is unfinished
            // says so, this one or the fourth one, without this file learning its name
            // (D-015 -- nothing about the meta side is hardcoded).
            if (location.HasNoItems)
            {
                if (comingSoonLabel != null) comingSoonLabel.SetActive(true);

                // Nothing further to build. The rows were already cleared above, so the sheet
                // shows the label over an empty list rather than the label over stale rows.
                return;
            }

            // The list is already filtered AND ordered by the rules layer: area-locked props
            // are gone, and what remains is affordable-first then cheapest-first (D-035). The
            // view does not re-sort or re-filter -- one authority for what a shop shows.
            var balance = session.State.SoftMoney;
            var offers = MetaPurchase.ShopItems(
                location, session.OwnedMetaItemIds, session.State.CurrentDayIndex, balance);

            foreach (var item in offers)
            {
                var row = Instantiate(rowTemplate, rowsParent);

                // Instantiate copies the template's inactive state, so a clone is born
                // hidden and has to be switched on explicitly. Same two lines
                // TicketCardView uses, and the reason it needs them.
                row.gameObject.SetActive(true);

                // Captured per row, because the closure is what carries WHICH prop this
                // row's BUY means. The loop variable itself would be shared under an older
                // C# and is captured here deliberately rather than relied upon.
                var offer = item;

                // Affordability is read off the price against the balance rather than by
                // calling Evaluate again: everything Evaluate could still refuse on has
                // already been filtered out of this list, so money is the only question left
                // and asking a six-verdict function for a yes/no would suggest otherwise.
                var affordable = offer.Price <= balance;

                // ShopIcon, not Sprite: the row shows however the catalog chooses to
                // represent this prop in a list, which falls back to the prop's own art when
                // no icon is authored. The ghost and the map prop still use Sprite -- those
                // answer "how will this look where it stands", and a preview drawn from a
                // list icon would be a preview that lies.
                row.Bind(offer.ShopIcon, RowName(offer), offer.Price, affordable,
                    affordable ? buyAffordableColor : buyUnaffordableColor,
                    () => ShowPreview(offer));

                rows.Add(row);
            }
        }

        // The catalog's display name, falling back to the id. Same fallback the grounds'
        // location label makes: an unnamed entry should be identifiable rather than blank,
        // because a blank row is indistinguishable from a broken one.
        private static string RowName(MetaItemDefinition item) =>
            string.IsNullOrWhiteSpace(item.DisplayName) ? item.Id : item.DisplayName;

        // Destroyed and rebuilt rather than pooled. Sixteen rows built on a tap do not
        // need a pool, and a pool would add a path where a row survives with stale
        // contents -- MetaGroundsView.Clear makes the same call for the same reason.
        private void ClearRows()
        {
            foreach (var row in rows)
            {
                if (row != null) Destroy(row.gameObject);
            }
            rows.Clear();

            // The label goes down with the rows, because it is the list's other face rather
            // than a thing of its own: RebuildRows raises exactly one of the two, and both
            // are dropped here. That is what keeps "when does COMING SOON go away" a question
            // with a single answer -- the same discipline ClosePreview enforces for the
            // ghost, and for the same reason (D-024: a second owner is how the first shop's
            // ghost learned to survive the panel that summoned it). It also means walking to
            // a finished location cannot leave the previous one's label standing over its
            // rows, since every open rebuilds through here.
            if (comingSoonLabel != null) comingSoonLabel.SetActive(false);
        }

        // ALL of these are REQUIRED, unlike the grounds' optional location bar: a shop with
        // no button cannot be opened, a button with no panel does nothing when tapped, and
        // a list with no session or no location cannot know what to offer. None has a
        // degraded mode worth shipping -- listing the first location's props when `grounds`
        // is unwired would show a wrong list as though it were right the day a second
        // restaurant exists, which is the failure mode D-028 was written about.
        private bool ValidateReferences()
        {
            var missing = new List<string>();
            if (marketButton == null) missing.Add(nameof(marketButton));
            if (panel == null) missing.Add(nameof(panel));
            if (sessionHost == null) missing.Add(nameof(sessionHost));
            if (grounds == null) missing.Add(nameof(grounds));
            if (rowsParent == null) missing.Add(nameof(rowsParent));
            if (rowTemplate == null) missing.Add(nameof(rowTemplate));

            // The popup's four interactive parts are required too. An unwired cancel button
            // strands the player in a preview with a ghost on the map and no way back, which
            // is the leak this whole part exists to make impossible. It is no longer the ONLY
            // way out -- the catcher built above cancels on an outside tap, which is a second
            // door and deliberately so -- but it stays required: a catcher is invisible, and
            // "tap somewhere" is not a thing a player can be expected to discover. The two
            // LABELS are optional by contrast -- they only say what is being bought.
            if (confirmPopup == null) missing.Add(nameof(confirmPopup));
            if (confirmBuyButton == null) missing.Add(nameof(confirmBuyButton));
            if (confirmCancelButton == null) missing.Add(nameof(confirmCancelButton));

            // Required rather than optional: without it the popup would quietly go back to
            // sitting in the middle of the screen, which is the behaviour the user asked to
            // change. A silently-missing feature is worse than a named missing reference.
            if (confirmBox == null) missing.Add(nameof(confirmBox));

            if (missing.Count == 0) return true;

            Debug.LogError(
                $"{nameof(MetaShopView)} on '{name}' is missing Inspector reference(s): {string.Join(", ", missing)}. " +
                "Run ExpoTheExplorer > Meta > Build Meta Shop.", this);
            return false;
        }
    }
}
