using System;
using System.Collections.Generic;
using ExpoTheExplorer.Bootstrap;
using ExpoTheExplorer.Data;
using ExpoTheExplorer.Session;
using ExpoTheExplorer.Systems.KeySystem;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace ExpoTheExplorer.UI
{
    // "You are out of keys." Shown when the player asks to play or retry a day with
    // nothing to spend, and it offers the only two ways forward: wait for the timer, or
    // buy a full refill with Gems (.claude/key-plan.md step 5).
    //
    // THE GATE LIVES HERE, not in the two callers, and HasKeyOrShow below is the whole
    // of it. That keeps one answer to "what happens when there are no keys" instead of
    // two copies drifting apart, and it means neither caller needs a session reference of
    // its own -- MainScreenView has never had one. The method name states the side effect
    // on purpose: a gate that silently opens a window would be a trap at the call site.
    //
    // NOTHING IS EVER DISABLED to enforce this (the user's rule). Buttons stay pressable
    // and the check happens on the click, because a greyed-out button tells the player
    // nothing about why -- this popup tells them, and hands them both exits.
    //
    // One instance per scene rather than a shared prefab: the day scene shows it over the
    // Game Over popup and the main screen over the menu, and sessionHost is a scene
    // reference either way, which a prefab asset could not carry (the lesson D-013 wrote
    // down about the HUD).
    public class NoKeysPopupView : MonoBehaviour
    {
        // Whichever component provides this scene's session -- GameManager in the day
        // scene, MainScreenRoot on the menu. Serialized and dragged, never searched for.
        [SerializeField] private SessionHost sessionHost;

        [Tooltip("The object switched on and off. Keep this view on an always-active parent so Start runs.")]
        [SerializeField] private GameObject popupRoot;

        [Tooltip("Optional. The scene's HapticsBinder, so being turned away can be felt as well as read. Unwired means a silent popup and nothing else changes.")]
        [SerializeField] private HapticsBinder haptics;

        [Tooltip("Counts down to the next key, as MM:SS.")]
        [SerializeField] private TMP_Text countdownText;

        [SerializeField] private Button gemButton;

        // REQUIRED, and the reason is a softlock: the only moment this popup closes by
        // itself is when a key arrives, so a player with no keys AND not enough Gems
        // would otherwise be stuck looking at it for up to a full regen interval.
        [SerializeField] private Button closeButton;

        [Tooltip("Optional: shows the Gem price. Left unwired, the price is simply not displayed.")]
        [SerializeField] private TMP_Text gemCostLabel;

        [Tooltip("Optional: shown when the player cannot afford the refill.")]
        [SerializeField] private TMP_Text notEnoughGemsLabel;

        [Tooltip("Optional. Assets/Data/BoardAnimationConfig.asset — read for Popup Fade In Duration only. Unwired, this popup appears instantly, exactly as it did before the fade existed. THIS VIEW EXISTS TWICE (day scene and main screen), so both instances want it.")]
        [SerializeField] private BoardAnimationConfig animConfig;

        private KeyManager keys;
        private float secondsSinceRedraw;
        private bool subscribed;

        // Start(), not Awake(): the session is built in its host's Awake and Unity does
        // not order Awake across GameObjects -- the same rule every view here follows.
        private void Start()
        {
            if (!ValidateReferences()) return;

            popupRoot.SetActive(false);

            keys = sessionHost.Session?.KeyManager;
            if (keys == null)
            {
                Debug.LogError(
                    $"{nameof(NoKeysPopupView)} on '{name}' in scene '{gameObject.scene.name}': the wired " +
                    $"{nameof(SessionHost)} has no session, so the key gate cannot work. Play and Retry will be " +
                    "allowed through rather than blocked.",
                    this);
                return;
            }

            gemButton.onClick.AddListener(OnGemClicked);
            closeButton.onClick.AddListener(Hide);

            if (gemCostLabel != null) gemCostLabel.text = keys.RefillGemCost.ToString();
        }

        private void OnDestroy()
        {
            if (gemButton != null) gemButton.onClick.RemoveListener(OnGemClicked);
            if (closeButton != null) closeButton.onClick.RemoveListener(Hide);

            Unsubscribe();
        }

        // The gate. True means the caller may proceed; false means this popup has taken
        // over and explained why not.
        //
        // FAILS OPEN, deliberately. If the references are missing or the scene has no
        // session, this returns true and logs -- a forgotten drag must not leave the
        // player unable to play at all, which is a far worse outcome than a key that goes
        // uncharged. The error at Start is what makes the misconfiguration findable.
        public bool HasKeyOrShow()
        {
            if (keys == null) return true;

            // HasKey refreshes for itself (D-065), so this answer accounts for every key
            // earned since the last question -- including while the game was closed.
            if (keys.HasKey) return true;

            Show();
            return false;
        }

        private void Show()
        {
            popupRoot.SetActive(true);
            if (animConfig != null) PopupFade.In(popupRoot, animConfig.PopupFadeInDuration);

            // In Show rather than in HasKeyOrShow's refusing branch, so that every route
            // that opens this window buzzes and none has to remember to. It lands in the
            // same frame as the UiTap of whichever button was pressed and outranks it --
            // the player feels the refusal, not the press, which is the whole point of
            // the priority rule.
            haptics?.Request(HapticMoment.BlockedByNoKeys);

            if (notEnoughGemsLabel != null) notEnoughGemsLabel.gameObject.SetActive(false);

            // Subscribed only while visible: the popup's single claim is "you have no
            // keys", so the moment that stops being true it should get out of the way --
            // the player waits, the key lands, the window closes and Retry works.
            if (!subscribed)
            {
                keys.KeysChanged.Subscribe(OnKeysChanged);
                subscribed = true;
            }

            secondsSinceRedraw = 0f;
            RedrawCountdown();
        }

        private void Hide()
        {
            popupRoot.SetActive(false);
            Unsubscribe();
        }

        private void Unsubscribe()
        {
            if (!subscribed || keys == null) return;

            keys.KeysChanged.Unsubscribe(OnKeysChanged);
            subscribed = false;
        }

        private void OnKeysChanged(int count)
        {
            if (count > 0) Hide();
        }

        // Only runs while the popup is on screen, and only once a second: the countdown's
        // smallest visible step is one second, so a per-frame redraw would be ~60x the
        // work for the same text. Unscaled, so a popup that pauses the game cannot also
        // freeze the wait it is asking the player to sit through.
        private void Update()
        {
            if (keys == null || !popupRoot.activeSelf) return;

            secondsSinceRedraw += Time.unscaledDeltaTime;
            if (secondsSinceRedraw < 1f) return;

            secondsSinceRedraw = 0f;
            RedrawCountdown();
        }

        // SecondsUntilNextKey calls Refresh for itself, so this is also what notices a key
        // arriving while the player watches -- the KeysChanged subscription above then
        // closes the window.
        private void RedrawCountdown()
        {
            var remaining = TimeSpan.FromSeconds(keys.SecondsUntilNextKey());
            countdownText.text = $"{(int)remaining.TotalMinutes:00}:{remaining.Seconds:00}";
        }

        private void OnGemClicked()
        {
            if (keys.TryRefillWithGems())
            {
                // Its own moment rather than sharing the Continue's, even though both are
                // "Gems spent, resource restored": the stakes differ by a lot -- one is a
                // rescue mid-day, this is topping up a meta resource -- and separate rows
                // let those be tuned apart.
                //
                // The REFUSAL below is deliberately still silent, and that asymmetry is a
                // scope decision rather than an oversight: it was offered alongside these
                // three and left for later. PurchaseRefused already exists and would fit
                // it in one line whenever it is wanted.
                haptics?.Request(HapticMoment.KeysRefilled);

                Hide();
                return;
            }

            // The only way this fails here is not enough Gems: the popup is shown at zero
            // keys, so the cap check inside TryRefillWithGems cannot be what refused. The
            // window deliberately stays OPEN -- the player should keep seeing the price
            // they are short of, rather than have it vanish.
            if (notEnoughGemsLabel != null) notEnoughGemsLabel.gameObject.SetActive(true);
        }

        private bool ValidateReferences()
        {
            var missing = new List<string>();
            if (sessionHost == null) missing.Add(nameof(sessionHost));
            if (popupRoot == null) missing.Add(nameof(popupRoot));
            if (countdownText == null) missing.Add(nameof(countdownText));
            if (gemButton == null) missing.Add(nameof(gemButton));
            if (closeButton == null) missing.Add(nameof(closeButton));

            if (missing.Count == 0) return true;

            Debug.LogError(
                $"{nameof(NoKeysPopupView)} on '{name}' in scene '{gameObject.scene.name}' is missing Inspector " +
                $"reference(s): {string.Join(", ", missing)}. The key gate will allow everything through.",
                this);
            return false;
        }
    }
}
