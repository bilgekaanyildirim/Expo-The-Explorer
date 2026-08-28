using System.Collections.Generic;
using DG.Tweening;
using ExpoTheExplorer.Bootstrap;
using ExpoTheExplorer.Core;
using ExpoTheExplorer.Data;
using ExpoTheExplorer.Systems.PowerupSystem;
using UnityEngine;

namespace ExpoTheExplorer.UI
{
    // The DOING half of GDD 5.2 #3 -- Noise Clear. What to clear is decided in
    // PowerupEffects.PlanNoiseClear (pure, and therefore testable); this class carries the
    // plan out and drops each cleared item off the board instead of blinking it out (D-120,
    // the user's ask on 2026-08-28).
    //
    // It is the same split AutoCollectRunner already stands on, and it exists for the same
    // reason: a tween needs a scene object and a MonoBehaviour, and PowerupSystem's whole
    // value is that it has neither.
    //
    // THE DAY DOES NOT WAIT FOR THE ANIMATION. The model removal is instant and the charge is
    // spent on the press; what falls is a corpse, already gone from the board as far as every
    // rule in the game is concerned. A powerup that froze the board to play its own animation
    // would be spending the time pressure it exists to relieve -- and this one is the powerup
    // whose whole job is to give the player room.
    //
    // NOT the old "dim the noise for a few seconds" design returning (root CLAUDE.md records
    // that correction). Nothing here is a duration the player waits out; the board is clear
    // the instant they press.
    public class NoiseClearRunner : MonoBehaviour
    {
        // Serialized and dragged, never searched for -- a runtime lookup for a scene
        // reference is ruled out project-wide. The same three references AutoCollectRunner
        // takes, minus the trays it does not touch.
        [SerializeField] private GameManager gameManager;

        [Tooltip("The scene's BoardView. Needed to take the cleared item's visual off the board's pool before it falls.")]
        [SerializeField] private BoardView boardView;

        [Tooltip("Where the fall distance, the durations and the per-item stagger are authored.")]
        [SerializeField] private BoardAnimationConfig animConfig;

        // Every tween this runner started, so a scene load or a retry mid-fall cannot leave a
        // half-faded item hanging in the air. Killed in OnDestroy, and each entry removes
        // itself when it completes.
        private readonly List<Tween> falling = new();

        private bool IsValid => gameManager != null && boardView != null && animConfig != null;

        // Returns whether anything was cleared, which is the powerup's charge rule (GDD 5.2):
        // a press with nothing to do costs nothing. That answer comes from the MODEL removals,
        // never from the tweens -- an item removed with no visual to drop is still cleared.
        public bool Run()
        {
            if (!IsValid)
            {
                Debug.LogWarning(
                    $"{nameof(NoiseClearRunner)} on '{name}' is missing a reference, so Noise Clear cannot animate. " +
                    "Drag in the GameManager, the BoardView and the BoardAnimationConfig.", this);
                return false;
            }

            var state = gameManager.State;
            if (state?.Board == null) return false;

            var plan = PowerupEffects.PlanNoiseClear(state, TrayContents(state));
            var removed = 0;

            foreach (var removal in plan)
            {
                // RE-VERIFIED, not trusted. The plan is a closed list computed before the
                // first removal, and every RemoveItem backfills its cell from the pending
                // queue -- so this cell may now hold something else, possibly a required item
                // that has just landed. Reference equality against the planned instance is
                // what tells the two apart; a coordinate alone cannot.
                if (!ReferenceEquals(state.Board.ItemAt(removal.X, removal.Y), removal.Item)) continue;

                DropVisual(removal.X, removal.Y, removed);

                // AFTER the visual is released, and that ordering is the whole trick: this
                // publishes CellChanged and backfills the cell, so a container still owned by
                // the pool would be hidden mid-fall or handed straight to the new item.
                state.Board.RemoveItem(removal.X, removal.Y);
                removed++;
            }

            return removed > 0;
        }

        // Takes the cleared item's own object off the board's pool and tweens it down and out.
        // Deliberately the SAME object a finger would have grabbed rather than a fresh ghost:
        // a ghost would have to rebuild the item's modification layer to look right, and would
        // drift the first time an item's visuals changed.
        //
        // Silent when there is no visual to take. A cell whose container was never built is a
        // real state (BoardView builds lazily), and the model removal beside this is what
        // actually clears the board -- the fall is decoration.
        private void DropVisual(int x, int y, int index)
        {
            if (!boardView.TryGetDragHandler(x, y, out var handler) || handler == null) return;

            var visual = handler.transform;
            boardView.ReleaseContainer(x, y);

            var delay = animConfig.NoiseClearStagger * index;
            var fallTo = visual.position + Vector3.down * animConfig.NoiseClearFallDistance;

            // One sequence per item so the whole drop can be killed as a unit, and so the
            // object is destroyed exactly once however the tweens end.
            var drop = DOTween.Sequence().SetLink(visual.gameObject);

            if (delay > 0f) drop.AppendInterval(delay);

            drop.Append(visual.DOMove(fallTo, animConfig.NoiseClearFallDuration).SetEase(Ease.InQuad));

            // Joined rather than appended: the item fades WHILE it falls. Every renderer under
            // it, so an item's modification layer goes with it instead of surviving as a
            // floating garnish.
            foreach (var renderer in handler.GetComponentsInChildren<SpriteRenderer>(includeInactive: true))
            {
                drop.Join(renderer.DOFade(0f, animConfig.NoiseClearFadeDuration));
            }

            drop.OnComplete(() =>
            {
                falling.Remove(drop);
                if (visual != null) Destroy(visual.gameObject);
            });

            falling.Add(drop);
        }

        // The trays feed the count rule (D-118): a cola already in a tray is one the board no
        // longer has to hold. Read here rather than passed in, because this runner is the one
        // object that holds both the plan's inputs.
        private IReadOnlyList<BoardItem>[] TrayContents(GameState state)
        {
            var contents = new IReadOnlyList<BoardItem>[GameState.TicketSlotCount];
            var trays = gameManager.TrayManager;
            if (trays == null) return contents;

            for (var slot = 0; slot < contents.Length; slot++)
            {
                contents[slot] = trays.GetContents(slot);
            }

            return contents;
        }

        // A tween outliving its object is the one way this leaks something visible. SetLink
        // already covers the object being destroyed; this covers the runner going away while
        // items are still in the air.
        private void OnDestroy()
        {
            foreach (var tween in falling)
            {
                tween?.Kill();
            }

            falling.Clear();
        }
    }
}
