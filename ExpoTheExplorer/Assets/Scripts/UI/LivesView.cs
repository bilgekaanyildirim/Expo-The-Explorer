using System.Collections.Generic;
using ExpoTheExplorer.Bootstrap;
using ExpoTheExplorer.Core;
using UnityEngine;

namespace ExpoTheExplorer.UI
{
    // The day's lives, drawn as a row of hearts instead of the "3/3" label this view
    // used to render (decisions.md D-064). One filled heart switches off per mistake.
    //
    // WHAT THIS VIEW DOES NOT DO, and why: it never touches the EMPTY hearts. The row
    // is authored as three slots whose own Image carries the empty heart, each with a
    // single filled-heart CHILD stacked on top -- so an empty heart is a parent, and
    // switching one off would take its filled child down with it. Driving only the
    // filled objects is therefore the correct rule for this hierarchy rather than a
    // shortcut; an "emptyHearts" array would be actively wrong here.
    //
    // It also has no idea the main screen exists. The heart row is added to the shared
    // HUD Canvas INSTANCE in the day scene, not to the prefab asset, so the main screen
    // simply never receives it and there is no mode to branch on. That is why this view
    // no longer reads through HudWalletSource (D-013/D-014): that indirection existed to
    // let one HUD serve two scenes with different sources, and lives are now a day-scene
    // concern with exactly one source.
    public class LivesView : MonoBehaviour
    {
        // Serialized and dragged, never searched for -- a runtime lookup for a scene
        // reference is ruled out project-wide. GameManager rather than HudWalletSource
        // because lives are read from the live GameState and nothing else can supply
        // them now that they are not persisted.
        [SerializeField] private GameManager gameManager;

        [Tooltip("The filled heart of each slot, in slot order (leftmost first). The empty hearts are their parents and are never touched.")]
        [SerializeField] private GameObject[] filledHearts;

        // Cached rather than re-read from gameManager on every refresh: GameManager
        // builds its GameSession once in Awake and never swaps the State afterwards
        // (AdvanceToNextDay mutates it in place), so the object this subscribes to is
        // the object it will still be unsubscribing from in OnDestroy. Re-walking
        // gameManager.State there instead would break the pairing the day that stops
        // being true, and EventBus removes by delegate equality on a specific instance.
        private GameState state;

        // Start(), not Awake() — Unity's Awake() order across different GameObjects is
        // unspecified and GameManager.Awake is what assigns State, so an Awake here
        // could read null. Every other HUD view in this project binds from Start for
        // exactly this reason.
        private void Start()
        {
            if (!ValidateReferences()) return;

            state = gameManager.State;
            if (state == null)
            {
                Debug.LogError(
                    $"{nameof(LivesView)} on '{name}': the wired {nameof(GameManager)} has no State. " +
                    "The heart row will not update.",
                    this);
                return;
            }

            WarnIfRowIsTooShort();

            // Both events, because they answer different questions and either can move a
            // heart: LoseLife moves Lives alone, while a refill (paid Continue, retry, or
            // the start of a new day) can move Lives and MaxLives together. Distinct from
            // LivesDepleted, which fires once at 0 and drives the game-over popup, not this.
            state.LivesChanged.Subscribe(OnLivesChanged);
            state.MaxLivesChanged.Subscribe(OnLivesChanged);

            Refresh();
        }

        private void OnDestroy()
        {
            if (state == null) return;

            state.LivesChanged.Unsubscribe(OnLivesChanged);
            state.MaxLivesChanged.Unsubscribe(OnLivesChanged);
        }

        // The payload is deliberately dropped, and it is confined to this one adapter
        // line: whichever of the two values changed, the whole row is re-rendered from
        // Lives, so a single new number cannot drive it on its own.
        private void OnLivesChanged(int _) => Refresh();

        // Renders the row from scratch rather than stepping it down by one. A
        // heart-per-mistake animation would be tempting to drive incrementally, but a
        // refill jumps several slots at once and a redraw handles both cases with one
        // rule -- and it cannot drift out of step with Lives the way an incremental
        // counter can.
        private void Refresh()
        {
            var lives = state.Lives;

            for (var i = 0; i < filledHearts.Length; i++)
            {
                var heart = filledHearts[i];
                if (heart == null) continue;

                var filled = i < lives;

                // Guarded because SetActive is not free when it actually flips: it walks
                // the child hierarchy and fires OnEnable/OnDisable. Refresh runs on every
                // lives event, and most of those leave most hearts exactly as they were.
                if (heart.activeSelf != filled) heart.SetActive(filled);
            }
        }

        // A row shorter than MaxLives would show the player fewer lives than they have
        // and would do it silently -- the last heart would simply never light up. Said
        // once, at bind time, rather than per refresh.
        private void WarnIfRowIsTooShort()
        {
            if (filledHearts.Length >= state.MaxLives) return;

            Debug.LogError(
                $"{nameof(LivesView)} on '{name}' has {filledHearts.Length} heart(s) wired but MaxLives is " +
                $"{state.MaxLives}. The row cannot show every life the player has.",
                this);
        }

        // Both fields are wired in the SCENE (the row lives on the day scene's HUD
        // instance, not in the prefab asset), so a missing one is a forgotten drag and
        // should say which field it was rather than throwing on the first life lost.
        private bool ValidateReferences()
        {
            var missing = new List<string>();
            if (gameManager == null) missing.Add(nameof(gameManager));
            if (filledHearts == null || filledHearts.Length == 0) missing.Add(nameof(filledHearts));

            if (missing.Count == 0) return true;

            Debug.LogError($"{nameof(LivesView)} on '{name}' is missing Inspector reference(s): {string.Join(", ", missing)}.", this);
            return false;
        }
    }
}
