using ExpoTheExplorer.Bootstrap;
using ExpoTheExplorer.Data;
using UnityEngine;
using UnityEngine.UI;

namespace ExpoTheExplorer.UI
{
    // Drop this on any Button to make it buzz when tapped. It exists so that giving a
    // dozen buttons a haptic costs no edit to the seven view classes that own them --
    // MainScreenView, DayCompletePopupView, GameOverPopupView, MetaShopView,
    // MetaShopRowView, MetaGroundsView and NoKeysPopupView would otherwise each have
    // grown a haptics field and a Request call per handler.
    //
    // THE DECIDING REASON IS NOT TIDINESS, it is that some of these buttons do not
    // exist in the scene. The shop's BUY button lives on an INACTIVE template row that
    // MetaShopView clones once per offer, so a "list of buttons" field on HapticsBinder
    // would have missed exactly the button most worth feeling. A component on the
    // template comes along with every clone -- and because Awake never runs on the
    // inactive template itself, the template registers nothing and only the live rows do.
    //
    // The moment is serialized rather than fixed at UiTap so a button can be given its
    // own character later (a Gem purchase reading as Success, say) without touching code.
    [RequireComponent(typeof(Button))]
    public class HapticButton : MonoBehaviour
    {
        [Tooltip("The scene's HapticsBinder. Unwired means this button is silent and nothing else changes.")]
        [SerializeField] private HapticsBinder haptics;

        [Tooltip("What this tap should feel like. UiTap is the light tick every button shares; change it for a button that deserves its own answer.")]
        [SerializeField] private HapticMoment moment = HapticMoment.UiTap;

        private Button button;

        private void Awake()
        {
            // GetComponent on this same object, guaranteed by RequireComponent -- not a
            // scene lookup, which this project rules out for references it cannot see.
            button = GetComponent<Button>();
            button.onClick.AddListener(OnClicked);
        }

        private void OnDestroy()
        {
            if (button != null) button.onClick.RemoveListener(OnClicked);
        }

        // Listener order against the view's own handler does not matter, and that is
        // worth saying because it looks like it should. Both run in the same frame, so
        // HapticsService decides between them on priority alone: UiTap sits below every
        // outcome, and a tap that causes something is felt as that something instead.
        private void OnClicked() => haptics?.Request(moment);
    }
}
