using System;
using DG.Tweening;
using ExpoTheExplorer.Data;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace ExpoTheExplorer.UI
{
    // A panel introducing ONE powerup -- that powerup's own button image on the left, its
    // description on the right -- dismissed by a button, after which the day is handed back
    // to the player (or, for a powerup taught at a Day's start, straight to the forced press
    // that follows).
    //
    // IT SHOWED ALL THREE AT ONCE UNTIL D-115, and the change is the user's (2026-08-27):
    // three powerups explained in one panel is three things to remember at a moment when the
    // player has used none of them. Each is now introduced on its own Day, scheduled on
    // PowerupConfig, and the panel that says so has one row.
    //
    // IT IS AN AUTHORED PREFAB SINCE D-116, AND IT USED TO ARGUE THE OPPOSITE. Every comment
    // in this file said "built entirely at runtime and authored nowhere: no prefab, no art,
    // nothing dragged" -- which was right for as long as nobody needed to restyle it, and
    // wrong the moment somebody did (the user, 2026-08-28). A panel assembled from constants
    // in C# cannot be restyled at all; you can only ask a programmer to change a number. So
    // this class stopped BUILDING and started BINDING: the hierarchy lives in
    // Assets/Prefabs/UI/TutorialPowerupIntro.prefab -- seeded once by a TutorialPopupSetup
    // menu step, which was deleted with the other one-shot builders on 2026-08-30, so the
    // prefab on disk is the only copy -- and everything here does is fill it in and take
    // it down.
    //
    // THE ROOT'S CANVAS MUST STAY SCREEN SPACE - OVERLAY, and that is a lesson rather than a
    // preference (decisions.md D-086): this scene's InGameCanvas is Screen Space - CAMERA at
    // sortingOrder -1, so anything parented under it sits UNDER world sprites and is drawn
    // and then buried. Overlay is the only mode that composites above everything the camera
    // renders unconditionally. If a later tidy-up reparents this panel into the game canvas,
    // it will simply vanish, and nothing will report it.
    //
    // EVERY BOUND FIELD IS OPTIONAL. The author owns this prefab now and may delete a piece
    // of it; a missing icon costs the icon, not the lesson. The one thing that is genuinely
    // required is the dismiss button, because without it the panel is a trap -- so its
    // absence is reported loudly and the step is completed rather than left blocking the day.
    public class TutorialPowerupIntroView : MonoBehaviour
    {
        [Tooltip("The powerup's name, printed as the panel's heading.")]
        [SerializeField] private TMP_Text titleText;

        [Tooltip("The one sentence saying what this powerup does. Read from PowerupConfig, never authored here.")]
        [SerializeField] private TMP_Text descriptionText;

        [Tooltip("Filled with the sprite off that powerup's live HUD button, so the row shows the button the player will press.")]
        [SerializeField] private Image iconImage;

        [Tooltip("REQUIRED. The button that closes the panel and lets the tutorial move on.")]
        [SerializeField] private Button dismissButton;

        [Tooltip("Optional. Faded from 0 to 1 on appearance so the panel arrives as one piece. Left empty, it simply appears.")]
        [SerializeField] private CanvasGroup fadeGroup;

        [Tooltip("How long the panel takes to fade in.")]
        [Min(0f)]
        [SerializeField] private float fadeInDuration = 0.25f;

        private Action onDismissed;
        private Tween fadeTween;

        // Created with everything already resolved -- this view looks nothing up. The icon is
        // the sprite on that powerup's live HUD button, which only PowerupBarView can answer.
        //
        // The prefab arrives INSTANTIATED rather than as an asset reference, so this method
        // never touches PrefabUtility and works identically on an object dropped into the
        // scene by hand. Returning null on a broken prefab is what lets the caller skip the
        // lesson instead of leaving a half-built panel over a frozen day.
        public static TutorialPowerupIntroView Create(
            TutorialPowerupIntroView instance,
            PowerupSettings settings,
            Sprite icon,
            Action onDismissed)
        {
            if (instance == null || settings == null) return null;

            instance.onDismissed = onDismissed;
            instance.Bind(settings, icon);
            return instance;
        }

        private void Bind(PowerupSettings settings, Sprite icon)
        {
            if (titleText != null) titleText.text = settings.DisplayName;
            if (descriptionText != null) descriptionText.text = settings.Description;

            if (iconImage != null)
            {
                // Hidden rather than left showing whatever the prefab was authored with: a
                // placeholder sprite standing in for a real powerup is worse than a row with
                // no icon, because it looks deliberate.
                iconImage.sprite = icon;
                iconImage.enabled = icon != null;

                if (icon == null)
                {
                    Debug.LogWarning(
                        $"{nameof(TutorialPowerupIntroView)}: '{settings.DisplayName}' has no sprite on its HUD button, " +
                        "so its row is shown without an icon.", this);
                }
            }

            if (dismissButton == null)
            {
                // The one unrecoverable gap. The panel is modal and the day is frozen behind
                // it, so a panel with no way out is a softlock -- completing the step at once
                // is strictly better than showing something the player cannot dismiss.
                Debug.LogError(
                    $"{nameof(TutorialPowerupIntroView)} on '{name}' has no dismiss Button wired, so this panel could " +
                    "never be closed. Skipping the lesson rather than freezing the day. Drag the button into the " +
                    "prefab's Dismiss Button field.", this);
                Dismiss();
                return;
            }

            // RemoveAllListeners first: this instance may have been pooled or re-shown, and a
            // second listener would advance the tutorial twice from one press.
            dismissButton.onClick.RemoveAllListeners();
            dismissButton.onClick.AddListener(Dismiss);

            // Fades in as one unit: the CanvasGroup means the panel, its row and its button
            // never appear at different times, which a per-graphic fade would allow.
            // SetUpdate(true) because the day is held still while this is on screen.
            if (fadeGroup == null || fadeInDuration <= 0f) return;

            fadeGroup.alpha = 0f;
            fadeTween = fadeGroup.DOFade(1f, fadeInDuration).SetLink(gameObject).SetUpdate(true);
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
    }
}
