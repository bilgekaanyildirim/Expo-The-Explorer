using UnityEngine;

namespace ExpoTheExplorer.Data
{
    // Board colors, shared between BoardView (runtime) and the Day Editor's
    // board-timeline grid preview -- one asset, so tuning a color here is
    // reflected in both instead of drifting between two hardcoded copies.
    [CreateAssetMenu(fileName = "BoardVisualsConfig", menuName = "ExpoTheExplorer/Data/Board Visuals Config")]
    public class BoardVisualsConfig : ScriptableObject
    {
        [SerializeField] private Color cellColorA = new(0.98f, 0.72f, 0.42f);
        [SerializeField] private Color cellColorB = new(0.93f, 0.64f, 0.34f);
        [SerializeField] private Color placeholderItemColor = new(0.85f, 0.35f, 0.12f);
        [SerializeField] private Color frameColor = new(0.45f, 0.25f, 0.1f);

        public Color CellColorA => cellColorA;
        public Color CellColorB => cellColorB;
        public Color PlaceholderItemColor => placeholderItemColor;
        public Color FrameColor => frameColor;
    }
}
