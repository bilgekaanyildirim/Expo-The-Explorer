using System.Collections.Generic;
using ExpoTheExplorer.Data;
using ExpoTheExplorer.Session;
using ExpoTheExplorer.Systems.MetaSystem;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace ExpoTheExplorer.UI
{
    // What the player actually sees on the meta screen: one location's grounds, with the
    // props they have unlocked standing on it, and a bar to walk between locations.
    //
    // It decides nothing. Which props are on screen comes from MetaResolver, where prices
    // and area gates and unlock days were already settled and tested (D-019); where each
    // prop sits comes from MetaCatalog; who the player is comes from GameSession. This
    // class turns those three answers into RectTransforms and nothing else -- which is
    // what keeps the rules testable, since none of them live here.
    //
    // Props are INSTANTIATED at runtime rather than placed in the scene. Nineteen
    // hand-placed objects would be a second copy of the catalog, free to disagree with it
    // silently; the catalog stays the single authority (D-015) and the scene only holds
    // the container the editor step builds.
    public class MetaGroundsView : MonoBehaviour
    {
        [Header("Bound by ExpoTheExplorer > Meta > Build Meta Grounds")]
        [SerializeField] private SessionHost sessionHost;
        [SerializeField] private MetaCatalog catalog;

        [Tooltip("Scrolls vertically when the art is taller than the screen. The backgrounds are drawn for a tall phone (0.463) while the canvas is authored at 0.5625, so on a shorter screen roughly 18% overflows — scrolling is what keeps every prop reachable instead of cropping some away.")]
        [SerializeField] private ScrollRect scroll;

        [Tooltip("The grounds themselves. Sized by this component: full width, height derived from the sprite's aspect. Props are parented to it, so they scroll with the art rather than needing to be re-aligned.")]
        [SerializeField] private Image background;

        [Header("Location bar")]
        [SerializeField] private TMP_Text locationLabel;
        [SerializeField] private Button previousButton;
        [SerializeField] private Button nextButton;

        [Tooltip("Shown when a further location exists but is still locked, e.g. \"Unlocks on Day 12\". Hidden once nothing is left to unlock.")]
        [SerializeField] private TMP_Text lockedHintLabel;

        private readonly List<GameObject> spawnedProps = new();
        private List<MetaLocation> unlockedLocations = new();
        private int viewedIndex = -1;

        // Which location the player is looking at is UI state, not saved state (K6-4): the
        // screen opens on the newest unlocked one and they walk back from there. Storing it
        // would mean a profile field and a schema version for a viewing preference.
        private void Start()
        {
            if (!ValidateReferences()) return;

            Refresh();
        }

        // Public because Adım 8's purchase has to call it: buying a prop changes what
        // IsActive returns, and nothing else would notice. Cheap to call -- this runs at
        // event frequency (screen open, purchase), never per frame.
        public void Refresh()
        {
            var session = sessionHost?.Session;
            if (session == null || catalog == null) return;

            // Layout first, then measure. Prop sizes are derived from the background's
            // WIDTH, and on the frame this runs from Start that width can still be zero --
            // Unity resolves stretched anchors during its own layout pass, which has not
            // happened yet. Measuring then would size every prop to nothing, and the
            // symptom is an empty-looking screen rather than an error. Cheap here: this
            // runs on screen open and after a purchase, never per frame.
            Canvas.ForceUpdateCanvases();

            var currentDay = session.State.CurrentDayIndex;
            unlockedLocations = MetaResolver.UnlockedLocations(catalog, currentDay);

            if (unlockedLocations.Count == 0)
            {
                // MetaCatalogValidator reports "no location unlocks at Day 0" as an error,
                // so this is a content bug rather than a state to design a screen for.
                Debug.LogWarning(
                    $"{nameof(MetaGroundsView)}: no location is unlocked at Day {currentDay}. " +
                    "Check the catalog — something must be reachable from Day 0.", this);
                Clear();
                return;
            }

            // Clamped rather than reset, so walking to an older location and then earning a
            // new one does not throw the player back to the newest.
            viewedIndex = viewedIndex < 0
                ? unlockedLocations.Count - 1
                : Mathf.Clamp(viewedIndex, 0, unlockedLocations.Count - 1);

            var location = unlockedLocations[viewedIndex];

            DrawBackground(location);
            DrawProps(location, session.OwnedMetaItemIds, currentDay);
            DrawLocationBar(location, currentDay);
        }

        private void DrawBackground(MetaLocation location)
        {
            background.sprite = location.BackgroundSprite;
            background.enabled = location.BackgroundSprite != null;

            if (location.BackgroundSprite == null) return;

            // Fit by WIDTH and let the height fall out of the aspect (M10, the user's
            // decision). The alternative -- fitting by height -- would letterbox the sides
            // on every device; fitting by width means the only variation is how much
            // vertical scrolling there is, which is zero on the phones the art was drawn
            // for.
            var rect = background.rectTransform;
            var viewportWidth = ((RectTransform)scroll.viewport ? scroll.viewport : rect.parent as RectTransform).rect.width;
            var sprite = location.BackgroundSprite.rect;
            var height = viewportWidth * (sprite.height / sprite.width);

            rect.sizeDelta = new Vector2(0f, height);
        }

        private void DrawProps(MetaLocation location, ISet<string> ownedKeys, int currentDayIndex)
        {
            Clear();

            if (location.BackgroundSprite == null) return;

            // ActiveItems already returns draw order (ascending SortOrder, ties by authored
            // order), so sibling index carries depth for free -- later siblings draw on top
            // in a Canvas.
            var active = MetaResolver.ActiveItems(location, ownedKeys, currentDayIndex);

            // The same derivation the Meta Editor's canvas uses: a prop's size is its own
            // pixel size times however much the background got scaled. There is no scale
            // field in the catalog on purpose (D-015), so this formula IS the size, and
            // both places must compute it identically or the preview lies.
            var scale = background.rectTransform.rect.width / location.BackgroundSprite.rect.width;

            foreach (var item in active)
            {
                spawnedProps.Add(CreateProp(item, scale));
            }
        }

        private GameObject CreateProp(MetaItemDefinition item, float scale)
        {
            var prop = new GameObject(item.Id, typeof(RectTransform), typeof(Image));
            var rect = (RectTransform)prop.transform;
            rect.SetParent(background.rectTransform, worldPositionStays: false);

            // Anchors carry the normalized position, so the prop rides the background
            // through every resize and every scroll without a line of layout code. This is
            // what K5's "normalize to the background rect, not the screen" was for.
            rect.anchorMin = item.NormalizedPosition;
            rect.anchorMax = item.NormalizedPosition;

            // The pivot is the prop's own contact point -- bottom-centre by default,
            // because these things stand on the ground. Anchoring the contact point is what
            // keeps a prop planted when its art is re-exported at a different height.
            rect.pivot = item.Pivot;
            rect.anchoredPosition = Vector2.zero;
            rect.sizeDelta = item.Sprite.rect.size * scale;

            var image = prop.GetComponent<Image>();
            image.sprite = item.Sprite;

            // Nothing here is clickable yet. Adım 8 owns the shop, and a raycast target
            // that does nothing would swallow drags meant for the scroll view.
            image.raycastTarget = false;

            return prop;
        }

        private void DrawLocationBar(MetaLocation location, int currentDayIndex)
        {
            locationLabel.text = string.IsNullOrWhiteSpace(location.DisplayName)
                ? location.Id
                : location.DisplayName;

            previousButton.interactable = viewedIndex > 0;
            nextButton.interactable = viewedIndex < unlockedLocations.Count - 1;

            // The first location in catalog order that is still locked -- "what comes next"
            // rather than "the nearest by day", because catalog order is authored order.
            MetaLocation nextLocked = null;
            foreach (var candidate in catalog.Locations)
            {
                if (candidate == null || MetaResolver.IsLocationUnlocked(candidate, currentDayIndex)) continue;
                nextLocked = candidate;
                break;
            }

            if (lockedHintLabel == null) return;

            lockedHintLabel.gameObject.SetActive(nextLocked != null);
            if (nextLocked != null)
            {
                // +1 because the player-facing day number is the catalog position plus one,
                // the same conversion MainScreenView makes.
                lockedHintLabel.text = $"Unlocks on Day {nextLocked.UnlockAtDayIndex + 1}";
            }
        }

        // Only ever between UNLOCKED locations. Letting the player walk into a locked one
        // would duplicate, in the UI, the refusal MetaPurchase.LocationLocked already owns.
        private void Step(int delta)
        {
            viewedIndex = Mathf.Clamp(viewedIndex + delta, 0, unlockedLocations.Count - 1);
            Refresh();

            // Back to the top: the new location's art is a different height, and leaving the
            // scroll where it was would drop the player into the middle of it.
            if (scroll != null) scroll.verticalNormalizedPosition = 1f;
        }

        private void Clear()
        {
            foreach (var prop in spawnedProps)
            {
                if (prop != null) Destroy(prop);
            }
            spawnedProps.Clear();
        }

        private void OnEnable()
        {
            if (previousButton != null) previousButton.onClick.AddListener(OnPrevious);
            if (nextButton != null) nextButton.onClick.AddListener(OnNext);
        }

        private void OnDisable()
        {
            if (previousButton != null) previousButton.onClick.RemoveListener(OnPrevious);
            if (nextButton != null) nextButton.onClick.RemoveListener(OnNext);
        }

        private void OnPrevious() => Step(-1);
        private void OnNext() => Step(1);

        // Every field is wired by the editor step, so a missing one should name itself
        // rather than surface as a NullReferenceException three frames later. Same pattern
        // MainScreenView and the two popups use.
        private bool ValidateReferences()
        {
            var missing = new List<string>();
            if (sessionHost == null) missing.Add(nameof(sessionHost));
            if (catalog == null) missing.Add(nameof(catalog));
            if (scroll == null) missing.Add(nameof(scroll));
            if (background == null) missing.Add(nameof(background));
            if (locationLabel == null) missing.Add(nameof(locationLabel));
            if (previousButton == null) missing.Add(nameof(previousButton));
            if (nextButton == null) missing.Add(nameof(nextButton));

            if (missing.Count == 0) return true;

            Debug.LogError(
                $"{nameof(MetaGroundsView)} on '{name}' is missing Inspector reference(s): {string.Join(", ", missing)}. " +
                "Run ExpoTheExplorer > Meta > Build Meta Grounds.", this);
            return false;
        }
    }
}
