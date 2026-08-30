using System.Collections.Generic;
using ExpoTheExplorer.Core;

namespace ExpoTheExplorer.Simulation
{
    public readonly struct SimulationView
    {
        public GameState State { get; }
        public IReadOnlyList<IReadOnlyList<BoardItem>> TrayContents { get; }
        public float VirtualTime { get; }

        public SimulationView(GameState state, IReadOnlyList<IReadOnlyList<BoardItem>> trayContents, float virtualTime)
        {
            State = state;
            TrayContents = trayContents;
            VirtualTime = virtualTime;
        }
    }

    public interface IPlayerPolicy
    {
        void Tick(SimulationView view, float deltaSeconds);
    }
}
