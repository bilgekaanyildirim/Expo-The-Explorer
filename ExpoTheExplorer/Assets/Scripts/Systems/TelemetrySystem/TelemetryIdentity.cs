using System;

namespace ExpoTheExplorer.Systems.TelemetrySystem
{
    // Who is playing, for playtest telemetry only. Three ids answer three different
    // questions and none of them can answer another's (.claude/telemetry-plan.md §C):
    //
    //   installationId  which physical install/device is this
    //   playerId        which logical playtester is sitting in front of it
    //   runId           which single Day attempt -- NOT here, it lives only in RAM
    //
    // NOTHING IN THE SHIPPING GAME READS THIS FILE. It carries no progress, no
    // balance and no unlock; deleting it costs the player nothing and costs the
    // analytics side only the link between this device and its past runs.
    //
    // IT IS A SECOND PERSISTENCE BOUNDARY, and that is the point rather than an
    // oversight (plan §C.3). The obvious cheaper move -- add two fields to
    // PlayerProfile -- was rejected: PlayerProfileStore.Delete() is built to know
    // NOTHING about its payload, which is exactly what makes "reset the player"
    // exact rather than a list of fields someone can forget to clear. Exempting two
    // fields from that delete would end the property for the sake of saving a file.
    // Keeping them apart is also what makes `Delete Save File` and `Reset Game +
    // New Test Player` two genuinely different operations instead of one with a
    // flag.
    //
    // Fields must stay public FIELDS, not properties, for the reason PlayerProfile
    // spells out: UnityEngine.JsonUtility serializes public fields only, and an
    // auto-property round-trips as "{}" with no error at all.
    [Serializable]
    public class TelemetryIdentity
    {
        // Schema version of the file this came from. 0 means no version was ever
        // written -- TelemetryIdentityStore refuses those rather than guessing, the
        // same rule the root CLAUDE.md invariant puts on the save file.
        public int Version;

        // The install this game sits in, e.g. "I_A72FC91D". Minted once, on the first
        // launch that ever reads this file, and then NEVER written again -- not by a
        // save wipe, not by a new tester, not by anything in this project. That
        // stability is the whole reason it exists: it is what lets the analytics side
        // see that three different playtesters came from the same physical phone.
        //
        // A reinstall legitimately produces a new one. There is no way around that
        // without a device identifier, and the plan (§27, §M.2) rules those out on
        // purpose: no advertising id, no serial, no SystemInfo.deviceUniqueIdentifier.
        public string InstallationId;

        // The logical playtester, e.g. "P_72AB19" by default or "T001" when a human
        // typed one into the debug panel. Rotated by "Reset Game + New Test Player",
        // which is the ONLY thing in this project that changes it.
        //
        // Random by default rather than sequential, and that is a decision rather than
        // laziness: an installation-local counter would mint "T001" on every phone in
        // the room, and two testers would collide in the reports under one id. A human
        // who wants readable ids assigns them deliberately, and then owns keeping them
        // unique.
        public string PlayerId;

        // How many playtesters this installation has been through -- 1 for the first,
        // 2 after the first reset, and so on. Not an id and never used as one; it is
        // there so a report can say "this was the third tester on that device" without
        // having to order runs by timestamp and hope.
        public int PlayerOrdinal;

        // When THIS player was minted, as UTC ticks. Ticks rather than a DateTime for
        // the reason PlayerProfile.LastKeyRegenUtcTicks already documents: JsonUtility
        // cannot serialize a DateTime and round-trips it as "{}" with no error.
        //
        // Deliberately the only clock reading in this system. Every run timestamp that
        // matters -- started, last updated, completed -- is a Firestore SERVER stamp
        // (plan §F.3), because a playtest device's clock can be wrong by hours and a
        // report that sorts attempts by a lying clock is worse than one with no times
        // at all. This field is not used for ordering anything; it answers "when did
        // this tester start" for a player who has not finished a run yet.
        public long PlayerCreatedAtUtcTicks;
    }
}
