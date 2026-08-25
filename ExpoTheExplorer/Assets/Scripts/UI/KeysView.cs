using System.Collections.Generic;
using TMPro;
using UnityEngine;

namespace ExpoTheExplorer.UI
{
    // The HUD's key readout, "current/max" (decisions.md D-065/D-066). Keys are the meta
    // resource that gates playing a day at all: 5 at most, one back every 30 real minutes,
    // and the clock runs while the game is closed.
    //
    // It reads through HudWalletSource, NOT through a session reference of its own, and
    // that is the opposite of what LivesView does since D-064 -- deliberately, because the
    // two widgets have opposite shapes. The heart row is a SampleScene object, so it can
    // hold a scene reference to GameManager directly. This readout is PREFAB content shown
    // on both screens, and a prefab asset cannot store a scene reference at all: that is
    // the precise problem HudWalletSource exists to solve (D-013), and its sessionHost
    // field is already the per-scene override that solves it.
    //
    // There is no per-view tick here either. HudWalletSource runs one display refresh per
    // second for the whole scene, so a key earned while the player watches shows up
    // without every view owning a timer. Nothing about correctness rides on that tick --
    // every gate refreshes for itself (D-065).
    public class KeysView : MonoBehaviour
    {
        [SerializeField] private HudWalletSource walletSource;
        [SerializeField] private TMP_Text keysText;

        // Start(), not Awake(): the session is built in its host's Awake and Unity does not
        // order Awake across GameObjects. HudWalletSource resolves lazily for this exact
        // reason, so asking it anything from here is what keeps that guarantee -- the same
        // rule every other HUD view in this project follows.
        private void Start()
        {
            if (!ValidateReferences()) return;

            // No session means no keys, and since D-022 both screens are supposed to have
            // one -- so this is a LOST REFERENCE, not a second mode to render. The label is
            // left exactly as the scene authored it rather than being filled with a number
            // this view would have to invent: the only candidate, "0/5", would tell the
            // player they are out of keys, which is a far worse failure than a stale label.
            // HudWalletSource already logs which mode it resolved to, so this adds the one
            // fact that log cannot know: that a view needed keys and could not get them.
            if (!walletSource.KeysAvailable)
            {
                Debug.LogError(
                    $"{nameof(KeysView)} on '{name}': this scene's {nameof(HudWalletSource)} has no session, " +
                    "so the key count cannot be shown. Check that the scene's SessionHost (GameManager, or " +
                    "MainScreenRoot on the menu) is wired into the HUD instance.",
                    this);
                return;
            }

            walletSource.KeysChanged.Subscribe(OnKeysChanged);
            Refresh();
        }

        private void OnDestroy()
        {
            if (walletSource != null) walletSource.KeysChanged.Unsubscribe(OnKeysChanged);
        }

        // The payload is dropped in this one adapter line, exactly as LivesView does it:
        // the label renders both halves of current/max, so the new count alone cannot
        // drive it.
        private void OnKeysChanged(int _) => Refresh();

        private void Refresh()
        {
            keysText.text = $"{walletSource.KeyCount}/{walletSource.MaxKeys}";
        }

        // Both fields are wired inside the PREFAB (walletSource points at the canvas root
        // beside it, the way SoftMoneyView and GemsView already are), so they are prefab
        // data needing no per-scene override -- and a missing one should fail loudly with a
        // pointer to the field rather than as a bare NullReferenceException.
        private bool ValidateReferences()
        {
            var missing = new List<string>();
            if (walletSource == null) missing.Add(nameof(walletSource));
            if (keysText == null) missing.Add(nameof(keysText));

            if (missing.Count == 0) return true;

            Debug.LogError($"{nameof(KeysView)} on '{name}' is missing Inspector reference(s): {string.Join(", ", missing)}.", this);
            return false;
        }
    }
}
