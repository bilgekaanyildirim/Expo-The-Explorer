using System.Collections.Generic;
using UnityEngine;
using ExpoTheExplorer.Data;

namespace ExpoTheExplorer.Core
{
    // A concrete food item sitting on the board — a FoodItemConfig with its
    // modifications already resolved (e.g. "Burger, no lettuce, extra cheese"),
    // since the player must be able to pick the exact matching variant off the
    // board (GDD Section 3.2 / 5). Built by whatever spawns it (future Board
    // Distribution module); this type is just the passive data holder.
    public class BoardItem
    {
        public FoodItemConfig Config { get; }
        public IReadOnlyList<Modification> Modifications { get; }

        public BoardItem(FoodItemConfig config, IReadOnlyList<Modification> modifications)
        {
            Config = config;
            Modifications = modifications;
        }

        // The sprite the board should show for THIS specific instance — matches
        // Config.SpriteVariants against the applied modifications (config +
        // direction, exact set), falling back to Config.Sprite (may be null,
        // callers fall back further to a placeholder). Lives here rather than on
        // FoodItemConfig because matching needs Modification (Core), and Data
        // can't depend on Core (Core already depends on Data).
        public Sprite ResolvedSprite
        {
            get
            {
                foreach (var variant in Config.SpriteVariants)
                {
                    if (MatchesVariant(variant)) return variant.Sprite;
                }

                return Config.Sprite;
            }
        }

        private bool MatchesVariant(SpriteVariant variant)
        {
            if (variant.Modifications.Count != Modifications.Count) return false;

            foreach (var state in variant.Modifications)
            {
                var found = false;
                foreach (var mod in Modifications)
                {
                    if (mod.Config == state.Config && mod.IsAddition == state.IsAddition)
                    {
                        found = true;
                        break;
                    }
                }

                if (!found) return false;
            }

            return true;
        }
    }
}
