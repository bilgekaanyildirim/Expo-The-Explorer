using UnityEngine;

namespace ExpoTheExplorer.Session
{
    // The one thing a scene has to offer for the shared HUD to work: a GameSession.
    // Both screens have a component that provides one -- GameManager in the day scene,
    // MainScreenRoot on the menu -- and this is what lets a view hold a reference to
    // "whichever of them this scene has" without knowing which.
    //
    // An abstract MonoBehaviour rather than an interface, and that is a Unity constraint
    // rather than a preference: [SerializeField] cannot serialize an interface, so an
    // Inspector field that is both draggable and type-checked has to name a concrete type.
    // The alternatives were a `MonoBehaviour` field plus a runtime cast (which lets any
    // component at all be dragged in and fails at play time instead of in the Inspector)
    // or a runtime lookup, which the user ruled out for this binding in decisions.md D-013.
    //
    // It lives in the Session assembly because it must SEE GameSession, and because
    // Assembly-CSharp can derive from an asmdef type while the reverse is impossible --
    // GameManager and MainScreenRoot both live there.
    //
    // Session is deliberately allowed to be null: it is only assigned in the deriving
    // component's Awake, and Unity does not order Awake across GameObjects. Every reader
    // is a view's Start, which is the same guarantee those views already relied on.
    public abstract class SessionHost : MonoBehaviour
    {
        public abstract GameSession Session { get; }
    }
}
