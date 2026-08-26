using System.Collections.Generic;
using System.IO;
using ExpoTheExplorer.Core;
using ExpoTheExplorer.Data;
using ExpoTheExplorer.Systems.DaySystem;
using ExpoTheExplorer.Systems.ProgressionSystem;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace ExpoTheExplorer.Editor
{
    // A dev jump: pick any authored Day and enter play mode on it, instead of playing
    // thirty days to reach the one being balanced. Editor-only in every sense -- it lives
    // under Assets/Editor (Editor-only asmdef, ships in no build) and the runtime has no
    // idea it exists. Nothing in Bootstrap, ProgressionSystem or DaySystem changed to make
    // it work; it only calls what those already expose.
    //
    // THE ONE THING THAT IS EASY TO GET WRONG HERE, and the reason this window parses the
    // catalog instead of listing files: PlayerProfile.CurrentDayIndex is a POSITION in the
    // resolved Day catalog, not a Day file's own dayIndex (PlayerProfile says so, and
    // DayCatalogNavigator.GetDayAt indexes the list directly). The two agree today only
    // because day_00..day_40 happen to be contiguous. They stop agreeing the moment a Day
    // fails to resolve -- DayCatalogParser DROPS it, loudly, and every Day after it slides
    // down one -- or the moment a dayIndex is renumbered with a gap. So the list below is
    // produced by the SAME parse the game runs (DayJsonSource + DayCatalogParser), and the
    // number written to the profile is the row's position in it. Reading Resources/Days
    // directly would have been shorter and would have quietly sent the tester to the wrong
    // Day exactly when a Day file is broken, i.e. exactly when they are debugging.
    //
    // It writes ONE field of the profile and writes it through PlayerProfileStore.Save, so
    // the save file keeps its single writer and its version stamp. It deliberately does not
    // touch the wallet, keys, powerup stock or owned props: a tester jumping to Day 30
    // wants their real inventory there, and inventing one here would be a second answer to
    // what PlayerProfileStore.NewPlayer already answers.
    public class DayJumpWindow : EditorWindow
    {
        [MenuItem("ExpoTheExplorer/Play From Day")]
        private static void Open()
        {
            var window = GetWindow<DayJumpWindow>();
            window.titleContent = new GUIContent("Play From Day");
            window.Show();
        }

        // Auto-discovered, overridable -- the same bargain DayEditorWindow's config toolbar
        // makes, for the same reason: "the first asset of this type" is a guess, and a
        // project with two FoodCatalogs would otherwise be stuck with the wrong one.
        [SerializeField] private FoodCatalog catalog;

        // The scenes are found by the names SceneFlow already declares. Those consts are the
        // project's authority on which scenes exist, so this is not a name invented here --
        // but a lookup is still a lookup, so both are overridable by dragging the scene in.
        [SerializeField] private SceneAsset dayScene;
        [SerializeField] private SceneAsset mainScreenScene;

        private List<DayDefinition> days;
        private PlayerProfile profile;
        private string profilePath;
        private Vector2 scroll;

        private void OnEnable()
        {
            titleContent = new GUIContent("Play From Day");
            Reload();
        }

        // Re-read whenever the window is brought forward: entering play at Day 12 and
        // finishing it advances the profile, and a window still showing "you are on Day 12"
        // after that is a stale readout of the one number this tool exists to set.
        private void OnFocus()
        {
            Reload();
        }

        private void Reload()
        {
            catalog = catalog != null ? catalog : FindFirstAsset<FoodCatalog>();
            dayScene = dayScene != null ? dayScene : FindSceneAsset(SceneFlow.DaySceneName);
            mainScreenScene = mainScreenScene != null ? mainScreenScene : FindSceneAsset(SceneFlow.MainScreenSceneName);

            days = catalog == null
                ? new List<DayDefinition>()
                : DayCatalogParser.ParseAll(new DayJsonSource().LoadAll(), catalog);

            // Shown, not used: the store owns the path (its constructor joins it), so this is
            // a readout for the tester -- "this is the file about to be edited" -- and never
            // the thing that is written to. Duplicating the join for display is the smallest
            // honest option; making the store hand out its path would widen a file boundary
            // that is deliberately narrow.
            profilePath = Path.Combine(Application.persistentDataPath, "player_profile.json");
            profile = new PlayerProfileStore().Load();
        }

        private void OnGUI()
        {
            DrawToolbar();

            if (catalog == null)
            {
                EditorGUILayout.HelpBox(
                    "No FoodCatalog found. Drag one into the field above -- the Day catalog cannot be resolved without it.",
                    MessageType.Error);
                return;
            }

            if (days.Count == 0)
            {
                EditorGUILayout.HelpBox(
                    "No Day resolved from Resources/Days. Check the Console: DayCatalogParser drops a Day whose ids do not resolve and says which one.",
                    MessageType.Warning);
                return;
            }

            DrawCurrentPosition();

            if (EditorApplication.isPlaying)
            {
                // The session reads the profile ONCE, when it is constructed (GameSession's
                // constructor). Writing the file mid-play would change nothing on screen and
                // would then be overwritten by the running session's next Save -- so the
                // jump is refused rather than half-applied.
                EditorGUILayout.HelpBox(
                    "Play mode is running. A jump only takes effect when the session is rebuilt, so stop play first.",
                    MessageType.Info);
            }

            using (new EditorGUI.DisabledScope(EditorApplication.isPlaying))
            {
                DrawDayList();
            }
        }

        private void DrawToolbar()
        {
            using (new EditorGUILayout.HorizontalScope(EditorStyles.toolbar))
            {
                if (GUILayout.Button("Refresh", EditorStyles.toolbarButton, GUILayout.Width(60f)))
                {
                    Reload();
                }

                GUILayout.FlexibleSpace();

                catalog = (FoodCatalog)EditorGUILayout.ObjectField(
                    new GUIContent(string.Empty, "Food Catalog"), catalog, typeof(FoodCatalog), false, GUILayout.MaxWidth(120f));

                dayScene = (SceneAsset)EditorGUILayout.ObjectField(
                    new GUIContent(string.Empty, $"Day scene ({SceneFlow.DaySceneName})"), dayScene, typeof(SceneAsset), false, GUILayout.MaxWidth(120f));

                mainScreenScene = (SceneAsset)EditorGUILayout.ObjectField(
                    new GUIContent(string.Empty, $"Main screen scene ({SceneFlow.MainScreenSceneName})"), mainScreenScene, typeof(SceneAsset), false, GUILayout.MaxWidth(120f));
            }
        }

        private void DrawCurrentPosition()
        {
            var current = DayCatalogNavigator.GetDayAt(days, profile.CurrentDayIndex);
            var where = current == null
                ? $"position {profile.CurrentDayIndex} -- past the last authored Day (the game clamps this on load)"
                : $"Day {current.DayIndex}   (position {profile.CurrentDayIndex} of {days.Count})";

            EditorGUILayout.Space(4f);
            EditorGUILayout.LabelField("Saved profile is on", where, EditorStyles.boldLabel);
            EditorGUILayout.LabelField(" ", profilePath, EditorStyles.miniLabel);
            EditorGUILayout.Space(4f);
        }

        private void DrawDayList()
        {
            using var scope = new EditorGUILayout.ScrollViewScope(scroll);
            scroll = scope.scrollPosition;

            for (var position = 0; position < days.Count; position++)
            {
                var day = days[position];
                var isCurrent = position == profile.CurrentDayIndex;

                using (new EditorGUILayout.HorizontalScope(isCurrent ? EditorStyles.helpBox : GUIStyle.none))
                {
                    EditorGUILayout.LabelField(
                        isCurrent ? $"> Day {day.DayIndex}" : $"   Day {day.DayIndex}",
                        isCurrent ? EditorStyles.boldLabel : EditorStyles.label,
                        GUILayout.Width(90f));

                    EditorGUILayout.LabelField(
                        $"{day.TicketsRequiredForDay} required  ·  {day.TicketSequence.Count} authored",
                        EditorStyles.miniLabel);

                    GUILayout.FlexibleSpace();

                    // Two entry points because they test different things. "Play" drops
                    // straight into the day scene, which is the fast loop for balancing a
                    // Day -- and it deliberately skips the key spend and the meta screen,
                    // because those are the main screen's job and are not what is being
                    // tested. "Main Screen" is the real route in, keys and all, for when the
                    // thing under test IS the way into the day.
                    if (GUILayout.Button("Play", GUILayout.Width(52f)))
                    {
                        JumpTo(position, dayScene, SceneFlow.DaySceneName);
                    }

                    if (GUILayout.Button("Main Screen", GUILayout.Width(90f)))
                    {
                        JumpTo(position, mainScreenScene, SceneFlow.MainScreenSceneName);
                    }
                }
            }
        }

        private void JumpTo(int position, SceneAsset scene, string sceneName)
        {
            if (scene == null)
            {
                EditorUtility.DisplayDialog(
                    "Scene not found",
                    $"No scene named '{sceneName}' was found. Drag it into the field in this window's toolbar.",
                    "OK");
                return;
            }

            var target = days[position];
            var currentDay = DayCatalogNavigator.GetDayAt(days, profile.CurrentDayIndex);
            var from = currentDay == null ? $"position {profile.CurrentDayIndex}" : $"Day {currentDay.DayIndex}";

            // A confirm rather than a silent write, because this edits the REAL save file:
            // the tester's own progress moves with it, and jumping back to Day 3 to check a
            // tutorial is indistinguishable at the file level from losing thirty days of it.
            // The wallet, keys and props are untouched, which is what the dialog says out loud.
            if (!EditorUtility.DisplayDialog(
                    "Play From Day",
                    $"Move the saved profile from {from} to Day {target.DayIndex} and open '{sceneName}'?\n\n" +
                    "Coins, gems, keys, powerups and owned props are left exactly as they are.",
                    $"Go to Day {target.DayIndex}",
                    "Cancel"))
            {
                return;
            }

            // Asked BEFORE the profile is written, so cancelling the save prompt cancels the
            // whole jump rather than leaving the file moved and the editor still sitting on
            // the old scene.
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
            {
                return;
            }

            profile.CurrentDayIndex = position;
            new PlayerProfileStore().Save(profile);

            EditorSceneManager.OpenScene(AssetDatabase.GetAssetPath(scene), OpenSceneMode.Single);
            EditorApplication.isPlaying = true;
        }

        private static T FindFirstAsset<T>() where T : Object
        {
            var guids = AssetDatabase.FindAssets($"t:{typeof(T).Name}");
            return guids.Length == 0 ? null : AssetDatabase.LoadAssetAtPath<T>(AssetDatabase.GUIDToAssetPath(guids[0]));
        }

        // FindAssets' name filter is a partial match, so "SampleScene" would also answer with
        // "SampleSceneOld". The exact-filename check is what makes this a lookup of the name
        // SceneFlow declares rather than of anything that merely resembles it.
        private static SceneAsset FindSceneAsset(string sceneName)
        {
            foreach (var guid in AssetDatabase.FindAssets($"t:SceneAsset {sceneName}"))
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                if (Path.GetFileNameWithoutExtension(path) == sceneName)
                {
                    return AssetDatabase.LoadAssetAtPath<SceneAsset>(path);
                }
            }

            return null;
        }
    }
}
