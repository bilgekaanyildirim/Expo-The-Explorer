namespace ExpoTheExplorer.Data
{
    // The three powerups GDD Section 5.2 locks down, and there are deliberately only
    // three: the section's own argument is that they relieve the game's three sources
    // of difficulty one each (gathering, time pressure, visual noise). A fourth entry
    // here is a design change, not a code change.
    //
    // Lives in Data rather than in the PowerupSystem assembly because PowerupConfig
    // (Data) has to name it and Data references nothing -- the same reason
    // PatienceType and FoodCategory live here.
    //
    // The numbers are EXPLICIT and are load-bearing: PowerupManager indexes its charge
    // and effect arrays by (int)type, so renumbering these would silently shuffle which
    // powerup holds whose charges. PowerupSystemTests pins all three values for exactly
    // that reason. They are NOT persisted as numbers, though -- PlayerProfile carries
    // one named int field per type, so a save file survives a renumbering even when the
    // runtime would not.
    public enum PowerupType
    {
        // GDD 5.2 #1 -- places the items active tickets still need into their trays.
        AutoCollect = 0,

        // GDD 5.2 #2 -- pulls every active ticket's countdown back to its own limit.
        TimeReset = 1,

        // GDD 5.2 #3 -- REMOVES from the board every item no active ticket needs.
        //
        // It was called BoardClarity until 2026-08-25, when the user corrected what it
        // does: an earlier design faded the noise for a few seconds, and "clarity" was an
        // honest name for that. It is not one for this. The effect now mutates the board
        // rather than how the board is drawn -- instant, permanent, and it frees cells --
        // so the name follows the GDD's own word for it (Gürültü Temizleme / Noise Clear).
        NoiseClear = 2,
    }

    // The iteration surface for the enum above. Three consumers need it -- the manager's
    // per-day grant, the day HUD's three buttons and the main screen's three shop rows --
    // which is what makes it worth having rather than three hand-written literals that
    // can each forget a type when a fourth is ever added.
    public static class PowerupTypes
    {
        // Order matches the enum's numbering, which is the order the HUD and the shop
        // render in. Kept in this order deliberately: it is the order GDD 5.2 lists them.
        public static readonly PowerupType[] All =
        {
            PowerupType.AutoCollect,
            PowerupType.TimeReset,
            PowerupType.NoiseClear,
        };

        public static int Count => All.Length;
    }
}
