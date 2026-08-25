using System.Collections.Generic;
using ExpoTheExplorer.Bootstrap;
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
        [Header("Bound by ExpoTheExplorer > Meta > Build Meta Shop")]
        [Tooltip("Opens and closes the panel. Drawn ON TOP of the panel on purpose, so it can still be tapped to close a panel that covers it.")]
        [SerializeField] private Button marketButton;

        [Tooltip("Everything the shop shows. Toggled with SetActive, so a closed shop costs nothing per frame.")]
        [SerializeField] private GameObject panel;

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

        [Header("Confirm popup")]
        [Tooltip("The confirm popup's root. Active only while a purchase is being previewed.")]
        [SerializeField] private GameObject confirmPopup;

        // There was a full-screen backdrop button here that cancelled on a tap outside the
        // box (MS6). The user removed it from the scene, so it is gone from the code too
        // (D-038): CANCEL is now the only way out of a preview. The other thing it did --
        // eating taps meant for the map behind -- costs nothing, because the ScrollRect is
        // already disabled for the duration of a preview (D-032), so a drag that lands
        // behind the popup cannot pan anything.
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

        // Keeps the popup off the very edge when the previewed prop sits at a corner of the
        // map. Not serialized: it is a "do not touch the screen border" constant, not a look.
        private const float ScreenMargin = 24f;

        private readonly List<MetaShopRowView> rows = new();

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
            confirmBuyButton.onClick.AddListener(OnConfirmBuyClicked);
            confirmCancelButton.onClick.AddListener(ClosePreview);
        }

        private void OnDestroy()
        {
            if (marketButton != null) marketButton.onClick.RemoveListener(OnMarketClicked);
            if (confirmBuyButton != null) confirmBuyButton.onClick.RemoveListener(OnConfirmBuyClicked);
            if (confirmCancelButton != null) confirmCancelButton.onClick.RemoveListener(ClosePreview);
        }

        private void OnMarketClicked() => SetOpen(!isOpen);

        // The one place the panel's visibility changes, even now that there are only two
        // states. Every later transition -- a row's BUY opening the preview, the confirm
        // popup committing, cancelling back out -- has to leave the panel in a defined
        // state, and they will all come through here rather than each calling SetActive.
        // The first shop's ghost ended up with more than one way to leak precisely because
        // its visibility had more than one owner (D-024/D-027).
        private void SetOpen(bool open)
        {
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
            panel.SetActive(isOpen && !previewing);
            confirmPopup.SetActive(previewing);
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

            // Visibly closed rather than silently refusing. Until Ş5 this button was live and
            // did nothing when the money was short -- it called Evaluate, got NotEnoughMoney,
            // logged, and left the screen unchanged, which is a dead button in every sense
            // the plan warned about. The commit still re-checks everything; this only stops
            // the player asking a question whose answer is already visible.
            var session = sessionHost.Session;
            var affordable = session != null && item.Price <= session.State.SoftMoney;
            confirmBuyButton.interactable = affordable;

            // Red as well as closed. The disabled state darkens whatever base colour is
            // here, so this reads as "red, and off" rather than as the generic grey a
            // disabled button would otherwise be -- the reason is the price, and the colour
            // says which reason.
            if (confirmBuyButton.targetGraphic != null)
            {
                confirmBuyButton.targetGraphic.color = affordable ? buyAffordableColor : buyUnaffordableColor;
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

            Debug.Log($"Bought '{item.Id}' for {item.Price}. Balance is now {session.State.SoftMoney}.", this);
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

            // The popup's four interactive parts are required too. An unwired cancel or
            // cancel button is the worst case on this screen, and more so since the backdrop
            // was removed (D-038) made it the ONLY way out: an unwired one strands the
            // player in a preview with a ghost on the map and no way back, which is the leak
            // this whole part exists to make impossible. The two LABELS are optional by
            // contrast -- they only say what is being bought.
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
