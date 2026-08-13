using System.Collections.Generic;
using System.Linq;
using ExpoTheExplorer.Core;
using ExpoTheExplorer.Data;
using UnityEditor;
using UnityEngine;

namespace ExpoTheExplorer.Editor
{
    // IMGUI re-implementation of TicketCard.prefab/TicketCardView's visual layout
    // (Assets/Scripts/UI/TicketCardView.cs) for the Day Editor -- a live uGUI prefab
    // can't be rendered inside an EditorWindow, so this mirrors the same background/
    // dish/side/drink/modification layout by hand, reusing the same sprite data
    // (FoodItemConfig.Sprite, ModificationConfig.Icon) the game itself reads. Cards
    // themselves are display-only; clicking one just reports its index back via
    // selectedIndex so DayEditorModel can draw an edit panel for it below the strip.
    public static class DayEditorTicketCardPreview
    {
        private const float CardWidth = 130f;
        private const float CardHeight = 190f;
        private const float Spacing = 8f;
        private static readonly Color SelectionHighlightColor = new(0.3f, 0.6f, 1f, 1f);

        public static void DrawStrip(List<DayEditorTicketEntry> entries, TicketCardVisualsConfig visuals, ref Vector2 scrollPos, ref int selectedIndex)
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
                DrawCard(cardRect, entries[i], visuals, i, i == selectedIndex);

                // Hit-tested here, in the strip's own (outer) coordinate space -- DrawCard
                // wraps its own content in GUI.BeginGroup(cardRect), which remaps
                // Event.current.mousePosition to group-local coordinates, so a click check
                // inside DrawCard would be comparing against the wrong space entirely.
                if (Event.current.type == EventType.MouseDown && Event.current.button == 0 && cardRect.Contains(Event.current.mousePosition))
                {
                    // Clicking the already-selected card toggles the selection off (-1),
                    // which collapses DayEditorModel's edit panel below the strip -- a
                    // click is the only way back out of editing, since the panel has no
                    // close button of its own.
                    selectedIndex = selectedIndex == i ? -1 : i;
                    GUI.changed = true;
                    Event.current.Use();
                }
            }
            EditorGUILayout.EndScrollView();
        }

        private static void DrawCard(Rect cardRect, DayEditorTicketEntry entry, TicketCardVisualsConfig visuals, int index, bool isSelected)
        {
            if (isSelected)
            {
                const float highlightMargin = 3f;
                EditorGUI.DrawRect(
                    new Rect(cardRect.x - highlightMargin, cardRect.y - highlightMargin, cardRect.width + highlightMargin * 2f, cardRect.height + highlightMargin * 2f),
                    SelectionHighlightColor);
            }

            DrawFrame(cardRect, visuals, entry.PatienceType);

            GUI.BeginGroup(cardRect);

            var y = 4f;
            GUI.Label(new Rect(4, y, cardRect.width - 8, 16), $"#{index}  {entry.PatienceType}", EditorStyles.whiteBoldLabel);
            y += 30;

            if (!string.IsNullOrEmpty(entry.CustomerNameOverride))
            {
                GUI.Label(new Rect(4, y, cardRect.width - 8, 14), entry.CustomerNameOverride, EditorStyles.whiteMiniLabel);
                y += 14;
            }
            y += 2;

            // Info hierarchy matches the real card (CLAUDE.md Section 3): dish -> modification
            // list -> side/drink.
            const float dishSize = 40f;
            DayEditorSpriteGUI.DrawSpriteFit(new Rect((cardRect.width - dishSize) / 2f, y, dishSize, dishSize), entry.MainItem != null ? entry.MainItem.Sprite : null);
            y += dishSize + 10;

            // Sized/badged to match the real Modification row (TicketCard.prefab): a 40x40
            // dish-relative icon with a ~0.6x direction (+/-) badge overlapping its top-right
            // corner -- centered as a row, same convention as the dish/side/drink rows above.
            const float modSize = 18f;
            const float modSpacing = 3f;
            const float badgeSize = modSize * 0.6f;
            var validMods = entry.Modifications.Where(m => m?.Config != null).ToList();
            var rowWidth = validMods.Count * modSize + Mathf.Max(0, validMods.Count - 1) * modSpacing;
            var modX = (cardRect.width - rowWidth) / 2f;
            foreach (var mod in validMods)
            {
                if (modX + modSize > cardRect.width - 4) break; // out of row width -- drop the rest rather than overlap the next card

                var modRect = new Rect(modX, y, modSize, modSize);
                DayEditorSpriteGUI.DrawSpriteFit(modRect, mod.Config.Icon);

                var badgeRect = new Rect((modRect.xMin + modRect.xMax - badgeSize) / 2, modRect.yMax, badgeSize, badgeSize);
                DayEditorSpriteGUI.DrawSpriteFit(badgeRect, DirectionSpriteFor(visuals, mod.IsAddition));

                modX += modSize + modSpacing;
            }
            y += modSize + 30;

            const float thumbSize = 30f;
            const float thumbSpacing = 12f;
            if (entry.SideItem != null && entry.SideItem.Sprite != null)
            {
                DayEditorSpriteGUI.DrawSpriteFit(new Rect(cardRect.width / 2f - thumbSize - thumbSpacing, y, thumbSize, thumbSize), entry.SideItem.Sprite);
            }
            if (entry.DrinkItem != null && entry.DrinkItem.Sprite != null)
            {
                DayEditorSpriteGUI.DrawSpriteFit(new Rect(cardRect.width / 2f + thumbSpacing, y, thumbSize, thumbSize), entry.DrinkItem.Sprite);
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
                DayEditorSpriteGUI.DrawTexCoords(cardRect, frameSprite);
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

        private static Sprite DirectionSpriteFor(TicketCardVisualsConfig visuals, bool isAddition)
        {
            if (visuals == null) return null;
            return isAddition ? visuals.AdditionSprite : visuals.RemovalSprite;
        }
    }
}
