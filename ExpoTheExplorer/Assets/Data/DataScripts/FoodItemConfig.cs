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

        public Sprite Sprite => sprite;
        public LayerVisibility Visibility => visibility;
        public ModificationConfig Modification => modification;
        public bool Direction => direction;
        public Vector2 Offset => offset;
        public Vector2 PushAmount => pushAmount;
    }

    [CreateAssetMenu(fileName = "FoodItemConfig", menuName = "ExpoTheExplorer/Data/Food Item Config")]
    public class FoodItemConfig : ScriptableObject
    {
        [SerializeField] private string id;
        [SerializeField] private string displayName;
        [SerializeField] private Sprite sprite;
        [SerializeField] private FoodCategory category;

        [Tooltip("Modifications that can appear on a ticket for this item. Only meaningful for Main category items — leave empty for Side/Drink (GDD Section 3.2).")]
        [SerializeField] private List<ModificationConfig> availableModifications = new();

        [Tooltip("Board-rendering layers, authored back-to-front. Empty for items with no modifications (e.g. Side/Drink) — Core.BoardItem.ResolvedLayers falls back to Sprite.")]
        [SerializeField] private List<SpriteLayer> spriteLayers = new();

        public string Id => id;
        public string DisplayName => displayName;
        public Sprite Sprite => sprite;
        public FoodCategory Category => category;
        public IReadOnlyList<ModificationConfig> AvailableModifications => availableModifications;
        public IReadOnlyList<SpriteLayer> SpriteLayers => spriteLayers;
    }
}
