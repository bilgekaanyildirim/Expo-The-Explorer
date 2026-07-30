using System.Collections.Generic;
using ExpoTheExplorer.Core;
using ExpoTheExplorer.Data;
using UnityEditor;
using UnityEngine;

namespace ExpoTheExplorer.Editor
{
    // Live preview of how this food's composited board sprite looks — lets you
    // tune SpriteLayer offset/pushAmount values and try different modification
    // combinations without entering Play mode. Reuses BoardItem.ResolvedLayers
    // directly (the same code BoardView renders from) so the preview can never
    // drift from actual in-game behavior.
    [CustomEditor(typeof(FoodItemConfig))]
    public class FoodItemConfigEditor : UnityEditor.Editor
    {
        private const float PreviewSize = 220f;

        private enum PreviewModState
        {
            Absent,
            Added,
            Removed
        }

        // Keyed by ModificationConfig — resets whenever a different asset is
        // selected (a fresh Editor instance), which is fine for a preview toggle.
        private readonly Dictionary<ModificationConfig, PreviewModState> previewStates = new();

        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();

            var food = (FoodItemConfig)target;

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Board Preview (read-only)", EditorStyles.boldLabel);

            DrawModificationToggles(food);
            EditorGUILayout.Space(6);
            DrawPreview(food);
        }

        private void DrawModificationToggles(FoodItemConfig food)
        {
            if (food.AvailableModifications.Count == 0) return;

            foreach (var mod in food.AvailableModifications)
            {
                if (mod == null) continue;
                if (!previewStates.ContainsKey(mod)) previewStates[mod] = PreviewModState.Absent;

                EditorGUILayout.LabelField(mod.DisplayName);

                switch (mod.AllowedDirection)
                {
                    case ModificationDirection.AdditionOnly:
                    {
                        var isAdded = previewStates[mod] == PreviewModState.Added;
                        var newIndex = GUILayout.Toolbar(isAdded ? 1 : 0, new[] { "Absent", "Added" });
                        previewStates[mod] = newIndex == 1 ? PreviewModState.Added : PreviewModState.Absent;
                        break;
                    }
                    case ModificationDirection.RemovalOnly:
                    {
                        var isRemoved = previewStates[mod] == PreviewModState.Removed;
                        var newIndex = GUILayout.Toolbar(isRemoved ? 1 : 0, new[] { "Absent", "Removed" });
                        previewStates[mod] = newIndex == 1 ? PreviewModState.Removed : PreviewModState.Absent;
                        break;
                    }
                    default: // Both
                    {
                        var currentIndex = previewStates[mod] switch
                        {
                            PreviewModState.Added => 1,
                            PreviewModState.Removed => 2,
                            _ => 0,
                        };
                        var newIndex = GUILayout.Toolbar(currentIndex, new[] { "Absent", "Added", "Removed" });
                        previewStates[mod] = newIndex switch
                        {
                            1 => PreviewModState.Added,
                            2 => PreviewModState.Removed,
                            _ => PreviewModState.Absent,
                        };
                        break;
                    }
                }

                EditorGUILayout.Space(2);
            }
        }

        private void DrawPreview(FoodItemConfig food)
        {
            var previewRect = GUILayoutUtility.GetRect(PreviewSize, PreviewSize, GUILayout.ExpandWidth(false));
            EditorGUI.DrawRect(previewRect, new Color(0.5f, 0.5f, 0.5f, 0.15f));

            var boardItem = new BoardItem(food, BuildPreviewModifications(food));
            var resolvedLayers = boardItem.ResolvedLayers;

            if (resolvedLayers.Count == 0)
            {
                EditorGUILayout.HelpBox("Nothing to preview — no base sprite and no sprite layers configured.", MessageType.Info);
                return;
            }

            foreach (var layer in resolvedLayers)
            {
                DrawSpriteInPreview(previewRect, layer.Sprite, layer.Offset, layer.Scale);
            }
        }

        private List<Modification> BuildPreviewModifications(FoodItemConfig food)
        {
            var modifications = new List<Modification>();
            foreach (var mod in food.AvailableModifications)
            {
                if (mod == null) continue;
                if (!previewStates.TryGetValue(mod, out var state) || state == PreviewModState.Absent) continue;

                modifications.Add(new Modification(mod, state == PreviewModState.Added));
            }

            return modifications;
        }

        // Mirrors BoardView's per-layer fit-then-offset approach (each layer is
        // independently scaled to fit the cell, then nudged by its resolved
        // offset as a fraction of the cell size, then adjusted by its own
        // relative-size multiplier) so the preview matches what actually renders
        // on the board.
        private static void DrawSpriteInPreview(Rect previewRect, Sprite sprite, Vector2 offsetFraction, float scale)
        {
            if (sprite == null) return;

            var spriteRect = sprite.rect;
            if (spriteRect.width <= 0 || spriteRect.height <= 0) return;

            var aspect = spriteRect.width / spriteRect.height;
            var fitSize = Mathf.Min(previewRect.width, previewRect.height) * scale;
            var drawWidth = aspect >= 1f ? fitSize : fitSize * aspect;
            var drawHeight = aspect >= 1f ? fitSize / aspect : fitSize;

            var centerX = previewRect.x + previewRect.width / 2f;
            var centerY = previewRect.y + previewRect.height / 2f;
            var offsetPxX = offsetFraction.x * previewRect.width;
            // GUI space Y grows downward while board/world space Y grows upward.
            var offsetPxY = -offsetFraction.y * previewRect.height;

            var drawRect = new Rect(
                centerX - drawWidth / 2f + offsetPxX,
                centerY - drawHeight / 2f + offsetPxY,
                drawWidth,
                drawHeight);

            var texture = sprite.texture;
            var texCoords = new Rect(
                spriteRect.x / texture.width,
                spriteRect.y / texture.height,
                spriteRect.width / texture.width,
                spriteRect.height / texture.height);

            GUI.DrawTextureWithTexCoords(drawRect, texture, texCoords);
        }
    }
}
