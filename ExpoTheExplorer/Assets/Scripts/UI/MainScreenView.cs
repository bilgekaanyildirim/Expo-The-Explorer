using System.Collections.Generic;
using ExpoTheExplorer.Core;
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
    // the two scenes, and this class is a pure READER of it -- it never saves.
    // Every write still goes through GameManager, which keeps the single-writer
    // invariant on both balances and the day index.
    //
    // Same hand-wired, never-instantiates-UI pattern as the two popups. The
    // scene itself is built once by MainScreenSceneBuilder (Tools > Expo), which
    // also wires the four references below.
    public class MainScreenView : MonoBehaviour
    {
        // Value text only: the static caption beside it ("DAY") is scene decoration
        // this script never touches, the same split DayCompletePopupView's receipt
        // rows use. It also keeps authored text out of code, which the root
        // CLAUDE.md invariant asks for.
        //
        // The wallet is deliberately NOT here any more: D-013 made the HUD Canvas a
        // prefab present in this scene too, so coins and gems are displayed by
        // SoftMoneyView/GemsView on that prefab. Two labels showing one balance is
        // how a screen ends up contradicting itself.
        [SerializeField] private TMP_Text dayValueText;
        [SerializeField] private Button playButton;

        private void Start()
        {
            if (!ValidateReferences()) return;

            var profile = new PlayerProfileStore().Load();

            // The player-facing day number is the catalog POSITION + 1, not a Day
            // file's own dayIndex (which only decides sort order and is not
            // readable from here -- the catalog lives in the day scene). A save
            // pointing past the last authored Day is clamped by GameManager when
            // the day actually loads, so this can read one day high in that case
            // rather than the screen and the game disagreeing silently.
            dayValueText.text = (profile.CurrentDayIndex + 1).ToString();

            playButton.onClick.AddListener(OnPlayClicked);
        }

        private void OnDestroy()
        {
            if (playButton != null) playButton.onClick.RemoveListener(OnPlayClicked);
        }

        private void OnPlayClicked()
        {
            SceneFlow.LoadDay();
        }

        // Every field here is wired by hand in the Editor (or by the scene builder)
        // -- a missing one should fail loudly with a clear pointer to which field,
        // not a bare NullReferenceException.
        private bool ValidateReferences()
        {
            var missing = new List<string>();
            if (dayValueText == null) missing.Add(nameof(dayValueText));
            if (playButton == null) missing.Add(nameof(playButton));

            if (missing.Count == 0) return true;

            Debug.LogError($"{nameof(MainScreenView)} on '{name}' is missing Inspector reference(s): {string.Join(", ", missing)}.", this);
            return false;
        }
    }
}
