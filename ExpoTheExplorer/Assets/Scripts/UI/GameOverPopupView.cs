using System.Collections.Generic;
using ExpoTheExplorer.Bootstrap;
using ExpoTheExplorer.Core;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace ExpoTheExplorer.UI
{
    // Shown once GameState.Lives hits 0 (GDD Section 6 -- Lives System /
    // "continue" mechanic). Offers two paid continues (SoftMoney or Gems,
    // both via LivesManager) plus a Main Menu button that has nowhere to go
    // yet (single-scene project) and stays non-interactable until a Main
    // Menu scene exists. The popup's visuals (background, title, icons,
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
        [SerializeField] private GameObject popupRoot;
        [SerializeField] private TMP_Text livesRefillText;
        [SerializeField] private Button softMoneyButton;
        [SerializeField] private Button gemButton;
        [SerializeField] private Button mainMenuButton;

        private GameState state;

        private void Start()
        {
            if (!ValidateReferences()) return;

            popupRoot.SetActive(false);

            state = gameManager.State;
            state.LivesDepleted.Subscribe(Show);

            softMoneyButton.onClick.AddListener(OnSoftMoneyClicked);
            gemButton.onClick.AddListener(OnGemClicked);

            // TODO: wire once a Main Menu scene exists.
            mainMenuButton.interactable = false;
        }

        private void OnDestroy()
        {
            if (state != null) state.LivesDepleted.Unsubscribe(Show);

            softMoneyButton.onClick.RemoveListener(OnSoftMoneyClicked);
            gemButton.onClick.RemoveListener(OnGemClicked);
        }

        private void Show(int _)
        {
            var config = gameManager.LivesManager.Config;

            popupRoot.SetActive(true);
            livesRefillText.text = state.MaxLives.ToString();
            softMoneyButton.interactable = state.SoftMoney >= config.ContinueSoftMoneyCost;
            gemButton.interactable = state.Gems >= config.ContinueGemCost;
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
            if (gameManager.LivesManager.TryContinueWithGems()) Hide();
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
            if (softMoneyButton == null) missing.Add(nameof(softMoneyButton));
            if (gemButton == null) missing.Add(nameof(gemButton));
            if (mainMenuButton == null) missing.Add(nameof(mainMenuButton));

            if (missing.Count == 0) return true;

            Debug.LogError($"{nameof(GameOverPopupView)} on '{name}' is missing Inspector reference(s): {string.Join(", ", missing)}.", this);
            return false;
        }
    }
}
