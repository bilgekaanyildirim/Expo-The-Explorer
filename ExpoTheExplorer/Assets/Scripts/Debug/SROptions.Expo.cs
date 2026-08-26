#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System.ComponentModel;
using ExpoTheExplorer.Bootstrap;
using ExpoTheExplorer.Core;
using ExpoTheExplorer.Data;
using ExpoTheExplorer.DebugMenu;
using ExpoTheExplorer.Session;
using ExpoTheExplorer.Systems.ProgressionSystem;
using UnityEngine;

// The cheat and inspection surface on SRDebugger's Options tab (decisions.md D-092).
//
// SHAPE IS DICTATED BY SRDEBUGGER, not chosen: it builds the panel by reflecting over the
// PUBLIC instance members of this class -- a public method becomes a button, a public
// property with a setter becomes an editable field, a getter-only property becomes a
// readout. Everything that is not an option on screen is therefore private or internal,
// and that is load-bearing rather than stylistic: making a helper public would silently
// put a junk row in the panel.
//
// It is a `partial` half of SRDebugger's own global SROptions class, which is why this file
// has no namespace and why Scripts/Debug/ carries no asmdef -- a partial's halves must
// share an assembly, and SRDebugger's half compiles into Assembly-CSharp.
//
// THE ONE RULE EVERY CHEAT HERE FOLLOWS: it commands the system that already owns the data
// and never assigns anything itself. Money goes through Wallet, keys through KeyManager,
// charges through PowerupManager, lives through LivesManager, the day index through
// GameSession.GoToDay. A cheat menu is the most tempting place in a codebase to write
// `state.Gems = 9999`, and one such line would hand a balance a second writer and void the
// root invariant for the sake of a debug button.
public partial class SROptions
{
    // ---- binding -----------------------------------------------------------------
    // Static, because SRDebugger reconstructs the SROptions INSTANCE on its
    // [RuntimeInitializeOnLoadMethod] and a scene's binder has no way to reach whichever
    // instance is current. Statics survive that reconstruction; instance fields do not.
    private static DebugMenuBinder _binder;

    internal static void BindDebugMenu(DebugMenuBinder binder)
    {
        _binder = binder;
    }

    internal static void UnbindDebugMenu(DebugMenuBinder binder)
    {
        // Identity-checked so a late OnDisable from an outgoing scene cannot clear the
        // binder the incoming scene has already registered.
        if (ReferenceEquals(_binder, binder)) _binder = null;
    }

    // Resolved per press rather than cached -- see the binder's note on why reading the
    // session at bind time would be a race. The `!= null` comparisons are Unity's overload,
    // so a destroyed component reads as null here instead of throwing later.
    private static GameSession Sess =>
        _binder != null && _binder.Host != null ? _binder.Host.Session : null;

    // Non-null only in the day scene. The day-only cheats use it to refuse politely on the
    // main screen instead of throwing a NullReference at a tester.
    private static GameManager Day => _binder != null ? _binder.Host as GameManager : null;

    private static bool NoSession()
    {
        if (Sess != null) return false;
        Debug.LogWarning("[DebugMenu] No session. Is DebugMenuBinder on this scene's host object, with its Host slot filled?");
        return true;
    }

    // ---- Wallet ------------------------------------------------------------------
    private int _amount = 100;

    [Category("Wallet"), Sort(0), Increment(100), DisplayName("Amount")]
    public int Amount
    {
        get { return _amount; }
        set { _amount = value; OnPropertyChanged("Amount"); }
    }

    [Category("Wallet"), Sort(1), DisplayName("Add Coins")]
    public void AddCoins()
    {
        if (NoSession()) return;
        Sess.Wallet.EarnSoftMoney(_amount);
        RebaselineDay();
    }

    [Category("Wallet"), Sort(2), DisplayName("Add Gems")]
    public void AddGems()
    {
        if (NoSession()) return;
        Sess.Wallet.EarnGems(_amount);
        RebaselineDay();
    }

    // The atomic-day contract's collision with cheating, handled out loud rather than
    // silently. Wallet snapshots both balances at day start and RevertToDayStart puts them
    // back when an attempt is abandoned -- so money granted mid-day would simply VANISH on
    // the next retry, and the cheat would read as broken while working perfectly.
    //
    // Re-taking the snapshot makes the grant stick. The price is real and is why this logs:
    // whatever the player legitimately earned earlier in this day stops being revertible
    // too. That only matters if they then abandon the attempt, and this is a debug build,
    // so the trade is worth it -- but a tester chasing a revert bug needs to know the cheat
    // touched the thing they are testing.
    private static void RebaselineDay()
    {
        Sess.Wallet.CaptureDayStart();
        Debug.Log("[DebugMenu] Granted, and the day-start baseline was re-taken so it sticks. This day's earlier earnings will no longer revert if you abandon the attempt.");
    }

    // ---- Keys --------------------------------------------------------------------
    [Category("Keys"), Sort(1), DisplayName("Fill Keys")]
    public void FillKeys()
    {
        if (NoSession()) return;
        // The cap is read from KeyConfig through the manager, never typed here: the number
        // of keys a bar holds is authored content and has exactly one home.
        Sess.KeyManager.DebugSetKeys(Sess.KeyManager.MaxKeys);
    }

    [Category("Keys"), Sort(2), DisplayName("Empty Keys")]
    public void EmptyKeys()
    {
        if (NoSession()) return;
        Sess.KeyManager.DebugSetKeys(0);
    }

    [Category("Keys"), Sort(3), DisplayName("Spend One Key")]
    public void SpendKey()
    {
        if (NoSession()) return;
        if (!Sess.KeyManager.TrySpendKey()) Debug.LogWarning("[DebugMenu] No keys to spend.");
    }

    // ---- Lives -------------------------------------------------------------------
    [Category("Lives"), Sort(1), DisplayName("Refill Lives")]
    public void RefillLives()
    {
        if (NoSession()) return;
        // The free day-reset primitive LivesManager already owns; it also clears
        // IsAwaitingContinue, which is what makes this usable from the game-over popup.
        Sess.LivesManager.RefillForNewDay();
    }

    // ---- Powerups ----------------------------------------------------------------
    private int _charges = 5;

    [Category("Powerups"), Sort(0), Increment(1), DisplayName("Charges")]
    public int Charges
    {
        get { return _charges; }
        set { _charges = value; OnPropertyChanged("Charges"); }
    }

    [Category("Powerups"), Sort(1), DisplayName("Grant Auto-Collect")]
    public void GrantAutoCollect()
    {
        GrantPowerup(PowerupType.AutoCollect);
    }

    [Category("Powerups"), Sort(2), DisplayName("Grant Time Reset")]
    public void GrantTimeReset()
    {
        GrantPowerup(PowerupType.TimeReset);
    }

    [Category("Powerups"), Sort(3), DisplayName("Grant Noise Clear")]
    public void GrantNoiseClear()
    {
        GrantPowerup(PowerupType.NoiseClear);
    }

    private void GrantPowerup(PowerupType type)
    {
        if (NoSession()) return;
        if (Sess.PowerupManager == null)
        {
            Debug.LogWarning("[DebugMenu] This scene's host has no PowerupConfig wired, so there is no PowerupManager to grant into.");
            return;
        }

        Sess.PowerupManager.DebugGrant(type, _charges);
    }

    // ---- Day ---------------------------------------------------------------------
    private int _dayNumber = 1;

    [Category("Day"), Sort(0), Increment(1), DisplayName("Day Number")]
    public int DayNumber
    {
        get { return _dayNumber; }
        set { _dayNumber = value; OnPropertyChanged("DayNumber"); }
    }

    // The runtime replacement for the editor's Play From Day window (D-091), and it inherits
    // that window's one hard-won correctness rule: the number written is a POSITION in the
    // RESOLVED catalog, not a Day file's own dayIndex. The two agree only while every Day
    // parses -- DayCatalogParser DROPS a broken Day and slides every later one down -- so
    // the field on screen is 1-based over the parsed list and nothing else.
    //
    // Main screen only, and it says so rather than half-working. From inside a running day
    // this would have to abandon the attempt to leave, which means deciding what happens to
    // that day's wallet -- a question the atomic-day contract already answers through
    // ReturnToMainScreenAbandoningDay, and one a cheat button has no business answering
    // differently.
    [Category("Day"), Sort(1), DisplayName("Go To Day")]
    public void GoToDay()
    {
        if (NoSession()) return;
        if (Day != null)
        {
            Debug.LogWarning("[DebugMenu] Go To Day is a main-screen cheat. Leave this day first, then jump.");
            return;
        }

        Sess.GoToDay(_dayNumber - 1);
        Sess.Save();

        // The screen would otherwise lie: MainScreenView reads the day from the profile once
        // in Start, and the meta views read State.CurrentDayIndex without subscribing to its
        // change event. Reloading rebuilds all of them from the file just written, which is
        // cheaper to reason about than teaching four views to repaint on demand.
        SceneFlow.LoadMainScreen();
    }

    [Category("Day"), Sort(2), DisplayName("Next Day")]
    public void NextDay()
    {
        if (RequireDayScene() && !Day.AdvanceToNextDay())
        {
            Debug.LogWarning("[DebugMenu] Already on the last authored Day.");
        }
    }

    [Category("Day"), Sort(3), DisplayName("Retry Day")]
    public void RetryDay()
    {
        if (RequireDayScene()) Day.RetryDay();
    }

    [Category("Day"), Sort(4), DisplayName("Deliver Ticket 1")]
    public void DeliverTicket1()
    {
        DeliverTicket(0);
    }

    [Category("Day"), Sort(5), DisplayName("Deliver Ticket 2")]
    public void DeliverTicket2()
    {
        DeliverTicket(1);
    }

    [Category("Day"), Sort(6), DisplayName("Deliver Ticket 3")]
    public void DeliverTicket3()
    {
        DeliverTicket(2);
    }

    // Replaces DebugTicketDeliveryController's 1/2/3 keys, which is why that component was
    // deleted: a keyboard-only cheat cannot be pressed on the device the game ships to.
    private void DeliverTicket(int slotIndex)
    {
        if (!RequireDayScene()) return;
        Day.TicketSlotManager.DeliverTicket(slotIndex);
    }

    private static bool RequireDayScene()
    {
        if (Day != null) return true;
        Debug.LogWarning("[DebugMenu] That cheat only works inside a day.");
        return false;
    }

    // ---- Meta --------------------------------------------------------------------
    [Category("Meta"), Sort(1), DisplayName("Unlock All Props")]
    public void UnlockAllProps()
    {
        if (NoSession()) return;

        var catalog = _binder.Catalog;
        if (catalog == null)
        {
            Debug.LogWarning("[DebugMenu] Drag a MetaCatalog into DebugMenuBinder to use this.");
            return;
        }

        // The key FORMAT has one authority (MetaCatalog.OwnershipKey) and this asks for it
        // rather than composing "location.item" by hand -- a second copy of that format is
        // exactly how an owned set stops matching the catalog that reads it.
        foreach (var location in catalog.Locations)
        {
            foreach (var item in location.Items)
            {
                Sess.OwnedMetaItemIds.Add(MetaCatalog.OwnershipKey(location.Id, item.Id));
            }
        }

        Sess.Save();
    }

    [Category("Meta"), Sort(2), DisplayName("Reset Owned Props")]
    public void ResetOwnedProps()
    {
        if (NoSession()) return;
        Sess.OwnedMetaItemIds.Clear();
        Sess.Save();
    }

    // ---- Save --------------------------------------------------------------------
    // Not a convenience: the game writes the profile only at a handful of day-lifecycle
    // moments, so every cheat above is lost on quit unless something forces a write. This
    // is that something.
    [Category("Save"), Sort(1), DisplayName("Save Now")]
    public void SaveNow()
    {
        if (NoSession()) return;
        Sess.Save();
        Debug.Log("[DebugMenu] Profile written.");
    }

    [Category("Save"), Sort(2), DisplayName("Delete Save File")]
    public void DeleteSaveFile()
    {
        Debug.Log(new PlayerProfileStore().Delete()
            ? "[DebugMenu] Save deleted. Restart play mode for a fresh player."
            : "[DebugMenu] No save file to delete.");
    }

    [Category("Save"), Sort(3), DisplayName("Save File Path")]
    public string SaveFilePath => Application.persistentDataPath;

    // ---- Info (read-only) --------------------------------------------------------
    [Category("Info"), Sort(1), DisplayName("Coins")]
    public int InfoCoins => Sess != null ? Sess.State.SoftMoney : -1;

    [Category("Info"), Sort(2), DisplayName("Gems")]
    public int InfoGems => Sess != null ? Sess.State.Gems : -1;

    [Category("Info"), Sort(3), DisplayName("Keys")]
    public int InfoKeys => Sess != null ? Sess.KeyManager.Keys : -1;

    [Category("Info"), Sort(4), DisplayName("Next Key In (s)")]
    public int InfoNextKeySeconds => Sess != null ? Sess.KeyManager.SecondsUntilNextKey() : -1;

    [Category("Info"), Sort(5), DisplayName("Lives")]
    public int InfoLives => Sess != null ? Sess.State.Lives : -1;

    [Category("Info"), Sort(6), DisplayName("Day")]
    public string InfoDay =>
        Sess != null ? $"{Sess.State.CurrentDayIndex + 1} / {Sess.DayCatalog.Count}" : "-";

    [Category("Info"), Sort(7), DisplayName("Delivered Today")]
    public int InfoDelivered => Day != null ? Day.DayLifecycleManager.OrdersDeliveredCount : -1;

    [Category("Info"), Sort(8), DisplayName("Failed Today")]
    public int InfoFailed => Day != null ? Day.DayLifecycleManager.OrdersFailedCount : -1;

    [Category("Info"), Sort(9), DisplayName("Stars")]
    public int InfoStars => Day != null ? Day.DayLifecycleManager.StarCount : -1;
}
#endif
