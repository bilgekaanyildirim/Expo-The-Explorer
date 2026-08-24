using System;
using System.Collections;
using System.Collections.Generic;
using DG.Tweening;
using ExpoTheExplorer.Bootstrap;
using ExpoTheExplorer.Systems.ProgressionSystem;
using UnityEngine;
using UnityEngine.UI;

namespace ExpoTheExplorer.UI
{
    // The Day Complete payout, performed rather than reported (decisions.md D-057).
    // Nothing reaches the player's balance during a day; this is what hands the day's
    // earnings over, and the handover IS the animation:
    //
    //   1. the stars the day scored seat themselves one at a time;
    //   2. as each one lands, a gem leaves it and flies to the HUD gem counter --
    //      the counter ticks up when the gem arrives, not before;
    //   3. then coins appear one by one around a small circle at the receipt's Total
    //      figure and fly to the HUD coin counter, each carrying its own share.
    //
    // Because every landing credits through GameManager, the HUD views need no changes
    // at all: they are already reactive, so "what the player watched arrive" and "what
    // they own" are one event instead of two that have to be kept in step. That is the
    // whole reason this is built as a payout and not as a cosmetic overlay on top of a
    // balance that already jumped.
    //
    // It cannot lose money. Every exit from a finished day commits the remainder through
    // GameManager first, so an unwired reference, a skipped sequence or a player who taps
    // Next Day mid-flight all still get paid in full -- see CompleteImmediately.
    //
    // Lives on the same always-active object as DayCompletePopupView, NOT under
    // popupRoot: a coroutine stops when its GameObject is deactivated, and hiding the
    // popup mid-flight would otherwise strand the sequence. flightRoot is deliberately a
    // separate object outside popupRoot for the same reason, plus it makes the icons draw
    // above the popup.
    public class DayRewardFlightView : MonoBehaviour
    {
        [SerializeField] private GameManager gameManager;

        [Tooltip("Where the flying icons are parented and what their positions are measured in. Put it on the top-most canvas, OUTSIDE the popup root, so icons draw above the popup and survive it being hidden.")]
        [SerializeField] private RectTransform flightRoot;

        [Tooltip("The HUD gem counter's icon. Gems released by the stars fly here.")]
        [SerializeField] private RectTransform gemFlightTarget;

        [Tooltip("The HUD coin counter's icon. Coins released by the receipt total fly here.")]
        [SerializeField] private RectTransform coinFlightTarget;

        [SerializeField] private Sprite gemSprite;
        [SerializeField] private Sprite coinSprite;

        [Header("Stars")]
        [Tooltip("How long one star takes to seat itself. The gem leaves the star when this finishes, so the two read as one motion.")]
        [SerializeField, Min(0f)] private float starSeatDuration = 0.28f;

        [Tooltip("Pause between one star seating and the next starting.")]
        [SerializeField, Min(0f)] private float starSeatInterval = 0.18f;

        [Tooltip("Overshoot of the star's seat bounce. 1 is no bounce.")]
        [SerializeField, Min(1f)] private float starOvershoot = 1.7f;

        [Header("Coins")]
        [Tooltip("How many coin icons the day's total is split across — a visual count, not one per coin. Clamped down when the day earned less than this, so no coin is ever worth nothing.")]
        [SerializeField, Min(1)] private int coinCount = 10;

        [Tooltip("Radius of the little circle around the Total figure that coins appear on.")]
        [SerializeField, Min(0f)] private float coinCircleRadius = 45f;

        [Tooltip("Angle in degrees of the first coin on that circle; the rest are spaced evenly around it. -90 starts at the bottom.")]
        [SerializeField] private float coinStartAngle = -90f;

        [Tooltip("Pause between one coin appearing and the next.")]
        [SerializeField, Min(0f)] private float coinSpawnInterval = 0.06f;

        [Tooltip("Wait after the last star's gem before the coins begin, so the two phases read separately.")]
        [SerializeField, Min(0f)] private float coinBurstDelay = 0.25f;

        [Header("Flight")]
        [SerializeField, Min(0f)] private float appearDuration = 0.14f;
        [SerializeField, Min(0.01f)] private float flightDuration = 0.5f;

        [Tooltip("How high the flight arcs above the straight line to its target. 0 flies straight.")]
        [SerializeField] private float flightArcHeight = 120f;

        [SerializeField, Min(1f)] private float flightIconSize = 48f;

        [Tooltip("Scale the icon shrinks to as it reaches the counter, so it reads as being absorbed.")]
        [SerializeField, Min(0f)] private float flightEndScale = 0.55f;

        [Tooltip("How hard the HUD counter punches when an icon lands on it. 0 disables the punch.")]
        [SerializeField, Min(0f)] private float targetPunchScale = 0.25f;

        [SerializeField, Min(0f)] private float targetPunchDuration = 0.2f;

        private readonly List<RectTransform> liveIcons = new List<RectTransform>();
        private readonly List<Tween> liveTweens = new List<Tween>();
        private readonly List<GameObject> starsBeingSeated = new List<GameObject>();

        private Coroutine sequence;
        private bool referencesValid;

        private void Awake()
        {
            referencesValid = ValidateReferences();
        }

        // Kills every tween before the object goes away. DOTween callbacks that outlive
        // their target are the classic way a scene teardown turns into a null reference,
        // and this sequence is running at exactly the moment the day scene is unloaded.
        private void OnDestroy()
        {
            KillFlights();
        }

        // Starts the handover for a day that has just completed. `starsToSeat` is the
        // subset of the popup's star objects the day actually earned, already hidden by
        // the caller; `coinOrigin` is the receipt's Total figure, which the coins appear
        // around.
        //
        // The AMOUNTS are not passed in and deliberately so: they are read from
        // GameManager's pending purse, so this class cannot disagree with the ledger
        // about what the day was worth.
        public void Play(IReadOnlyList<GameObject> starsToSeat, RectTransform coinOrigin)
        {
            // A second completion (the player redid the day) restarts cleanly rather than
            // interleaving two sequences over one purse. A no-op on the first call, which
            // is what keeps it from committing the purse that was just created.
            CompleteImmediately();

            // Anything left in the list at this point belongs to a sequence that already
            // finished -- CompleteImmediately clears it whenever one was still running,
            // and it cannot report "not playing" while an icon is still in the air. Left
            // alone, dead Tween handles would pile up across repeated redos of a day.
            liveTweens.Clear();

            // Nothing to animate with, but the player must still SEE their score. The
            // reward itself is not at risk -- the exits commit it -- so a wiring mistake
            // costs the performance and nothing else.
            if (!referencesValid || coinOrigin == null)
            {
                SeatRemainingStarsInstantly(starsToSeat);
                return;
            }

            starsBeingSeated.Clear();
            if (starsToSeat != null)
            {
                foreach (var star in starsToSeat)
                {
                    if (star != null) starsBeingSeated.Add(star);
                }
            }

            sequence = StartCoroutine(RunSequence(coinOrigin));
        }

        // Ends the performance NOW and pays whatever is left. Called by every one of the
        // popup's buttons, so tapping through the animation is a skip rather than a
        // forfeit, and called again by Play so a redo cannot leave icons in the air.
        //
        // Killing a tween does NOT run its OnComplete (DOTween's default), so an icon
        // destroyed in flight never claims its share -- which is precisely why the
        // commit below has to be unconditional: it is the thing that makes the skipped
        // shares reappear as one lump sum.
        public void CompleteImmediately()
        {
            // The guard is not an optimisation, it is a correctness rule: without it the
            // CompleteImmediately at the top of Play would commit the purse that
            // OnDayCompleted had just filled, and the whole sequence would animate an
            // empty debt. Nothing in flight means the payout is not this call's business.
            if (!IsPlaying) return;

            if (sequence != null)
            {
                StopCoroutine(sequence);
                sequence = null;
            }

            KillFlights();

            // Any star that had not seated yet is simply put in place. The player asked
            // to move on; they should still see the score they got.
            SeatRemainingStarsInstantly(starsBeingSeated);
            starsBeingSeated.Clear();

            if (gameManager != null) gameManager.CompleteRewardHandover();
        }

        private bool IsPlaying => sequence != null || liveIcons.Count > 0 || starsBeingSeated.Count > 0;

        private static void SeatRemainingStarsInstantly(IReadOnlyList<GameObject> stars)
        {
            if (stars == null) return;

            foreach (var star in stars)
            {
                if (star == null) continue;

                star.SetActive(true);
                star.transform.localScale = Vector3.one;
            }
        }

        private IEnumerator RunSequence(RectTransform coinOrigin)
        {
            var gemsOwed = gameManager.PendingRewardGems;
            var starCount = starsBeingSeated.Count;

            for (var i = 0; i < starCount; i++)
            {
                var star = starsBeingSeated[i];
                if (star == null) continue;

                star.SetActive(true);
                star.transform.localScale = Vector3.zero;
                Track(star.transform
                    .DOScale(Vector3.one, starSeatDuration)
                    .SetEase(Ease.OutBack, starOvershoot));

                yield return new WaitForSeconds(starSeatDuration);

                // From THIS star's seat, not from the popup's centre or a shared launch
                // point: the three stars sit in three different places, so each gem is
                // measured off the object that just landed. One flightRoot serves them
                // all -- it is the coordinate space the icons live in, not where they
                // start from.
                var share = DayRewardPurse.ShareOf(gemsOwed, i, starCount);
                Launch(gemSprite, ToFlightLocal(star.transform), gemFlightTarget, () => gameManager.ClaimRewardGems(share));

                yield return new WaitForSeconds(starSeatInterval);
            }

            yield return new WaitForSeconds(coinBurstDelay);

            var coinsOwed = gameManager.PendingRewardSoftMoney;
            if (coinsOwed > 0)
            {
                // Never more coins than there is money to put in them: a 4-coin day pays
                // 4 icons worth 1 each, not 10 icons of which 6 are worth nothing.
                var coins = Mathf.Clamp(coinCount, 1, coinsOwed);
                var origin = ToFlightLocal(coinOrigin);

                for (var j = 0; j < coins; j++)
                {
                    // Evenly spaced rather than random: the ring reads as deliberate, and
                    // it keeps the sequence reproducible, which a Random would not.
                    var angle = (coinStartAngle + j * (360f / coins)) * Mathf.Deg2Rad;
                    var spawn = origin + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * coinCircleRadius;

                    var share = DayRewardPurse.ShareOf(coinsOwed, j, coins);
                    Launch(coinSprite, spawn, coinFlightTarget, () => gameManager.ClaimRewardSoftMoney(share));

                    yield return new WaitForSeconds(coinSpawnInterval);
                }
            }

            // Let the last icon finish its arc before the books are closed.
            yield return new WaitForSeconds(appearDuration + flightDuration);

            // Cleared before the commit so IsPlaying reads false afterwards: the payout is
            // settled, and a later button tap must not re-enter CompleteImmediately.
            sequence = null;
            starsBeingSeated.Clear();

            gameManager.CompleteRewardHandover();
        }

        // Pops the icon into existence, arcs it to the counter, and credits on arrival.
        // The credit is the OnComplete rather than a timer, so it can only ever fire for
        // an icon that actually got there.
        private void Launch(Sprite sprite, Vector2 fromLocal, RectTransform target, Action onArrive)
        {
            var icon = SpawnIcon(sprite, fromLocal);
            var toLocal = ToFlightLocal(target);

            // One control point half way along and lifted: enough to read as a throw,
            // and cheap enough that a dozen of them cost nothing. A straight DOMove reads
            // as a slide, which is wrong for something being collected.
            var control = Vector2.Lerp(fromLocal, toLocal, 0.5f) + Vector2.up * flightArcHeight;

            var flight = DOTween.Sequence();
            flight.Append(icon.DOScale(1f, appearDuration).SetEase(Ease.OutBack));
            flight.Append(DOTween
                .To(() => 0f, t => icon.localPosition = QuadraticBezier(fromLocal, control, toLocal, t), 1f, flightDuration)
                .SetEase(Ease.InOutSine));
            flight.Join(icon.DOScale(flightEndScale, flightDuration).SetEase(Ease.InQuad));
            flight.OnComplete(() =>
            {
                onArrive();
                PunchTarget(target);
                Despawn(icon);
            });

            Track(flight);
        }

        private static Vector2 QuadraticBezier(Vector2 from, Vector2 control, Vector2 to, float t)
        {
            var inverse = 1f - t;
            return (inverse * inverse * from) + (2f * inverse * t * control) + (t * t * to);
        }

        // Built in code rather than instantiated from a prefab: two sprites and a size are
        // the whole of it, so a prefab would be one more asset to author, wire and keep in
        // step for no gain. Thirteen of these are created once per completed day, which
        // is event frequency -- pooling would be cost with no measurable saving.
        private RectTransform SpawnIcon(Sprite sprite, Vector2 localPosition)
        {
            var go = new GameObject("RewardFlightIcon", typeof(RectTransform), typeof(Image));
            var rect = (RectTransform)go.transform;
            rect.SetParent(flightRoot, false);
            rect.sizeDelta = new Vector2(flightIconSize, flightIconSize);
            rect.localPosition = localPosition;
            rect.localScale = Vector3.zero;

            var image = go.GetComponent<Image>();
            image.sprite = sprite;
            image.preserveAspect = true;

            // The popup's buttons sit under these as they fly past; an icon that ate a tap
            // would make the skip feel broken.
            image.raycastTarget = false;

            liveIcons.Add(rect);
            return rect;
        }

        private void Despawn(RectTransform icon)
        {
            liveIcons.Remove(icon);
            if (icon != null) Destroy(icon.gameObject);
        }

        private void PunchTarget(RectTransform target)
        {
            if (targetPunchScale <= 0f || target == null) return;

            // Punches are not tracked for killing: they are on the HUD, which outlives
            // this popup, and they finish in a fifth of a second either way.
            target.DOKill(true);
            target.DOPunchScale(Vector3.one * targetPunchScale, targetPunchDuration);
        }

        private void Track(Tween tween)
        {
            if (tween != null) liveTweens.Add(tween);
        }

        private void KillFlights()
        {
            foreach (var tween in liveTweens)
            {
                // complete: false -- an OnComplete here would credit a share the player
                // never saw land, on top of the lump sum the commit is about to pay.
                if (tween != null && tween.IsActive()) tween.Kill();
            }
            liveTweens.Clear();

            foreach (var icon in liveIcons)
            {
                if (icon != null) Destroy(icon.gameObject);
            }
            liveIcons.Clear();
        }

        // Positions come from two different canvases -- the receipt is on the popup, the
        // counters are on the shared HUD prefab -- so a world position cannot be used
        // directly. Screen space is the common ground between them.
        // Takes a plain Transform rather than a RectTransform so a star that is not a UI
        // object still launches its gem. The earlier version branched on the cast and
        // skipped silently when it failed, which is the wrong failure for this file: the
        // gem would vanish, the money would arrive anyway on the exit commit, and nothing
        // on screen would say why. Only `.position` is needed here, which every Transform
        // has.
        private Vector2 ToFlightLocal(Transform source)
        {
            var screenPoint = RectTransformUtility.WorldToScreenPoint(CameraFor(source), source.position);
            RectTransformUtility.ScreenPointToLocalPointInRectangle(
                flightRoot, screenPoint, CameraFor(flightRoot), out var local);

            return local;
        }

        // GetComponentInParent, not a scene lookup: it walks up from a reference that was
        // already handed to us, so it cannot find the wrong object the way a search by
        // name or type could. An Overlay canvas takes a null camera, which is what both
        // RectTransformUtility calls above want.
        private static Camera CameraFor(Transform target)
        {
            var canvas = target.GetComponentInParent<Canvas>();
            if (canvas == null) return null;

            var root = canvas.rootCanvas;
            return root.renderMode == RenderMode.ScreenSpaceOverlay ? null : root.worldCamera;
        }

        // Every field here is wired by hand in the Editor -- a missing one should fail
        // loudly with a clear pointer to which field, not a bare NullReferenceException
        // halfway through a payout.
        private bool ValidateReferences()
        {
            var missing = new List<string>();
            if (gameManager == null) missing.Add(nameof(gameManager));
            if (flightRoot == null) missing.Add(nameof(flightRoot));
            if (gemFlightTarget == null) missing.Add(nameof(gemFlightTarget));
            if (coinFlightTarget == null) missing.Add(nameof(coinFlightTarget));
            if (gemSprite == null) missing.Add(nameof(gemSprite));
            if (coinSprite == null) missing.Add(nameof(coinSprite));

            if (missing.Count == 0) return true;

            Debug.LogError($"{nameof(DayRewardFlightView)} on '{name}' is missing Inspector reference(s): {string.Join(", ", missing)}. The day's reward will still be paid in full when the player leaves the popup — only the animation is lost.", this);
            return false;
        }
    }
}
