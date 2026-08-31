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
    // BOTH POPUPS ARE AUTHORED PREFABS SINCE D-123, where this file used to assemble a
    // canvas, a backdrop, two panels, three labels, two buttons and a generated arrow sprite
    // out of literals and multipliers. The user asked for them to be prefabs, and the code
    // they replace makes the case on its own: the hint's plate was `fontSize * 6.4f`, carrying
    // a comment explaining how the text band and the button band had been made disjoint by
    // arithmetic -- a layout that breaks the next time somebody changes a font size. An
    // authored prefab has no such coupling.
    //
    // WHAT IS STILL COMPUTED, AND WHY. The arrow is reparented onto the store BUTTON at
    // runtime and sized from it, because only the running screen knows where the layout put
    // that button and how big it ended up. Everything else -- colours, fonts, spacing,
    // positions -- is the author's.
    public class MainScreenTutorialView : MonoBehaviour
    {
        [Tooltip("The main screen's composition root — read to find out whether this player has done anything yet. Nothing here writes to it.")]
        [SerializeField] private SessionHost sessionHost;

        [Tooltip("The shop, only so the arrow knows where its store button is. This view never opens or closes it.")]
        [SerializeField] private MetaShopView shop;

        [Tooltip("Every word this tutorial says. Created and assigned by ExpoTheExplorer > Tutorial > Set Up Main Screen Tutorial; edit the asset to change the wording.")]
        [SerializeField] private TutorialTextConfig texts;

        // Assigned by the same menu step that assigns `texts`, so neither needs dragging.
        // Both carry their own Screen Space - Overlay canvas: this view sits UNDER the
        // screen's canvas, and a Canvas nested in another Canvas inherits its parent's
        // RectTransform rather than the screen's -- this object's rect is zero-sized, which
        // once crushed the hint to one character per line down a sliver of the screen.
        // Instantiating them parentless is what keeps them screen-sized.
        [Tooltip("The first-run welcome panel. Built by ExpoTheExplorer > Tutorial > Set Up Main Screen Tutorial.")]
        [SerializeField] private MainScreenWelcomePopup welcomePrefab;

        [Tooltip("The store hint: the arrow that lands on the store button, and the plate that explains it.")]
        [SerializeField] private MainScreenStoreHintPopup storeHintPrefab;

        [Tooltip("Optional. Assets/Data/BoardAnimationConfig.asset — read for Popup Fade In Duration only, so these two plates arrive at the same speed as every other popup in the game. Unwired, they appear instantly.")]
        [SerializeField] private BoardAnimationConfig animConfig;

        // THE ONE TUNING NUMBER THAT SURVIVED, and it survived for a reason rather than by
        // omission: the arrow is reparented onto a button whose size only the running layout
        // knows, so sizing it from that button is what keeps a restyled button's arrow in
        // proportion. `canvasSortingOrder` and `storeHintFontScale` went with D-123 -- each
        // prefab's own canvas and its own TMP carry those now, and keeping either would have
        // left a second authority over the prefab's look.
        [Tooltip("Size of the arrow above the store button, as a multiple of that button's height. 1 means exactly as tall as the button.")]
        [SerializeField, Range(0.4f, 3f)] private float storeArrowScale = 1.35f;

        private MainScreenWelcomePopup welcome;
        private MainScreenStoreHintPopup storeHint;

        // Tracked separately because it is the one thing this view puts OUTSIDE its own
        // hierarchy: the arrow is reparented onto the store BUTTON so it follows the layout,
        // which means destroying the hint leaves it hanging on the button forever.
        private GameObject arrowObject;
        private Tween arrowBounce;
        private Button dismissOnStoreOpened;

        private void Start()
        {
            if (!ShouldRun()) return;

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

        // MODAL, and the prefab is what makes it so: a full-screen backdrop with its own
        // raycaster means a tap aimed at the panel cannot fall through to the Play button
        // underneath and start a day the player has not read about yet. If that backdrop is
        // ever deleted from the prefab, this stops being true.
        private void ShowWelcome()
        {
            if (welcomePrefab == null)
            {
                // An ERROR, not a warning, and it names the likeliest cause. This is the whole
                // tutorial failing to appear for a brand-new player, which is the most
                // expensive silent failure on this screen -- and the way it happens is
                // mundane: the setup step assigns these in memory and marks the scene dirty,
                // so a scene that was never SAVED afterwards comes back with empty fields.
                Debug.LogError(
                    $"{nameof(MainScreenTutorialView)} on '{name}': no welcome prefab is assigned, so the first-run " +
                    "panel cannot be shown. Run ExpoTheExplorer > Tutorial > Set Up Main Screen Tutorial with " +
                    "MainScreen open AND SAVE THE SCENE -- the step assigns the reference but does not save for you.",
                    this);
                ShowStoreHint(texts.StoreHint);
                return;
            }

            welcome = Instantiate(welcomePrefab);
            welcome.Bind(texts.WelcomeTitle, texts.WelcomeBody, texts.WelcomeDismissLabel, ShowStoreHint);
            FadeIn(welcome.gameObject);
        }

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
            // screen), so there is nothing to add -- instantiating again would stack a second
            // arrow on the button and a second plate on the screen.
            if (storeHint != null) return;

            ShowStoreHint(texts.PlayBlockedHint);
        }

        private void ShowStoreHint() => ShowStoreHint(texts.StoreHint);

        // NOT modal, and that is the point: the hint asks the player to tap the store, so the
        // store has to be tappable. It is dismissed either by its own button or by the store
        // being opened -- obeying the hint counts as reading it.
        private void ShowStoreHint(string message)
        {
            if (welcome != null) Destroy(welcome.gameObject);
            welcome = null;

            if (storeHintPrefab == null)
            {
                Debug.LogError(
                    $"{nameof(MainScreenTutorialView)} on '{name}': no store hint prefab is assigned, so the store " +
                    "hint cannot be shown. Run ExpoTheExplorer > Tutorial > Set Up Main Screen Tutorial with " +
                    "MainScreen open AND SAVE THE SCENE.", this);
                Finish();
                return;
            }

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

            storeHint = Instantiate(storeHintPrefab);
            storeHint.Bind(message, texts.StoreHintDismissLabel, Finish);

            AttachArrowTo(target);

            // Obeying the hint dismisses it too. Added rather than replacing the shop's own
            // listeners, and removed again in Cleanup, so the store keeps behaving exactly as
            // it does outside the tutorial.
            var marketButton = target.GetComponent<Button>();
            if (marketButton != null) marketButton.onClick.AddListener(Finish);
            dismissOnStoreOpened = marketButton;

            FadeIn(storeHint.gameObject);
        }

        // Parented to the BUTTON so the arrow follows wherever the layout puts it, and sized
        // from that button so it stays in proportion on every aspect ratio -- the one thing
        // here the prefab cannot know. Everything else about the arrow, its sprite included,
        // is authored: this used to generate a texture in code, and the same generator still
        // exists in TutorialSpotlightView, so authoring it here removed one of two copies.
        private void AttachArrowTo(RectTransform target)
        {
            var arrow = storeHint.Arrow;
            if (arrow == null) return;

            var buttonSize = Mathf.Max(target.rect.height, 1f);
            var arrowSize = buttonSize * storeArrowScale;

            arrow.SetParent(target, worldPositionStays: false);
            arrow.anchorMin = new Vector2(0.5f, 1f);
            arrow.anchorMax = new Vector2(0.5f, 1f);

            // PIVOT AT THE CENTRE, which is the whole correctness of this block. With a
            // bottom-centre pivot a -90 rotation swings the arrow about its BASE, moving the
            // whole body an arrow-length sideways -- and the store button is already in the
            // bottom-right corner, so that put it off the edge of the screen. The prefab
            // authors the rotation; this pins the pivot the rotation depends on.
            arrow.pivot = new Vector2(0.5f, 0.5f);
            arrow.sizeDelta = new Vector2(arrowSize, arrowSize);
            arrow.anchoredPosition = new Vector2(0f, arrowSize * 0.75f);
            arrow.localScale = Vector3.one;

            arrowObject = arrow.gameObject;

            // Bounces between its resting height and a little higher. Both ends are ABOVE the
            // button, so the arrow never covers the thing it is pointing at.
            arrowBounce = arrow.DOAnchorPosY(arrowSize * 1.05f, 0.7f)
                .SetEase(Ease.InOutSine).SetLoops(-1, LoopType.Yoyo).SetLink(arrow.gameObject);
        }

        private void Finish() => Cleanup();

        // Also runs if this view is destroyed with the tutorial still up. Both popups are ROOT
        // objects (a nested canvas gets no screen size) and the arrow rides the store button,
        // so none of the three dies with this component and all have to be taken down by hand.
        private void OnDestroy() => Cleanup();

        private void Cleanup()
        {
            if (dismissOnStoreOpened != null) dismissOnStoreOpened.onClick.RemoveListener(Finish);
            dismissOnStoreOpened = null;

            arrowBounce?.Kill();
            arrowBounce = null;

            if (arrowObject != null) Destroy(arrowObject);
            arrowObject = null;

            if (storeHint != null) Destroy(storeHint.gameObject);
            storeHint = null;

            if (welcome != null) Destroy(welcome.gameObject);
            welcome = null;
        }

        // These two plates fade in the same way and at the same SPEED as every other popup in
        // the game. They were the first things here to fade at all, and they did it with a
        // 0.25 written into this method -- a tuning number in code, which this project does
        // not allow (CLAUDE.md's first invariant) and which meant the main screen's popups
        // could silently drift away from the day scene's. Both halves now come from the one
        // place: the fade from PopupFade, the number from BoardAnimationConfig.
        private void FadeIn(GameObject target)
        {
            if (animConfig != null) PopupFade.In(target, animConfig.PopupFadeInDuration);
        }
    }
}
