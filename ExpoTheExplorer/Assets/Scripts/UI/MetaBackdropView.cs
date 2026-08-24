using System.Collections.Generic;
using ExpoTheExplorer.Data;
using ExpoTheExplorer.Session;
using ExpoTheExplorer.Systems.MetaSystem;
using UnityEngine;
using UnityEngine.UI;

namespace ExpoTheExplorer.UI
{
    // The day is played AT the expo the player has been decorating: this draws their grounds
    // -- the location's art with the props they own standing on it -- blurred, behind the
    // board. It is the only place the meta side reaches into the day scene, and it reaches in
    // one direction only: it reads the catalog and the owned set and writes nothing.
    //
    // BUILT ONCE, AT DAY START. Composition and blur happen in Start and never again; what
    // remains is a small texture on a RawImage, which costs a draw call and nothing else. So a
    // purchase made today shows up in the backdrop of the NEXT day the player opens, not
    // halfway through the current one -- correct, because the grounds are not something they
    // can change while a day is running.
    //
    // NO CUSTOM SHADER. The blur is progressive halving through Graphics.Blit: a bilinear
    // sample at half size lands exactly between four source texels, so each step is an exact
    // four-texel box average and N steps compound into a wide blur. That matters here because
    // the project runs the BUILT-IN pipeline -- the URP package is installed but no pipeline
    // asset is assigned in GraphicsSettings -- so URP's renderer features are not available,
    // and the alternative would have been a hand-written multi-tap shader running full screen
    // every frame for an image that never changes.
    //
    // It decides nothing about the meta rules. WHICH location and WHAT is standing on it come
    // from MetaResolver, the same two calls the meta screen makes (D-019), so the backdrop can
    // never show a different expo than the one the player walks around in.
    [RequireComponent(typeof(RawImage))]
    public class MetaBackdropView : MonoBehaviour
    {
        [Header("Bound by ExpoTheExplorer > Meta > Build Day Backdrop")]
        [Tooltip("Who the player is: which day they are on and which props they own. In the day scene this is the GameManager. Read only — nothing here writes to it.")]
        [SerializeField] private SessionHost sessionHost;

        [Tooltip("The single authority for locations, props and where they stand (D-015). Same asset the meta screen reads.")]
        [SerializeField] private MetaCatalog catalog;

        [Tooltip("Where the finished backdrop is drawn. A RawImage rather than an Image because the result is a RenderTexture, and a RenderTexture is not a sprite.")]
        [SerializeField] private RawImage target;

        [Header("Look")]
        [Tooltip("What fraction of the background art's own resolution the grounds are composed at. Small on purpose: the result is about to be blurred into soft shapes, so composing at full size would spend memory on detail that is thrown away in the next step.")]
        [SerializeField, Range(0.05f, 1f)] private float composeScale = 0.25f;

        [Tooltip("How many times the composed image is halved. Each step doubles the blur radius, so this is a coarse dial — 4 is a heavy, unreadable-by-design blur.")]
        [SerializeField, Range(0, 8)] private int blurSteps = 4;

        [Tooltip("Multiplied into the backdrop. Darkened by default so the board and the tickets stay the brightest things on screen — a backdrop that competes with the game is a backdrop that hurts.")]
        [SerializeField] private Color tint = new(0.55f, 0.55f, 0.6f, 1f);

        // Owned, and therefore released. A RenderTexture is not garbage collected like a
        // managed object -- the graphics memory behind it is held until Release, and a scene
        // reloaded a few times would otherwise leak one backdrop per load.
        private RenderTexture backdrop;

        // Start, not Awake, and for the reason the HUD's views already record: the session is
        // assigned in its host's Awake and Unity does not order Awake across GameObjects.
        private void Start()
        {
            if (target == null) target = GetComponent<RawImage>();

            // Every failure below leaves the RawImage EXACTLY as the author left it. That is
            // the fallback: whatever texture is on it in the scene stays, so a missing catalog
            // or an empty save shows the authored background rather than a blank screen. There
            // is deliberately no "fallback texture" field -- a second place to author the same
            // thing is a second place for it to be wrong.
            if (!Validate()) return;

            var session = sessionHost.Session;
            if (session == null)
            {
                Debug.LogWarning(
                    $"{nameof(MetaBackdropView)} on '{name}' ran before the session existed, so the authored " +
                    "background stays. Its host assigns Session in Awake; this reads it in Start.", this);
                return;
            }

            var day = session.State.CurrentDayIndex;

            // The NEWEST unlocked location -- the one the player is currently working on.
            // Derived from the day index rather than saved: which location is being looked at
            // is UI state on the meta screen too (K6-4), and persisting it here would mean a
            // profile field and a schema version for a backdrop.
            var unlocked = MetaResolver.UnlockedLocations(catalog, day);
            if (unlocked.Count == 0 || unlocked[unlocked.Count - 1]?.BackgroundSprite == null)
            {
                // MetaCatalogValidator reports "no location unlocks at Day 0" as a content
                // error, so this is not a state to design a screen for -- it is a catalog bug,
                // and the authored background is the honest thing to show meanwhile.
                Debug.LogWarning(
                    $"{nameof(MetaBackdropView)}: no unlocked location with a background at Day {day}. " +
                    "The authored background stays.", this);
                return;
            }

            var location = unlocked[unlocked.Count - 1];
            var composed = Compose(location, session.OwnedMetaItemIds, day);
            if (composed == null) return;

            backdrop = Blur(composed);
            RenderTexture.ReleaseTemporary(composed);

            target.texture = backdrop;
            target.color = tint;
        }

        private void OnDestroy()
        {
            if (backdrop == null) return;

            // Cleared off the RawImage first: a RawImage pointing at a released texture draws
            // whatever the driver leaves in that memory, which is a class of glitch that is
            // miserable to trace back to a teardown.
            if (target != null && target.texture == backdrop) target.texture = null;

            backdrop.Release();
            Destroy(backdrop);
            backdrop = null;
        }

        // Draws the location's grounds and everything standing on them into one texture, in
        // the catalog's own coordinate space. The props go through MetaResolver.ActiveItems --
        // the SAME call MetaGroundsView makes -- so "what is standing here" has one answer for
        // both screens, and through MetaLayout so it lands in the same place at the same size.
        private RenderTexture Compose(MetaLocation location, ISet<string> owned, int day)
        {
            var backgroundSprite = location.BackgroundSprite;

            var width = Mathf.Max(1, Mathf.RoundToInt(backgroundSprite.rect.width * composeScale));
            var height = Mathf.Max(1, Mathf.RoundToInt(backgroundSprite.rect.height * composeScale));

            var rt = RenderTexture.GetTemporary(width, height, 0);
            rt.filterMode = FilterMode.Bilinear;

            var previous = RenderTexture.active;
            RenderTexture.active = rt;

            // Cleared to the background's own art rather than to a colour: every pixel is
            // covered by the blit below, and clearing first is what keeps a location whose art
            // has transparency from compositing against last frame's leftovers.
            GL.Clear(true, true, Color.clear);

            GL.PushMatrix();

            // Pixel space with the origin at the TOP-left, which is what Graphics.DrawTexture
            // expects. The catalog's positions are bottom-up (NormalizedPosition.y = 0 is the
            // ground), so every rect from MetaLayout gets flipped on its way in -- see Flip.
            GL.LoadPixelMatrix(0f, width, height, 0f);

            var area = new Vector2(width, height);
            DrawSprite(backgroundSprite, new Rect(0f, 0f, width, height));

            // The scale is asked of MetaLayout rather than assumed to be composeScale, even
            // though it works out to it: the rounding above means the texture is not exactly
            // composeScale of the art, and a prop placed with the unrounded number would drift
            // by a pixel or two from where the meta screen draws it.
            var scale = MetaLayout.PropScale(width, backgroundSprite);

            // ActiveItems already returns draw order (ascending SortOrder, ties by authored
            // order), so drawing in sequence gives depth for free -- the same property
            // MetaGroundsView gets from sibling index.
            foreach (var item in MetaResolver.ActiveItems(location, owned, day))
            {
                if (item?.Sprite == null) continue;
                DrawSprite(item.Sprite, Flip(MetaLayout.PropRect(item, area, scale), height));
            }

            GL.PopMatrix();
            RenderTexture.active = previous;

            return rt;
        }

        // Bottom-up (the catalog's convention, and MetaLayout's) to top-down (the drawing
        // surface's). One function so the conversion cannot be half-applied.
        private static Rect Flip(Rect rect, float height) =>
            new(rect.x, height - rect.y - rect.height, rect.width, rect.height);

        // A sprite may be one region of a packed atlas, so the source rect is the sprite's own
        // textureRect in normalized coordinates -- NOT the whole texture. Drawing the whole
        // texture works right up until the art is packed, and then every prop becomes a
        // collage of its neighbours.
        private static void DrawSprite(Sprite sprite, Rect destination)
        {
            var texture = sprite.texture;
            if (texture == null) return;

            var region = sprite.textureRect;
            var uv = new Rect(
                region.x / texture.width,
                region.y / texture.height,
                region.width / texture.width,
                region.height / texture.height);

            Graphics.DrawTexture(destination, texture, uv, 0, 0, 0, 0);
        }

        // Progressive halving. Each step is an exact four-texel average because a bilinear
        // sample at half resolution falls precisely between four source texels, so this is a
        // real box blur rather than a downscale that merely looks soft -- and it needs no
        // shader, which is what makes it the right answer on the built-in pipeline.
        //
        // The result is left SMALL. Stretching a 60-pixel-wide texture across the screen is
        // itself a bilinear filter, so the upscale finishes the blur for free and the memory
        // held for the rest of the day is a few kilobytes.
        private RenderTexture Blur(RenderTexture source)
        {
            var current = source;

            for (var step = 0; step < blurSteps; step++)
            {
                var width = current.width / 2;
                var height = current.height / 2;

                // Stops at 1x1 rather than looping forever on an integer divide that has
                // bottomed out. A blurSteps set higher than the art can carry is a tuning
                // mistake, not a hang.
                if (width < 1 || height < 1) break;

                var next = RenderTexture.GetTemporary(width, height, 0);
                next.filterMode = FilterMode.Bilinear;
                Graphics.Blit(current, next);

                if (current != source) RenderTexture.ReleaseTemporary(current);
                current = next;
            }

            // Copied out of the temporary pool into a texture this component owns. A temporary
            // handed to a RawImage would be recycled under it the next time anything asks the
            // pool for that size, and the backdrop would start showing someone else's frame.
            var owned = new RenderTexture(current.width, current.height, 0)
            {
                name = $"{nameof(MetaBackdropView)}_Backdrop",
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp,
            };

            Graphics.Blit(current, owned);
            if (current != source) RenderTexture.ReleaseTemporary(current);

            return owned;
        }

        // All three are REQUIRED. A backdrop with no catalog cannot know what the grounds look
        // like, one with no session cannot know which props are owned, and one with no
        // RawImage has nowhere to draw. None has a degraded mode worth shipping -- guessing
        // the first location would put the wrong expo behind the board the day a second one
        // exists, which is the failure D-028 was written about.
        private bool Validate()
        {
            var missing = new List<string>();
            if (sessionHost == null) missing.Add(nameof(sessionHost));
            if (catalog == null) missing.Add(nameof(catalog));
            if (target == null) missing.Add(nameof(target));

            if (missing.Count == 0) return true;

            Debug.LogError(
                $"{nameof(MetaBackdropView)} on '{name}' is missing Inspector reference(s): {string.Join(", ", missing)}. " +
                "Run ExpoTheExplorer > Meta > Build Day Backdrop. The authored background stays meanwhile.", this);
            return false;
        }
    }
}
