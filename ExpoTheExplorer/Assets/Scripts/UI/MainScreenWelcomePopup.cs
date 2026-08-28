using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace ExpoTheExplorer.UI
{
    // The welcome prefab's own references: the first thing a brand-new player sees, filled in
    // by MainScreenTutorialView from TutorialTextConfig.
    //
    // IN A FILE OF ITS OWN, AND THAT IS A UNITY RULE RATHER THAN A PREFERENCE (D-125). It sat
    // inside MainScreenTutorialView.cs for about half an hour, on the reasoning that
    // PowerupButton sits inside PowerupBarView.cs -- but PowerupButton is a plain
    // [Serializable] class, and a MONOBEHAVIOUR must live in a file matching its class name or
    // Unity has no MonoScript to bind it to. The prefabs were written with `m_Script: {fileID:
    // 0}` and the Editor then refused to save them at all.
    //
    // A component rather than name lookups inside the instance, because this prefab holds
    // three texts and telling them apart by TYPE is exactly the guess that breaks when an
    // author adds a fourth.
    public class MainScreenWelcomePopup : MonoBehaviour
    {
        [Tooltip("The heading. Filled from TutorialTextConfig's Welcome Title.")]
        [SerializeField] private TMP_Text title;

        [Tooltip("The body text saying what the game is. Filled from TutorialTextConfig's Welcome Body.")]
        [SerializeField] private TMP_Text body;

        [Tooltip("The caption on the dismiss button. Filled from TutorialTextConfig's Welcome Dismiss Label.")]
        [SerializeField] private TMP_Text dismissLabel;

        [Tooltip("REQUIRED. The button that closes this panel and hands the tutorial on to the store hint.")]
        [SerializeField] private Button dismissButton;

        // Every field is optional except the button. A missing label costs a line of text; a
        // missing button leaves a MODAL panel the player cannot close, over a Play button they
        // cannot reach -- so that one completes the step instead of showing a trap.
        public void Bind(string titleText, string bodyText, string buttonLabel, Action onDismissed)
        {
            if (title != null) title.text = titleText;
            if (body != null) body.text = bodyText;
            if (dismissLabel != null) dismissLabel.text = buttonLabel;

            if (dismissButton == null)
            {
                Debug.LogError(
                    $"{nameof(MainScreenWelcomePopup)} on '{name}' has no dismiss Button, so this panel could never be " +
                    "closed. Skipping it rather than trapping the player.", this);
                Destroy(gameObject);
                onDismissed?.Invoke();
                return;
            }

            // Cleared first: this instance is fresh today, but a pooled or re-shown one would
            // otherwise advance the tutorial twice from a single press.
            dismissButton.onClick.RemoveAllListeners();
            dismissButton.onClick.AddListener(() => onDismissed?.Invoke());
        }
    }
}
