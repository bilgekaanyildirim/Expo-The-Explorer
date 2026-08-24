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

        // What a day's stars are WORTH. The star count itself is a rule and stays in
        // code (DayLifecycleManager.StarCount, D-008); its exchange rate is a balancing
        // number and must not be, so it is authored here.
        //
        // Same asset as StartingSoftMoney and for the same reason D-026 gave: this is
        // the config both scene roots already hold, while EconomyConfig is the
        // per-ticket tip math consumed by EconomyCalculator. Only the day scene pays
        // this out today, but a "3 stars = 3 gems" readout on the main screen is the
        // obvious next reader, and it would find the number here already.
        [Header("Day Rewards")]
        [Tooltip("Gems paid per star when a day is COMPLETED — 3 stars at 1 each pays 3 gems. A failed or abandoned day pays nothing, because it earns no stars. Set to 0 to turn the reward off entirely.")]
        [SerializeField, Min(0)] private int gemsPerStar = 1;

        public int BoardWidth => boardWidth;
        public int BoardHeight => boardHeight;
        public int StartingSoftMoney => startingSoftMoney;
        public int GemsPerStar => gemsPerStar;
    }
}
