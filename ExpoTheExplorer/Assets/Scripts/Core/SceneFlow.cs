using UnityEngine.SceneManagement;

namespace ExpoTheExplorer.Core
{
    // The game's two scenes and the only navigations between them. Both loads are
    // Single mode: leaving a scene destroys it, which is what lets GameManager keep
    // its "Awake builds the whole Day" shape -- nothing has to be torn down by hand
    // on the way out, and the main screen carries no day runtime behind it.
    //
    // Deliberately a thin static holder with no state and no logic. Every decision
    // that goes WITH a navigation -- which day index to persist, whether an
    // abandoned attempt settles first -- lives in GameManager and the two popups,
    // where it is reachable by a test; SceneManager is not. So there is nothing
    // here worth an interface or an injected service, and three call sites sharing
    // two names is the whole reason it exists.
    //
    // A scene name is a structural identifier owned by blueprint.md's scene
    // inventory, not authored content, so naming them here does not run into the
    // "content data is never embedded in code" invariant -- same call
    // PlayerProfileStore already makes for its filename.
    public static class SceneFlow
    {
        public const string MainScreenSceneName = "MainScreen";

        // The day scene kept Unity's template name. Renaming it would move a GUID
        // that Build Settings and every hand-wired reference in it point at, for a
        // cosmetic gain -- not something a navigation task should spend.
        public const string DaySceneName = "SampleScene";

        public static void LoadMainScreen()
        {
            SceneManager.LoadScene(MainScreenSceneName, LoadSceneMode.Single);
        }

        public static void LoadDay()
        {
            SceneManager.LoadScene(DaySceneName, LoadSceneMode.Single);
        }
    }
}
