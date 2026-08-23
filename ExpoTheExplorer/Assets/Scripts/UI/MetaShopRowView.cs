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
    // from MetaPurchase.ShopItems, and whether it can be afforded comes from
    // MetaPurchase.Evaluate (which Ş5 will surface here). This class turns four values
    // into four references and reports the tap.
    public class MetaShopRowView : MonoBehaviour
    {
        [SerializeField] private Image iconImage;
        [SerializeField] private TMP_Text nameLabel;
        [SerializeField] private TMP_Text priceLabel;
        [SerializeField] private Button buyButton;

        [Tooltip("Dims the whole row when the player cannot afford it. Optional — without it the row simply does not dim.")]
        [SerializeField] private CanvasGroup group;

        [Tooltip("How faded an unaffordable row looks. Over the panel's dark background, less alpha reads as darker, which is what was asked for.")]
        [SerializeField, Range(0.1f, 1f)] private float unaffordableAlpha = 0.4f;

        private Action onBuy;

        // Wired ONCE, in Awake, rather than in Bind. Bind is called once per clone today,
        // but a listener added there would stack silently the first time anything rebinds
        // an existing row -- and a BUY that fires twice is the worst bug this screen could
        // have. The stored callback is what changes; the subscription does not.
        private void Awake()
        {
            if (buyButton != null) buyButton.onClick.AddListener(HandleBuy);
        }

        private void OnDestroy()
        {
            if (buyButton != null) buyButton.onClick.RemoveListener(HandleBuy);
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

            // Dimmed, not disabled. The BUY on a row the player cannot afford still opens the
            // preview, because seeing where the prop would go and what it costs is exactly
            // what makes it worth saving for -- turning the row off would make it dead
            // furniture. The popup's own BUY is what refuses, and visibly (Ş5).
            //
            // Alpha rather than interactable, so the group never swallows that tap.
            if (group != null) group.alpha = affordable ? 1f : unaffordableAlpha;

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
