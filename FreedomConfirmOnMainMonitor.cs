using DB;
using HarmonyLib;
using Manager;
using MelonLoader;
using Process;

namespace BetterFreedomMode;

/// <summary>
/// PleaseWaitProcess opens the termination confirmation on the monitor with no player entered,
/// which on a 1P cabinet is the 2P side: the window is real but renders on a monitor nobody sees.
/// This moves just that one window to the monitor the player is actually looking at.
///
/// AquaMai's SinglePlayer mod solves the sibling problem the same way, reparenting the Freedom
/// timer from monitors[1] onto monitors[0]'s sub canvas.
/// </summary>
public static class FreedomConfirmOnMainMonitor
{
    /// <summary>Monitor the window was redirected away from, or -1 when no redirect is live.</summary>
    private static int _redirectedFrom = -1;

    private static MelonPreferences_Entry<bool> _enabled;
    private static MelonPreferences_Entry<int> _targetMonitor;

    public static void LoadPreferences(MelonPreferences_Category category)
    {
        _enabled = category.CreateEntry(
            "ShowConfirmationOnMainMonitor", true,
            description: "Move the Freedom Mode termination confirmation to the monitor the player " +
                         "can see. Only needed on a single-screen cabinet.");
        _targetMonitor = category.CreateEntry(
            "ConfirmationMonitor", 0,
            description: "Which monitor the confirmation is moved to. 0 is the 1P side.");
    }

    [HarmonyPrefix]
    [HarmonyPatch(typeof(ProcessManager), nameof(ProcessManager.EnqueueMessage),
        typeof(int), typeof(WindowMessageID), typeof(WindowParam))]
    public static void PreEnqueueMessage(ref int monitorId, WindowMessageID messageId)
    {
        if (_enabled is not { Value: true }) return;
        if (messageId != WindowMessageID.FreedomModeTerminationMessage) return;

        var target = _targetMonitor?.Value ?? 0;
        if (monitorId == target) return;

        _redirectedFrom = monitorId;
        MelonLogger.Msg($"Confirmation window moved from monitor {monitorId} to {target}.");
        monitorId = target;
    }

    /// <summary>
    /// The three exits from TerminationCheck all call CloseWindow with the original monitor index,
    /// so the redirect has to be undone here or the window would never close.
    /// </summary>
    [HarmonyPrefix]
    [HarmonyPatch(typeof(ProcessManager), nameof(ProcessManager.CloseWindow), typeof(int))]
    public static void PreCloseWindow(ref int monitorId)
    {
        if (_redirectedFrom < 0) return;
        if (monitorId != _redirectedFrom) return;

        _redirectedFrom = -1;
        monitorId = _targetMonitor?.Value ?? 0;
    }

    /// <summary>
    /// Backstop. PreCloseWindow is the normal disarm, but if that CloseWindow never arrives the flag
    /// would stay armed and every later CloseWindow on that monitor would be rewritten. Bound the
    /// leak to the lifetime of the process that opened the window.
    ///
    /// This cannot live on PleaseWaitMonitor.SetCountDown: on the 5s-timeout path SetCountDown runs
    /// *before* CloseWindow, so disarming there would strand the window open on the target monitor.
    /// </summary>
    [HarmonyPostfix]
    [HarmonyPatch(typeof(PleaseWaitProcess), nameof(PleaseWaitProcess.OnRelease))]
    public static void PostPleaseWaitRelease()
    {
        if (_redirectedFrom < 0) return;
        MelonLogger.Warning($"Confirmation redirect from monitor {_redirectedFrom} was never closed; disarming.");
        _redirectedFrom = -1;
    }
}
