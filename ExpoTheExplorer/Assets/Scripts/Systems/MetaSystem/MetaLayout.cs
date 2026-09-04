using ExpoTheExplorer.Data;
using UnityEngine;

namespace ExpoTheExplorer.Systems.MetaSystem
{
    // WHERE A PROP STANDS AND HOW BIG IT IS. One authority, because there are now two things
    // that draw the same prop and they must agree pixel for pixel: MetaGroundsView, which
    // builds it as a RectTransform on the meta screen, and MetaBackdropView, which composites
    // it into the day scene's blurred backdrop. MetaGroundsView's own comment already stated
    // the rule this file exists to keep -- "every place that draws a prop must compute it
    // identically or a preview lies about where the thing will land" -- and it stated it back
    // when there was one such place. Now there are two, so the arithmetic moved here rather
    // than being copied.
    //
    // A prop's size is DERIVED from how wide the background is being drawn, so re-exporting
    // the art at another resolution changes nothing about where anything sits. That
    // derivation is PropScale, and it is the reason this is a function rather than data.
    // MetaItemDefinition.Scale rides on top of it as a per-prop nudge (added 2026-09-04, the
    // one field D-015 left the door open for) -- a multiplier, never a size, which is what
    // keeps the derivation above intact.
    //
    // Pure arithmetic, no Unity objects, no side effects -- so it can be reasoned about (and
    // tested) without a scene.
    public static class MetaLayout
    {
        // Maps the catalog's authored art onto a background being drawn this wide. 1:1 when
        // the background is drawn at its own pixel width; everything else follows from it.
        //
        // Guarded rather than trusted: a sprite with a zero-width rect would otherwise divide
        // by zero and place every prop at infinity, which reads on screen as "the props are
        // gone" -- a symptom that sends you looking at the catalog instead of at the art.
        public static float PropScale(float backgroundWidth, Sprite backgroundSprite)
        {
            if (backgroundSprite == null || backgroundSprite.rect.width <= 0f) return 1f;
            return backgroundWidth / backgroundSprite.rect.width;
        }

        // The prop's size at that scale: its art, times how far the background got scaled,
        // times the prop's own authored nudge.
        //
        // That third factor is why the danger this comment used to warn about -- "the same
        // prop two different sizes on the two screens the moment somebody set it in one
        // place" -- did not arrive with it. The nudge is read HERE, in the one function both
        // screens and the editor canvas call, so there is no second place to set it in. A
        // caller that multiplies MetaItemDefinition.Scale in for itself is the bug; this
        // function already did it.
        public static Vector2 PropSize(MetaItemDefinition item, float scale)
        {
            if (item?.Sprite == null) return Vector2.zero;
            return item.Sprite.rect.size * (scale * item.Scale);
        }

        // The prop's whole rect inside a background area of this size, with the prop's PIVOT
        // sitting on its NormalizedPosition -- the pivot being the contact point, bottom-centre
        // by default, because these things stand on the ground.
        //
        // On the meta screen this rule is not called: Unity's anchors implement it, and they
        // implement it better, because an anchored prop rides the background through every
        // resize without a line of layout code. This function is that same rule spelled out
        // for a medium that has no anchors -- compositing into a texture -- and the anchored
        // version stays the reference. If the two ever disagree, the anchors are right.
        //
        // Returned in the BACKGROUND's own space: origin bottom-left, y upward, the same
        // convention NormalizedPosition is authored in. A caller drawing into a top-left-origin
        // surface has to flip it, and that flip belongs to the caller rather than to a second
        // overload here -- one of the two conventions has to be the one this file speaks, and
        // the catalog's is the one that cannot change.
        public static Rect PropRect(MetaItemDefinition item, Vector2 areaSize, float scale)
        {
            var size = PropSize(item, scale);
            if (item == null) return new Rect(Vector2.zero, size);

            var anchor = new Vector2(
                item.NormalizedPosition.x * areaSize.x,
                item.NormalizedPosition.y * areaSize.y);

            var origin = anchor - new Vector2(item.Pivot.x * size.x, item.Pivot.y * size.y);
            return new Rect(origin, size);
        }
    }
}
