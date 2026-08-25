using UnityEngine;

namespace ExpoTheExplorer.Data
{
    // The three numbers behind the key economy (.claude/key-plan.md), consumed by
    // KeyManager. Keys are the META resource that gates playing at all -- distinct
    // from Lives, which since decisions.md D-064 are a per-day thing that resets
    // every morning and is never saved. Nothing here reads or writes a life.
    //
    // All three live in data rather than in code because the root invariant says so,
    // and because every one of them is a pacing dial a designer will want to turn
    // without a recompile: how long a locked-out player waits, how many attempts
    // they bank while away, and what skipping the wait costs.
    //
    // The defaults ARE the user's authored values (5 / 30 / 40), not placeholders, so
    // creating this asset in Unity needs no typing. That is deliberate: hand-writing
    // a .asset file has silently shipped missing fields four times in this project
    // (D-004, EconomyConfig, TicketCardVisualsConfig, BoardAnimationConfig), so the
    // asset is made through this menu item instead and the defaults make that safe.
    [CreateAssetMenu(fileName = "KeyConfig", menuName = "ExpoTheExplorer/Data/Key Config")]
    public class KeyConfig : ScriptableObject
    {
        // [Min(1)] rather than a runtime guard: a zero cap would lock the player out
        // permanently and a zero interval would divide by zero, and both are far
        // better refused in the Inspector than defended against on every Refresh.
        [Tooltip("How many keys the player can hold at once. A new player starts full.")]
        [Min(1)]
        [SerializeField] private int maxKeys = 5;

        [Tooltip("Real-world minutes to earn one key back. The clock runs while the game is closed.")]
        [Min(1)]
        [SerializeField] private int regenMinutes = 30;

        [Tooltip("Gem cost to refill keys to the cap. Never grants more than the cap.")]
        [Min(0)]
        [SerializeField] private int refillGemCost = 40;

        public int MaxKeys => maxKeys;
        public int RegenMinutes => regenMinutes;
        public int RefillGemCost => refillGemCost;
    }
}
