using ExpoTheExplorer.Data;

namespace ExpoTheExplorer.Core
{
    // Runtime instance of a modification on a specific ticket. ModificationConfig
    // only describes the modification's identity/icon; the +/- direction is
    // decided per-ticket when the ticket is generated.
    public class Modification
    {
        public ModificationConfig Config { get; }
        public bool IsAddition { get; }

        public Modification(ModificationConfig config, bool isAddition)
        {
            Config = config;
            IsAddition = isAddition;
        }
    }
}
