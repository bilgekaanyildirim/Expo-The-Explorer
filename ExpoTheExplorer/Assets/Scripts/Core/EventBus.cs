using System;

namespace ExpoTheExplorer.Core
{
    // Generic pub/sub used so UI can bind reactively to GameState instead of
    // game logic reaching into UI directly (GDD Section 15 / CLAUDE.md Architecture).
    public class EventBus<T>
    {
        private event Action<T> handlers;

        public void Subscribe(Action<T> handler) => handlers += handler;
        public void Unsubscribe(Action<T> handler) => handlers -= handler;
        public void Publish(T payload) => handlers?.Invoke(payload);
    }
}
