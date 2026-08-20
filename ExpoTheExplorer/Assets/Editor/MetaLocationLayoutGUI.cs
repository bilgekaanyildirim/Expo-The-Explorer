using System.Collections.Generic;
using ExpoTheExplorer.Data;
using UnityEditor;
using UnityEngine;

namespace ExpoTheExplorer.Editor
{
    // Mutable state the layout canvas carries between repaints: what is selected, what is
    // being dragged, and how the view is zoomed/panned. Kept out of the drawing code so
    // the canvas itself stays a function of (rect, data, state).
    internal class MetaLayoutState
    {
        internal int SelectedIndex = -1;
        internal int DraggingIndex = -1;

        // 1 = the background fitted to the canvas. Above that the background overflows and
        // Pan decides which part is visible.
        internal float Zoom = 1f;
        internal Vector2 Pan;

        // Set by a middle- or right-button press over the canvas and cleared on release.
        // A press that never drags simply sets and clears it, which is why no separate
        // "did it actually move" flag is needed.
        internal bool IsPanning;

        // PREVIEW visibility only, never serialized. At runtime a prop's visibility is
        // decided by ownership and unlock state, so a saved "hidden" flag would be a
        // second authority arguing with the resolver. This is here for one reason: seeing
        // what sits BEHIND something while placing it.
        internal readonly HashSet<int> Hidden = new();

        // Indices are only meaningful against a given list length, so adding or removing a
        // prop invalidates the whole set. Clearing on a count change is cruder than
        // remapping but cannot silently hide the wrong prop, which is the failure that
        // would actually waste someone's afternoon.
        internal int KnownItemCount = -1;

        internal void SyncTo(int itemCount)
        {
            if (KnownItemCount == itemCount) return;
            KnownItemCount = itemCount;
            Hidden.Clear();
        }

        internal bool IsHidden(int index) => Hidden.Contains(index);

        internal void SetHidden(int index, bool hidden)
        {
            if (hidden) Hidden.Add(index);
            else Hidden.Remove(index);
        }

        internal void ResetView()
        {
            Zoom = 1f;
            Pan = Vector2.zero;
        }
    }

    // The one thing this editor window exists for: draw a location's background at its
    // real aspect and let props be DRAGGED onto it, instead of typing twenty pairs of
    // normalized floats. Everything else in the window is fields Unity could have drawn.
    //
    // Every mutation goes through SerializedProperty rather than assigning to the plain
    // MetaLocation objects directly. That is not ceremony: the locations live inside a
    // ScriptableObject's list, so a direct field write would change the in-memory object
    // and leave the asset unmarked -- the edit would survive until the next domain reload
    // and then silently vanish. SerializedObject gives dirtying and undo for free.
    //
    // Sprite drawing is DayEditorSpriteGUI.DrawTexCoords (same assembly, atlas-aware).
    // Its own comment names the trap this file has to respect: GUI space Y grows DOWNWARD
    // while a normalized UI position grows upward, so every vertical conversion here is
    // flipped. Getting it wrong mirrors the whole layout vertically -- loud, not silent.
    //
    // Zoom needed almost no new maths, because every prop rect and every hit test is
    // derived FROM the background rect: growing that one rectangle scales the props, the
    // click targets and the drag sensitivity together. A drag delta is divided by the
    // background's width, so zooming in makes dragging finer for free -- which is the
    // whole reason to zoom, given a 70x104 plant draws about 20px wide when a 853x1844
    // background is fitted to the canvas.
    internal static class MetaLocationLayoutGUI
    {
        private const float HandleHitPadding = 4f;

        // Vertical slack between the canvas and the background it holds. The backgrounds
        // are portrait, so a background fitted to the full area fills its height exactly
        // and leaves nowhere for a prop that overhangs the top or bottom edge to be drawn.
        // Horizontally there is already room to spare, since a portrait background sits
        // centred in a wide area.
        private const float VerticalSlack = 28f;

        private const float MinZoom = 1f;
        private const float MaxZoom = 12f;
        private const float ZoomPerNotch = 0.1f;

        internal static void Draw(
            Rect area,
            MetaLocation location,
            SerializedProperty locationProperty,
            MetaLayoutState state)
        {
            EditorGUI.DrawRect(area, new Color(0.16f, 0.16f, 0.16f));

            var background = location?.BackgroundSprite;
            if (background == null)
            {
                EditorGUI.LabelField(area, "No background sprite on this location.", EditorStyles.centeredGreyMiniLabel);
                return;
            }

            // Everything below runs in CLIP-LOCAL coordinates: BeginClip makes the area's
            // top-left the origin and translates Event.current.mousePosition to match, so
            // rects and mouse tests stay in one space. Clipping is what zoom requires --
            // a zoomed background is larger than the canvas and would otherwise paint over
            // the panels beneath it.
            GUI.BeginClip(area);
            try
            {
                var localArea = new Rect(0f, 0f, area.width, area.height);
                var fit = FitRect(localArea, background.rect.width / background.rect.height);
                var backgroundRect = ViewRect(fit, state);

                DayEditorSpriteGUI.DrawTexCoords(backgroundRect, background);

                // Props are authored against the background at ONE resolution (D-015: no
                // per-prop scale field), so a prop's size here is its own pixel size times
                // however much the background got scaled. Deriving it the same way the
                // runtime will is what keeps this a preview rather than a second opinion.
                var scale = backgroundRect.width / background.rect.width;

                var items = locationProperty?.FindPropertyRelative("items");
                if (items != null && location.Items != null)
                {
                    DrawProps(location, state, backgroundRect, scale);

                    // Hit-testing gets the WHOLE canvas, not just the background. The
                    // background is portrait and the canvas is wide, so it sits centred
                    // with empty space either side -- and a prop that legitimately
                    // overhangs an edge has its visible part out there. Gating clicks on
                    // the background rect made exactly those props unselectable, which is
                    // the bug this comment exists to stop anyone reintroducing.
                    HandleInput(location, items, state, fit, backgroundRect, localArea);
                }
            }
            finally
            {
                GUI.EndClip();
            }
        }

        // fit -> the rect at Zoom 1 with no Pan. Scaling around fit's CENTRE (rather than
        // its corner) is what makes zooming out land back exactly on the fitted view.
        private static Rect ViewRect(Rect fit, MetaLayoutState state)
        {
            var size = fit.size * state.Zoom;
            var centre = fit.center + state.Pan;
            return new Rect(centre - size / 2f, size);
        }

        private static void DrawProps(
            MetaLocation location, MetaLayoutState state, Rect backgroundRect, float scale)
        {
            // Ascending sortOrder, so the prop meant to be in FRONT is drawn last and
            // lands on top -- the same order the runtime's sibling index will produce.
            var order = SortedIndices(location);

            foreach (var index in order)
            {
                var item = location.Items[index];
                if (item?.Sprite == null || state.IsHidden(index)) continue;

                var rect = RectFor(item, backgroundRect, scale);
                DayEditorSpriteGUI.DrawTexCoords(rect, item.Sprite);

                if (index == state.SelectedIndex && Event.current.type == EventType.Repaint)
                {
                    DrawSelectionMarks(rect, AnchorFor(item, backgroundRect));
                }
            }
        }

        // Drawn with GUI rects rather than Handles: Handles goes through GL, which does
        // not respect the clip this canvas lives inside, so an outline could be painted
        // outside the canvas while the sprite it belongs to is correctly clipped.
        private static void DrawSelectionMarks(Rect rect, Vector2 anchor)
        {
            var colour = new Color(0.3f, 0.8f, 1f);
            const float t = 1f;

            EditorGUI.DrawRect(new Rect(rect.x, rect.y, rect.width, t), colour);
            EditorGUI.DrawRect(new Rect(rect.x, rect.yMax - t, rect.width, t), colour);
            EditorGUI.DrawRect(new Rect(rect.x, rect.y, t, rect.height), colour);
            EditorGUI.DrawRect(new Rect(rect.xMax - t, rect.y, t, rect.height), colour);

            // The anchor point, not the sprite's centre: this is the pixel the authored
            // position actually refers to, and seeing it is what makes a bottom-centre
            // pivot legible while dragging.
            EditorGUI.DrawRect(new Rect(anchor.x - 2f, anchor.y - 2f, 4f, 4f), colour);
        }

        private static void HandleInput(
            MetaLocation location,
            SerializedProperty items,
            MetaLayoutState state,
            Rect fit,
            Rect backgroundRect,
            Rect localArea)
        {
            var e = Event.current;
            var overCanvas = localArea.Contains(e.mousePosition);

            switch (e.type)
            {
                case EventType.ScrollWheel when overCanvas:
                    ApplyZoom(state, fit, e.mousePosition, -e.delta.y * ZoomPerNotch);
                    e.Use();
                    return;

                // Right-click over the canvas would otherwise open a context menu, and the
                // menu wins the event before MouseDrag ever arrives -- so panning with the
                // right button only works if this is swallowed first.
                case EventType.ContextClick when overCanvas:
                    e.Use();
                    return;

                // Middle OR right drag pans. Middle is the convention in Unity's own
                // views; right was asked for because it is the reflex from most 2D tools,
                // and there is nothing else for a right-drag to mean here.
                case EventType.MouseDown when (e.button == 2 || e.button == 1) && overCanvas:
                    state.IsPanning = true;
                    e.Use();
                    return;

                case EventType.MouseDrag when state.IsPanning:
                    state.Pan += e.delta;
                    // Use() alone is normally enough to get a repaint, but the view
                    // changing without any serialized data changing is exactly the case
                    // where it is not guaranteed -- so say so explicitly.
                    GUI.changed = true;
                    e.Use();
                    return;

                case EventType.MouseUp when state.IsPanning:
                    state.IsPanning = false;
                    e.Use();
                    return;

                case EventType.MouseDown when e.button == 0 && overCanvas:
                    SelectAt(location, state, backgroundRect, e.mousePosition);
                    e.Use();
                    return;

                case EventType.MouseDrag when state.DraggingIndex >= 0:
                    DragSelected(items, state, backgroundRect, e.delta);
                    e.Use();
                    return;

                case EventType.MouseUp when state.DraggingIndex >= 0:
                    state.DraggingIndex = -1;
                    e.Use();
                    return;
            }
        }

        // Keeps the point under the cursor pinned: compute where the cursor sits in
        // background-relative terms, re-derive the rect at the new zoom, then shift Pan by
        // however far that point moved. Doing it by measurement rather than algebra is
        // what keeps it correct when the rect definition changes.
        private static void ApplyZoom(MetaLayoutState state, Rect fit, Vector2 mouse, float amount)
        {
            var before = ViewRect(fit, state);
            if (before.width <= 0f || before.height <= 0f) return;

            var u = new Vector2(
                (mouse.x - before.x) / before.width,
                (mouse.y - before.y) / before.height);

            state.Zoom = Mathf.Clamp(state.Zoom * (1f + amount), MinZoom, MaxZoom);

            var after = ViewRect(fit, state);
            var landed = new Vector2(after.x + u.x * after.width, after.y + u.y * after.height);
            state.Pan += mouse - landed;

            // At Zoom 1 the fitted view is the only sensible framing, so drop any pan the
            // way out -- otherwise zooming back out leaves the background off-centre with
            // no obvious way to recover it.
            if (Mathf.Approximately(state.Zoom, MinZoom)) state.Pan = Vector2.zero;

            GUI.changed = true;
        }

        private static void SelectAt(
            MetaLocation location, MetaLayoutState state, Rect backgroundRect, Vector2 mouse)
        {
            // Topmost first, so clicking overlapping props picks the one the eye sees
            // rather than whichever happens to be earlier in the list.
            var order = SortedIndices(location);
            var scale = backgroundRect.width / location.BackgroundSprite.rect.width;

            for (var i = order.Length - 1; i >= 0; i--)
            {
                var index = order[i];
                var item = location.Items[index];
                // Hidden props are unclickable too: a target you cannot see is worse than
                // no target, because the selection changes for no visible reason.
                if (item?.Sprite == null || state.IsHidden(index)) continue;

                var rect = RectFor(item, backgroundRect, scale);
                rect = new Rect(
                    rect.x - HandleHitPadding, rect.y - HandleHitPadding,
                    rect.width + HandleHitPadding * 2f, rect.height + HandleHitPadding * 2f);

                if (!rect.Contains(mouse)) continue;

                state.SelectedIndex = index;
                state.DraggingIndex = index;
                return;
            }

            // A click on empty canvas clears the selection rather than keeping a highlight
            // on something the user has visually moved away from.
            state.SelectedIndex = -1;
        }

        private static void DragSelected(
            SerializedProperty items, MetaLayoutState state, Rect backgroundRect, Vector2 delta)
        {
            if (state.DraggingIndex >= items.arraySize)
            {
                state.DraggingIndex = -1;
                return;
            }

            var positionProperty = items
                .GetArrayElementAtIndex(state.DraggingIndex)
                .FindPropertyRelative("normalizedPosition");

            var current = positionProperty.vector2Value;

            // Divided by the ZOOMED width, so one screen pixel moves the prop less the
            // further you are zoomed in. That is the precision zoom exists to buy.
            positionProperty.vector2Value = new Vector2(
                current.x + delta.x / backgroundRect.width,
                // Flipped: dragging DOWN on screen lowers a normalized Y.
                current.y - delta.y / backgroundRect.height);
        }

        // Sorted by sortOrder, ties broken by list order so the result is stable across
        // repaints -- an unstable order would make overlapping props flicker.
        private static int[] SortedIndices(MetaLocation location)
        {
            var count = location.Items.Count;
            var indices = new int[count];
            for (var i = 0; i < count; i++) indices[i] = i;

            System.Array.Sort(indices, (a, b) =>
            {
                var byOrder = SortOrderOf(location, a).CompareTo(SortOrderOf(location, b));
                return byOrder != 0 ? byOrder : a.CompareTo(b);
            });

            return indices;
        }

        private static int SortOrderOf(MetaLocation location, int index) =>
            location.Items[index]?.SortOrder ?? 0;

        private static Rect RectFor(MetaItemDefinition item, Rect backgroundRect, float scale)
        {
            var size = item.Sprite.rect.size * scale;
            var anchor = AnchorFor(item, backgroundRect);

            // pivot is expressed in UI terms (y = 0 is the sprite's BOTTOM), so the top
            // edge sits a full height above the anchor when the pivot is at the bottom.
            return new Rect(
                anchor.x - item.Pivot.x * size.x,
                anchor.y - (1f - item.Pivot.y) * size.y,
                size.x,
                size.y);
        }

        private static Vector2 AnchorFor(MetaItemDefinition item, Rect backgroundRect) =>
            new(
                backgroundRect.x + item.NormalizedPosition.x * backgroundRect.width,
                // yMax minus, not y plus: a normalized Y of 0 is the bottom of the art.
                backgroundRect.yMax - item.NormalizedPosition.y * backgroundRect.height);

        private static Rect FitRect(Rect area, float aspect)
        {
            var inset = new Rect(
                area.x, area.y + VerticalSlack, area.width, Mathf.Max(1f, area.height - VerticalSlack * 2f));

            var width = inset.width;
            var height = width / aspect;

            if (height > inset.height)
            {
                height = inset.height;
                width = height * aspect;
            }

            return new Rect(
                inset.x + (inset.width - width) / 2f,
                inset.y + (inset.height - height) / 2f,
                width,
                height);
        }
    }
}
