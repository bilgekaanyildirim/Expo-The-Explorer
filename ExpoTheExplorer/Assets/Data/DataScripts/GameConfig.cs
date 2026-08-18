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

        public int BoardWidth => boardWidth;
        public int BoardHeight => boardHeight;
    }
}
