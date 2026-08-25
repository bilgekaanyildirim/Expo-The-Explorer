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
    // BUYING LIVES ON THE MENU, NOT IN THE DAY, and that is a design rule rather than a
    // layout convenience (the user's decision, 2026-08-25): opening a store mid-service
    // suspends the very time pressure a powerup exists to relieve. The day scene's
    // PowerupBarView has no price, no Gem icon and no purchase path for the same reason.
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
        // The scene's session provider (MainScreenRoot). Serialized and dragged, never
        // searched for. SessionHost rather than MainScreenRoot so this class does not care
        // which screen it is on -- the same seam MetaShopView and HudWalletSource take.
        [SerializeField] private SessionHost sessionHost;

        [Tooltip("The shop panel itself. Switched off at Start whatever the scene was saved as.")]
        [SerializeField] private GameObject panel;

        [Tooltip("Opens the shop. This view owns it, which is what keeps MainScreenView unaware powerups exist.")]
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
                    $"{nameof(PowerupShopView)} on '{name}': this session has no powerup stock " +
                    "(no PowerupConfig wired on MainScreenRoot), so the shop stays inert.",
                    this);
            }

            openButton.onClick.AddListener(OnOpenClicked);
            closeButton.onClick.AddListener(OnCloseClicked);

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

            RefreshAll();
        }

        private void OnDestroy()
        {
            if (manager != null) manager.ChargesChanged.Unsubscribe(OnChargesChanged);
            if (session?.State != null) session.State.GemsChanged.Unsubscribe(OnGemsChanged);

            if (openButton != null) openButton.onClick.RemoveListener(OnOpenClicked);
            if (closeButton != null) closeButton.onClick.RemoveListener(OnCloseClicked);

            // Removed rather than left, because this view is legal to disable and
            // re-enable: a second Start would otherwise stack a second listener and a
            // single tap would buy twice.
            foreach (var (_, ui) in rows)
            {
                if (ui?.BuyButton != null) ui.BuyButton.onClick.RemoveAllListeners();
            }
            rows.Clear();
        }

        // Both handlers refresh rather than only toggling, so a shop opened after a day
        // was completed shows the charges that day granted. Nothing else on this screen
        // would have redrawn them: the panel was inactive while the events fired.
        private void OnOpenClicked()
        {
            RefreshAll();
            if (panel != null) panel.SetActive(true);
        }

        private void OnCloseClicked()
        {
            if (panel != null) panel.SetActive(false);
        }

        // Deliberately no confirmation step, unlike MetaShopView's. A prop there is
        // expensive and is a placement decision the player lives with; a charge here costs
        // a few Gems and its effect is immediately visible. A confirm dialog would be
        // friction no other small purchase in this game pays.
        private void Buy(PowerupType type)
        {
            if (manager == null) return;

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

        // Redraws all three rather than only the row that moved, unlike the day scene's
        // panel. That is not an oversight: a purchase changes the shared balance, so every
        // row's affordability can change from one tap. Three rows at event frequency is far
        // below anything the cost model cares about, and the alternative -- redraw one row
        // for charges, all three for Gems -- is two rules where one is correct.
        private void RefreshAll()
        {
            var gems = session?.State?.Gems ?? 0;

            foreach (var (type, ui) in rows)
            {
                var price = manager?.GemCostOf(type) ?? 0;
                var owned = manager?.ChargesOf(type) ?? 0;

                if (ui.OwnedLabel != null) ui.OwnedLabel.text = owned.ToString();

                // Plain number, no currency word — the screen's Gem icon is the unit, the
                // same convention MetaShopRowView's price label follows.
                if (ui.PriceLabel != null) ui.PriceLabel.text = price.ToString();

                // Non-interactable rather than dimmed-but-tappable, which is where this
                // parts company with MetaShopRowView. That row stays tappable because its
                // popup can EXPLAIN the refusal and show the player what they are saving
                // for; this shop has no popup, so a tap that silently did nothing would be
                // the worst of the three options. The reason is already on screen anyway:
                // the price is in the row and the balance is in the HUD.
                if (ui.BuyButton != null) ui.BuyButton.interactable = manager != null && gems >= price;
            }
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

                rows.Add((type, ui));
            }
        }

        private bool ValidateReferences()
        {
            var missing = new List<string>();
            if (sessionHost == null) missing.Add(nameof(sessionHost));
            if (panel == null) missing.Add(nameof(panel));
            if (openButton == null) missing.Add(nameof(openButton));
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
        [SerializeField] private Button buyButton;

        [Tooltip("Shows how many charges of this powerup the player already owns.")]
        [SerializeField] private TMP_Text ownedLabel;

        [Tooltip("Shows the Gem price of one charge, read from PowerupConfig — never typed into the scene.")]
        [SerializeField] private TMP_Text priceLabel;

        public Button BuyButton => buyButton;
        public TMP_Text OwnedLabel => ownedLabel;
        public TMP_Text PriceLabel => priceLabel;
    }
}
