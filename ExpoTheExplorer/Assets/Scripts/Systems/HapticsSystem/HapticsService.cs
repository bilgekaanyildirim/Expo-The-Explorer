using ExpoTheExplorer.Data;

namespace ExpoTheExplorer.Systems.HapticsSystem
{
    // Decides WHICH haptic a frame gets. It never plays one: playback is the vendor's
    // business and lives in HapticsBinder, which is what keeps this class plain C# and
    // reachable from an EditMode test with no device, no scene and no MonoBehaviour.
    //
    // The whole reason it exists is that this project's events are synchronous and
    // cascade into each other (EventBus.Publish calls its handlers inline), so several
    // moments routinely land in ONE frame:
    //
    //   - a correct drop that completes an order: ItemDroppedInTray, then OrderDelivered
    //   - a wrong order: ItemDroppedInTray, then LifeLost
    //   - the last life: LifeLost, then GameOver
    //
    // Played as they arrive, those read as one smeared buzz instead of one clear
    // answer. So requests are COLLECTED and the caller drains them once per frame
    // (HapticsBinder does it in LateUpdate), and the highest-priority moment is the
    // one the player feels. The cost of that is a single int comparison per request
    // and one bool test per frame.
    public class HapticsService
    {
        private readonly HapticConfig config;

        private bool hasPending;
        private HapticPreset pending;
        private int pendingPriority;

        public HapticsService(HapticConfig config)
        {
            this.config = config;
        }

        // Asks for `moment` to be felt this frame. Safe to call from anywhere and as
        // often as the caller likes -- a moment the config silences, or one outranked
        // by something already queued, costs a lookup and nothing else.
        //
        // A null config makes this a no-op rather than an exception: an unwired
        // reference should cost the player a haptic, never a broken frame. The binder
        // reports the wiring gap once, where it can be read.
        public void Request(HapticMoment moment)
        {
            if (config == null) return;
            if (!config.TryResolve(moment, out var preset, out var priority)) return;

            // >= and not > : ties go to whoever asked FIRST. That matters for the
            // cascade above, where the earlier request is the one closer to what the
            // player actually did.
            if (hasPending && pendingPriority >= priority) return;

            pending = preset;
            pendingPriority = priority;
            hasPending = true;
        }

        // Hands over this frame's winner, if there is one, and clears the queue.
        // Draining is the caller's job precisely so that the "once per frame" rule
        // lives at the one place that knows what a frame is.
        public bool TryTakePending(out HapticPreset preset)
        {
            preset = pending;

            var had = hasPending;
            hasPending = false;
            pending = HapticPreset.None;
            pendingPriority = 0;

            return had;
        }
    }
}
