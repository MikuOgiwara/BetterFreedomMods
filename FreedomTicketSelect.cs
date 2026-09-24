using System;
using HarmonyLib;

using Manager;
using MelonLoader;
using Monitor.TicketSelect;

namespace BetterFreedomMode;

/// <summary>
/// The ticket select screen is reached from CHARACTER SELECT -> GetPresentProcess(Ticket), a path
/// that opens up again once AREA SELECT is restored (see FreedomAreaSelect). The screen itself then
/// still refuses to do anything in Freedom Mode: TicketSelectMonitor.Initialize auto-answers it.
///
///     line 1074: if (userType == New || IsEventMode || IsFreedomMode || IsCourseMode)
///                { _isAwake = _isDecidedOK = _isDecidedEntry = true; _decidedSelectTicketID = 0; }
///
/// The same method carries the only other Freedom test in the file:
///
///     line 918:  if (userType != New &amp;&amp; userType != Guest &amp;&amp; !IsEventMode
///                    &amp;&amp; !IsFreedomMode &amp;&amp; !IsCourseMode) SaveTrialTicketGet(monIndex);
///
/// Both move to the normal-credit behaviour together when the flag reads false, which is what we
/// want: the player picks a ticket, and the trial ticket is recorded as it would be otherwise.
/// </summary>
public static class FreedomTicketSelect
{
    private static bool _loggedFirstWindow;

    private static MelonPreferences_Entry<bool> _enabled;

    public static void LoadPreferences(MelonPreferences_Category category)
    {
        _enabled = category.CreateEntry(
            "TicketSelectInFreedom", true,
            description: "Let the ticket select screen work in Freedom Mode instead of " +
                         "auto-answering it with no ticket.");
    }

    [HarmonyPrefix]
    [HarmonyPatch(typeof(TicketSelectMonitor), nameof(TicketSelectMonitor.Initialize))]
    public static void PreTicketSelectInitialize()
    {
        var on = _enabled is { Value: true };
        FreedomFlagScope.Enter(on);

        if (!on || _loggedFirstWindow || !FreedomFlagScope.IsHeld) return;
        _loggedFirstWindow = true;
        Log.Info("Ticket select is being initialised as a normal credit.");
    }

    /// <summary>Finalizer, not postfix: the flag must come back even if Initialize throws.</summary>
    [HarmonyFinalizer]
    [HarmonyPatch(typeof(TicketSelectMonitor), nameof(TicketSelectMonitor.Initialize))]
    public static Exception FinTicketSelectInitialize(Exception __exception)
    {
        FreedomFlagScope.Exit();
        return __exception;
    }
}
