using DG.Tweening;
using ExpoTheExplorer.Data;
using ExpoTheExplorer.Session;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace ExpoTheExplorer.UI
{
    // A brand-new player's first two moments on the main screen: a welcome panel that says
    // what the game is, and then an arrow at the store telling them to start renovating.
    //
    // DELIBERATELY NOT the day scene's TutorialDirector. That system's entire surface is
    // board cells, trays and drop gates, and this screen has no board, no trays and no Day
    // -- reusing it would mean carrying Day-shaped fields into a screen that has none. The
    // two flows share a name and nothing else.
    //
    // The panel here and the day scene's powerup panel are both "modal with text and a
    // dismiss button", which is a repetition of TWO. abstraction-level.md's answer at two is
    // to write it twice; a shared panel builder waits for a third. Written down so the next
    // reader sees the duplication was weighed rather than missed.
    public class MainScreenTutorialView : MonoBehaviour
    {
        [Tooltip("The main screen's composition root — read to find out whether this player has done anything yet. Nothing here writes to it.")]
        [SerializeField] private SessionHost sessionHost;

        [Tooltip("The shop, only so the arrow knows where its store button is. This view never opens or closes it.")]
        [SerializeField] private MetaShopView shop;

        [Tooltip("Every word this tutorial says. Created and assigned by ExpoTheExplorer > Tutorial > Set Up Main Screen Tutorial; edit the asset to change the wording.")]
        [SerializeField] private TutorialTextConfig texts;

        [Tooltip("Sorting order of this view's own Overlay canvas. Above the main screen's canvas (0) so the welcome panel is not buried.")]
        [SerializeField] private int canvasSortingOrder = 50;

        // Serialized rather than constants because this is the third round of tuning them by
        // eye, and neither number can be judged from the code -- only from the screen. The
        // defaults are a starting point, not a decision: the reference resolution is
        // 1080x1920 and the screen's own body text sits at 30-34, so 0.030 puts the hint
        // meaningfully above ordinary UI text, which is what a callout wants.
        [Tooltip("Store hint text size, as a fraction of the reference resolution's height. The main screen's own body text is about 0.017 of it, so a larger value here is deliberate.")]
        [SerializeField, Range(0.012f, 0.06f)] private float storeHintFontScale = 0.030f;

        [Tooltip("Size of the arrow above the store button, as a multiple of that button's height. 1 means exactly as tall as the button.")]
        [SerializeField, Range(0.4f, 3f)] private float storeArrowScale = 1.35f;

        private Canvas canvas;
        private GameObject welcomePanel;
        private GameObject storeHintObject;
        private TMP_FontAsset font;

        private void Start()
        {
            if (!ShouldRun()) return;

            font = FindFont();
            if (font == null)
            {
                Debug.LogWarning(
                    $"{nameof(MainScreenTutorialView)} on '{name}': no TextMeshPro font could be borrowed from this " +
                    "screen, so the welcome and the store hint cannot be drawn. Skipping them.", this);
                return;
            }

            BuildCanvas();
            ShowWelcome();
        }

        // "First opened" is read as "has done nothing yet" rather than as a once-ever flag,
        // which would need a PlayerProfile field and a save-version bump. Derived from state
        // that already exists, so nothing has to remember having shown this -- and it has a
        // property a flag would not: a player who ignores the hint gets it again next time,
        // and it stops on its own the moment they play a day or buy anything.
        private bool ShouldRun()
        {
            if (sessionHost == null || shop == null || texts == null)
            {
                // No hard-coded fallback wording on purpose: a tutorial that silently used
                // built-in text would hide the fact that the setup step was never run, and
                // the words would then live in code, which is exactly what the config exists
                // to prevent.
                Debug.LogError(
                    $"{nameof(MainScreenTutorialView)} on '{name}' is not fully wired (session host, shop or text " +
                    "config missing), so the first-run welcome will never appear. Run " +
                    "ExpoTheExplorer > Tutorial > Set Up Main Screen Tutorial with this scene open.", this);
                return false;
            }

            var session = sessionHost.Session;
            if (session == null) return false;

            return session.State.CurrentDayIndex == 0 && session.OwnedMetaItemIds.Count == 0;
        }

        // This screen's own text is the font source, so the tutorial matches the game with
        // nothing wired by hand -- the same borrow the day-scene tutorial makes off a ticket
        // card. Searching this view's own scene siblings is a component read, not a scene
        // lookup for a reference: nothing is being bound, only a font asset copied.
        private TMP_FontAsset FindFont()
        {
            var sourceText = shop.GetComponentInChildren<TMP_Text>(true);
            return sourceText != null ? sourceText.font : null;
        }

        // Its own Overlay canvas rather than the screen's, so this view can sit above the
        // main screen without reordering anything the designer arranged. This screen's canvas
        // is already Overlay -- unlike the day scene's, which is Screen Space - Camera and
        // buried the first attempt at a tutorial message there (decisions.md D-086) -- so
        // this is about not disturbing the existing hierarchy rather than about being seen.
        private void BuildCanvas()
        {
            // A ROOT object, deliberately not a child of this view. This view sits under the
            // screen's own Canvas, and a Canvas nested inside another Canvas does NOT get the
            // screen's dimensions -- it inherits its parent's RectTransform. This object's
            // rect is zero-sized, so everything built inside was crushed to zero width and
            // TMP wrapped the hint one character per line down a sliver of the screen. As a
            // root object it is a real Overlay canvas, sized by the screen, which is what
            // every proportion in this file assumes.
            var canvasObject = new GameObject("TutorialCanvas");

            canvas = canvasObject.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = canvasSortingOrder;
            canvasObject.AddComponent<GraphicRaycaster>();

            var sourceScaler = shop.GetComponentInParent<Canvas>()?.GetComponent<CanvasScaler>();
            if (sourceScaler == null) return;

            var scaler = canvasObject.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = sourceScaler.uiScaleMode;
            scaler.referenceResolution = sourceScaler.referenceResolution;
            scaler.screenMatchMode = sourceScaler.screenMatchMode;
            scaler.matchWidthOrHeight = sourceScaler.matchWidthOrHeight;
            scaler.referencePixelsPerUnit = sourceScaler.referencePixelsPerUnit;
        }

        private Vector2 ReferenceResolution
        {
            get
            {
                var scaler = canvas != null ? canvas.GetComponent<CanvasScaler>() : null;
                return scaler != null && scaler.referenceResolution.y > 0f
                    ? scaler.referenceResolution
                    : new Vector2(1080f, 1920f);
            }
        }

        // MODAL, because it is a wall of text: a full-screen backdrop with its own raycaster
        // means a tap aimed at the panel cannot fall through to the Play button underneath and
        // start a day the player has not read about yet.
        private void ShowWelcome()
        {
            var reference = ReferenceResolution;
            var fontSize = reference.y * 0.024f;

            welcomePanel = new GameObject("Welcome", typeof(RectTransform));
            var root = (RectTransform)welcomePanel.transform;
            root.SetParent(canvas.transform, false);
            Stretch(root);

            var backdrop = NewRect("Backdrop", root);
            Stretch(backdrop);
            backdrop.gameObject.AddComponent<Image>().color = new Color(0f, 0f, 0f, 0.78f);

            var panel = NewRect("Panel", root);
            panel.anchorMin = new Vector2(0.5f, 0.5f);
            panel.anchorMax = new Vector2(0.5f, 0.5f);
            panel.sizeDelta = new Vector2(reference.x * 0.84f, reference.y * 0.34f);
            panel.gameObject.AddComponent<Image>().color = new Color(0.07f, 0.08f, 0.11f, 0.96f);

            var title = NewText("Title", panel, fontSize * 1.4f, TextAlignmentOptions.Center);
            title.rectTransform.anchorMin = new Vector2(0f, 1f);
            title.rectTransform.anchorMax = new Vector2(1f, 1f);
            title.rectTransform.pivot = new Vector2(0.5f, 1f);
            title.rectTransform.sizeDelta = new Vector2(-fontSize * 2f, fontSize * 2.2f);
            title.rectTransform.anchoredPosition = new Vector2(0f, -fontSize * 1.2f);
            title.text = texts.WelcomeTitle;
            title.fontStyle = FontStyles.Bold;

            var body = NewText("Body", panel, fontSize, TextAlignmentOptions.Center);
            Stretch(body.rectTransform);
            body.rectTransform.offsetMin = new Vector2(fontSize * 1.4f, fontSize * 4.2f);
            body.rectTransform.offsetMax = new Vector2(-fontSize * 1.4f, -fontSize * 4f);
            body.text = texts.WelcomeBody;
            body.color = new Color(0.86f, 0.89f, 0.94f);

            var button = BuildButton("StartButton", panel, texts.WelcomeDismissLabel, fontSize, reference);
            button.onClick.AddListener(ShowStoreHint);

            FadeIn(welcomePanel);
        }

        // NOT modal, and that is the point: the hint asks the player to tap the store, so the
        // store has to be tappable. It has no backdrop and no raycast target of its own, and
        // it is dismissed either by its own button or by the store being opened -- obeying
        // the hint counts as reading it.
        // Play was pressed before the player owns anything. Re-points at the store with its
        // own wording rather than repeating the first-run line, because the two are different
        // moments: one is an invitation, this one is an answer to "why did nothing happen?".
        //
        // Public because MainScreenView owns what Play DOES while this view owns what the
        // store hint SAYS and looks like; the alternative was Play drawing an arrow of its own,
        // which would be a second place that knows where the store button is.
        public void PointAtStoreAfterBlockedPlay()
        {
            if (texts == null || shop == null) return;

            // Already up (the player pressed Play while the first-run hint was still on
            // screen), so there is nothing to add -- rebuilding would stack a second arrow on
            // the button and a second plate on the screen.
            if (storeHintObject != null) return;

            // The canvas is torn down once the tutorial finishes, so a press that arrives
            // after that has to stand one back up. Font too: Start returns early for a player
            // who is past the tutorial, leaving it unresolved.
            if (font == null) font = FindFont();
            if (font == null) return;
            if (canvas == null) BuildCanvas();

            ShowStoreHint(texts.PlayBlockedHint);
        }

        private void ShowStoreHint() => ShowStoreHint(texts.StoreHint);

        private void ShowStoreHint(string message)
        {
            if (welcomePanel != null) Destroy(welcomePanel);
            welcomePanel = null;

            var target = shop.MarketButtonRect;
            if (target == null)
            {
                // MetaShopView's own validation already reports an unwired button; pointing
                // at nothing would be worse than saying nothing.
                Debug.LogWarning(
                    $"{nameof(MainScreenTutorialView)} on '{name}': the shop has no store button, so there is " +
                    "nothing to point at and the store hint is skipped.", this);
                Finish();
                return;
            }

            var reference = ReferenceResolution;
            var fontSize = reference.y * storeHintFontScale;

            storeHintObject = new GameObject("StoreHint", typeof(RectTransform));
            var root = (RectTransform)storeHintObject.transform;
            root.SetParent(canvas.transform, false);
            Stretch(root);

            // Parented to the BUTTON, so the arrow follows wherever the layout puts it rather
            // than trusting a hard-coded corner. Sized from the button so it stays in
            // proportion on every aspect ratio.
            var buttonSize = Mathf.Max(target.rect.height, 1f);

            var arrowSize = buttonSize * storeArrowScale;
            var arrow = NewRect("Arrow", target);
            arrow.anchorMin = new Vector2(0.5f, 1f);
            arrow.anchorMax = new Vector2(0.5f, 1f);

            // PIVOT AT THE CENTRE, which is the whole correctness of this block. With a
            // bottom-centre pivot the -90 below rotated the arrow ABOUT ITS BASE, swinging
            // the entire body a full arrow-length to the right -- and since the store button
            // is already in the bottom-right corner, that put it off the edge of the screen.
            // The same mistake the ticket-card arrow made once (D-085's neighbour); rotating
            // about the centre keeps the arrow where it was placed.
            arrow.pivot = new Vector2(0.5f, 0.5f);
            arrow.sizeDelta = new Vector2(arrowSize, arrowSize);
            arrow.anchoredPosition = new Vector2(0f, arrowSize * 0.75f);

            // The generated sprite points right at rest; -90 aims it DOWN at the button it
            // sits above.
            arrow.localRotation = Quaternion.Euler(0f, 0f, -90f);

            var arrowImage = arrow.gameObject.AddComponent<Image>();
            arrowImage.sprite = CreateArrowSprite();
            arrowImage.raycastTarget = false;

            // Bounces between its resting height and a little higher. Both ends are ABOVE the
            // button, so the arrow never covers the thing it is pointing at.
            arrowBounce = arrow.DOAnchorPosY(arrowSize * 1.05f, 0.7f)
                .SetEase(Ease.InOutSine).SetLoops(-1, LoopType.Yoyo).SetLink(arrow.gameObject);
            arrowObject = arrow.gameObject;

            // The words sit at the bottom of the screen above the button, not beside it: the
            // store is in a corner and a label pinned to it would run off the edge.
            var plate = NewRect("Plate", root);
            plate.anchorMin = new Vector2(0.08f, 0.18f);
            plate.anchorMax = new Vector2(0.92f, 0.18f);
            plate.pivot = new Vector2(0.5f, 0f);
            // Tall enough for the words AND the button beneath them. It was fontSize * 4.4,
            // which was not: the text occupied 1.9..4.0 and the button 0.8..3.3, so they
            // overlapped through the middle and the button -- added second -- printed over
            // the hint it was meant to dismiss. The two bands below are now disjoint: the
            // button lands at 0.72..2.97 (BuildButton is called with fontSize * 0.9 here) and
            // the text starts at 3.5, both in fontSize units off the plate's bottom.
            plate.sizeDelta = new Vector2(0f, fontSize * 6.4f);
            plate.gameObject.AddComponent<Image>().color = new Color(0.07f, 0.08f, 0.11f, 0.94f);

            var text = NewText("Text", plate, fontSize, TextAlignmentOptions.Center);
            Stretch(text.rectTransform);
            text.rectTransform.offsetMin = new Vector2(fontSize, fontSize * 3.5f);
            text.rectTransform.offsetMax = new Vector2(-fontSize, -fontSize * 0.5f);
            text.text = message;

            var button = BuildButton("GotItButton", plate, texts.StoreHintDismissLabel, fontSize * 0.9f, reference);
            button.onClick.AddListener(Finish);

            // Obeying the hint dismisses it too. Added rather than replacing the shop's own
            // listeners, and removed again in Finish, so the store keeps behaving exactly as
            // it does outside the tutorial.
            var marketButton = target.GetComponent<Button>();
            if (marketButton != null) marketButton.onClick.AddListener(Finish);
            dismissOnStoreOpened = marketButton;

            FadeIn(storeHintObject);
            FadeIn(arrow.gameObject);
        }

        private Button dismissOnStoreOpened;
        private Tween arrowBounce;

        // Tracked separately because it is the one thing this view builds OUTSIDE its own
        // hierarchy: the arrow is a child of the store BUTTON so it follows the layout, which
        // means destroying our canvas leaves it hanging on the button forever.
        private GameObject arrowObject;

        private void Finish() => Cleanup();

        // Also runs if this view is destroyed with the tutorial still up. Both the canvas and
        // the arrow are ROOT-or-foreign objects now -- the canvas because a nested one gets no
        // screen size, the arrow because it rides the store button -- so neither dies with
        // this component and both have to be taken down by hand.
        private void OnDestroy() => Cleanup();

        private void Cleanup()
        {
            if (dismissOnStoreOpened != null) dismissOnStoreOpened.onClick.RemoveListener(Finish);
            dismissOnStoreOpened = null;

            arrowBounce?.Kill();
            arrowBounce = null;

            if (arrowObject != null) Destroy(arrowObject);
            arrowObject = null;

            if (storeHintObject != null) Destroy(storeHintObject);
            storeHintObject = null;

            if (welcomePanel != null) Destroy(welcomePanel);
            welcomePanel = null;

            if (canvas != null) Destroy(canvas.gameObject);
            canvas = null;
        }

        private Button BuildButton(string name, RectTransform parent, string label, float fontSize, Vector2 reference)
        {
            var rect = NewRect(name, parent);
            rect.anchorMin = new Vector2(0.5f, 0f);
            rect.anchorMax = new Vector2(0.5f, 0f);
            rect.pivot = new Vector2(0.5f, 0f);
            rect.sizeDelta = new Vector2(reference.x * 0.32f, fontSize * 2.5f);
            rect.anchoredPosition = new Vector2(0f, fontSize * 0.8f);

            var image = rect.gameObject.AddComponent<Image>();
            image.color = new Color(0.24f, 0.55f, 0.36f, 1f);

            var text = NewText("Label", rect, fontSize, TextAlignmentOptions.Center);
            Stretch(text.rectTransform);
            text.text = label;
            text.fontStyle = FontStyles.Bold;

            var button = rect.gameObject.AddComponent<Button>();
            button.targetGraphic = image;
            return button;
        }

        private void FadeIn(GameObject target)
        {
            var group = target.AddComponent<CanvasGroup>();
            group.alpha = 0f;
            group.DOFade(1f, 0.25f).SetLink(target);
        }

        private static RectTransform NewRect(string name, Transform parent)
        {
            var rect = (RectTransform)new GameObject(name, typeof(RectTransform)).transform;
            rect.SetParent(parent, false);
            return rect;
        }

        private TextMeshProUGUI NewText(string name, Transform parent, float size, TextAlignmentOptions alignment)
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

        // Drawn in code for the same reason the day-scene tutorial draws its own: this
        // feature ships no art. A filled head over the right half and a shaft along the left,
        // on a transparent square, so one sprite can simply be rotated to any direction.
        private static Sprite CreateArrowSprite()
        {
            const int size = 64;
            var texture = new Texture2D(size, size) { filterMode = FilterMode.Bilinear };
            var pixels = new Color[size * size];

            for (var y = 0; y < size; y++)
            {
                for (var x = 0; x < size; x++)
                {
                    var fromCentre = Mathf.Abs(y - (size - 1) / 2f);
                    var inHead = x >= size * 0.55f && fromCentre <= (size - x) * 0.62f;
                    var inShaft = x < size * 0.6f && fromCentre <= size * 0.11f;
                    pixels[y * size + x] = inHead || inShaft ? Color.white : Color.clear;
                }
            }

            texture.SetPixels(pixels);
            texture.Apply();
            return Sprite.Create(texture, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), size);
        }
    }
}
