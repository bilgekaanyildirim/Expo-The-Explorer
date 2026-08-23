using UnityEngine;

namespace ExpoTheExplorer.Data
{
    [CreateAssetMenu(fileName = "GameConfig", menuName = "ExpoTheExplorer/Data/Game Config")]
    public class GameConfig : ScriptableObject
    {
        [Header("Board")]
        [Tooltip("Board grid size — starting point is 6x5, not locked (CLAUDE.md Section 4). Keep parametric, do not hardcode elsewhere.")]
        [SerializeField] private int boardWidth = 6;
        [SerializeField] private int boardHeight = 5;

        // What a brand-new player's wallet holds, so the opening balance is authored
        // rather than compiled (root CLAUDE.md: "content data is never embedded in
        // code"). It lives on THIS asset, not on EconomyConfig, because both screens
        // already hold a GameConfig reference -- EconomyConfig is the per-ticket tip
        // math consumed by EconomyCalculator and is not reachable from the main screen,
        // so putting it there would mean a new serialized field that can sit quietly
        // unwired.
        //
        // It is a NEW-PLAYER grant, not a floor: PlayerProfileStore.NewPlayer is the
        // only reader, so a saved profile's own balance always wins and nobody is
        // topped up to this number on later launches.
        [Header("New Player")]
        [Tooltip("Soft money (coins) a player starts the game with, before any day is played. Only used when there is no readable save file — an existing player's balance comes from their profile and is never topped up to this.")]
        [SerializeField, Min(0)] private int startingSoftMoney = 1000;

        public int BoardWidth => boardWidth;
        public int BoardHeight => boardHeight;
        public int StartingSoftMoney => startingSoftMoney;
    }
}
