using System;
using UnityEngine;

namespace ExpoTheExplorer.Data
{
    // The moments the game can ask for a haptic. Adding one here is half the work:
    // the other half is a call site, because nothing in this enum finds its own
    // trigger. Deliberately NOT a moment: "a ticket was assigned" and "a wrong
    // order scattered" -- both are published in the same frame as another moment
    // that outranks them (see HapticsService), so they would never be felt.
    //
    // APPEND ONLY. Unity serializes an enum field by its VALUE, and every authored
    // row in HapticConfig.asset stores one of these. Inserting a name in the middle
    // would silently re-point every row after it at the wrong moment -- an asset
    // that still loads, still validates, and plays the wrong haptic for everything
    // past the insertion. New moments go at the END, whatever the reading order
    // would prefer, which is why the second block below is not sorted into the first.
    public enum HapticMoment
    {
        ItemPickup,
        ItemDroppedInTray,
        OrderDelivered,
        LifeLost,
        GameOver,
        StarSeated,
        PropPurchased,
        PropLanded,

        // Added in the second pass (decisions.md D-071).
        UiTap,
        CoinLanded,
        GemLanded,
        DropRejected,
        KeySpent,
        PurchaseRefused,
        BlockedByNoKeys,

        // Added in the third pass (decisions.md D-072): the successful halves of two
        // spends whose refusals already had a moment, and the meta celebration.
        KeysRefilled,
        ContinuePurchased,
        PropUnlocked,

        // Added in the fourth pass (decisions.md D-109), correcting D-071: the drop
        // that SUCCEEDS was the only half of the drag gesture with no answer.
        ItemPlacedOnBoard,
    }

    // A mirror of Nice Vibrations' HapticPatterns.PresetType, and it exists so that
    // THIS assembly stays vendor-free: ExpoTheExplorer.Data carries an empty
    // reference list, which is this project's way of saying data depends on nothing.
    // The translation to the vendor enum lives in HapticsBinder, the single file that
    // knows Nice Vibrations exists.
    //
    // Not cast by value onto the vendor enum even though the ordering happens to
    // match today -- a silent cast is exactly the kind of thing that keeps compiling
    // and starts lying the day the vendor renumbers.
    public enum HapticPreset
    {
        None = 0,
        Selection,
        Success,
        Warning,
        Failure,
        LightImpact,
        MediumImpact,
        HeavyImpact,
        RigidImpact,
        SoftImpact,
    }

    // Which haptic each moment plays, and which one wins when two land in the same
    // frame. The single authority for all three: nothing about the FEEL of a moment
    // is written in code.
    [CreateAssetMenu(fileName = "HapticConfig", menuName = "ExpoTheExplorer/Data/Haptic Config")]
    public class HapticConfig : ScriptableObject
    {
        [Serializable]
        public class Entry
        {
            [Tooltip("The game moment this row describes.")]
            public HapticMoment moment;

            [Tooltip("The haptic played for it. None plays nothing, same as unticking Enabled.")]
            public HapticPreset preset;

            [Tooltip("When two moments land in the same frame only the highest priority is felt. Ties go to whichever was requested first.")]
            public int priority;

            [Tooltip("Untick to silence this moment without losing the row's authored preset.")]
            public bool enabled = true;
        }

        // Initialised here as well as in Reset() on purpose: Reset only runs in the
        // editor, and an asset created any other way (a test, a future tool) should
        // still come up with a working table rather than an empty one that silently
        // never buzzes.
        [SerializeField] private Entry[] entries = DefaultEntries();

        // First matching row wins. A duplicated moment is therefore harmless rather
        // than ambiguous, and a DELETED row means "no haptic" -- which is why Reset
        // seeds every moment: an author who wants silence should untick Enabled and
        // keep the preset they chose, not delete the evidence.
        public bool TryResolve(HapticMoment moment, out HapticPreset preset, out int priority)
        {
            preset = HapticPreset.None;
            priority = 0;

            if (entries == null) return false;

            foreach (var entry in entries)
            {
                if (entry == null || entry.moment != moment) continue;
                if (!entry.enabled || entry.preset == HapticPreset.None) return false;

                preset = entry.preset;
                priority = entry.priority;
                return true;
            }

            return false;
        }

        private void Reset() => entries = DefaultEntries();

#if UNITY_EDITOR
        // Grows an EXISTING asset to cover moments added after it was created, and
        // touches nothing that is already in it.
        //
        // Reset() only ever runs on a brand-new asset, so without this an author who
        // made the asset before a moment existed would get no row for it -- and a
        // missing row is SILENT, which is the hardest kind of failure to notice.
        // Telling them to hit Reset instead would work and would also throw away
        // every preset they had tuned, which is a bad trade for a problem the file
        // can solve for itself.
        //
        // It only ever APPENDS, and that is consistent with what this file already
        // asks of authors: silence a moment by unticking Enabled, not by deleting its
        // row. A deleted row therefore reads as an accident here, and comes back.
        //
        // Editor-only, so the runtime meaning of a missing row is untouched: in a
        // build TryResolve still answers false and the moment is still silent.
        private void OnValidate()
        {
            var existing = new System.Collections.Generic.HashSet<HapticMoment>();
            if (entries != null)
            {
                foreach (var entry in entries)
                {
                    if (entry != null) existing.Add(entry.moment);
                }
            }

            var missing = new System.Collections.Generic.List<Entry>();
            foreach (var seed in DefaultEntries())
            {
                if (!existing.Contains(seed.moment)) missing.Add(seed);
            }

            if (missing.Count == 0) return;

            var grown = new System.Collections.Generic.List<Entry>();
            if (entries != null) grown.AddRange(entries);
            grown.AddRange(missing);
            entries = grown.ToArray();

            UnityEditor.EditorUtility.SetDirty(this);
        }
#endif

        // The authored starting point, not a fallback the runtime reads: once the
        // asset exists these numbers live in it and changing them here changes
        // nothing. The priorities are spaced by tens so a new moment can be slotted
        // between two existing ones without renumbering the table.
        private static Entry[] DefaultEntries() => new[]
        {
            // Listed in priority order rather than in enum order, because priority is
            // what an author actually reasons about when two moments collide.
            new Entry { moment = HapticMoment.UiTap,             preset = HapticPreset.Selection,    priority = 5 },
            new Entry { moment = HapticMoment.ItemPickup,        preset = HapticPreset.LightImpact,  priority = 10 },
            new Entry { moment = HapticMoment.CoinLanded,        preset = HapticPreset.Selection,    priority = 12 },
            new Entry { moment = HapticMoment.GemLanded,         preset = HapticPreset.LightImpact,  priority = 15 },
            new Entry { moment = HapticMoment.ItemPlacedOnBoard, preset = HapticPreset.RigidImpact,  priority = 16 },
            new Entry { moment = HapticMoment.DropRejected,      preset = HapticPreset.SoftImpact,   priority = 18 },
            new Entry { moment = HapticMoment.ItemDroppedInTray, preset = HapticPreset.MediumImpact, priority = 20 },
            new Entry { moment = HapticMoment.KeySpent,          preset = HapticPreset.LightImpact,  priority = 25 },
            new Entry { moment = HapticMoment.PropPurchased,     preset = HapticPreset.Selection,    priority = 30 },
            new Entry { moment = HapticMoment.KeysRefilled,      preset = HapticPreset.MediumImpact, priority = 35 },
            new Entry { moment = HapticMoment.OrderDelivered,    preset = HapticPreset.Success,      priority = 40 },
            new Entry { moment = HapticMoment.PurchaseRefused,   preset = HapticPreset.Warning,      priority = 45 },
            new Entry { moment = HapticMoment.StarSeated,        preset = HapticPreset.MediumImpact, priority = 50 },
            new Entry { moment = HapticMoment.BlockedByNoKeys,   preset = HapticPreset.Warning,      priority = 55 },
            new Entry { moment = HapticMoment.LifeLost,          preset = HapticPreset.Failure,      priority = 60 },
            new Entry { moment = HapticMoment.ContinuePurchased, preset = HapticPreset.Success,      priority = 65 },
            new Entry { moment = HapticMoment.PropLanded,        preset = HapticPreset.HeavyImpact,  priority = 70 },
            new Entry { moment = HapticMoment.PropUnlocked,      preset = HapticPreset.Success,      priority = 72 },
            new Entry { moment = HapticMoment.GameOver,          preset = HapticPreset.HeavyImpact,  priority = 80 },
        };
    }
}
