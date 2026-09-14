using HarmonyLib;
using Manager;
using MelonLoader;
using Monitor;

namespace BetterFreedomMode;

/// <summary>
/// In Freedom Mode the operator ends the credit by tapping their Aime on the sub monitor and
/// then pressing button 4 to confirm (PleaseWaitProcess.OnUpdate, FreedomModeState.TerminationCheck).
/// That whole flow runs on the monitor with no player entered, so on a 1P cabinet the window opens
/// on the 2P side: invisible, and driven by buttons that are not wired up.
///
/// Rather than rewriting that state machine — it lives inline in a huge OnUpdate and would need a
/// transpiler — this reports button 4 as pressed whenever any other button is pressed on *either*
/// side, for as long as the window is open. Button 5 is likewise accepted from either side so the
/// stock cancel stays reachable. Everything downstream (LEDs, SE, ForcedTerminationForFreedomMode,
/// the button 4 press animation) still runs through the stock path.
/// </summary>
public static class FreedomExitAnyButton
{
    /// <summary>Monitor whose termination window is open, or -1 when no window is open.</summary>
    private static int _armedMonitor = -1;

    /// <summary>Guards the recursive GetButtonDown calls made while scanning the other buttons.</summary>
    private static bool _scanning;

    /// <summary>Keeps the substitution log to one line per window instead of one per frame.</summary>
    private static bool _loggedSubstitution;

    private static MelonPreferences_Entry<bool> _cancelButtonConfirms;

    /// <summary>
    /// Buttons that stand in for button 4. Button 4 itself is excluded (the game already reports it),
    /// button 5 is the stock cancel, and Select is the service button.
    /// </summary>
    private static readonly InputManager.ButtonSetting[] ConfirmButtons =
    [
        InputManager.ButtonSetting.Button01,
        InputManager.ButtonSetting.Button02,
        InputManager.ButtonSetting.Button03,
        InputManager.ButtonSetting.Button06,
        InputManager.ButtonSetting.Button07,
        InputManager.ButtonSetting.Button08,
    ];

    public static void LoadPreferences(MelonPreferences_Category category)
    {
        _cancelButtonConfirms = category.CreateEntry(
            "CancelButtonConfirms", false,
            description: "Let button 5 confirm the Freedom Mode termination too. " +
                         "When false (default) it keeps cancelling the window, as in the stock game.");
    }

    /// <summary>Only call site is PleaseWaitProcess, right after the swiped Aime matches the player.</summary>
    [HarmonyPostfix]
    [HarmonyPatch(typeof(PleaseWaitMonitor), nameof(PleaseWaitMonitor.SetTerminationCheck))]
    public static void PostSetTerminationCheck(PleaseWaitMonitor __instance)
    {
        _armedMonitor = __instance.MonitorIndex;
        _loggedSubstitution = false;
        MelonLogger.Msg($"Aime matched: termination window open on monitor {_armedMonitor}. " +
                        "Any button on either side now confirms; button 5 cancels.");
    }

    /// <summary>Covers all three ways out of TerminationCheck: confirm, cancel, and the 5s timeout.</summary>
    [HarmonyPostfix]
    [HarmonyPatch(typeof(PleaseWaitMonitor), nameof(PleaseWaitMonitor.SetCountDown))]
    public static void PostSetCountDown()
    {
        if (_armedMonitor < 0) return;
        MelonLogger.Msg($"Termination window on monitor {_armedMonitor} closed.");
        _armedMonitor = -1;
    }

    [HarmonyPostfix]
    [HarmonyPatch(typeof(InputManager), nameof(InputManager.GetButtonDown),
        typeof(int), typeof(InputManager.ButtonSetting))]
    public static void PostGetButtonDown(int monitorId, InputManager.ButtonSetting button, ref bool __result)
    {
        if (_armedMonitor < 0) return;
        if (monitorId != _armedMonitor) return;
        if (__result) return;
        if (_scanning) return;

        _scanning = true;
        try
        {
            var confirmWithCancelButton = _cancelButtonConfirms is { Value: true };

            if (button == InputManager.ButtonSetting.Button04)
            {
                foreach (var candidate in ConfirmButtons)
                {
                    if (!IsDownOnEitherSide(candidate)) continue;
                    Substitute(ref __result, candidate, "confirm");
                    return;
                }

                if (confirmWithCancelButton && IsDownOnEitherSide(InputManager.ButtonSetting.Button05))
                {
                    Substitute(ref __result, InputManager.ButtonSetting.Button05, "confirm");
                }
            }
            // The stock cancel lives on the same dead 2P side, so relay it from either side too.
            else if (button == InputManager.ButtonSetting.Button05 && !confirmWithCancelButton)
            {
                if (IsDownOnEitherSide(InputManager.ButtonSetting.Button05))
                {
                    Substitute(ref __result, InputManager.ButtonSetting.Button05, "cancel");
                }
            }
        }
        finally
        {
            _scanning = false;
        }
    }

    private static void Substitute(ref bool result, InputManager.ButtonSetting source, string action)
    {
        result = true;
        if (_loggedSubstitution) return;
        _loggedSubstitution = true;
        MelonLogger.Msg($"{source} pressed -> {action} freedom mode termination.");
    }

    /// <summary>
    /// Reads the button on both player sides. Recursion back into PostGetButtonDown is bounded by
    /// the _scanning guard, which is already set by the only caller.
    /// </summary>
    private static bool IsDownOnEitherSide(InputManager.ButtonSetting button)
    {
        for (var monitor = 0; monitor < 2; monitor++)
        {
            if (InputManager.GetButtonDown(monitor, button)) return true;
        }

        return false;
    }
}
