using UnityEditor;
using UnityEngine;

namespace ExpoTheExplorer.Editor
{
    // Shared atlas-aware sprite drawing for hand-rolled IMGUI previews (ticket
    // cards, board grid, and the Meta Editor's layout canvas) -- a live uGUI
    // prefab/scene renderer can't run inside an EditorWindow, so these previews
    // draw sprites by hand instead. The `DayEditor` prefix is now historical:
    // MetaLocationLayoutGUI uses DrawTexCoords too, and renaming the file would
    // move a GUID for a cosmetic gain.
    internal static class DayEditorSpriteGUI
    {
        // Centers and fits within rect preserving aspect -- matches how the in-game
        // dish/side/drink/modification Image components size to their own sprite.
        internal static void DrawSpriteFit(Rect rect, Sprite sprite)
        {
            if (sprite == null) return;

            var spriteRect = sprite.rect;
            if (spriteRect.width <= 0 || spriteRect.height <= 0) return;

            var aspect = spriteRect.width / spriteRect.height;
            var fitWidth = aspect >= 1f ? rect.width : rect.height * aspect;
            var fitHeight = aspect >= 1f ? rect.width / aspect : rect.height;
            var fitRect = new Rect(
                rect.x + (rect.width - fitWidth) / 2f,
                rect.y + (rect.height - fitHeight) / 2f,
                fitWidth,
                fitHeight);

            DrawTexCoords(fitRect, sprite);
        }

        // Handles sprites packed in an atlas via normalized UV.
        internal static void DrawTexCoords(Rect rect, Sprite sprite)
        {
            var spriteRect = sprite.rect;
            var texture = sprite.texture;
            var texCoords = new Rect(
                spriteRect.x / texture.width,
                spriteRect.y / texture.height,
                spriteRect.width / texture.width,
                spriteRect.height / texture.height);

            GUI.DrawTextureWithTexCoords(rect, texture, texCoords);
        }

        // Mirrors BoardView's per-layer fit-then-offset approach (each layer is
        // independently scaled to fit the cell, then nudged by its resolved
        // offset as a fraction of the cell size) so a preview matches what
        // actually renders on the board.
        internal static void DrawLayeredSprite(Rect rect, Sprite sprite, Vector2 offsetFraction, float scale)
        {
            if (sprite == null) return;

            var spriteRect = sprite.rect;
            if (spriteRect.width <= 0 || spriteRect.height <= 0) return;

            var aspect = spriteRect.width / spriteRect.height;
            var fitSize = Mathf.Min(rect.width, rect.height) * scale;
            var drawWidth = aspect >= 1f ? fitSize : fitSize * aspect;
            var drawHeight = aspect >= 1f ? fitSize / aspect : fitSize;

            var centerX = rect.x + rect.width / 2f;
            var centerY = rect.y + rect.height / 2f;
            var offsetPxX = offsetFraction.x * rect.width;
            // GUI space Y grows downward while board/world space Y grows upward.
            var offsetPxY = -offsetFraction.y * rect.height;

            var drawRect = new Rect(
                centerX - drawWidth / 2f + offsetPxX,
                centerY - drawHeight / 2f + offsetPxY,
                drawWidth,
                drawHeight);

            DrawTexCoords(drawRect, sprite);
        }
    }
}
