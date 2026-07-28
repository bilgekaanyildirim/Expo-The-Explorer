using ExpoTheExplorer.Core;
using ExpoTheExplorer.Data;
using UnityEngine;

namespace ExpoTheExplorer.Bootstrap
{
    // Thin entry point: builds the central GameState from config and exposes it.
    // No gameplay logic belongs here — systems will bind to State once they exist.
    public class GameManager : MonoBehaviour
    {
        [SerializeField] private GameConfig gameConfig;

        public GameState State { get; private set; }

        private void Awake()
        {
            State = new GameState(gameConfig);
        }
    }
}
