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

        // Which sprites the board should draw for THIS specific instance, back to
        // front, each with its final (already-pushed) position offset. Lives here
        // rather than on FoodItemConfig because resolving visibility needs
        // Modification (Core), and Data can't depend on Core (Core already
        // depends on Data).
        public IReadOnlyList<ResolvedLayer> ResolvedLayers
        {
            get
            {
                var result = new List<ResolvedLayer>();
                var cumulativePush = Vector2.zero;

                foreach (var layer in Config.SpriteLayers)
                {
                    if (!IsVisible(layer)) continue;

                    result.Add(new ResolvedLayer(layer.Sprite, layer.Offset + cumulativePush));
                    cumulativePush += layer.PushAmount;
                }

                if (result.Count == 0 && Config.Sprite != null)
                {
                    result.Add(new ResolvedLayer(Config.Sprite, Vector2.zero));
                }

                return result;
            }
        }

        private bool IsVisible(SpriteLayer layer)
        {
            if (layer.Visibility == LayerVisibility.AlwaysVisible) return true;

            if (layer.Visibility == LayerVisibility.VisibleByDefault)
            {
                // Hidden by the tied modification in EITHER direction, not just a
                // specific one — e.g. the base cheese layer hides whether Cheese
                // was removed (gone entirely) or added (Extra Cheese takes its
                // place instead of stacking on top of it).
                foreach (var mod in Modifications)
                {
                    if (mod.Config == layer.Modification) return false;
                }
                return true;
            }

            // HiddenByDefault: visible only for the exact (modification, direction)
            // pair this layer represents.
            foreach (var mod in Modifications)
            {
                if (mod.Config == layer.Modification && mod.IsAddition == layer.Direction) return true;
            }
            return false;
        }
    }

    // A single resolved board sprite + its final screen-space offset (in cell-size
    // fractions — BoardView scales this by its own dynamic cell size).
    public readonly struct ResolvedLayer
    {
        public Sprite Sprite { get; }
        public Vector2 Offset { get; }

        public ResolvedLayer(Sprite sprite, Vector2 offset)
        {
            Sprite = sprite;
            Offset = offset;
        }
    }
}
