using System;
using System.Collections.Generic;
using DG.Tweening;
using ExpoTheExplorer.Data;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace ExpoTheExplorer.UI
{
    // The tutorial's third step: a panel introducing the three powerups, one row each --
    // that powerup's own button image on the left, its name and description on the right --
    // dismissed by a button, after which the tutorial ends and the day runs normally.
    //
    // Built entirely at runtime and authored nowhere, like the rest of the tutorial: no
    // prefab, no scene object, no art, nothing dragged into an Inspector. It is created by
    // PowerupBarView, which is the object that already holds what this step needs -- the
    // three live buttons, and therefore the three icons.
    //
    // IT BUILDS ITS OWN SCREEN SPACE - OVERLAY CANVAS, and that is a lesson rather than a
    // preference (decisions.md D-086): this scene's InGameCanvas is Screen Space - CAMERA
    // at sortingOrder -1, so anything placed on it sits UNDER world sprites and was being
    // drawn and then buried. Overlay is the only mode that composites above everything the
    // camera renders unconditionally.
    public class TutorialPowerupIntroView : MonoBehaviour
    {
        // Below Popup Canvas (Overlay, order 0) on purpose: the day is frozen while this is
        // up, but if anything ever does put a popup on screen alongside it, a tutorial panel
        // floating over that popup would be worse than one behind it.
        private const int CanvasSortingOrder = -1;

        // Everything below is proportion, not pixels: the panel is described relative to the
        // reference resolution the game's own CanvasScaler defines, so it holds its shape on
        // every device rather than being tuned for one.
        private const float PanelWidthFraction = 0.86f;
        private const float RowHeightFraction = 0.13f;
        private const float IconWidthFraction = 0.2f;

        private Action onDismissed;
        private Tween fadeTween;

        // Created with everything already resolved -- this view looks nothing up. iconFor
        // hands back each powerup's button sprite, which only PowerupBarView can answer.
        public static TutorialPowerupIntroView Create(
            PowerupConfig config,
            Canvas gameCanvas,
            TMP_FontAsset font,
            Func<PowerupType, Sprite> iconFor,
            Action onDismissed)
        {
            if (config == null || font == null || iconFor == null) return null;

            var host = new GameObject(nameof(TutorialPowerupIntroView));
            var view = host.AddComponent<TutorialPowerupIntroView>();
            view.onDismissed = onDismissed;
            view.Build(config, gameCanvas, font, iconFor);
            return view;
        }

        private void Build(PowerupConfig config, Canvas gameCanvas, TMP_FontAsset font, Func<PowerupType, Sprite> iconFor)
        {
            var canvasObject = new GameObject("Canvas");
            canvasObject.transform.SetParent(transform, false);

            var canvas = canvasObject.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = CanvasSortingOrder;

            // The game's scaler is copied rather than guessed at, so every proportion below
            // and the font size resolve the same way they do on a ticket card.
            var reference = new Vector2(1080f, 1920f);
            var gameScaler = gameCanvas != null ? gameCanvas.GetComponent<CanvasScaler>() : null;
            if (gameScaler != null)
            {
                var scaler = canvasObject.AddComponent<CanvasScaler>();
                scaler.uiScaleMode = gameScaler.uiScaleMode;
                scaler.referenceResolution = gameScaler.referenceResolution;
                scaler.screenMatchMode = gameScaler.screenMatchMode;
                scaler.matchWidthOrHeight = gameScaler.matchWidthOrHeight;
                scaler.referencePixelsPerUnit = gameScaler.referencePixelsPerUnit;
                if (gameScaler.referenceResolution.y > 0f) reference = gameScaler.referenceResolution;
            }

            // The panel is modal, so it needs its own raycaster and a full-screen backdrop
            // that swallows presses -- otherwise a tap aimed at the panel could fall through
            // to whatever is behind it. The board is gated anyway, but relying on that would
            // make this panel's correctness depend on a rule living somewhere else.
            canvasObject.AddComponent<GraphicRaycaster>();

            var backdrop = NewRect("Backdrop", canvasObject.transform);
            Stretch(backdrop);
            var backdropImage = backdrop.gameObject.AddComponent<Image>();
            backdropImage.color = new Color(0f, 0f, 0f, 0.78f);

            var fontSize = reference.y * 0.022f;

            var panel = NewRect("Panel", canvasObject.transform);
            panel.anchorMin = new Vector2(0.5f, 0.5f);
            panel.anchorMax = new Vector2(0.5f, 0.5f);
            panel.pivot = new Vector2(0.5f, 0.5f);
            panel.sizeDelta = new Vector2(reference.x * PanelWidthFraction, reference.y * (RowHeightFraction * 3f + 0.16f));
            panel.anchoredPosition = Vector2.zero;

            var panelImage = panel.gameObject.AddComponent<Image>();
            panelImage.color = new Color(0.07f, 0.08f, 0.11f, 0.96f);

            var title = NewText("Title", panel, font, fontSize * 1.25f, TextAlignmentOptions.Center);
            title.rectTransform.anchorMin = new Vector2(0f, 1f);
            title.rectTransform.anchorMax = new Vector2(1f, 1f);
            title.rectTransform.pivot = new Vector2(0.5f, 1f);
            title.rectTransform.sizeDelta = new Vector2(0f, fontSize * 2f);
            title.rectTransform.anchoredPosition = new Vector2(0f, -fontSize);
            title.text = "Your powerups";
            title.fontStyle = FontStyles.Bold;

            // One row per powerup, laid out top-down in PowerupTypes.All order -- the same
            // order the HUD bar and the shop render, which is GDD 5.2's own listing order.
            // Positioned by hand rather than with a VerticalLayoutGroup: three rows of known
            // height need no layout pass, and a layout group here would fight the explicit
            // sizes the panel is built from.
            var rowHeight = reference.y * RowHeightFraction;
            var top = -(fontSize * 3.2f);

            for (var i = 0; i < PowerupTypes.All.Length; i++)
            {
                var type = PowerupTypes.All[i];
                BuildRow(panel, type, config.For(type), iconFor(type), font, fontSize, rowHeight, top - i * rowHeight);
            }

            var button = BuildDismissButton(panel, font, fontSize, reference);
            button.onClick.AddListener(Dismiss);

            // Fades in as one unit: the CanvasGroup means the panel, its rows and its button
            // never appear at different times, which a per-graphic fade would allow.
            var group = canvasObject.AddComponent<CanvasGroup>();
            group.alpha = 0f;
            fadeTween = group.DOFade(1f, 0.25f).SetLink(gameObject).SetUpdate(true);
        }

        private void BuildRow(RectTransform panel, PowerupType type, PowerupSettings settings, Sprite icon,
            TMP_FontAsset font, float fontSize, float rowHeight, float top)
        {
            var row = NewRect($"Row_{type}", panel);
            row.anchorMin = new Vector2(0f, 1f);
            row.anchorMax = new Vector2(1f, 1f);
            row.pivot = new Vector2(0.5f, 1f);
            row.sizeDelta = new Vector2(-fontSize * 2f, rowHeight);
            row.anchoredPosition = new Vector2(0f, top);

            var iconWidth = panel.sizeDelta.x * IconWidthFraction;

            if (icon != null)
            {
                var iconRect = NewRect("Icon", row);
                iconRect.anchorMin = new Vector2(0f, 0.5f);
                iconRect.anchorMax = new Vector2(0f, 0.5f);
                iconRect.pivot = new Vector2(0f, 0.5f);
                var iconSize = Mathf.Min(iconWidth, rowHeight * 0.8f);
                iconRect.sizeDelta = new Vector2(iconSize, iconSize);
                iconRect.anchoredPosition = Vector2.zero;

                var iconImage = iconRect.gameObject.AddComponent<Image>();
                iconImage.sprite = icon;

                // The button art is not necessarily square; keeping its aspect is what makes
                // the row read as "that button" rather than as a stretched approximation.
                iconImage.preserveAspect = true;
            }
            else
            {
                // A row with no icon is still a row: the description is the part that
                // teaches, and dropping the whole powerup because its button has no sprite
                // would hide one third of the tutorial over a missing image.
                Debug.LogWarning($"{nameof(TutorialPowerupIntroView)}: {type} has no sprite on its HUD button, so its row is shown without an icon.", this);
            }

            // Text starts after the icon column whether or not an icon was drawn, so the
            // three rows stay aligned with each other rather than each one starting wherever
            // its own art ended.
            var textRect = NewRect("Text", row);
            textRect.anchorMin = new Vector2(0f, 0f);
            textRect.anchorMax = new Vector2(1f, 1f);
            textRect.offsetMin = new Vector2(iconWidth + fontSize * 0.6f, 0f);
            textRect.offsetMax = Vector2.zero;

            var nameText = NewText("Name", textRect, font, fontSize, TextAlignmentOptions.TopLeft);
            Stretch(nameText.rectTransform);
            nameText.rectTransform.offsetMin = new Vector2(0f, rowHeight * 0.45f);
            nameText.text = FallbackName(type, settings);
            nameText.fontStyle = FontStyles.Bold;

            var descriptionText = NewText("Description", textRect, font, fontSize * 0.82f, TextAlignmentOptions.TopLeft);
            Stretch(descriptionText.rectTransform);
            descriptionText.rectTransform.offsetMax = new Vector2(0f, -rowHeight * 0.4f);
            descriptionText.text = settings != null ? settings.Description : string.Empty;
            descriptionText.color = new Color(0.82f, 0.85f, 0.9f);
        }

        // The enum name is a readable last resort rather than a blank row: an unauthored
        // config should look unfinished, not broken.
        private static string FallbackName(PowerupType type, PowerupSettings settings)
        {
            if (settings != null && !string.IsNullOrEmpty(settings.DisplayName)) return settings.DisplayName;
            return type.ToString();
        }

        private Button BuildDismissButton(RectTransform panel, TMP_FontAsset font, float fontSize, Vector2 reference)
        {
            var buttonRect = NewRect("GotItButton", panel);
            buttonRect.anchorMin = new Vector2(0.5f, 0f);
            buttonRect.anchorMax = new Vector2(0.5f, 0f);
            buttonRect.pivot = new Vector2(0.5f, 0f);
            buttonRect.sizeDelta = new Vector2(reference.x * 0.34f, fontSize * 2.6f);
            buttonRect.anchoredPosition = new Vector2(0f, fontSize * 0.9f);

            var image = buttonRect.gameObject.AddComponent<Image>();
            image.color = new Color(0.24f, 0.55f, 0.36f, 1f);

            var label = NewText("Label", buttonRect, font, fontSize, TextAlignmentOptions.Center);
            Stretch(label.rectTransform);
            label.text = "Got it";
            label.fontStyle = FontStyles.Bold;

            var button = buttonRect.gameObject.AddComponent<Button>();
            button.targetGraphic = image;
            return button;
        }

        // Dismissal runs through here whatever triggered it, so the step is advanced exactly
        // once: the button is disabled by the object going away, and onDismissed is cleared
        // before it is called so a second press in the same frame cannot advance twice.
        private void Dismiss()
        {
            var callback = onDismissed;
            onDismissed = null;
            Destroy(gameObject);
            callback?.Invoke();
        }

        private void OnDestroy() => fadeTween?.Kill();

        private static RectTransform NewRect(string name, Transform parent)
        {
            var rect = (RectTransform)new GameObject(name, typeof(RectTransform)).transform;
            rect.SetParent(parent, false);
            return rect;
        }

        private static TextMeshProUGUI NewText(string name, Transform parent, TMP_FontAsset font, float size, TextAlignmentOptions alignment)
        {
            var rect = NewRect(name, parent);
            var text = rect.gameObject.AddComponent<TextMeshProUGUI>();
            text.font = font;
            text.fontSize = size;
            text.alignment = alignment;
            text.color = Color.white;
            text.raycastTarget = false;
            text.textWrappingMode = TextWrappingModes.Normal;
            return text;
        }

        private static void Stretch(RectTransform rect)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
        }
    }
}
