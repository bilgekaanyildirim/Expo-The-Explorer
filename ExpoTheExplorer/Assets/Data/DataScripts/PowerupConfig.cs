using System;
using UnityEngine;
using UnityEngine.Serialization;

namespace ExpoTheExplorer.Data
{
    // Every number behind GDD Section 5.2, consumed by PowerupManager. They live here
    // rather than in code because the root invariant says content does, and because each
    // one is a pacing dial: how much help a player is handed for free, and what skipping
    // the wait costs.
    //
    // IT CARRIED TWO MORE FIELDS UNTIL 2026-08-25 -- clarityDurationSeconds and
    // clarityDimAlpha -- and they are worth a line here rather than a silent deletion.
    // They existed because the third powerup was believed to FADE the board's noise for a
    // few seconds. The user corrected what it does: it REMOVES those items. A removal has
    // no duration to author and no opacity to author, so both numbers stopped describing
    // anything, and nothing replaced them -- the effect needs no authored value at all.
    //
    // Unlike KeyConfig -- whose defaults ARE the user's authored values -- these are
    // PLACEHOLDERS, the shape LivesConfig and StarScoreConfig are in. They exist so
    // creating the asset needs no typing and so nothing is silently zero; the real
    // balance comes from play. Do not treat any number below as a design decision.
    //
    // THE STALE-ASSET TRAP APPLIES HERE (decisions.md D-075): adding a field to this
    // class does not add it to an already-saved PowerupConfig.asset, which then reads
    // back as zero with no error. After touching this file, run
    // Tools > ExpoTheExplorer > Re-serialize Data Configs.
    [CreateAssetMenu(fileName = "PowerupConfig", menuName = "ExpoTheExplorer/Data/Powerup Config")]
    public class PowerupConfig : ScriptableObject
    {
        // One block per powerup rather than an array indexed by the enum, and that is a
        // deliberate trade: an array would be shorter here but shows up in the Inspector
        // as "Element 0/1/2" with no way to tell which powerup is which, and can be
        // resized to the wrong length by a stray drag. Three named fields cannot.
        [Tooltip("GDD 5.2 #1 — auto-places the items active tickets still need into their trays.")]
        [SerializeField] private PowerupSettings autoCollect = new(startingCharges: 1, gemCost: 30, chargesPerDayCompleted: 1);

        [Tooltip("GDD 5.2 #2 — pulls every active ticket's countdown back to its own limit.")]
        [SerializeField] private PowerupSettings timeReset = new(startingCharges: 1, gemCost: 20, chargesPerDayCompleted: 1);

        // THESE NUMBERS ARE NOW BACKWARDS and are left that way on purpose rather than
        // guessed at again. They were set while this powerup was believed to be the
        // gentlest of the three (a few seconds of easier reading), so it got the cheapest
        // price and the largest free stock. Clearing the board outright is plausibly the
        // STRONGEST of the three, which makes cheapest-and-most exactly the wrong corner.
        // Correcting it is a balancing pass with real play behind it, not a second guess
        // typed in the same afternoon.
        //
        // [FormerlySerializedAs] is doing real work here, not decoration: the field was
        // called boardClarity when PowerupConfig.asset was first authored and TUNED, and a
        // plain rename would have orphaned that block -- Unity matches serialized data by
        // field name, so the authored numbers would have been silently replaced by the
        // defaults on this line. The attribute makes the existing asset load unchanged
        // with no Inspector work. (Its JSON counterpart, PlayerProfile, gets no such help:
        // JsonUtility ignores this attribute, which is why the save file needed a real v9
        // migration for the same rename.)
        [Tooltip("GDD 5.2 #3 — removes every board item no active ticket needs. Probably the strongest of the three: price and starting stock still need re-tuning.")]
        [FormerlySerializedAs("boardClarity")]
        [SerializeField] private PowerupSettings noiseClear = new(startingCharges: 2, gemCost: 15, chargesPerDayCompleted: 1);

        // The single lookup every consumer goes through, so nothing outside this class
        // has to know there are three separate fields. Written out as a switch rather
        // than an array index for the reason HapticConfig's mirror enum is: a new
        // PowerupType stops the build here instead of silently falling through to a
        // default that pays out zero charges forever.
        public PowerupSettings For(PowerupType type) => type switch
        {
            PowerupType.AutoCollect => autoCollect,
            PowerupType.TimeReset => timeReset,
            PowerupType.NoiseClear => noiseClear,
            _ => throw new ArgumentOutOfRangeException(nameof(type), type, "No PowerupSettings block on PowerupConfig for this type."),
        };
    }

    // One powerup's three stock numbers. A [Serializable] class rather than a struct so
    // the field initializers above can name their arguments; Unity serializes both the
    // same way, and nothing here is copied often enough for the reference to cost
    // anything (it is read on a button press, not per frame).
    //
    // Unity never calls this constructor when LOADING an asset -- it fills the fields
    // directly -- so the constructor's only job is making the defaults above readable.
    // That is exactly why the stale-asset warning on the class above matters.
    [Serializable]
    public class PowerupSettings
    {
        [Tooltip("How many charges a brand-new player owns. Also what an older save (which predates powerups) resolves to.")]
        [Min(0)]
        [SerializeField] private int startingCharges;

        [Tooltip("Gem price of one charge, bought on the MAIN SCREEN. The day scene never sells.")]
        [Min(0)]
        [SerializeField] private int gemCost;

        [Tooltip("Charges granted when a day is completed. 0 turns this earn path off for this powerup.")]
        [Min(0)]
        [SerializeField] private int chargesPerDayCompleted;

        public PowerupSettings(int startingCharges, int gemCost, int chargesPerDayCompleted)
        {
            this.startingCharges = startingCharges;
            this.gemCost = gemCost;
            this.chargesPerDayCompleted = chargesPerDayCompleted;
        }

        public int StartingCharges => startingCharges;
        public int GemCost => gemCost;
        public int ChargesPerDayCompleted => chargesPerDayCompleted;
    }
}
