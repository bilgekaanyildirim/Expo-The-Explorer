using System.Collections.Generic;
using ExpoTheExplorer.Data;
using ExpoTheExplorer.Systems.MetaSystem;
using NUnit.Framework;
using UnityEngine;

namespace ExpoTheExplorer.Tests.EditMode
{
    // The arithmetic three surfaces share: the meta screen builds a RectTransform from it,
    // the day scene's backdrop composites into a texture from it, and the Meta Editor's
    // canvas previews with it. They can only be trusted to agree because they call one
    // function -- so what this file pins is that function's contract, not any of the three.
    //
    // Sprites are real (Sprite.Create needs no scene) because rect.size is the input; the
    // rest is pure arithmetic.
    public class MetaLayoutTests
    {
        private readonly List<Object> spawned = new();

        [TearDown]
        public void TearDown()
        {
            foreach (var asset in spawned) Object.DestroyImmediate(asset);
            spawned.Clear();
        }

        private Sprite CreateSprite(int width, int height)
        {
            var texture = new Texture2D(width, height);
            spawned.Add(texture);
            var sprite = Sprite.Create(texture, new Rect(0, 0, width, height), new Vector2(0.5f, 0.5f));
            spawned.Add(sprite);
            return sprite;
        }

        private MetaItemDefinition Prop(
            int width = 40, int height = 80, float scale = 1f, Vector2 pivot = default, Vector2 position = default) =>
            new("prop", MetaUnlockKind.Purchase, price: 10, sprite: CreateSprite(width, height),
                normalizedPosition: position, pivot: pivot, scale: scale);

        // The background's own width is the 1:1 point -- everything else is a ratio off it.
        [Test]
        public void PropScale_IsOneWhenTheBackgroundDrawsAtItsOwnWidth()
        {
            var background = CreateSprite(800, 1600);

            Assert.AreEqual(1f, MetaLayout.PropScale(800f, background), 0.0001f);
            Assert.AreEqual(0.5f, MetaLayout.PropScale(400f, background), 0.0001f);
        }

        // A zero-width sprite would otherwise divide by zero and put every prop at
        // infinity, which reads on screen as "the props are gone" -- a symptom that sends
        // you looking at the catalog instead of at the art.
        [Test]
        public void PropScale_FallsBackToOneWhenTheBackgroundIsUnusable()
        {
            Assert.AreEqual(1f, MetaLayout.PropScale(800f, null), 0.0001f);
        }

        [Test]
        public void PropSize_IsTheArtTimesTheBackgroundScale()
        {
            var size = MetaLayout.PropSize(Prop(width: 40, height: 80), 0.5f);

            Assert.AreEqual(20f, size.x, 0.0001f);
            Assert.AreEqual(40f, size.y, 0.0001f);
        }

        // The per-prop nudge, and the reason it lives in this function: an authored scale
        // multiplies the derived size rather than replacing it, so the background's own
        // scaling still rides through.
        [Test]
        public void PropSize_MultipliesTheAuthoredScaleOnTop()
        {
            var size = MetaLayout.PropSize(Prop(width: 40, height: 80, scale: 1.5f), 0.5f);

            Assert.AreEqual(30f, size.x, 0.0001f);
            Assert.AreEqual(60f, size.y, 0.0001f);
        }

        // A catalog written before the scale field existed has no key for it, and a
        // hand-edited one could carry 0. Either way the prop draws at its art's own size --
        // a prop scaled to nothing is invisible with nothing on screen to explain it.
        [Test]
        public void PropSize_TreatsAnUnauthoredScaleAsOne()
        {
            var size = MetaLayout.PropSize(Prop(width: 40, height: 80, scale: 0f), 1f);

            Assert.AreEqual(40f, size.x, 0.0001f);
            Assert.AreEqual(80f, size.y, 0.0001f);
        }

        [Test]
        public void PropSize_IsZeroForAPropWithNoArt()
        {
            Assert.AreEqual(Vector2.zero, MetaLayout.PropSize(null, 1f));
        }

        // The pivot is the contact point, so scaling has to leave it where it is: a prop
        // that grew off the ground would be the same defect as one that moved.
        [Test]
        public void PropRect_KeepsThePivotOnTheAuthoredPositionAtAnyScale()
        {
            var area = new Vector2(1000f, 2000f);
            var position = new Vector2(0.25f, 0.5f);
            var pivot = new Vector2(0.5f, 0f);

            var small = MetaLayout.PropRect(Prop(scale: 1f, pivot: pivot, position: position), area, 1f);
            var large = MetaLayout.PropRect(Prop(scale: 3f, pivot: pivot, position: position), area, 1f);

            // x = the horizontal centre, y = the bottom edge: that is where a
            // (0.5, 0) pivot sits.
            Assert.AreEqual(250f, small.center.x, 0.0001f);
            Assert.AreEqual(250f, large.center.x, 0.0001f);
            Assert.AreEqual(1000f, small.yMin, 0.0001f);
            Assert.AreEqual(1000f, large.yMin, 0.0001f);

            // And it did grow, or the assertions above would pass on a scale nobody applied.
            Assert.Greater(large.height, small.height);
        }
    }
}
