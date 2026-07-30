using System.Collections.Generic;
using ExpoTheExplorer.Core;
using ExpoTheExplorer.Data;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace ExpoTheExplorer.Tests.EditMode
{
    public class BoardItemLayerTests
    {
        private readonly List<Object> spawnedAssets = new();

        [TearDown]
        public void TearDown()
        {
            foreach (var asset in spawnedAssets)
            {
                Object.DestroyImmediate(asset);
            }
            spawnedAssets.Clear();
        }

        private static Sprite CreateTestSprite()
        {
            return Sprite.Create(new Texture2D(1, 1), new Rect(0, 0, 1, 1), Vector2.zero);
        }

        private ModificationConfig CreateModification(ModificationDirection direction)
        {
            var modConfig = ScriptableObject.CreateInstance<ModificationConfig>();
            spawnedAssets.Add(modConfig);

            var serialized = new SerializedObject(modConfig);
            serialized.FindProperty("allowedDirection").enumValueIndex = (int)direction;
            serialized.ApplyModifiedPropertiesWithoutUndo();

            return modConfig;
        }

        private FoodItemConfig CreateFoodItemWithLayers(
            Sprite baseSprite,
            params (Sprite sprite, LayerVisibility visibility, ModificationConfig modification, bool direction, Vector2 offset, Vector2 pushAmount)[] layers)
        {
            var foodConfig = ScriptableObject.CreateInstance<FoodItemConfig>();
            spawnedAssets.Add(foodConfig);

            var serialized = new SerializedObject(foodConfig);
            serialized.FindProperty("sprite").objectReferenceValue = baseSprite;

            var layersProperty = serialized.FindProperty("spriteLayers");
            layersProperty.ClearArray();
            for (var i = 0; i < layers.Length; i++)
            {
                layersProperty.InsertArrayElementAtIndex(i);
                var element = layersProperty.GetArrayElementAtIndex(i);
                element.FindPropertyRelative("sprite").objectReferenceValue = layers[i].sprite;
                element.FindPropertyRelative("visibility").enumValueIndex = (int)layers[i].visibility;
                element.FindPropertyRelative("modification").objectReferenceValue = layers[i].modification;
                element.FindPropertyRelative("direction").boolValue = layers[i].direction;
                element.FindPropertyRelative("offset").vector2Value = layers[i].offset;
                element.FindPropertyRelative("pushAmount").vector2Value = layers[i].pushAmount;
            }

            serialized.ApplyModifiedPropertiesWithoutUndo();
            return foodConfig;
        }

        [Test]
        public void ResolvedLayers_WithNoLayersConfigured_FallsBackToBaseSprite()
        {
            var baseSprite = CreateTestSprite();
            var foodConfig = CreateFoodItemWithLayers(baseSprite);
            var item = new BoardItem(foodConfig, new List<Modification>());

            var layers = item.ResolvedLayers;

            Assert.AreEqual(1, layers.Count);
            Assert.AreSame(baseSprite, layers[0].Sprite);
            Assert.AreEqual(Vector2.zero, layers[0].Offset);
        }

        [Test]
        public void ResolvedLayers_AlwaysVisibleLayer_IsAlwaysIncluded()
        {
            var layerSprite = CreateTestSprite();
            var foodConfig = CreateFoodItemWithLayers(null,
                (layerSprite, LayerVisibility.AlwaysVisible, null, false, Vector2.zero, Vector2.zero));
            var item = new BoardItem(foodConfig, new List<Modification>());

            var layers = item.ResolvedLayers;

            Assert.AreEqual(1, layers.Count);
            Assert.AreSame(layerSprite, layers[0].Sprite);
        }

        [Test]
        public void ResolvedLayers_VisibleByDefaultLayer_HiddenWhenTiedModificationPresent()
        {
            var mod = CreateModification(ModificationDirection.RemovalOnly);
            var layerSprite = CreateTestSprite();
            var foodConfig = CreateFoodItemWithLayers(null,
                (layerSprite, LayerVisibility.VisibleByDefault, mod, false, Vector2.zero, Vector2.zero));

            var withoutModification = new BoardItem(foodConfig, new List<Modification>());
            Assert.AreEqual(1, withoutModification.ResolvedLayers.Count);
            Assert.AreSame(layerSprite, withoutModification.ResolvedLayers[0].Sprite);

            var withModification = new BoardItem(foodConfig, new List<Modification> { new(mod, false) });
            Assert.AreEqual(0, withModification.ResolvedLayers.Count);
        }

        [Test]
        public void ResolvedLayers_HiddenByDefaultLayer_VisibleWhenTiedModificationPresent()
        {
            var mod = CreateModification(ModificationDirection.AdditionOnly);
            var layerSprite = CreateTestSprite();
            var foodConfig = CreateFoodItemWithLayers(null,
                (layerSprite, LayerVisibility.HiddenByDefault, mod, true, Vector2.zero, Vector2.zero));

            var withoutModification = new BoardItem(foodConfig, new List<Modification>());
            Assert.AreEqual(0, withoutModification.ResolvedLayers.Count);

            var withModification = new BoardItem(foodConfig, new List<Modification> { new(mod, true) });
            Assert.AreEqual(1, withModification.ResolvedLayers.Count);
            Assert.AreSame(layerSprite, withModification.ResolvedLayers[0].Sprite);
        }

        [Test]
        public void ResolvedLayers_PushAmount_OnlyAppliesToLaterLayersWhenTriggeringLayerIsVisible()
        {
            var mod = CreateModification(ModificationDirection.AdditionOnly);
            var pushingSprite = CreateTestSprite();
            var pushedSprite = CreateTestSprite();
            var pushAmount = new Vector2(0f, 0.5f);
            var pushedOffset = new Vector2(0f, 0.2f);

            var foodConfig = CreateFoodItemWithLayers(null,
                (pushingSprite, LayerVisibility.HiddenByDefault, mod, true, Vector2.zero, pushAmount),
                (pushedSprite, LayerVisibility.AlwaysVisible, null, false, pushedOffset, Vector2.zero));

            var withoutModification = new BoardItem(foodConfig, new List<Modification>());
            var layersWithout = withoutModification.ResolvedLayers;
            Assert.AreEqual(1, layersWithout.Count);
            Assert.AreEqual(pushedOffset, layersWithout[0].Offset);

            var withModification = new BoardItem(foodConfig, new List<Modification> { new(mod, true) });
            var layersWith = withModification.ResolvedLayers;
            Assert.AreEqual(2, layersWith.Count);
            Assert.AreEqual(Vector2.zero, layersWith[0].Offset);
            Assert.AreEqual(pushedOffset + pushAmount, layersWith[1].Offset);
        }

        // Cheese-shaped scenario: a VisibleByDefault "base" layer and a
        // HiddenByDefault "extra" layer tied to the same Both-direction
        // modification. Removing hides both and undoes the push (gap closes);
        // adding swaps base->extra in place with the SAME push (no shift);
        // no modification at all leaves the base layer showing normally.
        [Test]
        public void ResolvedLayers_CheeseLikeModification_ExtraReplacesBaseWithoutStackingOrExtraShift()
        {
            var cheeseMod = CreateModification(ModificationDirection.Both);
            var baseCheeseSprite = CreateTestSprite();
            var extraCheeseSprite = CreateTestSprite();
            var bunTopSprite = CreateTestSprite();
            var cheesePush = new Vector2(0f, 0.3f);
            var bunTopOffset = new Vector2(0f, 0.8f);

            var foodConfig = CreateFoodItemWithLayers(null,
                (baseCheeseSprite, LayerVisibility.VisibleByDefault, cheeseMod, false, Vector2.zero, cheesePush),
                (extraCheeseSprite, LayerVisibility.HiddenByDefault, cheeseMod, true, Vector2.zero, cheesePush),
                (bunTopSprite, LayerVisibility.AlwaysVisible, null, false, bunTopOffset, Vector2.zero));

            // No modification: base cheese shows, bun top sits at its normal
            // (pushed-by-cheese) position.
            var unmodified = new BoardItem(foodConfig, new List<Modification>());
            var unmodifiedLayers = unmodified.ResolvedLayers;
            Assert.AreEqual(2, unmodifiedLayers.Count);
            Assert.AreSame(baseCheeseSprite, unmodifiedLayers[0].Sprite);
            Assert.AreEqual(bunTopOffset + cheesePush, unmodifiedLayers[1].Offset);

            // Removed: both cheese layers gone, bun top falls back (no push).
            var removed = new BoardItem(foodConfig, new List<Modification> { new(cheeseMod, false) });
            var removedLayers = removed.ResolvedLayers;
            Assert.AreEqual(1, removedLayers.Count);
            Assert.AreSame(bunTopSprite, removedLayers[0].Sprite);
            Assert.AreEqual(bunTopOffset, removedLayers[0].Offset);

            // Added: extra cheese REPLACES base cheese (not stacked alongside
            // it), and since both share the same pushAmount, bun top doesn't
            // shift relative to the unmodified case.
            var added = new BoardItem(foodConfig, new List<Modification> { new(cheeseMod, true) });
            var addedLayers = added.ResolvedLayers;
            Assert.AreEqual(2, addedLayers.Count);
            Assert.AreSame(extraCheeseSprite, addedLayers[0].Sprite);
            Assert.AreEqual(bunTopOffset + cheesePush, addedLayers[1].Offset);
        }
    }
}
