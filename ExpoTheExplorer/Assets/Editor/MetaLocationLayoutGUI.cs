using System.Collections.Generic;
using ExpoTheExplorer.Data;
using ExpoTheExplorer.Systems.MetaSystem;
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

        // The scale handle's own drag, kept apart from DraggingIndex because the two mean
        // opposite things about the same mouse gesture: one moves the prop, the other
        // resizes it, and a press that starts on the handle must never do both.
        internal int ScalingIndex = -1;

        // Captured on the press, not recomputed per frame: a resize is
        // "start scale x how much further the cursor is now than it was", so both halves
        // of that ratio have to come from the same moment. Deriving it incrementally from
        // Event.delta instead would accumulate its own rounding over a long drag.
        internal float ScaleAtDragStart = 1f;
        internal float DistanceAtDragStart = 1f;

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

        // Both drags end here rather than each case clearing its own field, so a mouse-up
        // that arrives in an unexpected order cannot leave one of them armed -- an armed
        // ScalingIndex would resize a prop on the next unrelated drag.
        internal void EndDrag()
        {
            DraggingIndex = -1;
            ScalingIndex = -1;
        }
    }

    // The one thing this editor window exists for: draw a location's background at its
    // real aspect and let props be DRAGGED onto it, instead of typing twenty pairs of
    // normalized floats. Everything else in the window is fields Unity could have drawn.
    //
    // Two gestures, both the same argument: dragging the prop moves it, dragging the grip
    // at its top-right corner SCALES it (2026-09-04). A size is judged against the art
    // around it and nothing else, so typing 0.8 and recompiling to look is the wrong loop
    // -- the same reason the position was never a pair of typed floats. The grip writes
    // MetaItemDefinition.scale, which is a real authored field the game reads; this is not
    // a preview-only zoom.
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

        // The square you grab to resize the selected prop. A CONSTANT number of screen
        // pixels, deliberately not scaled with the zoom: it is a piece of chrome, and a
        // handle that shrank as you zoomed out would be unhittable on exactly the small
        // props (a 70x104 plant draws ~20px in a fitted 853x1844 background) that need
        // resizing most.
        private const float ScaleHandleSize = 10f;

        // Wide bounds on purpose. This is an authoring nudge, and the author is looking at
        // the result while they drag -- the limits are here to keep a slip from putting a
        // prop at zero (invisible, and nothing on screen says why) or at a size that paints
        // over the whole canvas, not to express an opinion about what looks right.
        private const float MinPropScale = 0.05f;
        private const float MaxPropScale = 10f;

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

                // The continuation layer first, so the grounds land on top of it -- the same
                // order the runtime builds (D-046). Drawn here at all because the alternative
                // is the editor and the game showing different pictures, which is precisely
                // the defect D-045 was reported for; a preview that leaves out a layer is a
                // preview that will be trusted and then contradicted.
                //
                // Centred on the grounds, no offset: that is the contract on the field.
                var continuation = location.BackgroundBgSprite;
                if (continuation != null)
                {
                    var factor = backgroundRect.width / background.rect.width;
                    var size = continuation.rect.size * factor;
                    DayEditorSpriteGUI.DrawTexCoords(
                        new Rect(
                            backgroundRect.center.x - size.x * 0.5f,
                            backgroundRect.center.y - size.y * 0.5f,
                            size.x,
                            size.y),
                        continuation);
                }

                DayEditorSpriteGUI.DrawTexCoords(backgroundRect, background);

                // How much the background got scaled to fit this canvas -- and ONLY that.
                // The prop's own authored nudge is not folded in here: RectFor asks
                // MetaLayout.PropSize, which applies it, so this stays the same number the
                // runtime passes and there is exactly one place that knows about both
                // factors. Deriving it the way the runtime does is what keeps this a
                // preview rather than a second opinion.
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

            // The resize grip, at the TOP-RIGHT of the outline. Top rather than bottom
            // because the default pivot is bottom-centre: the prop's contact point stays
            // put while it grows, so the bottom edge is the one that does not move and
            // putting a grip there would look like it does nothing.
            var handle = ScaleHandleRect(rect);
            EditorGUI.DrawRect(handle, colour);
            EditorGUI.DrawRect(
                new Rect(handle.x + 2f, handle.y + 2f, handle.width - 4f, handle.height - 4f),
                new Color(0.16f, 0.16f, 0.16f));
        }

        // Centred ON the corner rather than tucked inside it, so the grip reads as
        // belonging to the outline and stays grabbable when the prop is smaller than the
        // grip itself -- which is the normal case for a small prop at zoom 1.
        private static Rect ScaleHandleRect(Rect propRect) =>
            new(
                propRect.xMax - ScaleHandleSize * 0.5f,
                propRect.y - ScaleHandleSize * 0.5f,
                ScaleHandleSize,
                ScaleHandleSize);

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

                // Before the move case, because a press on the grip sets ScalingIndex and
                // deliberately leaves DraggingIndex clear -- ordering them this way makes
                // "one gesture does one thing" true by structure rather than by both cases
                // agreeing to check the other's field.
                case EventType.MouseDrag when state.ScalingIndex >= 0:
                    ScaleSelected(items, state, backgroundRect, e.mousePosition);
                    e.Use();
                    return;

                case EventType.MouseDrag when state.DraggingIndex >= 0:
                    DragSelected(items, state, backgroundRect, e.delta);
                    e.Use();
                    return;

                case EventType.MouseUp when state.DraggingIndex >= 0 || state.ScalingIndex >= 0:
                    state.EndDrag();
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

            // The grip wins over every prop, including ones drawn in front of the selected
            // one. It is a 10px target on something the author has already picked out, and
            // the alternative -- a prop overlapping that corner swallowing the press -- is
            // a resize that silently turns into a move of the wrong thing.
            if (BeginScaleAt(location, state, backgroundRect, scale, mouse)) return;

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

        // True when the press landed on the selected prop's resize grip, in which case the
        // gesture is a resize and the caller must not fall through to selection.
        private static bool BeginScaleAt(
            MetaLocation location, MetaLayoutState state, Rect backgroundRect, float scale, Vector2 mouse)
        {
            var index = state.SelectedIndex;
            if (index < 0 || index >= location.Items.Count || state.IsHidden(index)) return false;

            var item = location.Items[index];
            if (item?.Sprite == null) return false;

            if (!ScaleHandleRect(RectFor(item, backgroundRect, scale)).Contains(mouse)) return false;

            state.ScalingIndex = index;
            state.ScaleAtDragStart = item.Scale;

            // The floor is what makes a pivot sitting ON the grip survivable: with a
            // (1, 1) pivot the anchor IS the top-right corner, so the starting distance is
            // ~0 and the ratio below would be an instant jump to the clamp. One pixel of
            // floor turns that into an ordinary, if very sensitive, drag.
            state.DistanceAtDragStart =
                Mathf.Max(1f, Vector2.Distance(AnchorFor(item, backgroundRect), mouse));

            return true;
        }

        // Resize by RATIO from the anchor: however much further the cursor is from the
        // prop's contact point than when the drag began, the prop is that much bigger. It
        // is measured from the anchor rather than from the grip's own corner because the
        // anchor is the one point that does not move while scaling -- the same reason the
        // grip sits opposite it.
        //
        // Position is read from the SerializedProperty, not from the plain MetaLocation:
        // the prop may have been dragged earlier in this same repaint, and the plain object
        // does not catch up until ApplyModifiedProperties.
        private static void ScaleSelected(
            SerializedProperty items, MetaLayoutState state, Rect backgroundRect, Vector2 mouse)
        {
            if (state.ScalingIndex >= items.arraySize)
            {
                state.ScalingIndex = -1;
                return;
            }

            var element = items.GetArrayElementAtIndex(state.ScalingIndex);
            var scaleProperty = element.FindPropertyRelative("scale");
            var positionProperty = element.FindPropertyRelative("normalizedPosition");
            if (scaleProperty == null || positionProperty == null)
            {
                state.ScalingIndex = -1;
                return;
            }

            var position = positionProperty.vector2Value;
            var anchor = new Vector2(
                backgroundRect.x + position.x * backgroundRect.width,
                // yMax minus, for the reason AnchorFor gives: a normalized Y of 0 is the
                // bottom of the art while GUI Y grows downward.
                backgroundRect.yMax - position.y * backgroundRect.height);

            var ratio = Vector2.Distance(anchor, mouse) / state.DistanceAtDragStart;

            scaleProperty.floatValue =
                Mathf.Clamp(state.ScaleAtDragStart * ratio, MinPropScale, MaxPropScale);
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
            // Asked of MetaLayout rather than multiplied out here, which is the change that
            // made a per-prop scale safe to add at all. This file used to keep its own copy
            // of "sprite pixels times the background's fit scale"; a second factor in that
            // formula would have been a second place to forget it, and the editor quietly
            // disagreeing with the game about a prop's size is precisely the
            // preview-that-lies failure this canvas exists to avoid.
            var size = MetaLayout.PropSize(item, scale);
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
