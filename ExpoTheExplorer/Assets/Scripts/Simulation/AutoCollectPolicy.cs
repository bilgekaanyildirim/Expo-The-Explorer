using System.Collections.Generic;
using ExpoTheExplorer.Core;
using ExpoTheExplorer.Systems.PowerupSystem;
using ExpoTheExplorer.Systems.TraySystem;

namespace ExpoTheExplorer.Simulation
{
    public class AutoCollectPolicy : IPlayerPolicy
    {
        private readonly TrayManager trayManager;
        private readonly float actionDelaySeconds;
        private readonly Queue<AutoCollectMove> plan = new();

        private float actionCooldownSeconds;

        public AutoCollectPolicy(TrayManager trayManager, float actionDelaySeconds)
        {
            this.trayManager = trayManager;
            this.actionDelaySeconds = actionDelaySeconds < 0f ? 0f : actionDelaySeconds;
            actionCooldownSeconds = this.actionDelaySeconds;
        }

        public void Tick(SimulationView view, float deltaSeconds)
        {
            if (view.State?.Board == null) return;

            if (actionCooldownSeconds > 0f)
            {
                actionCooldownSeconds -= deltaSeconds;
                return;
            }

            if (plan.Count == 0)
            {
                ReturnUnwantedTrayItems(view);
                foreach (var move in PowerupEffects.PlanAutoCollect(view.State, view.TrayContents))
                {
                    plan.Enqueue(move);
                }
            }

            ExecuteNextValidMove(view.State);
            actionCooldownSeconds = actionDelaySeconds;
        }

        private void ReturnUnwantedTrayItems(SimulationView view)
        {
            for (var slot = 0; slot < GameState.TicketSlotCount; slot++)
            {
                foreach (var item in PowerupEffects.UnwantedTrayItems(view.State, slot, view.TrayContents[slot]))
                {
                    if (!trayManager.RemoveItem(slot, item)) continue;
                    view.State.Board.RequestSpawn(item);
                }
            }
        }

        private void ExecuteNextValidMove(GameState state)
        {
            while (plan.Count > 0)
            {
                var move = plan.Dequeue();
                if (move.SlotIndex < 0 || move.SlotIndex >= GameState.TicketSlotCount) continue;

                var ticket = state.TicketSlots[move.SlotIndex];
                if (ticket == null || ticket.State != TicketState.Active) continue;
                if (!ReferenceEquals(state.Board.ItemAt(move.X, move.Y), move.Item)) continue;

                trayManager.TryAddItem(move.SlotIndex, move.Item, () => state.Board.RemoveItem(move.X, move.Y));
                return;
            }
        }
    }
}
