using HarmonyLib;
using Manager;
using MelonLoader;
using Monitor;

namespace BetterFreedomMode;

/// <summary>
/// The termination prompt runs in PleaseWaitProcess, on the monitor with no player entered — on a 1P
/// cabinet that is the 2P side. MusicSelectProcess keeps running underneath it on the player's side
/// and reads the same physical buttons, so a press on NEXT reaches music select first and starts a
/// song instead of answering the prompt.
///
/// The stock roles already line up with the labels on screen, so nothing is remapped:
///
///     Button04  bottom right  red   NEXT  -> confirm, end the credit
///     Button05  bottom left   blue  BACK  -> cancel, keep playing
///
/// What changes is who gets to see the press. While the prompt is up, the player side is made to
/// report nothing at all, which both stops music select acting on the press and leaves the six
/// unrelated buttons inert — matching the LEDs that FreedomExitLeds turns off. The prompt's own side
/// sees only those two buttons, taken from whichever side was actually pressed, since the 2P
/// buttons may not exist on a 1P cabinet.
///
/// Cancelling restores everything on its own: SetCountDown ends the prompt here and calls
/// ResetLEDColor in the game, which repaints all eight buttons.
///
/// An earlier version let any button confirm, as a way around button 4 seeming unreachable. It is
/// not unreachable — it is NEXT, and it was being eaten by music select. That workaround is gone:
/// it would now fire on buttons this prompt is supposed to ignore.
/// </summary>
public static class FreedomExitAnyButton
{
    /// <summary>Monitor whose termination prompt is open, or -1 when none is.</summary>
    private static int _armedMonitor = -1;

    /// <summary>
    /// Set while this patch reads the button state for itself. Without it the player-side
    /// suppression below would hide the very press being relayed to the prompt.
    /// </summary>
    private static bool _reading;

    private static MelonPreferences_Entry<bool> _enabled;

    public static void LoadPreferences(MelonPreferences_Category category)
    {
        _enabled = category.CreateEntry(
            "ExitPromptOwnsTheButtons", true,
            description: "While the Freedom Mode termination prompt is up, give it sole use of the " +
                         "buttons: NEXT confirms, BACK cancels, everything else does nothing and " +
                         "music select cannot act on the press.");
    }

    /// <summary>Only call site is PleaseWaitProcess, right after the swiped Aime matches the player.</summary>
    [HarmonyPostfix]
    [HarmonyPatch(typeof(PleaseWaitMonitor), nameof(PleaseWaitMonitor.SetTerminationCheck))]
    public static void PostSetTerminationCheck(PleaseWaitMonitor __instance)
    {
        _armedMonitor = __instance.MonitorIndex;
        MelonLogger.Msg($"Termination prompt open on monitor {_armedMonitor}: NEXT confirms, " +
                        "BACK cancels, every other button is inert.");
    }

    /// <summary>Covers all three ways out of the prompt: confirm, cancel, and the 5s timeout.</summary>
    [HarmonyPostfix]
    [HarmonyPatch(typeof(PleaseWaitMonitor), nameof(PleaseWaitMonitor.SetCountDown))]
    public static void PostSetCountDown()
    {
        if (_armedMonitor < 0) return;
        MelonLogger.Msg($"Termination prompt on monitor {_armedMonitor} closed; buttons released.");
        _armedMonitor = -1;
    }

    [HarmonyPostfix]
    [HarmonyPatch(typeof(InputManager), nameof(InputManager.GetButtonDown),
        typeof(int), typeof(InputManager.ButtonSetting))]
    public static void PostGetButtonDown(int monitorId, InputManager.ButtonSetting button, ref bool __result)
    {
        if (_armedMonitor < 0) return;
        if (_reading) return;
        if (_enabled is not { Value: true }) return;

        if (monitorId != _armedMonitor)
        {
            // The player's side. Swallow everything so music select cannot start a song on the
            // press that is meant to answer the prompt.
            __result = false;
            return;
        }

        if (button != InputManager.ButtonSetting.Button04 &&
            button != InputManager.ButtonSetting.Button05)
        {
            __result = false;
            return;
        }

        var wasDown = __result;
        __result = IsDownOnEitherSide(button);
        if (!__result || wasDown) return;

        MelonLogger.Msg(button == InputManager.ButtonSetting.Button04
            ? "NEXT pressed -> ending the credit."
            : "BACK pressed -> keeping the credit running.");
    }

    /// <summary>
    /// Reads the button on both player sides. The prompt lives on the side whose buttons a 1P
    /// cabinet does not have, so the press has to be picked up from the other one.
    /// </summary>
    private static bool IsDownOnEitherSide(InputManager.ButtonSetting button)
    {
        _reading = true;
        try
        {
            for (var monitor = 0; monitor < 2; monitor++)
            {
                if (InputManager.GetButtonDown(monitor, button)) return true;
            }

            return false;
        }
        finally
        {
            _reading = false;
        }
    }
}
