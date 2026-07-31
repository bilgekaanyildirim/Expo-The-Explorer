using System;
using System.Collections.Generic;
using System.Linq;
using ExpoTheExplorer.Data;

namespace ExpoTheExplorer.Core
{
    // Identifies a spawnable/matchable board item by its food + modification
    // combination, independent of modification order or which Ticket/BoardItem
    // instance built the list — two Modification lists describing the same
    // (config, direction) set are the same key even if built separately (e.g.
    // by two different tickets). Used by BoardDistributor to count "how many of
    // exactly this combo are already on the board" and by TraySlot to compare
    // a tray's contents against a ticket's required items.
    public readonly struct RequiredItemKey : IEquatable<RequiredItemKey>
    {
        private readonly HashSet<(ModificationConfig Config, bool IsAddition)> modificationSignature;

        public FoodItemConfig Food { get; }
        public IReadOnlyList<Modification> Modifications { get; }

        public RequiredItemKey(FoodItemConfig food, IReadOnlyList<Modification> modifications)
        {
            Food = food;
            Modifications = modifications;
            modificationSignature = new HashSet<(ModificationConfig, bool)>(
                modifications.Select(m => (m.Config, m.IsAddition)));
        }

        public bool Equals(RequiredItemKey other)
        {
            return Food == other.Food && modificationSignature.SetEquals(other.modificationSignature);
        }

        public override bool Equals(object obj) => obj is RequiredItemKey other && Equals(other);

        public override int GetHashCode()
        {
            var hash = Food != null ? Food.GetHashCode() : 0;
            foreach (var entry in modificationSignature)
            {
                // XOR combination is order-independent, matching SetEquals semantics.
                hash ^= entry.GetHashCode();
            }

            return hash;
        }
    }
}
