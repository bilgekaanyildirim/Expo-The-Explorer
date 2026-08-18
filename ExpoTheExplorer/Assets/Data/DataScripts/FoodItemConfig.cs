using System;
using System.Collections.Generic;
using UnityEngine;

namespace ExpoTheExplorer.Data
{
    public enum FoodCategory
    {
        Main,
        Side,
        Drink
    }

    // Whether a layer is part of the always-present base art, part of the
    // default recipe (hidden when its tied modification's removal is present),
    // or an add-on (hidden until its tied modification's addition is present).
    public enum LayerVisibility
    {
        AlwaysVisible,
        VisibleByDefault,
        HiddenByDefault
    }

    // One composited board-visual layer (e.g. "lettuce", "extra cheese"). Data-only
    // (no dependency on Core.Modification) so this stays in the Data assembly;
    // Core.BoardItem does the actual visibility/offset resolution since it's the
    // one that knows a runtime item's applied modifications.
    [Serializable]
    public class SpriteLayer
    {
        [SerializeField] private Sprite sprite;
        [SerializeField] private LayerVisibility visibility;
        [Tooltip("Unused when Visibility is AlwaysVisible.")]
        [SerializeField] private ModificationConfig modification;
        [Tooltip("Which IsAddition value (of Modification) makes this layer visible/hidden. Unused when Visibility is AlwaysVisible.")]
        [SerializeField] private bool direction;
        [Tooltip("This layer's own position offset, tuned visually per food item.")]
        [SerializeField] private Vector2 offset;
        [Tooltip("Added to every layer AFTER this one in the list, only while this layer is visible — lets one ingredient (e.g. an extra patty) push the ones stacked above it, and lets removing a default ingredient close the gap it leaves.")]
        [SerializeField] private Vector2 pushAmount;
        [Tooltip("Relative size multiplier on top of the default fit-to-cell scale — every layer independently fits the cell by default, so differently-cropped art (e.g. a thin cheese slice vs. a full bun) needs this to look correctly sized relative to the others. 1 = default fit-to-cell size.")]
        [SerializeField] private float scale = 1f;

        public Sprite Sprite => sprite;
        public LayerVisibility Visibility => visibility;
        public ModificationConfig Modification => modification;
        public bool Direction => direction;
        public Vector2 Offset => offset;
        public Vector2 PushAmount => pushAmount;
        public float Scale => scale;
    }

    [CreateAssetMenu(fileName = "FoodItemConfig", menuName = "ExpoTheExplorer/Data/Food Item Config")]
    public class FoodItemConfig : ScriptableObject
    {
        [SerializeField] private string id;
        [SerializeField] private string displayName;
        [SerializeField] private Sprite sprite;
        [SerializeField] private FoodCategory category;

        [Tooltip("What this item is worth on a delivered ticket. A ticket's Order Value is the sum of its required items' base prices and is paid in full on every successful delivery; the speed tier only scales the tip on top of it (ExpoTheExplorer/CLAUDE.md — Food Pricing / Order Value). Authored per item on purpose: a burger, fries and a cola are not worth the same. Left at 0 this item is a free dish, which is a content bug rather than a valid default.")]
        [SerializeField, Min(0)] private int basePrice;

        [Tooltip("Modifications that can appear on a ticket for this item. Only meaningful for Main category items — leave empty for Side/Drink (GDD Section 3.2).")]
        [SerializeField] private List<ModificationConfig> availableModifications = new();

        [Tooltip("Board-rendering layers, authored back-to-front. Empty for items with no modifications (e.g. Side/Drink) — Core.BoardItem.ResolvedLayers falls back to Sprite.")]
        [SerializeField] private List<SpriteLayer> spriteLayers = new();

        [Tooltip("How large this item appears relative to its board cell overall — a multiplier applied on top of every layer's own Scale. 1 = fills the cell (default fit-to-cell behavior). Use this to make e.g. a drink look smaller than a burger, independent of each layer's relative sizing.")]
        [SerializeField, Range(0.1f, 1.5f)] private float overallScale = 1f;

        public string Id => id;
        public string DisplayName => displayName;
        public Sprite Sprite => sprite;
        public FoodCategory Category => category;
        public int BasePrice => basePrice;
        public IReadOnlyList<ModificationConfig> AvailableModifications => availableModifications;
        public IReadOnlyList<SpriteLayer> SpriteLayers => spriteLayers;
        public float OverallScale => overallScale;
    }
}
