using System;

namespace ExpoTheExplorer.Systems.TelemetrySystem
{
    // The single place the SHAPE of a telemetry id is spelled. Every id in this
    // system is a prefix plus hex, and the prefix is what makes a value
    // self-describing in a Firestore console where three id columns sit side by
    // side: "I_A72FC91D" cannot be mistaken for "P_72AB19" by eye, whereas two bare
    // GUIDs can be mistaken for each other all day.
    //
    // Pure and static on purpose -- no state, no config, no Unity types -- so the
    // whole class is testable without a scene, a file or a player loop.
    //
    // NewRunId is NOT here yet. It belongs to the run layer, which is Step 3 of
    // .claude/telemetry-plan.md; adding it now would be an unused method whose
    // format nobody has exercised.
    public static class TelemetryIds
    {
        // Long enough that a collision is not worth thinking about, short enough to
        // read aloud across a room during a playtest -- which is a real requirement
        // here, since a tester reads their id off the debug panel to whoever is
        // taking notes. 8 hex = 4.3 billion; a playtest is tens of installs.
        private const int InstallationIdHexLength = 8;

        // Shorter than the installation id, deliberately. A playtester id is the one
        // a human types by hand ("T001") and reads most often, and the two are easier
        // to tell apart at a glance when they are not the same width.
        private const int PlayerIdHexLength = 6;

        // What a hand-typed tester id may contain. Letters, digits, underscore and
        // dash only: these strings become Firestore FIELD VALUES that reports group
        // by, and a stray quote, newline or trailing space turns one tester into two
        // in the output -- silently, because both look identical in a table.
        private const int MaxTesterIdLength = 32;

        public static string NewInstallationId() => "I_" + RandomHex(InstallationIdHexLength);

        public static string NewPlayerId() => "P_" + RandomHex(PlayerIdHexLength);

        // Cleans up what a human typed into the debug panel, or refuses it.
        //
        // REFUSING IS THE POINT, rather than silently repairing: a tester who typed
        // "T 001" and got "T001" would believe they had entered the first, and the
        // note next to the phone would disagree with the report forever after. So a
        // value that is not already usable is rejected out loud and the caller leaves
        // the current id alone. The only silent fix is trimming surrounding
        // whitespace, which no human means to type.
        //
        // Returns false with `sanitized` null when the input cannot be used.
        public static bool TrySanitizeTesterId(string raw, out string sanitized)
        {
            sanitized = null;
            if (string.IsNullOrWhiteSpace(raw)) return false;

            var trimmed = raw.Trim();
            if (trimmed.Length > MaxTesterIdLength) return false;

            foreach (var c in trimmed)
            {
                var allowed = (c >= 'a' && c <= 'z')
                              || (c >= 'A' && c <= 'Z')
                              || (c >= '0' && c <= '9')
                              || c == '_'
                              || c == '-';
                if (!allowed) return false;
            }

            sanitized = trimmed;
            return true;
        }

        // Guid rather than System.Random: this runs once per install and once per
        // tester, so the cost is irrelevant, and a Guid needs no seeding decision --
        // a System.Random seeded from a clock would hand two devices started in the
        // same second the same "unique" id, which is the one failure mode that would
        // be invisible until the reports were already wrong.
        //
        // Uppercase because the prefixes are, and a mixed-case id is harder to read
        // back over a table.
        private static string RandomHex(int length)
        {
            return Guid.NewGuid().ToString("N").Substring(0, length).ToUpperInvariant();
        }
    }
}
