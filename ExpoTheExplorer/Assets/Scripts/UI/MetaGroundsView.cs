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

        // A purchased prop is PLACED rather than simply present: it drops the last stretch
        // into its spot and the ground takes the hit. Every number here is feel, so it is
        // serialized next to the other feel on this component and NOT in the catalog -- D-015
        // keeps MetaCatalog to price/position/art/unlock, and "how hard the map shakes" is
        // none of those. A prop authored tomorrow needs no new content for this to work.
        [Header("Purchase placement")]
        [Tooltip("How far above its spot a just-bought prop starts, in the grounds' own units (the same space as the upcoming label's offset). Small on purpose: this is a thing being set down, not dropped off a roof.")]
        [SerializeField, Min(0f)] private float placementDropHeight = 48f;

        [Tooltip("How long the drop takes. The fade in runs over the same stretch, so the prop is solid exactly as it lands.")]
        [SerializeField, Min(0.05f)] private float placementDropSeconds = 0.32f;

        [Tooltip("Easing for the drop. An IN ease accelerates downward, which is what makes the shake at the end read as an impact rather than as the map twitching.")]
        [SerializeField] private Ease placementDropEase = Ease.InCubic;

        [Tooltip("How long the ground shakes after the prop lands. 0 turns the shake off and leaves the drop.")]
        [SerializeField, Min(0f)] private float placementShakeSeconds = 0.22f;

        [Tooltip("How far the map moves at the worst of the shake, in the grounds' own units. Keep it small — this is a confirmation, not a screen shake.")]
        [SerializeField, Min(0f)] private float placementShakeStrength = 9f;

        [Tooltip("How many times the map swings before it settles. The swing damps to nothing across the duration, so a higher count reads as a buzz and a lower one as a single thud.")]
        [SerializeField, Min(1f)] private float placementShakeOscillations = 2f;

        private readonly List<GameObject> spawnedProps = new();

        // The same objects as spawnedProps, keyed so the celebration can find the ONE prop it
        // is about to reveal and hide it while an animated copy takes its place. Rebuilt with
        // the props, so it never outlives them.
        private readonly Dictionary<MetaItemDefinition, GameObject> propsByItem = new();

        // The upcoming-prop silhouette DrawUpcoming last drew, or null when nothing is
        // coming. Lives and dies with the other props, so it is cleared alongside them.
        private GameObject upcomingProp;

        // How many art layers sit in front of the props in sibling order (D-046). Props start
        // after these, so any sibling-index arithmetic has to add it -- getting that wrong
        // silently would be the same class of bug D-045 just fixed.
        private int artLayerCount;

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

        // The running placement animation, HELD so it can be stopped. It animates a prop and
        // the map's position, and both of those can be pulled out from under it: a second
        // purchase, or walking to another location, calls Refresh, which destroys every prop.
        // Clear() stops this for exactly that reason -- a coroutine driving a destroyed
        // RectTransform is the one failure this feature can produce, and it is not one the
        // player could be shown an error about.
        private Coroutine placementRoutine;

        // True from the moment a purchase is handed over until the placement gives the map
        // back. While it is up, the framing belongs to the placement and RestoreFocus does
        // nothing -- which is what keeps the player looking at the prop they just bought
        // instead of being pulled back out the instant the shop closed (D-049).
        //
        // A flag rather than a parameter threaded through the shop's SetOpen/ClosePreview: the
        // question "may the zoom be given back right now" is this class's to answer, and it is
        // answered in the one method that gives it back, next to the `focused` guard that is
        // already there for the same kind of reason.
        private bool placing;

        // Bumped every time the framing is claimed. A placement's finally hands the map back
        // only if this still matches the number it started with -- which is how "am I still
        // the one who owns this" gets answered without the finally having to know WHO
        // interrupted it. Without it, a second purchase would have the first placement's
        // teardown travel the map out from under the second one's drop.
        private int placementGeneration;

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

        // What the shop calls instead of Refresh once a purchase has gone through. ONE method
        // rather than letting the shop call Refresh and then an animate method: the animation
        // has to prepare the prop in the SAME FRAME the prop is drawn (see below), so the two
        // halves are order-dependent, and an order-dependent pair of public methods is a pair
        // that is eventually called in the wrong order. Composing them here means the shop
        // says what happened -- "this was bought" -- and the grounds decide what that looks
        // like, which is the division of labour the rest of this class keeps.
        public void RefreshAfterPurchase(MetaItemDefinition purchased)
        {
            // Claimed BEFORE the redraw, and that order is the whole trick (D-049). The shop's
            // exit path has already run by now -- SetOpen(false) closed the panel and went
            // through ClearGhost, whose second half is RestoreFocus -- and Refresh below
            // clears the ghost again for its own reason. Both of those hand the zoom back,
            // which is exactly what must NOT happen yet: the player is meant to watch the
            // prop land at the size they were previewing it at. So the placement takes
            // ownership of the framing here and RestoreFocus goes quiet until it gives it up.
            //
            // Nothing in MetaShopView had to change for that. Its state machine still has one
            // door and that door still calls ClearGhost; the call simply becomes a no-op on
            // the framing half while a placement owns it. A parameter threaded through
            // SetOpen and ClosePreview would have put the same decision in the shop, where it
            // is not the shop's to make.
            placing = true;

            // Claimed BEFORE Refresh, because Refresh is what stops any placement already
            // running (through Clear). Bumping first is what tells that one's finally it is no
            // longer the owner, so it tears down its prop without travelling the map out from
            // under this purchase's drop.
            var generation = ++placementGeneration;

            Refresh();

            // A celebration owns the framing and the props while it runs (it hides one and
            // animates a copy), so a placement on top of it would be two animations arguing
            // over the same map. Unreachable today -- the celebration puts a full-screen
            // catcher over everything, so the confirm popup cannot be tapped while one is
            // playing -- and guarded anyway, because "unreachable" is a property of today's
            // scene rather than of this code.
            //
            // Every early exit hands the framing straight back. A map left parked on a zoom
            // that nobody owns is the one failure this feature can produce that the player
            // cannot get out of -- horizontal scrolling is off, so they would be stuck
            // looking at a fraction of their own grounds.
            if (celebrating || purchased == null)
            {
                ReleaseFraming();
                return;
            }

            if (!propsByItem.TryGetValue(purchased, out var prop) || prop == null)
            {
                // Not an error and not silent-by-accident: the purchase is already complete
                // and saved, so the honest outcome is the prop simply being there. This is
                // reachable if a bought prop is not among the ACTIVE items for some reason
                // the rules layer decides -- MetaResolver owns that call, not this method.
                ReleaseFraming();
                return;
            }

            placementRoutine = StartCoroutine(PlacePurchasedProp(prop, generation));
        }

        // Ends a placement WITHOUT letting it travel the map home, and hands the framing back
        // by snapping instead. For the caller that is walking to another location: the framing
        // being restored belonged to grounds the player is leaving, and an animated return
        // would still be moving when the new location's scroll position is set a line later --
        // the tween would win, and the player would arrive somewhere they never asked for. The
        // snap is invisible there because the whole map is being replaced in the same frame.
        private void CancelPlacement()
        {
            // Bumped first, so the routine's finally knows it no longer owns the framing and
            // leaves the hand-back to the code below.
            placementGeneration++;

            if (placementRoutine != null)
            {
                StopCoroutine(placementRoutine);
                placementRoutine = null;
            }

            placing = false;
            RestoreFocus(animated: false);
        }

        // Gives the zoom back and travels the map out. THE one way a placement ends, whether
        // it finished, was never started, or was stopped halfway -- so "when does the map
        // come back" has a single answer, the same property D-031 gave the ghost.
        private void ReleaseFraming()
        {
            placing = false;
            RestoreFocus();
        }

        // The purchase payoff: the prop falls the last stretch into its spot, fading in as it
        // goes, and the ground takes the hit when it lands.
        //
        // Animated with a frame loop and Time.unscaledDeltaTime rather than a tween, which is
        // the same choice CelebrateOne made and for the same two reasons: the try/finally can
        // then guarantee the finished state whatever interrupts it, and there is no tween left
        // pointing at a RectTransform that Refresh is about to destroy.
        private IEnumerator PlacePurchasedProp(GameObject prop, int generation)
        {
            var rect = (RectTransform)prop.transform;
            var image = prop.GetComponent<Image>();
            var content = background.rectTransform;

            // Where the prop BELONGS, read before anything moves it. Not assumed to be zero
            // even though CreateProp leaves it there: the anchors carry the authored position
            // (K5), and the day that changes this reads the truth instead of a constant.
            var landed = rect.anchoredPosition;
            var solid = image.color;

            // Prepared in the SAME FRAME Refresh drew it, before a single frame is yielded.
            // This is the load-bearing line of the whole method: DrawProps draws a bought prop
            // solid and in place, so waiting even one frame would show it standing there and
            // then jerk it back up into the air.
            //
            // The drop is measured in the CONTENT's own space, so it grows with the preview
            // zoom -- and it should: the prop is drawn that much bigger too, so the fall stays
            // the same fraction of the prop's own height whatever the map is magnified to. The
            // shake below is the opposite case, for the opposite reason; see there.
            rect.anchoredPosition = landed + new Vector2(0f, placementDropHeight);
            image.color = new Color(solid.r, solid.g, solid.b, 0f);

            var shakeBase = Vector2.zero;
            var shaking = false;

            try
            {
                // Normally not a single frame of waiting: the map has been parked on this prop
                // since the row was tapped, and D-049 keeps it there for the whole placement.
                // The one case this covers is a player who taps BUY before the travel INTO the
                // zoom has finished -- fast enough is fast enough -- and a drop played against
                // a moving background reads as one confused motion instead of two clear ones.
                //
                // Asked of DOTween rather than timed against previewTravelSeconds, because
                // only DOTween knows how much of that travel is left. A fixed wait would be
                // too long in the normal case and the wrong length in this one.
                while (DOTween.IsTweening(content)) yield return null;

                var elapsed = 0f;
                while (elapsed < placementDropSeconds)
                {
                    elapsed += Time.unscaledDeltaTime;
                    var t = Mathf.Clamp01(elapsed / placementDropSeconds);

                    // Position is eased and the fade is LINEAR, on purpose. The ease is what
                    // makes the fall accelerate; running the alpha through it too would keep
                    // the prop nearly invisible for most of the drop and then pop it in at
                    // the end, which reads as an appearing prop rather than a falling one.
                    var height = DOVirtual.EasedValue(placementDropHeight, 0f, t, placementDropEase);
                    rect.anchoredPosition = landed + new Vector2(0f, height);

                    // Lerped toward the prop's OWN alpha rather than to 1. CreateProp leaves
                    // props opaque today, so the two are the same number -- but a translucent
                    // prop would otherwise be faded up to solid by this animation and stay
                    // wrong until the next Refresh redrew it.
                    image.color = new Color(solid.r, solid.g, solid.b, Mathf.Lerp(0f, solid.a, t));

                    yield return null;
                }

                rect.anchoredPosition = landed;
                image.color = solid;

                if (placementShakeSeconds <= 0f || placementShakeStrength <= 0f) yield break;

                // The map is shaken by moving the CONTENT, not the viewport. Moving the
                // viewport would move the mask window and open a sliver of empty screen at
                // the edges; the content carries D-046's oversized continuation layer as a
                // child, which is exactly the art that keeps the edges covered while it
                // moves.
                //
                // The offset is the content's anchoredPosition, which lives in the VIEWPORT's
                // space and is therefore untouched by the zoom -- so the shake is a fixed
                // number of screen units however far the map is magnified. That is the right
                // answer here and the opposite of the drop's, and for the same principle: a
                // camera shake is about the screen, a falling prop is about the prop.
                //
                // Nothing here touches the ScrollRect. It has been off since FocusOn (D-032)
                // and RestoreFocus is what turns it back on, once the map has travelled out
                // at the end of this method -- so the whole placement runs inside a window
                // where scrolling is already suspended. An earlier version disabled and
                // re-enabled it around the shake; that was dead code the moment D-049 kept
                // the framing, and dead code that claims to own a flag is worse than none.
                shakeBase = content.anchoredPosition;
                shaking = true;

                var shaken = 0f;
                while (shaken < placementShakeSeconds)
                {
                    shaken += Time.unscaledDeltaTime;
                    var t = Mathf.Clamp01(shaken / placementShakeSeconds);

                    // A damped swing rather than random jitter per frame: an impact has a
                    // direction, and noise at 60fps reads as a glitch. Negative first, so the
                    // ground gives way UNDER the prop that just hit it, and the amplitude
                    // falls linearly to nothing so the last frame is already home.
                    var swing = -Mathf.Sin(t * placementShakeOscillations * 2f * Mathf.PI);
                    content.anchoredPosition =
                        shakeBase + new Vector2(0f, swing * placementShakeStrength * (1f - t));

                    yield return null;
                }
            }
            finally
            {
                // Whatever ended this -- the loops finishing, Clear stopping it, the screen
                // being torn down -- the prop is standing where it belongs at full strength
                // and the map is where it was. Null-checked because the most likely stopper
                // is Refresh, which destroys the prop before this runs.
                if (prop != null)
                {
                    rect.anchoredPosition = landed;
                    image.color = solid;
                }

                // Before the framing is released, not after: RestoreFocus tweens FROM wherever
                // the content is standing, so a shake offset left in place would be baked into
                // the start of the journey home.
                if (shaking && content != null) content.anchoredPosition = shakeBase;

                placementRoutine = null;

                // And now the map travels out (D-049) -- but only if this placement is still
                // the one that owns the framing. This is the LAST line for a reason: it is the
                // payoff of holding the zoom through the drop, and putting it in the finally
                // rather than after the shake loop is what makes it happen on the interrupted
                // paths too -- the screen being torn down owes the player their whole map back
                // just as much as a drop that finished.
                //
                // The generation check is what keeps that from being too eager. Something that
                // claimed the framing while this was running -- a second purchase, or a step
                // to another location -- has its own plan for it, and a teardown that travelled
                // the map home anyway would undo that plan from a coroutine nobody is looking
                // at any more.
                if (generation == placementGeneration) ReleaseFraming();
            }
        }

        private void DrawBackground(MetaLocation location)
        {
            // The grounds Image is a CONTAINER from here on, never the art (D-046). It keeps
            // being the ScrollRect's content, the zoom target and the props' parent -- what it
            // stops being is a thing that draws. The art moved into a child so that the
            // continuation layer can sit BEHIND it: a child always renders over its parent's
            // own graphic, so a parent that draws cannot have anything behind it.
            //
            // Done this way rather than by inserting a real container object into the
            // hierarchy, which would have meant a new serialized reference and another
            // "delete the root and rebuild" round (D-036).
            background.sprite = null;
            background.enabled = false;

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
            var scale = PropScale(location);
            DrawBackgroundLayers(location, scale);

            var active = MetaResolver.ActiveItems(location, ownedKeys, currentDayIndex);

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
        // NOTE this returns a SIBLING index, so it starts after the art layers (D-046) rather
        // than at zero. Forgetting that shift would place every ghost one or two slots too
        // early -- behind the grounds art, invisible -- which is the same silent-depth defect
        // D-045 was written about.
        private int SortedInsertIndex(
            MetaLocation location, List<MetaItemDefinition> active, MetaItemDefinition item)
        {
            var itemAuthored = AuthoredIndex(location, item);
            var index = artLayerCount;

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

            // Through Detach, like every other child of the grounds (D-051): the animated copy
            // lives under the background too, so leaving its corpse in the child list for the
            // rest of the frame would shift the next absolute sibling index by one. The
            // celebration reveals props one after another, so there IS a next one.
            Detach(silhouette);
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

        // The two layers that stand in for the container's own Image (D-046), created before
        // any prop so they end up behind all of them:
        //   [0] the continuation art, larger and centred, when one is authored
        //   [1] the grounds themselves, filling the container exactly
        // Counted, because SortedInsertIndex works in sibling indices and these sit in front
        // of every prop's index.
        private void DrawBackgroundLayers(MetaLocation location, float scale)
        {
            if (location.BackgroundBgSprite != null)
            {
                // CENTRED, with no authored offset: the contract on that field is that the
                // extension grows outward equally on all four sides. Sized from its own
                // pixels times the SAME scale the grounds use, so one number decides how big
                // everything on this screen is -- a second scale here is how a continuation
                // layer would start drifting away from the art it continues.
                var continuation = CreateArtLayer("BackgroundContinuation", location.BackgroundBgSprite);
                var rect = (RectTransform)continuation.transform;
                rect.anchorMin = new Vector2(0.5f, 0.5f);
                rect.anchorMax = new Vector2(0.5f, 0.5f);
                rect.pivot = new Vector2(0.5f, 0.5f);
                rect.anchoredPosition = Vector2.zero;
                rect.sizeDelta = location.BackgroundBgSprite.rect.size * scale;
            }

            // Stretched over the container rather than sized from the sprite, so the rect the
            // props' normalized positions are fractions of stays exactly the container's --
            // the thing every prop position in the catalog was authored against.
            var grounds = CreateArtLayer("BackgroundArt", location.BackgroundSprite);
            var groundsRect = (RectTransform)grounds.transform;
            groundsRect.anchorMin = Vector2.zero;
            groundsRect.anchorMax = Vector2.one;
            groundsRect.pivot = new Vector2(0.5f, 0.5f);
            groundsRect.offsetMin = Vector2.zero;
            groundsRect.offsetMax = Vector2.zero;
        }

        private GameObject CreateArtLayer(string name, Sprite sprite)
        {
            var layer = new GameObject(name, typeof(RectTransform), typeof(Image));
            layer.transform.SetParent(background.rectTransform, worldPositionStays: false);

            var image = layer.GetComponent<Image>();
            image.sprite = sprite;
            // Backdrops swallow nothing: the ScrollRect's own drag handling lives on the
            // content, and a raycast target here would sit in front of it.
            image.raycastTarget = false;

            spawnedProps.Add(layer);
            artLayerCount++;
            return layer;
        }

        // The same derivation the Meta Editor's canvas uses: a prop's size is its own pixel
        // size times however much the background got scaled. There is no scale field in the
        // catalog on purpose (D-015), so this formula IS the size, and every place that
        // draws a prop must compute it identically or a preview lies about where the thing
        // will land. One method rather than two call sites for exactly that reason.
        private float PropScale(MetaLocation location) =>
            MetaLayout.PropScale(background.rectTransform.rect.width, location.BackgroundSprite);

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
            if (ghost != null) Detach(ghost);
            ghost = null;
        }

        // THE ONLY WAY ANYTHING UNDER THE GROUNDS IS DESTROYED, and the SetParent is the whole
        // point of it (D-051).
        //
        // Destroy is DEFERRED to the end of the frame, and until then the object is still a
        // child of the background. A redraw destroys the old art layers, props, silhouette and
        // ghost and then immediately builds the new ones, so for the rest of that frame the
        // parent carries BOTH sets. That is invisible to anything that only appends -- the new
        // props keep their relative order -- but it is fatal to DrawUpcoming, which moves its
        // silhouette to an ABSOLUTE sibling index computed as though the dead children were
        // already gone. On the purchase that surfaced this, the dead block was 13 children and
        // the computed index was 12: the silhouette landed inside the corpses, BEHIND the new
        // background art, and vanished. It looked like the prop had been deleted.
        //
        // SetParent(null) removes it from the child list on THIS line, so every index computed
        // afterwards counts only what is really there. Destroy still runs; this only takes the
        // object out of the hierarchy first. DestroyImmediate would also work and is the wrong
        // tool at run time.
        //
        // The cost is a layout dirty flag per object on an object about to die, on an event
        // that already rebuilds the whole screen.
        private static void Detach(GameObject spawned)
        {
            spawned.transform.SetParent(null, worldPositionStays: false);
            Destroy(spawned);
        }

        // Travels the map back to where it was. Guarded by `focused` rather than by comparing
        // values, because a preview that happened to open on an unzoomed prop moved nothing
        // and must still hand scrolling back.
        private void RestoreFocus() => RestoreFocus(animated: true);

        // `animated: false` snaps instead of travelling, for the caller that is about to set
        // the map's position itself (CancelPlacement, on the way to another location). Same
        // method rather than a second one, because "what giving the framing back MEANS" --
        // which values, the flag, the ScrollRect -- is knowledge that must not exist twice.
        private void RestoreFocus(bool animated)
        {
            // A placement owns the framing while it runs (D-049), so this is a no-op until it
            // is done. Both of the shop's exit calls land here -- SetOpen(false) goes through
            // ClosePreview and ClearGhost, and Refresh clears the ghost again for its own
            // reason -- and both have to be ignored, or the map would be pulled back out from
            // under a prop that has not landed yet. The placement's own finally is what lifts
            // this, through ReleaseFraming, so the suppression cannot outlive it.
            if (placing) return;

            if (!focused) return;
            focused = false;

            var content = background.rectTransform;
            content.DOKill();

            if (!animated)
            {
                content.localScale = preFocusContentScale;
                content.anchoredPosition = preFocusContentPosition;

                // Handed back here rather than by an OnComplete, because there is no tween to
                // complete. The tween path below cannot use this line instead: re-enabling
                // scrolling while the map is still travelling would have the ScrollRect's own
                // clamping pull at a value the tween is writing.
                if (scroll != null) scroll.enabled = true;
                return;
            }

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
            // Through MetaLayout, not inline, since the day scene's backdrop composites the
            // same prop into a texture and the two sizes have to agree. The POSITION stays
            // anchors + pivot, which is the better implementation of the same rule -- an
            // anchored prop rides the background through every resize -- and MetaLayout.PropRect
            // is that rule written out for the backdrop, which has no anchors to lean on.
            rect.sizeDelta = MetaLayout.PropSize(item, scale);

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
            // A placement belongs to the grounds it is standing on, so walking away ends it --
            // and ends it HERE, before the redraw, rather than leaving it to Clear. Clear
            // cannot tell a location change from a second purchase, and the two want opposite
            // things from the framing; this caller knows which one it is.
            CancelPlacement();

            viewedIndex = Mathf.Clamp(viewedIndex + delta, 0, unlockedLocations.Count - 1);
            Refresh();

            // Back to the top: the new location's art is a different height, and leaving the
            // scroll where it was would drop the player into the middle of it.
            if (scroll != null) scroll.verticalNormalizedPosition = 1f;
        }

        private void Clear()
        {
            // BEFORE the props go, not after: the placement animation drives one of them and
            // the map's position, and its finally is what puts the map back. Stopping it here
            // is what makes "the props are gone" and "nothing is still animating them" the
            // same moment -- and this is the single place props are destroyed, so it is the
            // only place that has to know.
            if (placementRoutine != null)
            {
                StopCoroutine(placementRoutine);
                placementRoutine = null;
            }

            foreach (var prop in spawnedProps)
            {
                if (prop != null) Detach(prop);
            }
            spawnedProps.Clear();
            propsByItem.Clear();
            upcomingProp = null;
            artLayerCount = 0;
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
