using System.Collections.Generic;
using ExpoTheExplorer.Core;
using ExpoTheExplorer.Data;
using UnityEditor;
using UnityEngine;

namespace ExpoTheExplorer.Editor
{
    // Read-only IMGUI re-implementation of TicketCard.prefab/TicketCardView's visual
    // layout (Assets/Scripts/UI/TicketCardView.cs) for the Day Editor -- a live uGUI
    // prefab can't be rendered inside an EditorWindow, so this mirrors the same
    // background/dish/side/drink/modification layout by hand, reusing the same sprite
    // data (FoodItemConfig.Sprite, ModificationConfig.Icon) the game itself reads.
    public static class DayEditorTicketCardPreview
    {
        private const float CardWidth = 130f;
        private const float CardHeight = 190f;
        private const float Spacing = 8f;

        public static void DrawStrip(List<DayEditorTicketEntry> entries, TicketCardVisualsConfig visuals, ref Vector2 scrollPos)
        {
            if (entries == null || entries.Count == 0) return;

            if (visuals == null)
            {
                EditorGUILayout.HelpBox("Ticket Card Visuals config not assigned (toolbar above) -- frame/box colors will show as placeholders.", MessageType.Info);
            }

            var totalWidth = entries.Count * (CardWidth + Spacing);
            scrollPos = EditorGUILayout.BeginScrollView(scrollPos, false, false, GUILayout.Height(CardHeight + 20));
            var stripRect = GUILayoutUtility.GetRect(totalWidth, CardHeight);
            for (var i = 0; i < entries.Count; i++)
            {
                var cardRect = new Rect(stripRect.x + i * (CardWidth + Spacing), stripRect.y, CardWidth, CardHeight);
                DrawCard(cardRect, entries[i], visuals, i);
            }
            EditorGUILayout.EndScrollView();
        }

        private static void DrawCard(Rect cardRect, DayEditorTicketEntry entry, TicketCardVisualsConfig visuals, int index)
        {
            DrawFrame(cardRect, visuals, entry.PatienceType);

            GUI.BeginGroup(cardRect);

            var y = 4f;
            GUI.Label(new Rect(4, y, cardRect.width - 8, 16), $"#{index}  {entry.PatienceType}", EditorStyles.boldLabel);
            y += 16;

            if (!string.IsNullOrEmpty(entry.CustomerNameOverride))
            {
                GUI.Label(new Rect(4, y, cardRect.width - 8, 14), entry.CustomerNameOverride, EditorStyles.miniLabel);
                y += 14;
            }
            y += 2;

            // Info hierarchy matches the real card (CLAUDE.md Section 3): dish -> modification
            // list -> side/drink.
            const float dishSize = 40f;
            DrawSpriteFit(new Rect((cardRect.width - dishSize) / 2f, y, dishSize, dishSize), entry.MainItem != null ? entry.MainItem.Sprite : null);
            y += dishSize + 4;

            const float modSize = 20f;
            var modX = 4f;
            foreach (var mod in entry.Modifications)
            {
                if (mod?.Config == null) continue;
                if (modX + modSize > cardRect.width - 4) break; // out of row width -- drop the rest rather than overlap the next card

                var modRect = new Rect(modX, y, modSize, modSize);
                EditorGUI.DrawRect(modRect, ModificationBoxColorFor(visuals, entry.PatienceType));
                DrawSpriteFit(modRect, mod.Config.Icon);
                modX += modSize + 2;
            }
            y += modSize + 4;

            const float thumbSize = 30f;
            if (entry.SideItem != null && entry.SideItem.Sprite != null)
            {
                DrawSpriteFit(new Rect(cardRect.width / 2f - thumbSize - 2, y, thumbSize, thumbSize), entry.SideItem.Sprite);
            }
            if (entry.DrinkItem != null && entry.DrinkItem.Sprite != null)
            {
                DrawSpriteFit(new Rect(cardRect.width / 2f + 2, y, thumbSize, thumbSize), entry.DrinkItem.Sprite);
            }
            y += thumbSize + 4;

            if (entry.TimeLimitSecondsOverride > 0f)
            {
                GUI.Label(new Rect(4, cardRect.height - 16, cardRect.width - 8, 14), $"{entry.TimeLimitSecondsOverride:0}s", EditorStyles.miniLabel);
            }

            GUI.EndGroup();
        }

        private static void DrawFrame(Rect cardRect, TicketCardVisualsConfig visuals, PatienceType patienceType)
        {
            var frameSprite = TicketSpriteFor(visuals, patienceType);
            if (frameSprite != null)
            {
                DrawTexCoords(cardRect, frameSprite);
            }
            else
            {
                EditorGUI.DrawRect(cardRect, new Color(0.5f, 0.5f, 0.5f, 0.2f));
            }
        }

        private static Sprite TicketSpriteFor(TicketCardVisualsConfig visuals, PatienceType patienceType)
        {
            if (visuals == null) return null;
            return patienceType switch
            {
                PatienceType.Impatient => visuals.ImpatientTicketSprite,
                PatienceType.Patient => visuals.PatientTicketSprite,
                _ => visuals.NormalTicketSprite,
            };
        }

        private static Color ModificationBoxColorFor(TicketCardVisualsConfig visuals, PatienceType patienceType)
        {
            if (visuals == null) return new Color(0.5f, 0.5f, 0.5f, 0.3f);
            return patienceType switch
            {
                PatienceType.Impatient => visuals.ImpatientModificationBoxColor,
                PatienceType.Patient => visuals.PatientModificationBoxColor,
                _ => visuals.NormalModificationBoxColor,
            };
        }

        // Centers and fits within rect preserving aspect -- matches how the in-game
        // dish/side/drink/modification Image components size to their own sprite.
        private static void DrawSpriteFit(Rect rect, Sprite sprite)
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

        // Handles sprites packed in an atlas via normalized UV (mirrors
        // FoodItemConfigEditor.DrawSpriteInPreview's approach).
        private static void DrawTexCoords(Rect rect, Sprite sprite)
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
    }
}
