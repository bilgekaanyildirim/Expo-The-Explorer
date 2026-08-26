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

                    // OverallScale multiplies BOTH size and position — the whole
                    // stack must scale like one rigid object (zoomed in/out around
                    // the cell center), not just have each layer inflate in place
                    // while every layer's relative offset stays fixed.
                    var offset = (layer.Offset + cumulativePush) * Config.OverallScale;

                    // Carried through only for a layer that exists BECAUSE of a
                    // modification (HiddenByDefault -- see IsVisible below); an
                    // always-visible or default-visible layer leaves it null. That makes
                    // "which of these sprites IS the extra mustard" answerable by a reader
                    // holding nothing but the resolved list. The tutorial's arrow is the
                    // first such reader -- before it, this fact was computed here and
                    // thrown away.
                    var sourceModification = layer.Visibility == LayerVisibility.HiddenByDefault ? layer.Modification : null;
                    result.Add(new ResolvedLayer(layer.Sprite, offset, layer.Scale * Config.OverallScale, sourceModification));
                    cumulativePush += layer.PushAmount;
                }

                if (result.Count == 0 && Config.Sprite != null)
                {
                    result.Add(new ResolvedLayer(Config.Sprite, Vector2.zero, Config.OverallScale));
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
    // fractions — BoardView scales this by its own dynamic cell size) and its
    // relative size multiplier on top of the default fit-to-cell scale.
    public readonly struct ResolvedLayer
    {
        public Sprite Sprite { get; }
        public Vector2 Offset { get; }
        public float Scale { get; }

        // Non-null only when this layer is on the item BECAUSE of a modification -- the
        // extra mustard, the added cheese. Null for the bun, the sausage, and for any
        // layer that is merely still visible because a modification did NOT remove it.
        // BoardView draws every layer the same way and ignores this; it exists so a reader
        // can point at one specific ingredient, which is what the tutorial's arrow does.
        public ModificationConfig SourceModification { get; }

        // Optional so the fallback construction below (a config with no layers at all,
        // drawing its plain Sprite) does not have to say "no modification" out loud.
        public ResolvedLayer(Sprite sprite, Vector2 offset, float scale, ModificationConfig sourceModification = null)
        {
            Sprite = sprite;
            Offset = offset;
            Scale = scale;
            SourceModification = sourceModification;
        }
    }
}
