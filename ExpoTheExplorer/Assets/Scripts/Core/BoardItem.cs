using System.Collections.Generic;
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
    }
}
