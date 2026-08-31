using ExpoTheExplorer.Data;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace ExpoTheExplorer.UI
{
    // What a Day-unlocked prop says when it opens: a picture of what it brought and a line
    // explaining it. Filled from the catalog by MetaGroundsView's celebration, which is
    // already the one place that knows a prop has just opened.
    //
    // IT HOLDS THE CELEBRATION OPEN. The camera is zoomed in on the prop while this is up and
    // stays there until the button is pressed (the user's requirement, 2026-08-28) -- the
    // coroutine waits on IsDismissed, and only then moves to the next prop or lets the map
    // zoom back out. That is why the dismiss button is the one part that is NOT optional: a
    // popup with no way out would strand the screen zoomed in behind a full-screen skip
    // catcher, which is the worst failure this screen has available.
    //
    // In a file of its own because it is a MonoBehaviour, which Unity can only bind when the
    // file name matches the class name (D-125, learned by having two prefabs refuse to save).
    // Its prefab's ROOT is its Canvas, because Unity drives an Overlay canvas's rect from the
    // screen and gives a ROOT canvas that treatment in the prefab stage, while a nested one
    // sits at 0x0 and collapses every child into it (D-126, learned twice).
    public class MetaUnlockPopup : MonoBehaviour
    {
        [Tooltip("The prop's name, from the catalog's Display Name.")]
        [SerializeField] private TMP_Text title;

        [Tooltip("The line saying what just opened, from the catalog's Unlock Message.")]
        [SerializeField] private TMP_Text body;

        [Tooltip("The picture of what the prop brought, from the catalog's Unlock Image.")]
        [SerializeField] private Image image;

        [Tooltip("REQUIRED. The button that closes this popup and lets the celebration move on.")]
        [SerializeField] private Button dismissButton;

        [Tooltip("Optional. Assets/Data/BoardAnimationConfig.asset — read for Popup Fade In Duration only. Held HERE rather than by the view that spawns this popup, because the fade is this popup's own presentation and this way MetaGroundsView needs to know nothing about it. Dragged into the PREFAB, so every instance carries it.")]
        [SerializeField] private BoardAnimationConfig animConfig;

        // Read by the celebration coroutine, which cannot use a callback: it is a coroutine
        // waiting frame by frame, not an event handler. Starts true for a popup that could not
        // present itself, so a broken prefab never holds the map hostage.
        public bool IsDismissed { get; private set; }

        public void Bind(string titleText, string message, Sprite picture)
        {
            if (title != null) title.text = titleText;
            if (body != null) body.text = message;

            if (image != null)
            {
                // Hidden rather than left showing whatever the prefab was authored with: a
                // placeholder standing in for a real reward reads as deliberate, and the
                // catalog already falls back to the prop's own sprite before it gets here, so
                // a null at this point means the prop has no art at all.
                image.sprite = picture;
                image.enabled = picture != null;
            }

            if (dismissButton == null)
            {
                Debug.LogError(
                    $"{nameof(MetaUnlockPopup)} on '{name}' has no dismiss Button, so it could never be closed and the " +
                    "map would stay zoomed in behind it. Skipping this popup rather than trapping the player.", this);
                IsDismissed = true;
                return;
            }

            dismissButton.onClick.RemoveAllListeners();
            dismissButton.onClick.AddListener(Dismiss);

            // LAST, once every field above is filled: this popup is instantiated and bound in
            // the same frame, so fading earlier would fade up a panel still carrying the
            // prefab's authored placeholder text. Skipped entirely on the no-button path
            // above, which returns before reaching here -- a popup that is being abandoned
            // should not spend a fifth of a second arriving.
            if (animConfig != null) PopupFade.In(gameObject, animConfig.PopupFadeInDuration);
        }

        // Public so the celebration can close it on a path the player did not take -- the
        // scene going away mid-queue, for instance. Idempotent, so a second press in the same
        // frame cannot advance the queue twice.
        public void Dismiss() => IsDismissed = true;
    }
}
