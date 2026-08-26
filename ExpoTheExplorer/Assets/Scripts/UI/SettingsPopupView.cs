using System.Collections.Generic;
using ExpoTheExplorer.Bootstrap;
using ExpoTheExplorer.Core;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace ExpoTheExplorer.UI
{
    // The day scene's settings menu (decisions.md D-094): the pause button on the HUD
    // opens it, and it offers Resume, Retry, Main Menu, a haptics switch, and a read-only
    // line saying which day this is and how many stars it is worth so far.
    //
    // IT HOLDS TIME STILL WHILE IT IS OPEN. That is not decoration -- every ticket runs
    // its own countdown, so a menu that let them drain would charge the player lives for
    // reading it. The pause is GameState.IsPaused, which GameManager.Update already had
    // the shape for: it carries two gates around TicketSlotManager.Tick and this is the
    // third. Time.timeScale is deliberately NOT touched (this project has never used it):
    // the tween that flies a delivered order, the reward flight and the UI all keep
    // running, which is what a menu over a frozen day should look like.
    //
    // NOTHING HERE DECIDES WHAT AN EXIT COSTS. Retry and Main Menu call the two
    // GameManager methods the Game Over popup already calls, and those methods hold the
    // money and key rules -- notably that leaving a day that has NOT been lost spends no
    // key and banks nothing (SpendKeyForLostDay only fires on IsAwaitingContinue). A
    // settings menu is not the place to invent an economy rule.
    //
    // TWO CONFIRMATION PANELS RATHER THAN ONE SHARED PANEL, and that is the root
    // invariant rather than a style choice: a shared panel would need code to write the
    // two different questions into a label, which puts authored TEXT into C#. Two panels
    // carry their own text in the scene, exactly as every other label in this game does.
    //
    // Lives on an always-active object (NOT popupRoot itself) so Start() still runs while
    // popupRoot begins inactive -- the same Awake/Start-order reasoning as LivesView and
    // the other two popups. Hand-wired in the Editor; this script never instantiates UI.
    public class SettingsPopupView : MonoBehaviour
    {
        [SerializeField] private GameManager gameManager;

        [Tooltip("The object switched on and off. Keep this view on an always-active parent so Start runs.")]
        [SerializeField] private GameObject popupRoot;

        [Tooltip("The HUD button that opens the menu.")]
        [SerializeField] private Button openButton;

        [Tooltip("Closes the menu and lets the day run again.")]
        [SerializeField] private Button resumeButton;

        [Tooltip("Asks the retry question. The retry itself happens on the confirmation panel.")]
        [SerializeField] private Button retryButton;

        [Tooltip("Asks the main-menu question. Leaving happens on the confirmation panel.")]
        [SerializeField] private Button mainMenuButton;

        [Header("Retry confirmation")]
        [SerializeField] private GameObject retryConfirmRoot;
        [SerializeField] private Button retryConfirmYesButton;
        [SerializeField] private Button retryConfirmNoButton;

        [Header("Main menu confirmation")]
        [SerializeField] private GameObject mainMenuConfirmRoot;
        [SerializeField] private Button mainMenuConfirmYesButton;
        [SerializeField] private Button mainMenuConfirmNoButton;

        [Header("Haptics switch")]
        [SerializeField] private Button hapticsToggleButton;

        [Tooltip("OPTIONAL. Shown while haptics are ON. Leave both indicators empty and the switch still works, it just cannot be read.")]
        [SerializeField] private GameObject hapticsOnIndicator;

        [Tooltip("OPTIONAL. Shown while haptics are OFF.")]
        [SerializeField] private GameObject hapticsOffIndicator;

        [Header("Read-only day readout")]
        [Tooltip("OPTIONAL. Receives the day NUMBER only -- the word beside it belongs to a label in the scene.")]
        [SerializeField] private TMP_Text dayNumberText;

        [Tooltip("OPTIONAL. The filled halves of the three stars, in order. Switched on to match the score so far.")]
        [SerializeField] private GameObject[] filledStars = new GameObject[0];

        private GameState state;

        // "The Day Complete popup is up." Tracked here rather than on GameState because
        // nothing else in the game asks the question -- adding a field there for one
        // reader would be a shared flag with a private meaning.
        private bool dayIsOver;

        private void Start()
        {
            if (!ValidateReferences()) return;

            state = gameManager.State;

            popupRoot.SetActive(false);
            retryConfirmRoot.SetActive(false);
            mainMenuConfirmRoot.SetActive(false);

            openButton.onClick.AddListener(Open);
            resumeButton.onClick.AddListener(Close);
            retryButton.onClick.AddListener(ShowRetryConfirmation);
            mainMenuButton.onClick.AddListener(ShowMainMenuConfirmation);
            retryConfirmYesButton.onClick.AddListener(ConfirmRetry);
            retryConfirmNoButton.onClick.AddListener(CancelRetry);
            mainMenuConfirmYesButton.onClick.AddListener(ConfirmMainMenu);
            mainMenuConfirmNoButton.onClick.AddListener(CancelMainMenu);
            hapticsToggleButton.onClick.AddListener(ToggleHaptics);

            // THE OPEN BUTTON GOES AWAY WHENEVER THE DAY IS OVER -- lost or won -- rather
            // than standing there and refusing (see Open). A control that is visible,
            // pressable and silently does nothing is worse than no control: the player
            // reads it as the game hanging. And on a WON day it would be worse than
            // useless, because this menu's Retry is RetryDay, the lost-day path; the
            // completed day has its own Retry with its own money rules on the Day Complete
            // popup, and two buttons answering that question differently is a bug waiting
            // for a player to find it.
            //
            // Four subscriptions, each covering one edge, and no per-frame check:
            //   LivesDepleted  -- the Game Over popup goes up.
            //   LivesChanged   -- the ways back from it, all of which move lives off 0:
            //                     the paid Continue's refill and RetryDay's.
            //   DayCompleted   -- the Day Complete popup goes up.
            //   TicketAssigned -- a new day has started IN THIS SCENE (Next Day, or a
            //                     completed-day retry). Deliberately not LivesChanged for
            //                     this one: a day finished with all three hearts refills
            //                     3 -> 3, which publishes nothing, and the button would
            //                     stay gone for the rest of the session.
            state.LivesDepleted.Subscribe(OnLivesDepleted);
            state.LivesChanged.Subscribe(OnLivesChanged);
            state.DayCompleted.Subscribe(OnDayCompleted);
            state.TicketAssigned.Subscribe(OnTicketAssigned);
        }

        private void OnDestroy()
        {
            if (state != null)
            {
                state.LivesDepleted.Unsubscribe(OnLivesDepleted);
                state.LivesChanged.Unsubscribe(OnLivesChanged);
                state.DayCompleted.Unsubscribe(OnDayCompleted);
                state.TicketAssigned.Unsubscribe(OnTicketAssigned);
            }

            if (openButton != null) openButton.onClick.RemoveListener(Open);
            if (resumeButton != null) resumeButton.onClick.RemoveListener(Close);
            if (retryButton != null) retryButton.onClick.RemoveListener(ShowRetryConfirmation);
            if (mainMenuButton != null) mainMenuButton.onClick.RemoveListener(ShowMainMenuConfirmation);
            if (retryConfirmYesButton != null) retryConfirmYesButton.onClick.RemoveListener(ConfirmRetry);
            if (retryConfirmNoButton != null) retryConfirmNoButton.onClick.RemoveListener(CancelRetry);
            if (mainMenuConfirmYesButton != null) mainMenuConfirmYesButton.onClick.RemoveListener(ConfirmMainMenu);
            if (mainMenuConfirmNoButton != null) mainMenuConfirmNoButton.onClick.RemoveListener(CancelMainMenu);
            if (hapticsToggleButton != null) hapticsToggleButton.onClick.RemoveListener(ToggleHaptics);
        }

        // REFUSED ON A DAY THAT IS ALREADY OVER, won or lost -- the same condition that
        // hides the button, checked again here so the rule survives a stray click landing
        // in the frame the button disappears. Both cases are the same mistake: the popup
        // that is up already offers these exits under the rules that fit it (Game Over's
        // Retry spends a key because that day WAS lost; Day Complete's reverts a wallet
        // that has already banked), and a second menu would answer the same question
        // differently. Nothing else blocks opening -- during the tutorial, mid-drag, at
        // one life, the menu is always available.
        private void Open()
        {
            if (state == null || state.IsAwaitingContinue || dayIsOver) return;
            if (popupRoot.activeSelf) return;

            state.IsPaused = true;

            // Read once, here, rather than subscribed: the day is frozen behind this
            // panel, so neither the day number nor the star count can move while it is
            // being looked at. A subscription would be strictly more machinery for a
            // value that cannot change.
            RefreshDayReadout();
            RefreshHapticsIndicator();

            popupRoot.SetActive(true);
        }

        // The single place the pause is lifted, so there is exactly one way back into a
        // running day however the menu was left. The confirmation panels are reset too:
        // reopening the menu should never land on a half-answered question.
        private void Close()
        {
            state.IsPaused = false;

            retryConfirmRoot.SetActive(false);
            mainMenuConfirmRoot.SetActive(false);
            popupRoot.SetActive(false);
        }

        private void OnLivesDepleted(int _) => RefreshOpenButton();

        private void OnLivesChanged(int _) => RefreshOpenButton();

        private void OnDayCompleted(int _)
        {
            dayIsOver = true;
            RefreshOpenButton();
        }

        private void OnTicketAssigned((int SlotIndex, Ticket Ticket) _)
        {
            if (!dayIsOver) return;

            dayIsOver = false;
            RefreshOpenButton();
        }

        // The one place the button's visibility is decided, from the two conditions rather
        // than from whichever event happened to fire -- so the order they arrive in cannot
        // matter. IsAwaitingContinue is read live because the flag is cleared by
        // LivesManager without an event of its own; dayIsOver is tracked here because
        // "the day has been completed" is not a question GameState answers.
        private void RefreshOpenButton()
        {
            openButton.gameObject.SetActive(!state.IsAwaitingContinue && !dayIsOver);
        }

        private void ShowRetryConfirmation() => retryConfirmRoot.SetActive(true);

        private void CancelRetry() => retryConfirmRoot.SetActive(false);

        private void ShowMainMenuConfirmation() => mainMenuConfirmRoot.SetActive(true);

        private void CancelMainMenu() => mainMenuConfirmRoot.SetActive(false);

        // Close() BEFORE RetryDay, and the order matters: RetryDay resets the board, the
        // tray, the slots and the lives of a day that carries on running in this same
        // scene -- no load comes to tidy up after it -- so a pause left standing here
        // would freeze the fresh attempt with no menu on screen to lift it.
        private void ConfirmRetry()
        {
            Close();
            gameManager.RetryDay();
        }

        // Close() first here too, though for a weaker reason: the scene is about to be
        // replaced, so this GameState is on its way out either way. It is called anyway
        // rather than relying on that -- "the load will clean it up" is exactly the kind
        // of assumption that stops being true when a route changes.
        private void ConfirmMainMenu()
        {
            Close();
            gameManager.ReturnToMainScreenAbandoningDay();
        }

        // The switch is written to the SESSION, not to disk. GameSession.Save writes the
        // whole profile, so saving here would bank an unfinished day's earnings and break
        // the "a day attempt is atomic" contract; the flip reaches the file at the next
        // ordinary save point instead. See the comment on GameSession.HapticsEnabled.
        //
        // Nothing to re-arm on the way back: HapticsBinder reads this value at the moment
        // it would play, so switching haptics on mid-day makes the very next moment buzz.
        private void ToggleHaptics()
        {
            var session = gameManager.Session;
            if (session == null) return;

            session.HapticsEnabled = !session.HapticsEnabled;
            RefreshHapticsIndicator();
        }

        private void RefreshHapticsIndicator()
        {
            var session = gameManager.Session;
            var enabled = session == null || session.HapticsEnabled;

            if (hapticsOnIndicator != null) hapticsOnIndicator.SetActive(enabled);
            if (hapticsOffIndicator != null) hapticsOffIndicator.SetActive(!enabled);
        }

        // The day number and the stars earned so far. Both are READ-ONLY here -- this menu
        // reports the day, it does not take part in it.
        private void RefreshDayReadout()
        {
            // +1 because CurrentDayIndex is a catalog position and the player counts from
            // one, the same conversion MainScreenView makes for its Play caption. The word
            // beside the number ("Day", "Gun") is a label in the scene: writing it here
            // would put authored text in code.
            if (dayNumberText != null) dayNumberText.text = (state.CurrentDayIndex + 1).ToString();

            // DayLifecycleManager is built by GameManager in Awake and lives as long as the
            // day does, but it is read defensively anyway: this view is reachable from the
            // very first frame the HUD accepts a tap.
            var starsEarned = gameManager.DayLifecycleManager?.StarCount ?? 0;

            for (var i = 0; i < filledStars.Length; i++)
            {
                if (filledStars[i] != null) filledStars[i].SetActive(i < starsEarned);
            }
        }

        // Every field here is wired by hand in the Editor -- a missing one should fail
        // loudly with a pointer to WHICH field, not a bare NullReferenceException three
        // taps later. The optional fields (the two indicators, the day text, the stars)
        // are deliberately absent from this list: the menu's exits must keep working on a
        // half-dressed scene, since they are the way out of it.
        private bool ValidateReferences()
        {
            var missing = new List<string>();
            if (gameManager == null) missing.Add(nameof(gameManager));
            if (popupRoot == null) missing.Add(nameof(popupRoot));
            if (openButton == null) missing.Add(nameof(openButton));
            if (resumeButton == null) missing.Add(nameof(resumeButton));
            if (retryButton == null) missing.Add(nameof(retryButton));
            if (mainMenuButton == null) missing.Add(nameof(mainMenuButton));
            if (retryConfirmRoot == null) missing.Add(nameof(retryConfirmRoot));
            if (retryConfirmYesButton == null) missing.Add(nameof(retryConfirmYesButton));
            if (retryConfirmNoButton == null) missing.Add(nameof(retryConfirmNoButton));
            if (mainMenuConfirmRoot == null) missing.Add(nameof(mainMenuConfirmRoot));
            if (mainMenuConfirmYesButton == null) missing.Add(nameof(mainMenuConfirmYesButton));
            if (mainMenuConfirmNoButton == null) missing.Add(nameof(mainMenuConfirmNoButton));
            if (hapticsToggleButton == null) missing.Add(nameof(hapticsToggleButton));

            if (missing.Count == 0) return true;

            Debug.LogError($"{nameof(SettingsPopupView)} on '{name}' is missing Inspector reference(s): {string.Join(", ", missing)}.", this);
            return false;
        }
    }
}
