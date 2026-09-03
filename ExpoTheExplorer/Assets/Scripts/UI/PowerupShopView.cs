using System;
using System.Collections.Generic;
using ExpoTheExplorer.Core;
using ExpoTheExplorer.Data;
using ExpoTheExplorer.Session;
using ExpoTheExplorer.Systems.PowerupSystem;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace ExpoTheExplorer.UI
{
    // The main screen's powerup shop (GDD Section 5.2, .claude/powerup-plan.md Adım 3):
    // three rows, each showing how many charges the player owns, what one more costs in
    // Gems, and a BUY. This is the step that closes the economy loop -- earning (finishing
    // a day), buying (here) and spending (the day scene's panel) are finally connected.
    //
    // IT ONLY LISTS WHAT HAS BEEN INTRODUCED (D-160, the user's decision on 2026-09-02). A
    // powerup whose Day has not come is not a row here at all, and while none of them has
    // come, the screen's store button is not there either. D-117's locked row -- named, with
    // an owned count and a dead BUY -- is gone: a shop lists what is for sale, and a powerup
    // is taught by the day scene's bar, which still draws its own lock.
    //
    // IT NOW SELLS IN THE DAY SCENE TOO (D-105, the user's decision on 2026-08-27), which
    // reverses "buying lives on the menu, not in the day". The old rule's reason was that
    // opening a store mid-service suspends the very time pressure a powerup exists to
    // relieve -- and the reversal answers it head-on rather than ignoring it: the day is
    // FROZEN while this panel is up, so the pressure is not suspended, it is paused. What
    // is unchanged is that this view still knows nothing about that; the caller that opens
    // it holds the pause (PowerupBarView), which is what lets the same component serve a
    // screen with no day behind it at all.
    //
    // The day scene's PowerupBarView still has no price and no Gem icon of its own. It has
    // one thing: an empty powerup is a live button that opens THIS.
    //
    // IT DOES NOT LIVE INSIDE MetaShopView, and the alternative was considered. That file
    // owns props bought with SoftMoney and belongs to MetaSystem; adding a tab to it would
    // teach MetaSystem's view what a powerup is, plus give one 30KB class a tab state, a
    // second currency and a second row shape. This is a separate panel beside it.
    //
    // It also carries its OWN open button, exactly as MetaShopView carries its market
    // button. That is what keeps MainScreenView -- the screen's shell -- unaware that
    // powerups exist, and is why this step adds no new arrow to blueprint.md.
    public class PowerupShopView : MonoBehaviour
    {
        // Fired when the panel goes away, by whichever route. The day scene listens so it
        // can let the day run again; the main screen never subscribes and nothing there
        // notices. An event rather than the opener polling activeSelf, because the close
        // button is inside THIS view and a poll would be a second opinion about a fact this
        // object already knows exactly.
        public event Action Closed;

        // The scene's session provider (MainScreenRoot). Serialized and dragged, never
        // searched for. SessionHost rather than MainScreenRoot so this class does not care
        // which screen it is on -- the same seam MetaShopView and HudWalletSource take.
        [SerializeField] private SessionHost sessionHost;

        [Tooltip("The shop panel itself. Switched off at Start whatever the scene was saved as.")]
        [SerializeField] private GameObject panel;

        // CLOSES ON A TAP OUTSIDE THE SHEET (D-107, the user's decision on 2026-08-27), which
        // reverses D-105's "deliberately NOT a close-on-tap scrim". That reasoning -- a
        // mis-aimed tap dismissing a shop the player is mid-purchase in -- is real but small
        // beside what it cost: a full-screen block with only a corner X is the shape players
        // read as being trapped, and every other modal they use closes this way.
        //
        // It needs NO "was the click inside the panel" test, and that is the whole reason it
        // is one wired Button rather than a pointer handler doing hit-tests: the sheet is a
        // CHILD of this backdrop and carries its own raycast-target Image, so UGUI gives a
        // tap on the sheet to the sheet and only an outside tap ever reaches here. Wire this
        // to the same object `panel` points at, and the geometry does the filtering.
        [Tooltip("Optional. A Button on the full-screen backdrop — tapping outside the shop closes it. Set its Transition to None so it does not tint.")]
        [SerializeField] private Button backdropButton;

        // OPTIONAL since D-105. On the main screen this view owns its own opener, which is
        // what keeps MainScreenView unaware powerups exist. In the day scene there is no
        // such button by design -- an empty powerup in the bar IS the opener -- and a shop
        // that refused to work without one would force a dead button onto the day HUD.
        [Tooltip("Optional. The shop's own open button. Leave empty in the day scene, where an empty powerup opens it instead.")]
        [SerializeField] private Button openButton;

        [SerializeField] private Button closeButton;

        // Three named blocks rather than a cloned row template, and the difference from
        // MetaShopView is deliberate: its template exists because only the catalog knows
        // how many rows there are. Here the answer is three, it is written in the enum,
        // and it cannot change without a design decision -- so the rows are authored.
        // Named fields also cannot be mis-ordered against the enum the way an array can.
        [Tooltip("GDD 5.2 #1 — Auto-Collect.")]
        [SerializeField] private PowerupShopRow autoCollect;

        [Tooltip("GDD 5.2 #2 — Time Reset.")]
        [SerializeField] private PowerupShopRow timeReset;

        [Tooltip("GDD 5.2 #3 — Noise Clear. The day scene calls the same powerup's button LeakCleanerButton; same thing.")]
        [SerializeField] private PowerupShopRow noiseClear;

        [Tooltip("Optional. Assets/Data/BoardAnimationConfig.asset — read for Popup Fade In Duration only. This view lives on a PREFAB, so dragging it in there covers every instance. Unwired, the shop appears instantly, exactly as it did before the fade existed.")]
        [SerializeField] private BoardAnimationConfig animConfig;

        // Cached for the reason LivesView caches GameState: EventBus removes by delegate
        // equality on a specific instance, so what OnDestroy unsubscribes from must be the
        // object Start subscribed to. GameSession is built once in MainScreenRoot.Awake
        // and never swapped, so caching is safe and re-reading the property is not.
        private GameSession session;
        private PowerupManager manager;

        private readonly List<(PowerupType Type, PowerupShopRow Ui)> rows = new();

        // Start(), not Awake(): MainScreenRoot builds the session in ITS Awake and Unity
        // does not order Awake across GameObjects. Every view in this project binds from
        // Start for exactly this reason.
        private void Start()
        {
            // Hidden BEFORE anything can fail, and with its own null check. D-036 is the
            // recorded reason: when validation returned early, nothing was ever switched
            // off and the shop sat over the screen with a button that had no listener. An
            // incomplete shop must be invisible, not stuck open.
            if (panel != null) panel.SetActive(false);

            if (!ValidateReferences()) return;

            CollectRows();

            session = sessionHost.Session;
            manager = session?.PowerupManager;

            if (manager == null)
            {
                // A Log, not an error: a session with no PowerupConfig is the designed
                // fail-open state and MainScreenRoot has already reported it once. The
                // panel still opens -- hiding someone's authored UI is a worse surprise
                // than an inert one -- but every row reads zero and refuses to sell.
                Debug.Log(
                    $"{nameof(PowerupShopView)} on '{name}': this session has no powerup stock (no PowerupConfig " +
                    $"wired on this scene's {nameof(SessionHost)} — MainScreenRoot on the menu, GameManager in the " +
                    "day scene), so the shop stays inert.",
                    this);
            }

            if (openButton != null) openButton.onClick.AddListener(OnOpenClicked);
            if (backdropButton != null) backdropButton.onClick.AddListener(Close);
            closeButton.onClick.AddListener(Close);

            foreach (var (type, ui) in rows)
            {
                // Captured per iteration so all three buttons do not end up buying the last
                // powerup. foreach variables are already per-iteration in modern C#; the
                // local says so out loud.
                var offered = type;
                ui.BuyButton.onClick.AddListener(() => Buy(offered));
            }

            if (manager != null) manager.ChargesChanged.Subscribe(OnChargesChanged);

            // BOTH events, because a purchase moves two numbers and the second one changes
            // what the OTHER rows are allowed to do: spending Gems can make a row that was
            // affordable a moment ago unaffordable. Subscribing only to the charge count
            // would leave those rows buyable-looking until the panel was reopened.
            if (session?.State != null) session.State.GemsChanged.Subscribe(OnGemsChanged);

            // AND THE DAY, since D-160 -- which row is listed at all now depends on it. The
            // rows themselves would have been fine without this (Open refreshes before the
            // panel appears, so they are never drawn stale), but the open button is on screen
            // continuously, and a store button that only appeared after a scene reload would
            // leave the Day that introduces the first powerup with no shop behind it.
            //
            // The same event PowerupBarView (D-117) and DayNumberView listen to, and the same
            // reason: AdvanceToNextDay moves the index without reloading anything.
            if (session?.State != null) session.State.CurrentDayIndexChanged.Subscribe(OnDayIndexChanged);

            RefreshAll();
        }

        private void OnDestroy()
        {
            if (manager != null) manager.ChargesChanged.Unsubscribe(OnChargesChanged);
            if (session?.State != null) session.State.GemsChanged.Unsubscribe(OnGemsChanged);
            if (session?.State != null) session.State.CurrentDayIndexChanged.Unsubscribe(OnDayIndexChanged);

            if (openButton != null) openButton.onClick.RemoveListener(OnOpenClicked);
            if (backdropButton != null) backdropButton.onClick.RemoveListener(Close);
            if (closeButton != null) closeButton.onClick.RemoveListener(Close);

            // Told, not left hanging. A shop destroyed while open (a scene load landing
            // mid-purchase) would otherwise never fire Closed, and the day-scene caller
            // holding the pause on its behalf would have nothing to release against. It
            // releases from its own OnDestroy too; this is the half that also covers a
            // shop destroyed while its opener lives on.
            if (panel != null && panel.activeSelf) Closed?.Invoke();

            // Removed rather than left, because this view is legal to disable and
            // re-enable: a second Start would otherwise stack a second listener and a
            // single tap would buy twice.
            foreach (var (_, ui) in rows)
            {
                if (ui?.BuyButton != null) ui.BuyButton.onClick.RemoveAllListeners();
            }
            rows.Clear();
        }

        // The open button's listener, separate from Open() only because UnityAction takes
        // no return value and a named method is what RemoveListener can match -- a lambda
        // could be added but never taken off again.
        private void OnOpenClicked() => Open();

        // Both handlers refresh rather than only toggling, so a shop opened after a day
        // was completed shows the charges that day granted. Nothing else on the screen
        // would have redrawn them: the panel was inactive while the events fired.
        //
        // PUBLIC since D-105, so the day scene's powerup bar can open the same shop its own
        // open button does. It reports whether the panel actually came up: a shop with no
        // panel wired cannot open, and the caller needs to know that before it freezes a day
        // it would then have no way to unfreeze -- the panel that would carry the close
        // button is the missing thing.
        public bool Open()
        {
            if (panel == null) return false;

            // RefreshAll BEFORE the fade starts, so the rows are already showing the right
            // prices and owned counts as they come up. Fading in a panel and then correcting
            // its numbers would be visible at this duration.
            RefreshAll();
            panel.SetActive(true);
            if (animConfig != null) PopupFade.In(panel, animConfig.PopupFadeInDuration);
            return true;
        }

        // Idempotent on purpose: Closed fires only when something actually closed, so a
        // caller releasing a pause against it cannot be woken twice by one panel.
        public void Close()
        {
            if (panel == null || !panel.activeSelf) return;

            panel.SetActive(false);
            Closed?.Invoke();
        }

        // Deliberately no confirmation step, unlike MetaShopView's. A prop there is
        // expensive and is a placement decision the player lives with; a charge here costs
        // a few Gems and its effect is immediately visible. A confirm dialog would be
        // friction no other small purchase in this game pays.
        private void Buy(PowerupType type)
        {
            if (manager == null) return;

            // A locked powerup cannot be bought, checked here as well as in Render for the
            // reason the bar checks twice: Render decides what the player SEES, and this
            // decides what happens if anything re-enables the button -- a stale row, a tap
            // that lands in the same frame the day rolled over. Silent, because the lock is
            // already on screen saying why.
            var day = session?.CurrentDay;
            if (day != null && !manager.IsUnlocked(type, day.DayIndex)) return;

            if (!manager.TryBuyWithGems(type))
            {
                // Reachable only if the balance moved between the last refresh and this tap
                // -- the button is non-interactable while the player cannot afford it. Said
                // in the console rather than on screen because there is nothing for the
                // player to do differently: the price and their Gem count are both already
                // visible, and the row will redraw itself from the events below.
                Debug.Log($"{nameof(PowerupShopView)}: {type} was not bought — the wallet refused.", this);
                RefreshAll();
                return;
            }

            // Immediately, and in the same method as the purchase. Wallet has already moved
            // the balance in memory; without this write the player spends Gems now and finds
            // the charge missing on the next launch. MetaShopView pairs its purchase with a
            // Save in exactly the same place and for exactly this reason -- and one Save
            // covers both facts, since the profile is written whole.
            session.Save();
        }

        private void OnChargesChanged((PowerupType Type, int Charges) _) => RefreshAll();

        private void OnGemsChanged(int _) => RefreshAll();

        private void OnDayIndexChanged(int _) => RefreshAll();

        // Redraws all three rather than only the row that moved, unlike the day scene's
        // panel. That is not an oversight: a purchase changes the shared balance, so every
        // row's affordability can change from one tap. Three rows at event frequency is far
        // below anything the cost model cares about, and the alternative -- redraw one row
        // for charges, all three for Gems -- is two rules where one is correct.
        private void RefreshAll()
        {
            var gems = session?.State?.Gems ?? 0;

            // Read once for all three rows: the day does not change between them, and asking
            // per row would invite three different answers if it ever did.
            var day = session?.CurrentDay;

            // Counts what the player can actually see, for the open button below. Zero is a
            // real and common state, not an edge case: all three powerups are introduced on
            // Days 7, 8 and 10, so the shop has nothing to sell for the first six Days.
            var listed = 0;

            foreach (var (type, ui) in rows)
            {
                var price = manager?.GemCostOf(type) ?? 0;
                var owned = manager?.ChargesOf(type) ?? 0;

                // Fails OPEN on every uncertainty (no manager, no Day resolved), the same
                // direction the bar takes: a powerup nobody can buy because a reference was
                // forgotten is a far worse outcome than one that unlocks a day early.
                var locked = manager != null && day != null && !manager.IsUnlocked(type, day.DayIndex);

                // NOT LISTED AT ALL until it has been introduced (D-160, the user's decision
                // on 2026-09-02), which replaces D-117's locked-but-listed row. That row was
                // the honest half of the fix -- it refused to sell and hid its price -- but
                // what it left on screen was a powerup with a name, an owned count of zero
                // and a dead BUY, which reads as a broken row rather than as a locked one.
                // A shop lists what is for sale; the bar is where a powerup is taught.
                //
                // The row is hidden, not the row's contents: the layout group then closes the
                // gap, where blanking three labels would leave an empty band behind.
                // Hidden only if it CAN be, which is what `listed` has to count: a locked row
                // with no Root stays on screen, so the open button that reaches it must stay
                // too -- counting "unlocked" instead would hide the only way in to three rows
                // the player can see.
                var hidden = locked && ui.Root != null;
                if (ui.Root != null) ui.Root.SetActive(!locked);

                // Nothing below draws anything a hidden row could show, and skipping it is
                // also what leaves the lock branch reachable only by a row with no Root --
                // which is exactly what that branch is still there for.
                if (hidden) continue;

                listed++;

                if (ui.LockOverlay != null) ui.LockOverlay.SetActive(locked);
                if (ui.LockLabel != null && manager != null) ui.LockLabel.text = manager.LockLabelFor(type);

                if (ui.OwnedLabel != null) ui.OwnedLabel.text = owned.ToString();

                // Plain number, no currency word — the screen's Gem icon is the unit, the
                // same convention MetaShopRowView's price label follows.
                //
                // Hidden while locked: a price on something that cannot be bought reads as an
                // invitation, and the lock's own label is what the row should be saying instead.
                if (ui.PriceLabel != null)
                {
                    ui.PriceLabel.gameObject.SetActive(!locked);
                    ui.PriceLabel.text = price.ToString();
                }

                // Non-interactable rather than dimmed-but-tappable, which is where this
                // parts company with MetaShopRowView. That row stays tappable because its
                // popup can EXPLAIN the refusal and show the player what they are saving
                // for; this shop has no popup, so a tap that silently did nothing would be
                // the worst of the three options. The reason is already on screen anyway:
                // the price is in the row and the balance is in the HUD.
                if (ui.BuyButton != null) ui.BuyButton.interactable = !locked && manager != null && gems >= price;
            }

            // NO WAY IN WHILE THERE IS NOTHING TO SELL. This is the other half of not listing
            // an unintroduced powerup: without it, the first six Days offer a store button
            // that opens a sheet with a title and nothing under it, which is a worse thing to
            // have built than the locked rows this replaced. It comes back on its own the
            // moment the first powerup unlocks -- see the Day subscription in Start.
            //
            // Only the MAIN SCREEN has this button. The day scene wires none by design (an
            // empty powerup in the bar is its opener), and that path cannot reach an empty
            // shop anyway: a locked powerup in the bar is not pressable, so whatever opened
            // the shop is itself a listed row.
            if (openButton != null) openButton.gameObject.SetActive(listed > 0);
        }

        private void CollectRows()
        {
            Add(PowerupType.AutoCollect, autoCollect, nameof(autoCollect));
            Add(PowerupType.TimeReset, timeReset, nameof(timeReset));
            Add(PowerupType.NoiseClear, noiseClear, nameof(noiseClear));

            void Add(PowerupType type, PowerupShopRow ui, string fieldName)
            {
                if (ui == null || ui.BuyButton == null)
                {
                    Debug.LogError(
                        $"{nameof(PowerupShopView)} on '{name}': the '{fieldName}' row has no Buy button, so that " +
                        "powerup cannot be bought. Drag its button in.",
                        this);
                    return;
                }

                // Warnings, not errors, and reported per row rather than bailing on the
                // first: a missing label costs information, not the sale, and one round of
                // dragging should fix everything the author missed.
                if (ui.OwnedLabel == null)
                {
                    Debug.LogWarning(
                        $"{nameof(PowerupShopView)} on '{name}': the '{fieldName}' row has no owned-count label.", this);
                }

                if (ui.PriceLabel == null)
                {
                    Debug.LogWarning(
                        $"{nameof(PowerupShopView)} on '{name}': the '{fieldName}' row has no price label, so the " +
                        "player cannot see what it costs before tapping.", this);
                }

                // A warning rather than an error, and the row is still added: unwired, this
                // powerup is listed before it is introduced -- the old D-117 behaviour, with
                // the lock in place of the hiding -- and it still cannot be bought. Losing
                // the sale over a missing reference would be the more expensive failure.
                if (ui.Root == null)
                {
                    Debug.LogWarning(
                        $"{nameof(PowerupShopView)} on '{name}': the '{fieldName}' row has no Root, so it stays in " +
                        "the list before that powerup is introduced. Drag the row object (the parent of its Buy " +
                        "button) in.", this);
                }

                rows.Add((type, ui));
            }
        }

        private bool ValidateReferences()
        {
            var missing = new List<string>();
            // openButton is NOT on this list since D-105 -- see its field note. panel and
            // closeButton stay required for both screens: a shop with no close button is a
            // trap in the day scene, where it is also holding the day still.
            if (sessionHost == null) missing.Add(nameof(sessionHost));
            if (panel == null) missing.Add(nameof(panel));
            if (closeButton == null) missing.Add(nameof(closeButton));

            if (missing.Count == 0) return true;

            Debug.LogError(
                $"{nameof(PowerupShopView)} on '{name}' is missing Inspector reference(s): {string.Join(", ", missing)}. " +
                "The shop will not open.",
                this);
            return false;
        }
    }

    // One shop row's three scene objects. A [Serializable] class rather than nine loose
    // fields so the Inspector groups each row under the powerup it sells -- nine fields in
    // a row is exactly how a BUY button ends up beside another powerup's price.
    [Serializable]
    public class PowerupShopRow
    {
        // The whole row, switched off until the Day that introduces this powerup (D-160).
        // It is the row object the three labels and the Buy button live under -- wire it to
        // the parent of this row's Buy button, which is what the layout group arranges, so a
        // hidden row closes its gap instead of leaving a hole in the list.
        //
        // A reference of its own rather than reading buyButton.transform.parent: the code
        // would then be asserting a hierarchy shape it cannot see, and one reparented button
        // would start hiding the wrong object -- or the whole sheet.
        [Tooltip("The whole row object, hidden until the Day that introduces this powerup. Wire it to the parent of this row's Buy button.")]
        [SerializeField] private GameObject root;

        [SerializeField] private Button buyButton;

        [Tooltip("Shows how many charges of this powerup the player already owns.")]
        [SerializeField] private TMP_Text ownedLabel;

        [Tooltip("Shows the Gem price of one charge, read from PowerupConfig — never typed into the scene.")]
        [SerializeField] private TMP_Text priceLabel;

        // ONLY REACHED WHEN `root` IS UNWIRED, since D-160: a row that can hide is hidden,
        // and a lock badge on a row nobody can see is nothing. It stays as the degradation
        // for a forgotten `root` reference -- a visible row that refuses to sell should say
        // why -- which is also why it is optional. Same shape as the bar's lockOverlay, and
        // the bar's is the one that still draws in the normal case.
        [Tooltip("Optional, and only used if this row's Root is left empty — a row with a Root is hidden outright until its Day. The row refuses to sell either way.")]
        [SerializeField] private GameObject lockOverlay;

        [Tooltip("Optional. The label inside the lock object, filled with the Day this powerup unlocks on.")]
        [SerializeField] private TMP_Text lockLabel;

        public GameObject Root => root;
        public Button BuyButton => buyButton;
        public TMP_Text OwnedLabel => ownedLabel;
        public TMP_Text PriceLabel => priceLabel;
        public GameObject LockOverlay => lockOverlay;
        public TMP_Text LockLabel => lockLabel;
    }
}
