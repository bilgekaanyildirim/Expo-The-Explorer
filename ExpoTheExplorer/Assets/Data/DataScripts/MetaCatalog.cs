using System;
using System.Collections.Generic;
using UnityEngine;

namespace ExpoTheExplorer.Data
{
    // What decides whether a meta prop is on screen. Deliberately an enum discriminator
    // with one field per branch rather than [SerializeReference] polymorphism: the same
    // shape BoardDistributionSettings already uses for GuaranteedTicketCountMode
    // (decisions.md D-004), where only one of two fields is meaningful per mode.
    // [SerializeReference] would buy a cleaner type at the cost of a serialized
    // type-name that breaks on a class rename, and there are exactly two branches.
    //
    // Two branches is the whole of it (D-017 narrowed D-015 to this): either the player
    // buys the prop, or it shows up at an authored Day. Cosmetic decor and area
    // expansions are both Purchase -- they differ in `unlocksArea`, not in kind.
    public enum MetaUnlockKind
    {
        // Costs SoftMoney. Cosmetic decor and area expansions alike.
        Purchase,

        // Not for sale. Appears once the player reaches an authored Day, and stays.
        DayUnlock
    }

    // One prop in one location: what it looks like, what opens it, and where it sits on
    // that location's background.
    //
    // A prop has exactly ONE sprite and is simply absent until it is active
    // (decisions.md D-016, which narrows D-015). The whole view layer is
    // `image.enabled = active` -- no per-type branch, no second sprite to reason about.
    //
    // D-015 shipped a second `unownedSprite` so a prop could be visible BEFORE purchase
    // and vanish when bought, which was how the ruined starting building hid the finished
    // stand baked into Main.png. That is now expressed in the ART instead: the background
    // carries the ruin, and the stand is an ordinary bought prop drawn over it. Same
    // result on screen, one less mechanism -- but note the system can no longer express
    // "buying this removes it" at all, so a future prop that needs it needs a new
    // mechanism rather than a field.
    [Serializable]
    public class MetaItemDefinition
    {
        [Tooltip("Unique WITHIN this location, not globally. The persistence key the player's save file carries is \"<location id>.<this id>\", composed at runtime -- so two locations may both have a \"Fountain\" without colliding, and this field stays short and readable.")]
        [SerializeField] private string id;

        [Tooltip("Shown in the shop. Player-facing text, so it lives in data rather than in code.")]
        [SerializeField] private string displayName;

        [Tooltip("What this prop looks like. Drawn only once the prop is active — before that it is simply not there. Required: a prop with no sprite can never appear.")]
        [SerializeField] private Sprite sprite;

        [Tooltip("Optional. What the SHOP ROW shows for this prop. Leave it empty and the row uses the prop's own sprite, which is what happened everywhere before this field existed. Only the list icon: the map prop and the purchase ghost always use Sprite above, because those answer \"how will this look where it stands\" and this one answers \"how do we represent it in a list\".")]
        [SerializeField] private Sprite shopIcon;

        [Tooltip("Purchase = costs SoftMoney. Day Unlock = not for sale, appears once the player reaches Unlock At Day Index and stays from then on.")]
        [SerializeField] private MetaUnlockKind unlock = MetaUnlockKind.Purchase;

        [Tooltip("SoftMoney cost. Only read when Unlock is Purchase. Left at 0 the prop is free, which is a content bug rather than a valid default -- the same stance FoodItemConfig.basePrice takes.")]
        [SerializeField, Min(0)] private int price;

        [Tooltip("Only read when Unlock is Day Unlock. The CATALOG POSITION of the Day this prop appears before — 0-based, exactly like a location's own Unlock At Day Index, NOT the number the player sees on the main screen (that one is this + 1). So the prop the player should find waiting before their fifth day is authored as 4.")]
        [SerializeField, Min(0)] private int unlockAtDayIndex;

        [Tooltip("Where this prop sits, as a 0..1 fraction of the LOCATION BACKGROUND's rect -- not of the screen. Normalized on purpose: the background is aspect-fitted per device, and a pixel offset would drift away from the art while a fraction rides along with it. It also lets a location with a differently sized background reuse the same data shape.")]
        [SerializeField] private Vector2 normalizedPosition = new(0.5f, 0.5f);

        [Tooltip("Which point of the sprite Normalized Position refers to. Defaults to bottom-centre (0.5, 0) because these props stand on the ground: anchoring the contact point means a prop stays planted when its art is re-exported at a different height, where a centre anchor would float or sink it.")]
        [SerializeField] private Vector2 pivot = new(0.5f, 0f);

        [Tooltip("Draw order within the location. Higher draws in front. This is the depth of a top-down scene: a prop nearer the bottom of the art is nearer the camera and needs a higher value than one behind it.")]
        [SerializeField] private int sortOrder;

        [Tooltip("Leave empty for a prop that is available from the start. Otherwise the id of an area-expansion item IN THIS SAME LOCATION that must be owned first -- the paved square gates the props that stand on it. Pointing at another location's area is a validation error.")]
        [SerializeField] private string requiresAreaId;

        [Tooltip("Tick for a prop that is itself an area expansion: buying it opens every prop whose Requires Area Id names it. The paved square is the one Meta1 has.")]
        [SerializeField] private bool unlocksArea;

        public string Id => id;
        public string DisplayName => displayName;
        public Sprite Sprite => sprite;

        // The fall-back lives HERE, not in the shop. "Empty means use the prop's sprite" is
        // one rule, and a rule spread across its callers is one a caller eventually forgets
        // -- the shop would then draw nothing for every prop without a custom icon, which
        // reads as missing art rather than as missing code.
        public Sprite ShopIcon => shopIcon != null ? shopIcon : sprite;
        public MetaUnlockKind Unlock => unlock;
        public int Price => price;
        public int UnlockAtDayIndex => unlockAtDayIndex;
        public Vector2 NormalizedPosition => normalizedPosition;
        public Vector2 Pivot => pivot;
        public int SortOrder => sortOrder;
        public string RequiresAreaId => requiresAreaId;
        public bool UnlocksArea => unlocksArea;

        // NOTE there is deliberately no size or scale field. Every sprite in a location
        // is authored against that location's background at one resolution, so a prop's
        // size in canvas units follows from its own pixel size times the background's
        // scale factor -- authoring a size again would be a second authority that can
        // disagree with the art. If a single prop ever needs a nudge, that is the point
        // to add one field, not before (FoodItemConfig.overallScale is the precedent for
        // what that would look like).

        public MetaItemDefinition() { }

        // For tests and for the Editor placement tool, which writes positions back into
        // the asset rather than making someone type twenty pairs of floats.
        public MetaItemDefinition(
            string id,
            MetaUnlockKind unlock,
            int price = 0,
            int unlockAtDayIndex = 0,
            Sprite sprite = null,
            Sprite shopIcon = null,
            string requiresAreaId = null,
            bool unlocksArea = false,
            string displayName = null,
            Vector2 normalizedPosition = default,
            Vector2 pivot = default,
            int sortOrder = 0)
        {
            this.id = id;
            this.displayName = displayName;
            this.sprite = sprite;
            this.shopIcon = shopIcon;
            this.unlock = unlock;
            this.price = price;
            this.unlockAtDayIndex = unlockAtDayIndex;
            this.normalizedPosition = normalizedPosition;
            this.pivot = pivot;
            this.sortOrder = sortOrder;
            this.requiresAreaId = requiresAreaId;
            this.unlocksArea = unlocksArea;
        }
    }

    // One restaurant's grounds: its background and every prop that can stand on it.
    //
    // A location OWNS its props, which is what makes "which props are cosmetic and which
    // are Day-unlocked varies from location to location" free rather than a feature: the
    // Unlock field is per item and the items are in here, so a second restaurant is an
    // asset to fill in, not code to write (decisions.md D-015).
    [Serializable]
    public class MetaLocation
    {
        [Tooltip("Unique across the catalog. Also the prefix of every persistence key this location's props use (\"Meta1.Fountain\"), so renaming it orphans what players already bought here -- treat it as permanent once shipped.")]
        [SerializeField] private string id;

        [Tooltip("Shown on the location switcher. Player-facing, so it belongs in data.")]
        [SerializeField] private string displayName;

        [Tooltip("The grounds themselves. Every prop's Normalized Position is a fraction of THIS sprite's rect.")]
        [SerializeField] private Sprite backgroundSprite;

        [Tooltip("Optional. A LARGER version of the same art, drawn behind the grounds so the edges keep going when the map is zoomed in. Must be the grounds grown OUTWARD EQUALLY on all four sides — it is centred on the background, with no offset to author. Leave it empty and the screen looks exactly as it did before this existed.")]
        [SerializeField] private Sprite backgroundBgSprite;

        [Tooltip("The Day index at which this location becomes visitable. 0 for the location the game starts on. Derived, never saved: the player's current Day already answers it, and storing it as well would be a second authority that lies as soon as Day content is re-authored.")]
        [SerializeField, Min(0)] private int unlockAtDayIndex;

        [SerializeField] private List<MetaItemDefinition> items = new();

        public string Id => id;
        public string DisplayName => displayName;
        public Sprite BackgroundSprite => backgroundSprite;

        // Centred on BackgroundSprite, with NO offset to author, and that is a property of the
        // ART rather than a simplification: the extension has to grow outward equally on all
        // four sides. The first export of Meta1's did not (it sat at 117,387 inside a
        // 1074x2550 image while centring would have put it at 110,353) and the author
        // re-exported it rather than have the catalog carry a correction. Keeping it that way
        // is what lets this be one field instead of a field plus a pair of numbers that go
        // stale the next time the art is re-exported.
        public Sprite BackgroundBgSprite => backgroundBgSprite;
        public int UnlockAtDayIndex => unlockAtDayIndex;
        public IReadOnlyList<MetaItemDefinition> Items => items;

        public MetaLocation() { }

        public MetaLocation(
            string id,
            int unlockAtDayIndex = 0,
            IEnumerable<MetaItemDefinition> items = null,
            Sprite backgroundSprite = null,
            string displayName = null)
        {
            this.id = id;
            this.displayName = displayName;
            this.backgroundSprite = backgroundSprite;
            this.unlockAtDayIndex = unlockAtDayIndex;
            this.items = items == null ? new List<MetaItemDefinition>() : new List<MetaItemDefinition>(items);
        }
    }

    // The single authority for every meta prop's price, position, art and unlock
    // condition (decisions.md D-015). Nothing about the meta side is hardcoded: no price,
    // no id and no category name appears in code.
    //
    // Multi-location from the first line, not as a guess about the future: a second
    // restaurant is a stated plan, and retrofitting the location layer later would mean
    // changing the schema of ownership keys already written to players' save files, i.e.
    // a migration. Wrapping now costs one list.
    //
    // Deliberately NOT an authority on: the wallet, which props the player owns (both
    // live in player_profile.json), or whether a Day-unlocked prop is currently open
    // (derived from Day content plus the player's Day index, never stored).
    [CreateAssetMenu(fileName = "MetaCatalog", menuName = "ExpoTheExplorer/Data/Meta Catalog")]
    public class MetaCatalog : ScriptableObject
    {
        [Tooltip("Ordered by Unlock At Day Index, earliest first. The screen opens on the last one the player has unlocked.")]
        [SerializeField] private List<MetaLocation> locations = new();

        public IReadOnlyList<MetaLocation> Locations => locations;

        // The persistence key for one prop. It lives here, beside the two ids it joins,
        // so the resolver, the shop and any future migration cannot each invent their own
        // spelling of it. Local ids stay short in the asset; uniqueness across locations
        // is structural rather than something an author has to remember.
        public static string OwnershipKey(string locationId, string itemId) => $"{locationId}.{itemId}";
    }
}
