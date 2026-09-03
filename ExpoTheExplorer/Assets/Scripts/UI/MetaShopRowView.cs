using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace ExpoTheExplorer.UI
{
    // One row in the meta shop: the prop's icon, its name, its price, and a BUY button.
    // Cloned per offer by MetaShopView from an authored template kept inactive in the
    // scene -- the same shape ModificationSlotView has under TicketCardView, and for the
    // same reason: how many rows there are is something only the catalog and the player's
    // purchases know, so they cannot be laid out in the Editor ahead of time.
    //
    // A pure BIND component. It decides nothing: whether an item is listed at all comes
    // from MetaPurchase.ShopItems, and whether it can be afforded is worked out by
    // MetaShopView and handed in as a bool. This class turns those values into references
    // and reports the tap.
    public class MetaShopRowView : MonoBehaviour
    {
        [SerializeField] private Image iconImage;
        [SerializeField] private TMP_Text nameLabel;
        [SerializeField] private TMP_Text priceLabel;
        [SerializeField] private Button buyButton;

        [Tooltip("How faded the BUY button looks on a row the player cannot afford. The rest of the row -- icon, name, price -- stays at full strength.")]
        [SerializeField, Range(0.1f, 1f)] private float unaffordableAlpha = 0.4f;

        private Action onBuy;
        private CanvasGroup buyGroup;

        // Read by MetaShopView so the confirm popup's BUY fades by the SAME amount a row's
        // does. It is exposed rather than copied for the reason the two colours are handed
        // in rather than chosen here (D-037): one authored number for "how faded an
        // unaffordable BUY looks in this shop" cannot drift, and two can. The template is
        // the natural place to keep it, because it is the object an author actually looks
        // at while tuning the look -- and it is already a serialized reference on
        // MetaShopView, so nothing new has to be wired and no new serialized field can
        // arrive from the scene as a 0 that would make the button vanish.
        public float UnaffordableAlpha => unaffordableAlpha;

        // Wired ONCE, in Awake, rather than in Bind. Bind is called once per clone today,
        // but a listener added there would stack silently the first time anything rebinds
        // an existing row -- and a BUY that fires twice is the worst bug this screen could
        // have. The stored callback is what changes; the subscription does not.
        private void Awake()
        {
            if (buyButton == null) return;
            buyButton.onClick.AddListener(HandleBuy);
            EnsureBuyGroup();
        }

        private void OnDestroy()
        {
            if (buyButton != null) buyButton.onClick.RemoveListener(HandleBuy);
        }

        // BUILT, not authored. The CanvasGroup that fades the BUY could have been a
        // serialized slot, but this screen has already made the opposite call twice
        // (MetaShopView.CreateConfirmCatcher, MetaGroundsView.CreateSkipCatcher) for the
        // reason that applies here too: the row template lives in the SCENE, so a slot the
        // next re-author forgets to fill would drop the fade silently and nothing would
        // report it. GetComponent runs first, so a template that does carry one is reused
        // rather than doubled.
        //
        // A CanvasGroup's defaults are what make this safe: interactable and blocksRaycasts
        // both start true, so an unaffordable BUY keeps its tap -- and it must, because that
        // tap is what opens the preview. Only alpha is ever written.
        //
        // Idempotent and called from Bind as well as Awake: rows are cloned from an inactive
        // template and switched on before Bind today, but a caller that ever binds first
        // would otherwise lose the fade with no symptom.
        private void EnsureBuyGroup()
        {
            if (buyGroup != null) return;
            // Written as an explicit == null rather than ??, because ?? bypasses the
            // operator UnityEngine.Object overloads and is the standard way to get a
            // "not null but not alive" reference in Unity code.
            var existing = buyButton.GetComponent<CanvasGroup>();
            buyGroup = existing != null ? existing : buyButton.gameObject.AddComponent<CanvasGroup>();
        }

        public void Bind(
            Sprite icon, string displayName, int price, bool affordable, Color buyColor, Action onBuyTapped)
        {
            onBuy = onBuyTapped;

            // The colour is handed IN rather than chosen here, and both colours live on
            // MetaShopView. This class stays a binder that decides nothing -- and two
            // components each holding the same pair of colours is a pair that can drift.
            //
            // targetGraphic is the button's OWN declared graphic, already serialized by the
            // setup step, so there is nothing to wire and nothing to guess. Writing to its
            // `color` survives the Button's ColorTint transition: Selectable applies the
            // state tint to the CanvasRenderer and MULTIPLIES it with Graphic.color rather
            // than overwriting it, which is also why the authored green shows through today.
            if (buyButton != null && buyButton.targetGraphic != null)
            {
                buyButton.targetGraphic.color = buyColor;
            }

            // THE BUY FADES, THE ROW DOES NOT. Ş5 dimmed the whole row through a CanvasGroup
            // on its root, and the whole row going pale read as the prop itself being
            // withdrawn -- but its icon, its name and its price are exactly what makes a prop
            // worth saving for, so they now stay at full strength. Only the thing that would
            // actually refuse carries the fade, on top of the red it already gets: the fade
            // and the colour then say the same thing about the same button instead of the
            // fade talking about the row and the colour about the button.
            //
            // Dimmed, not disabled, and that much has not changed: the BUY on a row the
            // player cannot afford still opens the preview, because seeing where the prop
            // would go and what it costs is what makes it worth saving for -- turning it off
            // would make the row dead furniture. Alpha rather than interactable, so the group
            // never swallows that tap; the popup's own BUY is what refuses, and visibly.
            if (buyButton != null)
            {
                EnsureBuyGroup();
                buyGroup.alpha = affordable ? 1f : unaffordableAlpha;
            }

            if (iconImage != null)
            {
                iconImage.sprite = icon;
                // Hidden rather than left as a white box when the catalog has no sprite.
                // MetaCatalogValidator already reports a missing sprite as a content error,
                // so this row does not add a second verdict about it -- it just does not
                // draw a placeholder that could be mistaken for the art.
                iconImage.enabled = icon != null;
            }

            if (nameLabel != null) nameLabel.text = displayName;

            // Plain number, no currency word. The HUD's coin icon is the unit on this
            // screen, and a shop row that spelled out "coins" would be the only place in
            // the game that does.
            if (priceLabel != null) priceLabel.text = price.ToString();
        }

        private void HandleBuy() => onBuy?.Invoke();
    }
}
