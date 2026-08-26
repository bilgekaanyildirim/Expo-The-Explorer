using UnityEngine;

namespace ExpoTheExplorer.Data
{
    // The words the main screen's first-run tutorial says. An asset rather than fields on
    // the view, because the view is wired by a setup script now and nobody would ever see
    // those fields to edit them -- and because this project's invariant is explicit that
    // text is content and content is never embedded in code.
    //
    // Deliberately three named strings rather than a table keyed by id: there are three, a
    // fourth is a design change, and a lookup table would let a typo'd key fail silently at
    // the exact moment the tutorial is supposed to speak.
    //
    // The DAY scene's tutorial does not read this. Its words live per-Day in the Day JSON
    // (the forced moves' messages) and on PowerupConfig (the powerup descriptions), each
    // beside the thing it describes. Nothing about this screen's welcome belongs to a Day.
    [CreateAssetMenu(fileName = "TutorialTextConfig", menuName = "ExpoTheExplorer/Data/Tutorial Text Config")]
    public class TutorialTextConfig : ScriptableObject
    {
        [Tooltip("Heading of the welcome panel a brand-new player sees on the main screen.")]
        [SerializeField] private string welcomeTitle = "Welcome to the expo";

        [Tooltip("What the game is, in a few lines. Shown once, before the player has played anything.")]
        [SerializeField, TextArea(3, 6)]
        private string welcomeBody =
            "You're running the food stall. Every customer brings one order - drag the matching items off the " +
            "board into their tray before their patience runs out. Serve them well and the day pays.";

        [Tooltip("Shown beside the arrow that points at the store button.")]
        [SerializeField, TextArea(2, 4)]
        private string storeHint = "Spend what you earn on the grounds. Tap the store to start renovating.";

        [Tooltip("Shown when Play is pressed before the player has bought anything — a different moment from the first hint, so it gets its own words rather than repeating them.")]
        [SerializeField, TextArea(2, 4)]
        private string playBlockedHint = "Your stall isn't ready for customers yet. Tap the store to renovate it first.";

        [Tooltip("Caption of the button that closes the welcome panel.")]
        [SerializeField] private string welcomeDismissLabel = "Let's go";

        [Tooltip("Caption of the button that closes the store hint.")]
        [SerializeField] private string storeHintDismissLabel = "Got it";

        public string WelcomeTitle => welcomeTitle;
        public string WelcomeBody => welcomeBody;
        public string StoreHint => storeHint;
        public string PlayBlockedHint => playBlockedHint;
        public string WelcomeDismissLabel => welcomeDismissLabel;
        public string StoreHintDismissLabel => storeHintDismissLabel;
    }
}
