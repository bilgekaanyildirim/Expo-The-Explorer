using DG.Tweening;
using UnityEngine;

namespace ExpoTheExplorer.UI
{
    // The one way a popup appears in this game. Every popup calls this on the line right
    // after the one that makes it visible, so the whole project has a single fade speed
    // (BoardAnimationConfig.PopupFadeInDuration) and a single set of edge cases.
    //
    // A STATIC HELPER RATHER THAN A COMPONENT OR A BASE CLASS, and that is a decision about
    // this project's popups specifically: the eleven of them share nothing but "become
    // visible" -- some are scene objects toggled with SetActive, some are prefabs
    // instantiated per use, one is a bottom sheet swapped against a confirmation box. A base
    // class would have to be inherited by views that already inherit nothing and would still
    // not reach the instantiated ones; a MonoBehaviour would have to be added to eleven
    // objects across two scenes and four prefabs, and a popup whose component was forgotten
    // would silently stop fading. A call at the show site cannot be forgotten -- it is on the
    // line you are already reading.
    //
    // THERE IS NO FADE OUT, by the user's decision (2026-08-31). A popup goes away in the
    // frame it is dismissed, so the tap that closed it feels answered rather than acknowledged
    // and then processed. That asymmetry is intentional; do not "complete" it.
    public static class PopupFade
    {
        // `target` is the object that was just shown -- the popup ROOT, not the view that owns
        // it, since on several of these the view sits on a parent canvas that never turns off.
        //
        // A duration of 0 (or a config nobody dragged in) leaves the popup exactly as it was
        // before this existed: fully visible, immediately. Every popup here is modal or
        // blocking, so degrading to "instant" is the only safe direction -- degrading to
        // "invisible" would strand a player behind a panel they cannot see.
        public static void In(GameObject target, float duration)
        {
            if (target == null || duration <= 0f) return;

            var group = target.GetComponent<CanvasGroup>();

            // Added rather than required, because not one popup root in either scene carries a
            // CanvasGroup today and demanding eleven manual component additions would make
            // this feature a wiring chore. AddComponent runs once per object -- the popups
            // that are scene objects keep the group across every reopen, and the ones that are
            // instantiated get it with the instance.
            if (group == null) group = target.AddComponent<CanvasGroup>();

            // The critical line for a popup that is closed and reopened faster than it fades.
            // Without it the previous fade is still driving this same group, and the two write
            // alpha in whatever order DOTween happens to hold them -- which reads as a popup
            // that flickers or opens half-transparent. Killing first makes the newest open
            // authoritative, always.
            group.DOKill();
            group.alpha = 0f;

            // SetLink so a popup destroyed mid-fade (the day scene going away, a celebration
            // queue cut short) takes its tween with it. Deliberately NOT SetUpdate(true):
            // this project never touches Time.timeScale -- a paused day is a logical hold, not
            // a stopped clock (SettingsPopupView says so at the top of its file) -- and an
            // unscaled tween would be a claim about a mechanism that does not exist here.
            group.DOFade(1f, duration).SetLink(target);
        }
    }
}
