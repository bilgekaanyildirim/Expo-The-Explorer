using System.Collections;
using System.Collections.Generic;
using ExpoTheExplorer.Core;
using ExpoTheExplorer.Session;
using ExpoTheExplorer.Systems.ProgressionSystem;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace ExpoTheExplorer.UI
{
    // The main screen: where the game opens, and where a finished or abandoned day
    // returns to (decisions.md D-012). Today it is a navigation shell -- the day the
    // player is on and a Play button -- so that the flow between the day and the
    // meta side exists and is correct before any meta CONTENT is designed on top of
    // it. The wallet beside it comes from the shared HUD Canvas prefab, not from
    // here (D-013).
    //
    // It reads the player's state straight from the save file rather than from a
    // GameState, because there is no GameManager in this scene: the day scene is
    // destroyed on the way here, which is exactly what keeps GameManager's "Awake
    // builds the whole Day" shape intact. So the profile IS the hand-off between
    // the two scenes, and this class never SAVES: every write still goes through
    // GameManager, which keeps the single-writer invariant on both balances and the
    // day index. The one exception is the Start Over button (decisions.md D-026),
    // which does not write a value either -- it asks the store to throw the whole
    // file away and reloads the scene, so no number is ever authored from here.
    //
    // Same hand-wired, never-instantiates-UI pattern as the two popups. The
    // scene itself is built once by MainScreenSceneBuilder (Tools > Expo), which
    // also wires the four references below.
    public class MainScreenView : MonoBehaviour
    {
        // The screen's one REQUIRED button (Start Over below is optional). The day readout
        // used to be a label of its own; it now lives ON this button as "Continue Day X"
        // (decisions.md D-025) --
        // the button says what it will do rather than a number sitting next to it saying
        // where you are.
        //
        // The wallet is deliberately NOT here: D-013 made the HUD Canvas a prefab present
        // in this scene too, so coins and gems are displayed by SoftMoneyView/GemsView on
        // it. Two labels showing one balance is how a screen contradicts itself.
        [SerializeField] private Button playButton;

        // The out-of-keys explanation, asked on every Play (key-plan step 5). OPTIONAL on
        // purpose, and this is the one place in this class where an unwired field is not
        // simply cosmetic: leaving it empty means Play is never gated. That is the
        // deliberate direction to fail. A forgotten drag costing the player an uncharged
        // key is a small wrong; a forgotten drag leaving them unable to start the game at
        // all is a large one, and it would look identical to a bug in the key economy.
        // NoKeysPopupView logs loudly at Start when it cannot work, which is what makes
        // the misconfiguration findable rather than silent.
        [Tooltip("Shown when Play is pressed with no keys left. Leave empty and Play is never gated.")]
        [SerializeField] private NoKeysPopupView noKeysPopup;

        [Tooltip("Where the player is: read at click time to see whether they have bought anything yet. Assigned by ExpoTheExplorer > Tutorial > Set Up Main Screen Tutorial.")]
        [SerializeField] private SessionHost sessionHost;

        [Tooltip("Asked to point at the store when Play is pressed before the first building is bought. Assigned by the same menu item.")]
        [SerializeField] private MainScreenTutorialView tutorial;

        // Dragged in, never searched for. The project rule since 2026-08-21: no runtime
        // code resolves a scene reference by name or by type — every one is a serialized
        // field the author wires. That generalises the instruction D-013 already recorded
        // for the HUD. An earlier version of this class found this caption with
        // GetComponentInChildren and argued for it; the user overruled that, and the
        // objection is sound — a search is a guess, and it fails SILENTLY the day the
        // hierarchy changes.
        //
        // Optional: a button with no caption still works, it just says nothing. Cosmetic
        // gap rather than a broken screen, so it warns instead of erroring.
        [Tooltip("The Play button's caption. This class overwrites it with \"Continue Day X\".")]
        [SerializeField] private TMP_Text playLabel;

        // The second button, and the only one that can destroy something: it throws the
        // save away and starts the player over (decisions.md D-026). OPTIONAL on purpose
        // -- a MainScreen built before this existed still runs, and a project that does
        // not want the button on screen just leaves it unwired instead of editing this
        // class. Play is the button this screen cannot work without; this one is not.
        [Header("Start Over")]
        [SerializeField] private Button resetButton;

        // Authored rather than compiled, because it is player-facing text (root CLAUDE.md:
        // content is never embedded in code). The IDLE caption is not here at all -- it is
        // whatever the button says in the scene, cached at Start and never overwritten,
        // which is one less string for this class to own. Only the transient confirm
        // caption needs a home, since no scene object holds it.
        [Tooltip("What the button says after the first tap, while it waits for the confirming second one.")]
        [SerializeField] private string resetConfirmLabel = "Tap again to erase";

        [Tooltip("How long the confirming second tap is accepted. After this the button goes back to its normal caption and the next tap only arms it again.")]
        [SerializeField, Min(0.5f)] private float resetConfirmSeconds = 3f;

        // A two-tap confirm rather than a popup: one mis-tap must not be able to delete a
        // real player's progress, and the cheapest guard that cannot be missed is the
        // button contradicting itself for three seconds. A confirmation dialog would be a
        // second piece of UI to build, wire and localise for one button.
        [Tooltip("The Start Over button's caption. Whatever it says in the scene IS the idle caption — this class caches that and puts it back when the confirm window closes.")]
        [SerializeField] private TMP_Text resetLabel;

        private bool resetArmed;
        private Coroutine disarmRoutine;
        private string resetIdleLabel;

        private void Start()
        {
            if (!ValidateReferences()) return;

            var profile = new PlayerProfileStore().Load();

            // The player-facing day number is the catalog POSITION + 1, not a Day file's
            // own dayIndex (which only decides sort order and is not readable from here --
            // the catalog lives in the day scene). A save pointing past the last authored
            // Day is clamped by GameManager when the day actually loads, so this can read
            // one day high in that case rather than the screen and the game disagreeing
            // silently.
            //
            // Still read from the PROFILE rather than from the session this scene now has
            // (D-022), which means the profile is loaded twice on this screen. Accepted
            // rather than overlooked: taking it from the session would need a third
            // serialized reference on a screen whose missing references have already cost
            // an afternoon, and the only thing read here cannot change while the player is
            // on the menu.
            SetPlayLabel(profile.CurrentDayIndex + 1);

            playButton.onClick.AddListener(OnPlayClicked);

            if (resetButton == null) return;

            // Whatever the scene's caption says IS the idle text, cached once and never
            // overwritten -- one less player-facing string for this class to own.
            resetIdleLabel = resetLabel != null ? resetLabel.text : null;
            resetButton.onClick.AddListener(OnResetClicked);
        }

        private void SetPlayLabel(int playerFacingDayNumber)
        {
            if (playLabel == null)
            {
                Debug.LogWarning(
                    $"{nameof(MainScreenView)} on '{name}': Play Label is not wired, so the button " +
                    "cannot say which day it continues. Drag the caption into that field.", this);
                return;
            }

            playLabel.text = $"Continue Day {playerFacingDayNumber}";
        }

        private void OnDestroy()
        {
            if (playButton != null) playButton.onClick.RemoveListener(OnPlayClicked);
            if (resetButton != null) resetButton.onClick.RemoveListener(OnResetClicked);
        }

        private void OnPlayClicked()
        {
            // Checked HERE, on the click, rather than by grey-ing the button out: a
            // disabled Play tells the player nothing about why, while the popup names the
            // reason and offers both ways forward -- wait, or pay (.claude/key-plan.md
            // step 5). Starting a day is a GATE, not a price: nothing is spent here, and
            // the key is only charged if the day is later given up on (D-068).
            //
            // Unwired, this field is a no-op that logs at Start rather than a lock: see
            // the field's comment for why a forgotten drag must fail OPEN.
            if (noKeysPopup != null && !noKeysPopup.HasKeyOrShow()) return;

            // A player who has bought nothing has not renovated the stall yet, and the game
            // does not open until they have. Same click-time shape as the key gate above and
            // for the identical reason (D-069): Play stays pressable and ANSWERS, because a
            // dead button on a new player's first screen explains nothing.
            //
            // Read live rather than from the profile snapshot this view loaded at Start --
            // buying the building is exactly the event that opens this gate, so a snapshot
            // taken before it would keep the player locked out after they had complied.
            if (IsAwaitingFirstBuilding())
            {
                tutorial.PointAtStoreAfterBlockedPlay();
                return;
            }

            SceneFlow.LoadDay();
        }

        // EVERY uncertainty here resolves toward LET THEM PLAY, and that is not defensiveness
        // -- this gate sits on the only path into the game, so a wrong answer in the other
        // direction is an unplayable build that looks exactly like a save bug. An unwired
        // reference, a session that has not been built, or a null owned-list all mean "do not
        // block", the same stance noKeysPopup takes on a forgotten drag.
        private bool IsAwaitingFirstBuilding()
        {
            if (sessionHost == null || tutorial == null) return false;

            var session = sessionHost.Session;
            if (session?.OwnedMetaItemIds == null) return false;

            return session.OwnedMetaItemIds.Count == 0;
        }

        // First tap arms, second tap within resetConfirmSeconds erases. Nothing is written
        // by this class: the save file's owner is PlayerProfileStore, so the reset is a
        // COMMAND to it (UI rules: UI reads state and asks the owner to change it).
        private void OnResetClicked()
        {
            if (!resetArmed)
            {
                Arm();
                return;
            }

            Disarm();

            // A failed delete leaves the player exactly where they were, which is the
            // honest outcome -- the store has already logged why. Reloading anyway would
            // show them a "fresh" screen still backed by the old file.
            if (!new PlayerProfileStore().Delete()) return;

            // Reload rather than patch. Everything on this screen -- the session and its
            // wallet, the HUD, the grounds, the shop -- is built from the profile in
            // Awake/Start, so rebuilding the scene is the only path that resets all of
            // them through their existing single writers. Editing the live session in
            // place would mean a second writer for the balance, the day index and the
            // owned props, which is precisely what the wallet's `internal` setters exist
            // to prevent.
            SceneFlow.LoadMainScreen();
        }

        private void Arm()
        {
            resetArmed = true;
            if (resetLabel != null) resetLabel.text = resetConfirmLabel;

            // A coroutine rather than a timestamp checked on the next click: the caption
            // has to go back on its own, or the button keeps saying "tap again to erase"
            // long after that tap would no longer erase anything.
            if (disarmRoutine != null) StopCoroutine(disarmRoutine);
            disarmRoutine = StartCoroutine(DisarmAfterDelay());
        }

        private IEnumerator DisarmAfterDelay()
        {
            // Realtime, not scaled: a menu has no reason to run at timeScale 0 today, but
            // a paused-looking screen that never disarms would be a silent trap.
            yield return new WaitForSecondsRealtime(resetConfirmSeconds);

            // Cleared BEFORE the call, so Disarm does not StopCoroutine the routine it is
            // being called from -- stopping a coroutine from inside itself would abandon
            // the rest of Disarm and leave the confirm caption on screen forever.
            disarmRoutine = null;
            Disarm();
        }

        private void Disarm()
        {
            resetArmed = false;
            if (disarmRoutine != null)
            {
                StopCoroutine(disarmRoutine);
                disarmRoutine = null;
            }

            if (resetLabel != null && resetIdleLabel != null) resetLabel.text = resetIdleLabel;
        }

        // Every field here is wired by hand in the Editor (or by the scene builder)
        // -- a missing one should fail loudly with a clear pointer to which field,
        // not a bare NullReferenceException.
        private bool ValidateReferences()
        {
            var missing = new List<string>();
            if (playButton == null) missing.Add(nameof(playButton));

            if (missing.Count == 0) return true;

            Debug.LogError($"{nameof(MainScreenView)} on '{name}' is missing Inspector reference(s): {string.Join(", ", missing)}.", this);
            return false;
        }
    }
}
