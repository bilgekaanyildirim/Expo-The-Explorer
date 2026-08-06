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

        [Header("Starting Resources")]
        [SerializeField] private int startingLives = 3;
        [SerializeField] private int startingSoftMoney = 0;
        [SerializeField] private int startingGems = 0;
        [SerializeField] private int startingXp = 0;
        [SerializeField] private int startingLevel = 0;

        public int BoardWidth => boardWidth;
        public int BoardHeight => boardHeight;
        public int StartingLives => startingLives;
        public int StartingSoftMoney => startingSoftMoney;
        public int StartingGems => startingGems;
        public int StartingXp => startingXp;
        public int StartingLevel => startingLevel;
    }
}
