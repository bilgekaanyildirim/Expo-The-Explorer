using System.Collections;
using System.Collections.Generic;
using DG.Tweening;
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

        // ALL OPTIONAL (decisions.md D-025). With one location there is nothing to switch
        // between, so requiring the bar would force scene clutter for a feature that has no
        // content yet -- and deleting it is a legitimate choice, not a mistake. It becomes
        // meaningful when a second restaurant exists, and its absence is then obvious on
        // screen (no name, no arrows) rather than silent.
        [Header("Location bar — optional until a second location exists")]
        [SerializeField] private TMP_Text locationLabel;
        [SerializeField] private Button previousButton;
        [SerializeField] private Button nextButton;

        [Tooltip("Shown when a further location exists but is still locked, e.g. \"Unlocks on Day 12\". Hidden once nothing is left to unlock.")]
        [SerializeField] private TMP_Text lockedHintLabel;

        [Header("Purchase preview")]
        [Tooltip("How solid the ghost of a prop being previewed looks. Cosmetic — try numbers here rather than in code.")]
        [SerializeField, Range(0.1f, 1f)] private float ghostAlpha = 0.45f;

        [Tooltip("How much of the viewport the previewed prop should fill. This is what makes a trash bin zoom in while a map-wide fence does not: the zoom needed to fill this fraction is clamped at 1x, so a prop already that big simply stays put.")]
        [SerializeField, Range(0.15f, 0.9f)] private float previewFill = 0.45f;

        [Tooltip("Ceiling on the preview zoom. Stops a tiny prop from filling the screen with four blurry pixels.")]
        [SerializeField, Range(1f, 6f)] private float previewMaxZoom = 3f;

        [Tooltip("Where the prop sits vertically in the viewport while previewed: 0 = bottom, 1 = top. Kept BELOW centre on purpose, so the confirm popup has room above the prop instead of being pushed under it.")]
        [SerializeField, Range(0.1f, 0.9f)] private float previewFocusHeight = 0.38f;

        [Header("Upcoming Day-unlock prop")]
        [Tooltip("How faint the silhouette of the prop the player is waiting for looks. The solid part filling up from the bottom is drawn over this at full strength.")]
        [SerializeField, Range(0.05f, 0.9f)] private float upcomingSilhouetteAlpha = 0.25f;

        [Tooltip("Size of the \"%60\" label over the prop the player is waiting for.")]
        [SerializeField, Min(1f)] private float upcomingLabelSize = 30f;

        [SerializeField] private Color upcomingLabelColor = Color.white;

        [Tooltip("How far above the prop that label sits, in canvas units.")]
        [SerializeField] private float upcomingLabelOffset = 16f;

        [Tooltip("How long the map takes to travel to the previewed prop, and back. The feel of this screen is settled here, not in code.")]
        [SerializeField, Min(0f)] private float previewTravelSeconds = 0.35f;

        [Tooltip("Easing for that travel. OutCubic arrives softly, which reads as the camera settling rather than stopping.")]
        [SerializeField] private Ease previewTravelEase = Ease.OutCubic;

        [Header("Unlock celebration")]
        [Tooltip("How long a newly unlocked prop takes to rise from silhouette to solid.")]
        [SerializeField, Min(0.05f)] private float celebrationRevealSeconds = 0.9f;

        [Tooltip("How long the finished prop is held on screen before the map moves on. A beat, not a pause — the player already knows what they got.")]
        [SerializeField, Min(0f)] private float celebrationHoldSeconds = 0.6f;

        [Tooltip("How long the map lingers on the NEXT prop's ghost after a celebration, before zooming back out. A separate number from the hold above on purpose: that beat is \"look at what you got\", this one is \"and this is next\".")]
        [SerializeField, Min(0f)] private float celebrationNextPeekSeconds = 1.1f;

        private readonly List<GameObject> spawnedProps = new();

        // The same objects as spawnedProps, keyed so the celebration can find the ONE prop it
        // is about to reveal and hide it while an animated copy takes its place. Rebuilt with
        // the props, so it never outlives them.
        private readonly Dictionary<MetaItemDefinition, GameObject> propsByItem = new();

        // The upcoming-prop silhouette DrawUpcoming last drew, or null when nothing is
        // coming. Lives and dies with the other props, so it is cleared alongside them.
        private GameObject upcomingProp;

        private List<MetaLocation> unlockedLocations = new();
        private int viewedIndex = -1;

        // Guards against a second celebration starting on top of a running one -- Refresh is
        // called on screen open, on a location change and after a purchase, and only the
        // first of those should ever be able to begin one.
        private bool celebrating;

        // Set by the full-screen catcher below. One tap ends the CURRENT prop's animation,
        // not the whole queue: a player who wants to move on should not have to sit through
        // three of them, but neither should one tap silently swallow two unlocks.
        private bool skipRequested;

        // Deliberately NOT in spawnedProps. Clear() would then take it too, which is what
        // is wanted today -- but it would tie two different lifetimes to one list, and in
        // Ş4 "was the ghost destroyed, or the prop" becomes a question worth being able to
        // answer. The ghost belongs to a purchase that has not happened; the props belong
        // to purchases that have.
        private GameObject ghost;

        // The map's framing before a preview took it over. Restored by ClearGhost, which
        // every exit the shop has already funnels through (D-031) -- so the zoom cannot get
        // stuck without a seventh exit path existing, and there is not one.
        private Vector2 preFocusContentPosition;
        private Vector3 preFocusContentScale = Vector3.one;
        private bool focused;

        // Where the ghost's edges END UP, measured at the destination rather than read live.
        // The shop places the confirm popup against these, so the popup is right on the
        // first frame and stays put while the map travels under it.
        private Vector3 settledGhostTop;
        private Vector3 settledGhostBottom;

        // Which location the shop should be listing. D-027 deleted this property with the
        // first shop, on the grounds that an entry point with no caller reads as one that
        // is used somewhere -- so it comes back only now that MetaShopView actually reads
        // it (Ş2). What did NOT come back is the ViewedLocationChanged event: the shop
        // rebuilds its list every time its panel opens, and the only moment the player can
        // walk to another location is while that panel is closed. An event would be a
        // publisher written before its subscriber.
        //
        // This is also why the shop has no MetaCatalog field of its own. The catalog
        // reference stays in one place; two components free to point at two different
        // catalogs would break D-015's single-authority rule quietly.
        public MetaLocation ViewedLocation =>
            viewedIndex >= 0 && viewedIndex < unlockedLocations.Count ? unlockedLocations[viewedIndex] : null;

        // Which location the player is looking at is UI state, not saved state (K6-4): the
        // screen opens on the newest unlocked one and they walk back from there. Storing it
        // would mean a profile field and a schema version for a viewing preference.
        private void Start()
        {
            if (!ValidateReferences()) return;

            Refresh();

            // After Refresh, because the celebration hides a prop Refresh has to have drawn
            // first. Only from here, never from Refresh itself -- see TryStartCelebration.
            TryStartCelebration();
        }

        // Public again, and only now (D-034). It was public for the first shop, went back to
        // private when that shop was reverted (D-027) because an entry point with no caller
        // reads as one that is used somewhere, and stayed private through Ş1 and Ş3 on the
        // same argument -- neither of those steps bought anything. Ş4 does, and a purchase
        // changes what MetaResolver.IsActive answers while nothing else would notice, so the
        // caller this method needed exists exactly as of now.
        //
        // Runs at EVENT frequency (screen open, location change, a purchase), never per
        // frame, which is what makes the layout rebuild below affordable.
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

            // A ghost belongs to ONE location's layout. Walking to another location while
            // one is up would leave it standing on ground it was never positioned against.
            ClearGhost();

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
            var scale = PropScale(location);

            foreach (var item in active)
            {
                var prop = CreateProp(item, scale);
                spawnedProps.Add(prop);
                propsByItem[item] = prop;
            }

            DrawUpcoming(location, active, ownedKeys, currentDayIndex, scale);
        }

        // The prop the player is waiting for: a faint silhouette of the whole thing, with the
        // solid version filling up from the bottom as the days pass, and the percentage over
        // it. Which prop and how far along come from MetaResolver.NextDayUnlock -- this
        // method decides nothing, the same division of labour the rest of this class keeps.
        //
        // Two layers rather than one, because a single Image cannot be both faint AND partly
        // solid: fillAmount CUTS the image off, it does not fade it. So the back layer says
        // "this is coming" and the front layer says "this much of the wait is done".
        //
        // Everything here is built in code rather than cloned from an authored template. A
        // template would be nicer to style, but it would mean a new object reference on this
        // component, which would mean re-running Build Meta Grounds -- and that step refuses
        // to rebuild what already exists, which is exactly the trap D-036 was about. The
        // tuning that matters is serialized above instead, and new serialized values are born
        // with their defaults without anyone touching the scene.
        private void DrawUpcoming(
            MetaLocation location,
            List<MetaItemDefinition> active,
            ISet<string> ownedKeys,
            int currentDayIndex,
            float scale)
        {
            var upcoming = MetaResolver.NextDayUnlock(location, ownedKeys, currentDayIndex);
            if (upcoming.Item?.Sprite == null) return;

            // The silhouette goes through CreateProp, so the preview stands exactly where and
            // at what size the real prop will -- the same reason the purchase ghost does
            // (D-032). A preview drawn by its own arithmetic is a preview that can lie.
            var silhouette = CreateProp(upcoming.Item, scale);
            silhouette.name = $"Upcoming_{upcoming.Item.Id}";
            var silhouetteImage = silhouette.GetComponent<Image>();
            var color = silhouetteImage.color;
            silhouetteImage.color = new Color(color.r, color.g, color.b, upcomingSilhouetteAlpha);
            spawnedProps.Add(silhouette);

            // Kept so the celebration can pan over to it afterwards (D-043). Not re-created
            // there: Refresh runs BEFORE the celebration and the day index is already the new
            // one, so this silhouette is ALREADY the prop after the one being unlocked, drawn
            // at the right progress. A second copy would be the same thing twice.
            upcomingProp = silhouette;

            // Placed at the sibling index its SortOrder asks for, NOT appended (D-045). It
            // used to be created last, which put it in front of everything regardless of
            // depth -- and that is how the game came to disagree with the Meta Editor, whose
            // canvas has always sorted. The reason to fix it this way rather than force the
            // ghost forward is the reason the preview exists at all (D-032, D-040): it shows
            // where the prop WILL stand, and depth is part of where. A ghost pulled to the
            // front tells the truth about position and lies about occlusion.
            //
            // The consequence is accepted rather than worked around: a prop authored at a low
            // SortOrder really is behind the scenery, so its ghost can be partly or wholly
            // hidden. The celebration zooms to it, so "something is coming" is not lost.
            silhouette.transform.SetSiblingIndex(SortedInsertIndex(location, active, upcoming.Item));

            CreateFillLayer(silhouette.transform, upcoming.Item.Sprite, upcoming.Progress);
            CreateUpcomingLabel(silhouette.transform, upcoming.Progress);
        }

        // Where a prop that is NOT in the active list belongs among the ones that are, under
        // the one ordering rule this project has: ascending SortOrder, ties keeping authored
        // catalog order. `active` arrived already in that order from MetaResolver.ActiveItems,
        // so counting how many of them sort before the newcomer IS its sibling index.
        //
        // The tie-break needs the authored positions, which is why the location is passed in:
        // "ties keep catalog order" means nothing without knowing what that order was, and
        // inferring it from the active list would be a second, quietly different rule.
        private static int SortedInsertIndex(
            MetaLocation location, List<MetaItemDefinition> active, MetaItemDefinition item)
        {
            var itemAuthored = AuthoredIndex(location, item);
            var index = 0;

            foreach (var other in active)
            {
                if (other.SortOrder > item.SortOrder) break;

                // Equal SortOrder: whichever was authored earlier stays behind, matching what
                // ActiveItems' stable insertion sort does among the active props themselves.
                if (other.SortOrder == item.SortOrder && AuthoredIndex(location, other) > itemAuthored) break;

                index++;
            }

            return index;
        }

        // By reference, not by id: two locations may legitimately carry the same local id
        // (MetaCatalog.OwnershipKey exists precisely because they do), and this is only ever
        // asked about items taken from this location's own list.
        private static int AuthoredIndex(MetaLocation location, MetaItemDefinition item)
        {
            if (location?.Items == null) return 0;

            for (var i = 0; i < location.Items.Count; i++)
            {
                if (ReferenceEquals(location.Items[i], item)) return i;
            }

            return 0;
        }

        // The solid part of a two-layer prop: a CHILD of the silhouette, stretched over it, so
        // it inherits the placement instead of computing it a second time. Shared by the
        // upcoming-prop preview and the unlock celebration -- the same two layers mean the
        // same thing in both, and a second copy of this setup would be free to disagree.
        //
        // Bottom-up, because these props stand on the ground and rising out of it reads as
        // being built. NOTE: preserveAspect is deliberately not used anywhere on these props
        // -- the size already comes from the sprite's own pixels times the background scale --
        // and it interacts badly with Filled, so leave it alone.
        private static Image CreateFillLayer(Transform parent, Sprite sprite, float amount)
        {
            var fill = new GameObject("Fill", typeof(RectTransform), typeof(Image));
            var rect = (RectTransform)fill.transform;
            rect.SetParent(parent, worldPositionStays: false);
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;

            var image = fill.GetComponent<Image>();
            image.sprite = sprite;
            image.raycastTarget = false;
            image.type = Image.Type.Filled;
            image.fillMethod = Image.FillMethod.Vertical;
            image.fillOrigin = (int)Image.OriginVertical.Bottom;
            image.fillAmount = amount;

            return image;
        }

        // ---- Unlock celebration (D-041) -------------------------------------------------
        //
        // Started ONLY from Start, never from Refresh: a purchase also calls Refresh, and a
        // shopping trip is not the moment to interrupt with a day-unlock fanfare. Which props
        // are owed comes from MetaResolver.DayUnlocksBetween -- this method decides nothing
        // beyond "is anything owed, and am I already busy".
        //
        // The main screen works this out for ITSELF rather than being told by the day scene.
        // That is what makes this independent of the forced-return step that follows it: the
        // player gets the celebration whether they came back through the Day Complete popup,
        // through Game Over, or by quitting and relaunching two days later.
        private void TryStartCelebration()
        {
            if (celebrating) return;

            var session = sessionHost?.Session;
            var location = ViewedLocation;
            if (session == null || location == null) return;

            var opened = MetaResolver.DayUnlocksBetween(
                location, session.OwnedMetaItemIds, session.LastCelebratedDayIndex, session.State.CurrentDayIndex);

            if (opened.Count == 0) return;

            StartCoroutine(Celebrate(session, opened));
        }

        private IEnumerator Celebrate(GameSession session, List<MetaItemDefinition> opened)
        {
            celebrating = true;
            var catcher = CreateSkipCatcher();

            // try/finally, not try/catch: whatever happens, the map has to come back and the
            // catcher has to go. A screen left zoomed in with an invisible full-screen button
            // over it is unusable, and that is the one outcome worth protecting against.
            try
            {
                foreach (var item in opened)
                {
                    yield return CelebrateOne(item);
                }

                yield return PeekAtWhatIsNext();

                // Written only after the WHOLE queue finished. An interrupted celebration --
                // the scene unloading, the app dying -- leaves the marker where it was, so
                // the player sees it again next time rather than losing it silently. Replaying
                // is the cheap failure; swallowing is the expensive one.
                session.LastCelebratedDayIndex = session.State.CurrentDayIndex;
                session.Save();
            }
            finally
            {
                if (catcher != null) Destroy(catcher);

                // RestoreFocus, not ClearGhost: there is no ghost here. ClearGhost is the
                // shop's single door for the ghost's LIFETIME (D-031); the framing is a
                // separate thing and this is the call that owns putting it back.
                RestoreFocus();
                celebrating = false;
            }
        }

        private IEnumerator CelebrateOne(MetaItemDefinition item)
        {
            var location = ViewedLocation;
            if (item?.Sprite == null || location?.BackgroundSprite == null) yield break;

            // The real prop is already standing there -- its day has passed, so DrawProps drew
            // it solid. It is hidden for the duration and an animated copy takes its place, so
            // that "it opens" is something the player watches rather than something that
            // already happened before the map arrived.
            propsByItem.TryGetValue(item, out var realProp);
            if (realProp != null) realProp.SetActive(false);

            var scale = PropScale(location);
            var silhouette = CreateProp(item, scale);
            silhouette.name = $"Unlocking_{item.Id}";

            // Takes over the real prop's exact sibling index rather than being appended
            // (D-045). Inheriting the position it is standing in for is what makes "the copy
            // and the original are at the same depth" true by construction instead of by a
            // second calculation. Appended, the prop would jump to the front for the length
            // of the celebration and drop back when the copy went away.
            if (realProp != null)
            {
                silhouette.transform.SetSiblingIndex(realProp.transform.GetSiblingIndex());
            }

            var silhouetteImage = silhouette.GetComponent<Image>();
            var baseColor = silhouetteImage.color;
            silhouetteImage.color = new Color(baseColor.r, baseColor.g, baseColor.b, upcomingSilhouetteAlpha);

            var fillImage = CreateFillLayer(silhouette.transform, item.Sprite, 0f);

            skipRequested = false;
            FocusOn((RectTransform)silhouette.transform);

            // Let the map arrive before anything grows, or the reveal plays against a moving
            // background and reads as one confused motion instead of two clear ones.
            yield return WaitOrSkip(previewTravelSeconds);

            var elapsed = 0f;
            while (elapsed < celebrationRevealSeconds && !skipRequested)
            {
                elapsed += Time.unscaledDeltaTime;
                var t = Mathf.Clamp01(elapsed / celebrationRevealSeconds);
                fillImage.fillAmount = t;
                silhouetteImage.color = new Color(
                    baseColor.r, baseColor.g, baseColor.b, Mathf.Lerp(upcomingSilhouetteAlpha, 1f, t));
                yield return null;
            }

            // Snapped to the finished state whether it ran out or was skipped, so the frame
            // before the copy disappears matches the prop underneath it exactly.
            fillImage.fillAmount = 1f;
            silhouetteImage.color = baseColor;

            yield return WaitOrSkip(celebrationHoldSeconds);

            Destroy(silhouette);
            if (realProp != null) realProp.SetActive(true);
        }

        // After the last unlock, the map slides on to the prop the player is now waiting for
        // and lingers there (D-043). The timing pays for itself: D-040 measures the wait from
        // the PREVIOUS unlock, so a prop that just opened leaves the next one reading exactly
        // %0 -- "the counter for this one starts now" is already on screen and needs no words.
        //
        // Not a new drawing pass. Refresh ran before the celebration with the new day index,
        // so upcomingProp is already the prop AFTER the one that just opened, at the right
        // progress.
        private IEnumerator PeekAtWhatIsNext()
        {
            // Nothing coming: everything is open, or the next one is area-gated and therefore
            // not drawn at all. Either way there is nothing to look at, so the map goes
            // straight back out via the caller's finally.
            if (upcomingProp == null) yield break;

            // Reset, so a player who skipped the reveal is not taken to have skipped this
            // too. If they want past it as well, one more tap does it.
            skipRequested = false;

            FocusOn((RectTransform)upcomingProp.transform);

            yield return WaitOrSkip(previewTravelSeconds + celebrationNextPeekSeconds);
        }

        // Unscaled, like the reset button's confirm window: a menu has no reason to run at
        // timeScale 0 today, but a celebration that never advances would be a trap rather
        // than a pause.
        private IEnumerator WaitOrSkip(float seconds)
        {
            var elapsed = 0f;
            while (elapsed < seconds && !skipRequested)
            {
                elapsed += Time.unscaledDeltaTime;
                yield return null;
            }
        }

        // A transparent, full-screen button for the duration. Input-system agnostic on
        // purpose: reading Input.GetMouseButtonDown would depend on which input backend the
        // project is configured for, while a Button on the canvas goes through whatever event
        // system is already raising the shop's clicks.
        //
        // Parented to the CANVAS and made the last sibling, not parented to the grounds. The
        // grounds are the canvas's FIRST child and the shop its last (D-029), so a catcher
        // inside the grounds would sit under the market button and the player could open the
        // shop mid-celebration. Reaching outside this component's own hierarchy is worth
        // calling out, and it is bounded: one object, created and destroyed by one coroutine.
        private GameObject CreateSkipCatcher()
        {
            var canvas = GetComponentInParent<Canvas>();
            if (canvas == null) return null;

            var catcher = new GameObject("CelebrationSkip", typeof(RectTransform), typeof(Image), typeof(Button));
            var rect = (RectTransform)catcher.transform;
            rect.SetParent(canvas.transform, worldPositionStays: false);
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
            rect.SetAsLastSibling();

            var image = catcher.GetComponent<Image>();
            // Fully transparent but still hit-testable: Image raycasts on its rect, not on
            // pixel alpha, unless an alpha threshold is set -- and none is.
            image.color = new Color(0f, 0f, 0f, 0f);
            image.raycastTarget = true;

            var button = catcher.GetComponent<Button>();
            button.targetGraphic = image;
            button.transition = Selectable.Transition.None;
            button.onClick.AddListener(() => skipRequested = true);

            return catcher;
        }

        // Just the number, no words: the rest of this game's UI is English ("Continue Day X",
        // "BUY"), and a word here would be a fourth string to localise for no gain.
        private void CreateUpcomingLabel(Transform parent, float progress)
        {
            if (TMP_Settings.defaultFontAsset == null)
            {
                // The fill still draws. A missing font should cost the caption, not the
                // feature -- and an invisible label is worse than none, so it says why.
                Debug.LogWarning(
                    $"{nameof(MetaGroundsView)}: no TMP default font asset, so the upcoming prop's percentage " +
                    "is not shown. Import TMP Essentials (Window > TextMeshPro).", this);
                return;
            }

            var labelObject = new GameObject("UpcomingProgress", typeof(RectTransform));
            var rect = (RectTransform)labelObject.transform;
            rect.SetParent(parent, worldPositionStays: false);

            // Anchored to the TOP of the prop and pivoted at its own bottom, so the label
            // sits above the art whatever height that art happens to be.
            rect.anchorMin = new Vector2(0.5f, 1f);
            rect.anchorMax = new Vector2(0.5f, 1f);
            rect.pivot = new Vector2(0.5f, 0f);
            rect.sizeDelta = new Vector2(220f, upcomingLabelSize * 1.6f);
            rect.anchoredPosition = new Vector2(0f, upcomingLabelOffset);

            var label = labelObject.AddComponent<TextMeshProUGUI>();
            label.font = TMP_Settings.defaultFontAsset;
            label.fontSize = upcomingLabelSize;
            label.color = upcomingLabelColor;
            label.alignment = TextAlignmentOptions.Center;
            label.raycastTarget = false;

            // COMPLETED percentage, the user's choice: it climbs in the same direction as the
            // fill they are watching, where a remaining figure would count the other way.
            label.text = $"%{Mathf.RoundToInt(progress * 100f)}";
        }

        // The same derivation the Meta Editor's canvas uses: a prop's size is its own pixel
        // size times however much the background got scaled. There is no scale field in the
        // catalog on purpose (D-015), so this formula IS the size, and every place that
        // draws a prop must compute it identically or a preview lies about where the thing
        // will land. One method rather than two call sites for exactly that reason.
        private float PropScale(MetaLocation location) =>
            background.rectTransform.rect.width / location.BackgroundSprite.rect.width;

        // The shop's preview: the prop drawn where it WILL stand, at the size it will be,
        // only translucent. It goes through CreateProp rather than laying itself out, so the
        // ghost cannot disagree with the real thing -- a preview that lies about the
        // position is worse than no preview.
        public void ShowGhost(MetaItemDefinition item)
        {
            // Destroys the old ghost WITHOUT restoring the framing, unlike ClearGhost. That
            // difference is load-bearing since the travel became a tween (D-033): restoring
            // first would mean the "before" framing is re-captured below from a position
            // halfway through the return tween, and cancelling would then send the map
            // somewhere it had never been. Walking from one row to the next goes straight to
            // the new frame; only a real exit restores.
            DestroyGhostObject();

            var location = ViewedLocation;
            if (item == null || item.Sprite == null || location?.BackgroundSprite == null) return;

            // Created last, so it is the last sibling and draws over every owned prop. A
            // ghost hidden behind the props already standing there would read as nothing
            // having happened.
            ghost = CreateProp(item, PropScale(location));
            ghost.name = $"Ghost_{item.Id}";

            var image = ghost.GetComponent<Image>();
            var color = image.color;
            image.color = new Color(color.r, color.g, color.b, ghostAlpha);

            FocusOn((RectTransform)ghost.transform);
        }

        // Frames the previewed prop: zooms toward it and slides the map so it lands where
        // the confirm popup can sit above it. Inside ShowGhost rather than a public method
        // of its own -- the shop says "preview this", and how the map is framed is the map's
        // business. Keeping it private also keeps D-031's single-door property intact.
        //
        // The zoom is NOT a special case per prop. It is the zoom that would make the prop
        // fill `previewFill` of the viewport, clamped to [1, previewMaxZoom]. A trash bin is
        // a small fraction of the map, so the ratio is large and it zooms to the ceiling. A
        // fence as wide as the grounds needs a ratio BELOW one to fit, so the clamp holds it
        // at 1x and the map does not move -- which is the user's own observation ("trashbine
        // zoom yapılır da fencelere yapılamaz") falling out of the arithmetic rather than
        // being written in as an exception. A prop of some middling size added tomorrow gets
        // a sensible number without anyone authoring it.
        private void FocusOn(RectTransform propRect)
        {
            var content = background.rectTransform;
            var viewport = scroll.viewport as RectTransform ?? content.parent as RectTransform;
            if (viewport == null) return;

            content.DOKill();

            // Captured only the FIRST time, so hopping from row to row keeps pointing at the
            // frame the player actually arrived from. Re-capturing on every preview would
            // record a position from the middle of a tween.
            if (!focused)
            {
                preFocusContentPosition = content.anchoredPosition;
                preFocusContentScale = content.localScale;
                focused = true;
            }

            // Panning is off while the player is deciding, which is the honest behaviour --
            // they are answering a question, not exploring. It also keeps the ScrollRect's
            // own bounds arithmetic out of a fight with a scaled content rect, and stops a
            // zoomed map from being unreachable sideways (horizontal scrolling is off).
            scroll.enabled = false;

            var viewportSize = viewport.rect.size;
            var propSize = propRect.rect.size;

            var zoom = 1f;
            if (propSize.x > 0f && propSize.y > 0f)
            {
                zoom = Mathf.Min(
                    viewportSize.x * previewFill / propSize.x,
                    viewportSize.y * previewFill / propSize.y);
            }

            zoom = Mathf.Clamp(zoom, 1f, previewMaxZoom);

            // --- The destination is MEASURED, not derived. ---
            //
            // The framing below is a world-space delta: where the ghost is versus where it
            // should be. That can only be read once the scale is applied, which is exactly
            // what a tween cannot do -- so the map is put at its destination for an instant,
            // everything needed is measured there, and it is snapped back before anything
            // renders. The alternative was going back to pivot/anchor arithmetic, which
            // D-032 rejected: the background is width-stretched with a top pivot, and
            // hand-rolled maths keeps working right up until one of those changes, then
            // focuses the wrong spot silently.
            var startScale = content.localScale;
            var startPosition = content.anchoredPosition;

            content.localScale = new Vector3(zoom, zoom, 1f);

            // Without this the corners below are still measured at the OLD scale, one frame
            // stale, and the framing lands off by exactly the zoom factor. Same trap as the
            // measure-before-layout one in Refresh.
            Canvas.ForceUpdateCanvases();

            var viewportCorners = new Vector3[4];
            viewport.GetWorldCorners(viewportCorners);
            var target = new Vector3(
                (viewportCorners[0].x + viewportCorners[2].x) * 0.5f,
                Mathf.Lerp(viewportCorners[0].y, viewportCorners[1].y, previewFocusHeight),
                content.position.z);

            content.position += target - PropWorldCentre(propRect);

            // Read here, at the destination: the SETTLED edges of the ghost. This is why the
            // shop needs no change and no timing dependency on this animation -- it places
            // the popup once, at the position the prop is travelling to, and the map slides
            // in underneath it. Repositioning the popup every frame, or delaying it until
            // the tween finished, would tie the shop to the map's animation length.
            var settledCorners = new Vector3[4];
            propRect.GetWorldCorners(settledCorners);
            var settledMidX = (settledCorners[1].x + settledCorners[2].x) * 0.5f;
            settledGhostTop = new Vector3(settledMidX, settledCorners[1].y, settledCorners[1].z);
            settledGhostBottom = new Vector3(settledMidX, settledCorners[0].y, settledCorners[0].z);

            var endScale = content.localScale;
            var endPosition = content.anchoredPosition;

            content.localScale = startScale;
            content.anchoredPosition = startPosition;

            content.DOScale(endScale, previewTravelSeconds).SetEase(previewTravelEase);
            content.DOAnchorPos(endPosition, previewTravelSeconds).SetEase(previewTravelEase);
        }

        private static Vector3 PropWorldCentre(RectTransform rect)
        {
            var corners = new Vector3[4];
            rect.GetWorldCorners(corners);
            return (corners[0] + corners[2]) * 0.5f;
        }

        // Where the confirm popup should hang. Both edges, so the shop can put the box above
        // the prop and fall back to below it when the prop is near the top of the screen.
        // Returns false when nothing is being previewed, which is the shop's cue to leave
        // the box where it is rather than move it somewhere meaningless.
        // Reports the SETTLED edges -- where the prop will be once the map finishes
        // travelling -- not where it happens to be this frame. Measured at the destination
        // inside FocusOn. Reading them live would put the popup wherever the prop was
        // mid-tween and then leave it there.
        public bool TryGetGhostBounds(out Vector3 worldTopCentre, out Vector3 worldBottomCentre)
        {
            worldTopCentre = settledGhostTop;
            worldBottomCentre = settledGhostBottom;
            return ghost != null;
        }

        // Safe to call in any state, and called from every exit the shop has. That is the
        // point: the ghost's one owner is MetaShopView's state machine, and this is the one
        // door out. The first shop's ghost could leak because more than one thing decided
        // when it went away (D-024).
        public void ClearGhost()
        {
            DestroyGhostObject();
            RestoreFocus();
        }

        // Split out from ClearGhost so that ShowGhost can replace the ghost WITHOUT giving
        // the framing back -- see the comment there for why that matters now that the travel
        // is animated.
        private void DestroyGhostObject()
        {
            if (ghost != null) Destroy(ghost);
            ghost = null;
        }

        // Travels the map back to where it was. Guarded by `focused` rather than by comparing
        // values, because a preview that happened to open on an unzoomed prop moved nothing
        // and must still hand scrolling back.
        private void RestoreFocus()
        {
            if (!focused) return;
            focused = false;

            var content = background.rectTransform;
            content.DOKill();

            content.DOScale(preFocusContentScale, previewTravelSeconds).SetEase(previewTravelEase);
            content.DOAnchorPos(preFocusContentPosition, previewTravelSeconds)
                .SetEase(previewTravelEase)
                // Scrolling comes back only when the map has ARRIVED. The ScrollRect is
                // Clamped and drags the content toward its bounds every LateUpdate, so
                // re-enabling it mid-tween would have the two of them pulling at the same
                // value. OnComplete does not fire on a DOKill, which is why the tween is not
                // the only thing responsible for that flag -- FocusOn turns it off again
                // on its own, so an interrupted return leaves a coherent state either way.
                .OnComplete(() =>
                {
                    if (scroll != null && !focused) scroll.enabled = true;
                });
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

            // Nothing here is clickable. Whatever shop replaces the reverted one may want
            // props tappable, but a raycast target that does nothing today would swallow
            // drags meant for the scroll view.
            image.raycastTarget = false;

            return prop;
        }

        private void DrawLocationBar(MetaLocation location, int currentDayIndex)
        {
            if (locationLabel != null)
            {
                locationLabel.text = string.IsNullOrWhiteSpace(location.DisplayName)
                    ? location.Id
                    : location.DisplayName;
            }

            if (previousButton != null) previousButton.interactable = viewedIndex > 0;
            if (nextButton != null) nextButton.interactable = viewedIndex < unlockedLocations.Count - 1;

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
            propsByItem.Clear();
            upcomingProp = null;
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

            // A tween outliving the transform it drives is DOTween's classic null on scene
            // change. Both meta screens are torn down by a scene load, so this is reachable
            // in normal play, not just in the Editor.
            if (background != null) background.rectTransform.DOKill();
        }

        private void OnPrevious() => Step(-1);
        private void OnNext() => Step(1);

        // Every field is wired by the editor step, so a missing one should name itself
        // rather than surface as a NullReferenceException three frames later. Same pattern
        // MainScreenView and the two popups use.
        private bool ValidateReferences()
        {
            // Only what the screen cannot draw anything without. The location bar's four
            // references are deliberately absent from this list -- see their header.
            var missing = new List<string>();
            if (sessionHost == null) missing.Add(nameof(sessionHost));
            if (catalog == null) missing.Add(nameof(catalog));
            if (scroll == null) missing.Add(nameof(scroll));
            if (background == null) missing.Add(nameof(background));

            if (missing.Count == 0) return true;

            Debug.LogError(
                $"{nameof(MetaGroundsView)} on '{name}' is missing Inspector reference(s): {string.Join(", ", missing)}. " +
                "Run ExpoTheExplorer > Meta > Build Meta Grounds.", this);
            return false;
        }
    }
}
