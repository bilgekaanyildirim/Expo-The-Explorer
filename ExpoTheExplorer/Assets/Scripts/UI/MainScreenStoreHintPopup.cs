using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace ExpoTheExplorer.UI
{
    // The store hint prefab's own references: the arrow that lands on the store button and the
    // plate that explains it. Used for BOTH hint moments -- the first-run invitation and the
    // answer to a Play pressed before the player owns anything -- because they differ only in
    // wording, and a second prefab for one string would be an asset nobody could tell apart.
    //
    // IN A FILE OF ITS OWN for the reason its sibling gives (D-125): a MonoBehaviour must live
    // in a file matching its class name or Unity has no MonoScript to bind it to, and the
    // prefabs were written with `m_Script: {fileID: 0}` until it did.
    //
    // THE ARROW IS EXPOSED because it is the one part of this prefab that does not stay where
    // it was authored: MainScreenTutorialView reparents it onto the store button and sizes it
    // from that button, since only the running layout knows where the button landed. It
    // therefore outlives this object, and the view destroys it explicitly.
    public class MainScreenStoreHintPopup : MonoBehaviour
    {
        [Tooltip("The arrow. Reparented onto the store button at runtime and sized from it — its authored size is only a starting point.")]
        [SerializeField] private RectTransform arrow;

        [Tooltip("The hint's sentence. Filled from TutorialTextConfig — either Store Hint or Play Blocked Hint.")]
        [SerializeField] private TMP_Text text;

        [Tooltip("The caption on the dismiss button. Filled from TutorialTextConfig's Store Hint Dismiss Label.")]
        [SerializeField] private TMP_Text dismissLabel;

        [Tooltip("Optional. Opening the store dismisses this hint too, so a missing button costs a convenience rather than trapping anyone.")]
        [SerializeField] private Button dismissButton;

        public RectTransform Arrow => arrow;

        public void Bind(string message, string buttonLabel, Action onDismissed)
        {
            if (text != null) text.text = message;
            if (dismissLabel != null) dismissLabel.text = buttonLabel;

            // Softer than the welcome panel's rule, and deliberately so: this hint is not
            // modal and is dismissed by opening the store as well as by its own button, so a
            // missing button leaves the player a way out either way.
            if (dismissButton == null)
            {
                Debug.LogWarning(
                    $"{nameof(MainScreenStoreHintPopup)} on '{name}' has no dismiss Button. The hint still closes when " +
                    "the store is opened.", this);
                return;
            }

            dismissButton.onClick.RemoveAllListeners();
            dismissButton.onClick.AddListener(() => onDismissed?.Invoke());
        }
    }
}
