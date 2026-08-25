using UnityEngine;

namespace ExpoTheExplorer.Data
{
    // The four numbers behind a day's star rating (decisions.md D-060), and nothing
    // else. The RULE stays in code where it can be tested
    // (DayLifecycleManager.StarCount); these are balancing values, so per the root
    // CLAUDE.md invariant they cannot live there.
    //
    // Its own asset rather than another header on GameConfig: GameConfig holds
    // GemsPerStar, which is what a star is WORTH once earned, and that is a different
    // question from what earns one. Keeping them apart is also what makes retuning
    // safe -- the star curve is the number a designer will touch dozens of times
    // during balancing, and it should not sit in the same asset as the board
    // dimensions that must never move.
    //
    // The score these thresholds are compared against is
    // `clamp01(timeEfficiency - failurePenalty)`, where timeEfficiency is the fraction
    // of the day's TOTAL authored ticket clock the player handed back by delivering
    // early. Every value below is therefore in the same currency: a fraction of the
    // day's own length, which is why one set of numbers works for a 5-ticket day and
    // a 10-ticket one without a per-Day table.
    [CreateAssetMenu(fileName = "StarScoreConfig", menuName = "ExpoTheExplorer/Data/Star Score Config")]
    public class StarScoreConfig : ScriptableObject
    {
        // Both thresholds are inclusive lower bounds and Three is tested FIRST, so
        // authoring Three below Two makes the two-star band unreachable rather than
        // throwing -- the same posture EconomyConfig's tier ratios take, for the same
        // reason: an Inspector can produce any pair of numbers, and a config that
        // crashes on a nonsense pair is worse than one that reads it literally.
        [Header("Star Thresholds — net score, a fraction of the day's own ticket clock")]
        [Tooltip("Net score at or above which the day is worth 3 stars. 0.55 = the player handed back 55% of the day's total ticket time after mistakes were deducted. Starting number from the D-060 estimate, NOT balanced — retune from real play.")]
        [SerializeField, Range(0f, 1f)] private float threeStarScore = 0.55f;

        [Tooltip("Net score at or above which the day is worth 2 stars. Below it a COMPLETED day still earns 1 star — finishing is never worth nothing, except after a paid Continue (see DayLifecycleManager.StarCount).")]
        [SerializeField, Range(0f, 1f)] private float twoStarScore = 0.3f;

        // The two penalties are deliberately NOT equal, and the asymmetry is the whole
        // design: a timeout has already cost the player its ticket's entire share of
        // the day's clock (that ticket contributes zero saved seconds), so charging it
        // the full amount again would punish one mistake twice. A wrong delivery costs
        // almost no time at all -- it is over in a shake and a scatter -- so its bill
        // has to be explicit or it would be the cheaper way to fail.
        [Header("Failure Penalties — score points per failure")]
        [Tooltip("Subtracted from the score for every WRONG delivery (TrayManager's batch check). 0.2 is roughly one star band, so a single slip costs a star unless the player was fast enough to absorb it.")]
        [SerializeField, Min(0f)] private float wrongDeliveryPenalty = 0.2f;

        [Tooltip("Subtracted from the score for every ticket that TIMED OUT. Deliberately smaller than the wrong-delivery penalty: a timeout already forfeited that ticket's whole share of the day's clock, so this is the remainder of its bill, not the bill.")]
        [SerializeField, Min(0f)] private float timeoutPenalty = 0.05f;

        public float ThreeStarScore => threeStarScore;
        public float TwoStarScore => twoStarScore;
        public float WrongDeliveryPenalty => wrongDeliveryPenalty;
        public float TimeoutPenalty => timeoutPenalty;
    }
}
