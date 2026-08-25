using System.Collections.Generic;
using ExpoTheExplorer.Bootstrap;
using ExpoTheExplorer.Core;
using ExpoTheExplorer.Data;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace ExpoTheExplorer.UI
{
    // Shown once GameState.Lives hits 0 (GDD Section 6 -- Lives System /
    // "continue" mechanic). Offers two paid continues (SoftMoney or Gems,
    // both via LivesManager), a free Retry of the attempt, and a Main Menu
    // button that abandons it for the main screen -- which settles the attempt
    // on the way out (earnings taken back, spending kept) rather than just
    // navigating, see GameManager.ReturnToMainScreenAbandoningDay and
    // decisions.md D-012. The popup's visuals (background, title, icons,
    // button art) are hand-built under Canvas in the Editor -- this script
    // never instantiates UI, it only toggles popupRoot and wires the three
    // buttons already sitting there.
    //
    // Lives on an always-active object (NOT popupRoot itself) so Start()
    // still runs while popupRoot starts inactive -- same Awake/Start-order
    // reasoning as LivesView.
    public class GameOverPopupView : MonoBehaviour
    {
        [SerializeField] private GameManager gameManager;

        [Tooltip("Optional. The scene's HapticsBinder, so a paid Continue landing can be felt. Unwired means a silent rescue and nothing else changes.")]
        [SerializeField] private HapticsBinder haptics;

        [SerializeField] private GameObject popupRoot;
        [SerializeField] private TMP_Text livesRefillText;
        [SerializeField] private Button gemButton;
        [SerializeField] private Button retryButton;
        [SerializeField] private Button mainMenuButton;

        // The out-of-keys explanation, asked on every Retry (key-plan step 5). OPTIONAL,
        // and the direction of that failure is chosen: unwired, Retry is simply never
        // gated. A forgotten drag that costs an uncharged key is a small wrong; one that
        // leaves the player unable to retry a lost day is a large one, and it would be
        // indistinguishable from a bug in the key economy. NoKeysPopupView logs loudly at
        // Start when it cannot work, which is what makes this findable.
        [Tooltip("Shown when Retry is pressed with no keys left. Leave empty and Retry is never gated.")]
        [SerializeField] private NoKeysPopupView noKeysPopup;

        private GameState state;

        private void Start()
        {
            if (!ValidateReferences()) return;

            popupRoot.SetActive(false);

            state = gameManager.State;
            state.LivesDepleted.Subscribe(Show);

            gemButton.onClick.AddListener(OnGemClicked);
            retryButton.onClick.AddListener(OnRetryClicked);
            mainMenuButton.onClick.AddListener(OnMainMenuClicked);
        }

        private void OnDestroy()
        {
            if (state != null) state.LivesDepleted.Unsubscribe(Show);

            gemButton.onClick.RemoveListener(OnGemClicked);
            retryButton.onClick.RemoveListener(OnRetryClicked);
            mainMenuButton.onClick.RemoveListener(OnMainMenuClicked);
        }

        private void Show(int _)
        {
            var config = gameManager.LivesManager.Config;

            popupRoot.SetActive(true);
            livesRefillText.text = state.MaxLives.ToString();
            gemButton.interactable = state.Gems >= config.ContinueGemCost;
            retryButton.interactable = true;
        }

        private void Hide()
        {
            popupRoot.SetActive(false);
        }

        private void OnSoftMoneyClicked()
        {
            if (gameManager.LivesManager.TryContinueWithSoftMoney()) Hide();
        }

        private void OnGemClicked()
        {
            if (!gameManager.LivesManager.TryContinueWithGems()) return;

            // The most expensive tap in the game: hard currency spent to come back from a
            // day that was over. Before D-072 it felt exactly like tapping a menu button,
            // because the gem button's UiTap was the only thing this frame produced --
            // LivesChanged goes UP here and HapticsBinder deliberately reads only the drop.
            //
            // The refusal side needs nothing: Show() disables this button when the player
            // cannot afford it, so an unaffordable Continue is never a click to answer.
            haptics?.Request(HapticMoment.ContinuePurchased);

            Hide();
        }

        private void OnRetryClicked()
        {
            // The key check happens on the CLICK, and the Retry button is never disabled
            // for it (.claude/key-plan.md step 5). A greyed button would leave the player
            // staring at a dead control with no explanation; this way the out-of-keys
            // popup opens over this one and offers the two exits -- wait, or 40 Gems.
            //
            // Note what is NOT gated: Main Menu below. Blocking the only way out of a lost
            // day at zero keys would be a softlock, so that path always works and simply
            // spends nothing when there is nothing to spend (D-068).
            //
            // Hide() is deliberately not called on the refused branch: this popup must
            // stay behind the explanation, or refusing to retry would dump the player onto
            // a finished day with no UI at all.
            if (noKeysPopup != null && !noKeysPopup.HasKeyOrShow()) return;

            gameManager.RetryDay();
            Hide();
        }

        // Abandons the attempt instead of replaying it. No Hide() -- the scene
        // load takes this popup with it -- and no wallet handling here: leaving a
        // failed attempt has money rules (earnings reverted, spending kept, then
        // written), and those belong with the Wallet's owner, not in a view.
        private void OnMainMenuClicked()
        {
            gameManager.ReturnToMainScreenAbandoningDay();
        }

        // Every field here is wired by hand in the Editor -- a missing one
        // should fail loudly with a clear pointer to which field, not a bare
        // NullReferenceException.
        private bool ValidateReferences()
        {
            var missing = new List<string>();
            if (gameManager == null) missing.Add(nameof(gameManager));
            if (popupRoot == null) missing.Add(nameof(popupRoot));
            if (livesRefillText == null) missing.Add(nameof(livesRefillText));
            if (gemButton == null) missing.Add(nameof(gemButton));
            if (retryButton == null) missing.Add(nameof(retryButton));
            if (mainMenuButton == null) missing.Add(nameof(mainMenuButton));

            if (missing.Count == 0) return true;

            Debug.LogError($"{nameof(GameOverPopupView)} on '{name}' is missing Inspector reference(s): {string.Join(", ", missing)}.", this);
            return false;
        }
    }
}
