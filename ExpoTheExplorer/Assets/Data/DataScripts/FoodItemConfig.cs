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

    // A modification type + direction, used only to describe which combination a
    // SpriteVariant depicts. Data-only (no dependency on Core.Modification) so
    // this stays in the Data assembly; Core.BoardItem does the actual matching
    // since it's the one that knows a runtime item's applied modifications.
    [Serializable]
    public struct ModificationState
    {
        [SerializeField] private ModificationConfig config;
        [SerializeField] private bool isAddition;

        public ModificationConfig Config => config;
        public bool IsAddition => isAddition;
    }

    // A pre-composed sprite for one specific modification combination (e.g. the
    // burger with lettuce removed and cheese added). The board needs these so the
    // player can visually tell modified items apart; the ticket card always shows
    // the base Sprite regardless (GDD Section 3.2 — base dish photo + separate
    // modification icon list).
    [Serializable]
    public class SpriteVariant
    {
        [SerializeField] private List<ModificationState> modifications = new();
        [SerializeField] private Sprite sprite;

        public IReadOnlyList<ModificationState> Modifications => modifications;
        public Sprite Sprite => sprite;
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

        [Tooltip("Pre-composed sprites per exact modification combination, for board rendering. Falls back to Sprite when no combination matches (Core.BoardItem.ResolvedSprite does the matching).")]
        [SerializeField] private List<SpriteVariant> spriteVariants = new();

        public string Id => id;
        public string DisplayName => displayName;
        public Sprite Sprite => sprite;
        public FoodCategory Category => category;
        public IReadOnlyList<ModificationConfig> AvailableModifications => availableModifications;
        public IReadOnlyList<SpriteVariant> SpriteVariants => spriteVariants;
    }
}
