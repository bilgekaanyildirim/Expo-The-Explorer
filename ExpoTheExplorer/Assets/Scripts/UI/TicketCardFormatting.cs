using UnityEngine;

namespace ExpoTheExplorer.UI
{
    // Pure display-formatting helpers, kept out of the MonoBehaviours themselves
    // (CLAUDE.md Section 5 — keep MonoBehaviours thin).
    public static class TicketCardFormatting
    {
        public static string FormatRemainingTime(float seconds)
        {
            var clamped = Mathf.Max(0f, seconds);
            var totalSeconds = Mathf.CeilToInt(clamped);
            var minutes = totalSeconds / 60;
            var remainingSeconds = totalSeconds % 60;
            return $"{minutes}:{remainingSeconds:00}";
        }
    }
}
