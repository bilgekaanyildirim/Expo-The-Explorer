using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace ExpoTheExplorer.Data
{
    // Same split DayValidationResult makes and for the same reason: warnings cover a
    // catalog that WORKS but probably is not what the author meant, so they must never
    // gate anything -- turning "this looks odd" into "you may not save" is not a
    // validator's call to make about a design decision.
    public class MetaCatalogValidationResult
    {
        public bool IsValid => Errors.Count == 0;

        public IReadOnlyList<string> Errors { get; }
        public IReadOnlyList<string> Warnings { get; }

        public MetaCatalogValidationResult(IReadOnlyList<string> errors, IReadOnlyList<string> warnings = null)
        {
            Errors = errors;
            Warnings = warnings ?? Array.Empty<string>();
        }
    }

    // Authoring-time gate for the meta catalog. It lives in Data rather than in
    // MetaSystem deliberately: it needs nothing outside this assembly (Data has no
    // references at all), which lets both the runtime resolver and the Editor placement
    // tool reach it without either of them growing a dependency on the other.
    //
    // It takes the location LIST rather than the MetaCatalog asset so it can be tested
    // without a ScriptableObject -- the same reason BoardDistributionSettings is a plain
    // value object (decisions.md D-004). The asset's own overload is a one-liner below.
    //
    // What it does NOT check: prices being balanced, positions looking right, or whether
    // the Day a prop is pinned to is the RIGHT one. The first two are judgement; the
    // third is unanswerable here, because Data cannot see the Day catalog and, since
    // D-017, nothing derives an unlock from Day content any more -- the day index is
    // simply authored, so keeping it in step with what a Day serves is the author's job.
    public static class MetaCatalogValidator
    {
        public static MetaCatalogValidationResult Validate(MetaCatalog catalog)
        {
            if (catalog == null)
            {
                return new MetaCatalogValidationResult(new[] { "Catalog is null." });
            }

            return Validate(catalog.Locations);
        }

        public static MetaCatalogValidationResult Validate(IReadOnlyList<MetaLocation> locations)
        {
            var errors = new List<string>();
            var warnings = new List<string>();

            if (locations == null || locations.Count == 0)
            {
                errors.Add("Catalog has no locations: the meta screen would have nothing to show.");
                return new MetaCatalogValidationResult(errors, warnings);
            }

            ValidateLocationIdentities(locations, errors, warnings);

            // Built once and shared, so a requiresAreaId that names a real item in the
            // WRONG location can be reported as exactly that rather than as "not found",
            // which is the mistake a multi-location catalog invites.
            var itemOwners = MapItemIdsToLocations(locations);

            foreach (var location in locations)
            {
                if (location == null) continue;

                var label = string.IsNullOrWhiteSpace(location.Id) ? "<unnamed location>" : location.Id;
                ValidateLocationContents(location, label, itemOwners, errors, warnings);
            }

            return new MetaCatalogValidationResult(errors, warnings);
        }

        private static void ValidateLocationIdentities(
            IReadOnlyList<MetaLocation> locations, List<string> errors, List<string> warnings)
        {
            var seenIds = new HashSet<string>();
            var previousUnlockDay = int.MinValue;

            for (var i = 0; i < locations.Count; i++)
            {
                var location = locations[i];
                if (location == null)
                {
                    errors.Add($"Location at index {i} is null.");
                    continue;
                }

                if (string.IsNullOrWhiteSpace(location.Id))
                {
                    errors.Add($"Location at index {i} has no id. The id prefixes every ownership key this location writes, so it cannot be blank.");
                }
                else if (!seenIds.Add(location.Id))
                {
                    errors.Add($"Duplicate location id '{location.Id}'. Ownership keys would collide between the two.");
                }

                if (location.BackgroundSprite == null)
                {
                    errors.Add($"Location '{location.Id}' has no background sprite. Every prop's position is a fraction of that sprite's rect, so without it nothing can be placed.");
                }

                // Not an error: a catalog mid-authoring is allowed to be out of order,
                // and the screen sorts by this value anyway. It is worth saying, because
                // a later location that unlocks EARLIER than an earlier one is almost
                // always a typo.
                if (location.UnlockAtDayIndex < previousUnlockDay)
                {
                    warnings.Add($"Location '{location.Id}' unlocks at Day {location.UnlockAtDayIndex}, before the location listed above it. Order locations by Unlock At Day Index unless this is deliberate.");
                }
                previousUnlockDay = location.UnlockAtDayIndex;

                if (location.Items == null || location.Items.Count == 0)
                {
                    warnings.Add($"Location '{location.Id}' has no items: it would render as a bare background.");
                }
            }

            // The player has to land somewhere on a brand-new save. Nothing unlocked at
            // Day 0 means the meta screen is empty until some later Day, which is a
            // content bug rather than a design anyone chose.
            if (locations.Count > 0 && !locations.Any(l => l != null && l.UnlockAtDayIndex == 0))
            {
                errors.Add("No location unlocks at Day 0: a new player would open the meta screen with nowhere to go.");
            }
        }

        private static void ValidateLocationContents(
            MetaLocation location,
            string label,
            IReadOnlyDictionary<string, string> itemOwners,
            List<string> errors,
            List<string> warnings)
        {
            var items = location.Items;
            if (items == null) return;

            var seenItemIds = new HashSet<string>();
            var areaIds = new HashSet<string>(
                items.Where(i => i != null && i.UnlocksArea && !string.IsNullOrWhiteSpace(i.Id)).Select(i => i.Id));
            var requestedAreaIds = new HashSet<string>();

            for (var i = 0; i < items.Count; i++)
            {
                var item = items[i];
                if (item == null)
                {
                    errors.Add($"[{label}] Item at index {i} is null.");
                    continue;
                }

                if (string.IsNullOrWhiteSpace(item.Id))
                {
                    errors.Add($"[{label}] Item at index {i} has no id.");
                }
                else if (!seenItemIds.Add(item.Id))
                {
                    errors.Add($"[{label}] Duplicate item id '{item.Id}'. Both would map to the same ownership key '{MetaCatalog.OwnershipKey(location.Id, item.Id)}'.");
                }

                if (item.Sprite == null)
                {
                    errors.Add($"[{label}] Item '{item.Id}' has no sprite, so it could never appear once the player unlocks it.");
                }

                ValidateUnlock(item, label, errors, warnings);
                ValidateAreaRequirement(item, location, label, areaIds, itemOwners, errors);

                if (!string.IsNullOrWhiteSpace(item.RequiresAreaId))
                {
                    requestedAreaIds.Add(item.RequiresAreaId);
                }

                var p = item.NormalizedPosition;
                if (p.x < -0.5f || p.x > 1.5f || p.y < -0.5f || p.y > 1.5f)
                {
                    warnings.Add($"[{label}] Item '{item.Id}' sits at {p}, far outside the background (0..1). Slightly outside is legitimate for art that bleeds off an edge; this far out is usually a position never authored.");
                }
            }

            // An area nobody stands on is money the player spends for one sprite. Worth
            // flagging while it is still cheap to notice, but legitimate if the expansion
            // is meant to be scenery in its own right.
            foreach (var areaId in areaIds.Where(a => !requestedAreaIds.Contains(a)))
            {
                warnings.Add($"[{label}] Item '{areaId}' unlocks an area, but no item in this location requires it. Nothing would open when it is bought.");
            }
        }

        private static void ValidateUnlock(
            MetaItemDefinition item, string label, List<string> errors, List<string> warnings)
        {
            switch (item.Unlock)
            {
                case MetaUnlockKind.Purchase:
                    // Same stance FoodItemConfig.basePrice takes: a free item is a content
                    // bug, not a default worth honouring silently.
                    if (item.Price <= 0)
                    {
                        errors.Add($"[{label}] Item '{item.Id}' is for sale at {item.Price}. A purchasable prop needs a price above zero; leaving it at 0 gives it away.");
                    }
                    break;

                case MetaUnlockKind.DayUnlock:
                    // Not an error -- the field is simply unread in this mode -- but a
                    // priced Day-unlock prop reads as "the author expected to sell this".
                    if (item.Price != 0)
                    {
                        warnings.Add($"[{label}] Item '{item.Id}' has a price of {item.Price} but is not for sale (Day Unlock). The price is ignored.");
                    }

                    // Day 0 is legitimate: it means "there from the very first day", which
                    // is how a prop that is part of the starting scene is authored. There
                    // is deliberately nothing else to check here -- whether a Day really
                    // calls for this prop is a judgement no code in Data can make, since
                    // this assembly cannot see the Day catalog at all (D-017).
                    break;
            }
        }

        private static void ValidateAreaRequirement(
            MetaItemDefinition item,
            MetaLocation location,
            string label,
            HashSet<string> areaIdsInThisLocation,
            IReadOnlyDictionary<string, string> itemOwners,
            List<string> errors)
        {
            var required = item.RequiresAreaId;
            if (string.IsNullOrWhiteSpace(required)) return;

            if (required == item.Id)
            {
                errors.Add($"[{label}] Item '{item.Id}' requires its own area, so it could never be bought.");
                return;
            }

            if (areaIdsInThisLocation.Contains(required)) return;

            // The multi-location trap: the id resolves, just not here. Saying so beats
            // "not found", which sends the author looking for a typo that is not there.
            if (itemOwners.TryGetValue(required, out var owner) && owner != location.Id)
            {
                errors.Add($"[{label}] Item '{item.Id}' requires area '{required}', which belongs to location '{owner}'. A location must be self-contained: an area gate cannot reach across locations.");
                return;
            }

            errors.Add($"[{label}] Item '{item.Id}' requires area '{required}', but no item in this location has that id with Unlocks Area ticked.");
        }

        // id -> the FIRST location that declares it. Only used to improve the message for
        // a cross-location area reference, so "first" is enough: the duplicate-id check
        // above already owns the case where two locations share an id.
        private static IReadOnlyDictionary<string, string> MapItemIdsToLocations(IReadOnlyList<MetaLocation> locations)
        {
            var owners = new Dictionary<string, string>();

            foreach (var location in locations)
            {
                if (location?.Items == null) continue;

                foreach (var item in location.Items)
                {
                    if (item == null || string.IsNullOrWhiteSpace(item.Id)) continue;
                    if (!owners.ContainsKey(item.Id)) owners[item.Id] = location.Id;
                }
            }

            return owners;
        }
    }
}
