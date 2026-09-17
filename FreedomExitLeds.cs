using System.Collections.Generic;
using HarmonyLib;
using IO;
using Mecha;
using MelonLoader;
using Monitor;
using UI;
using UnityEngine;

namespace BetterFreedomMode;

/// <summary>
/// While the termination prompt is up, only two buttons mean anything: index 3 is NEXT (confirm)
/// and index 4 is BACK (cancel). So for as long as it is open the prompt owns all eight lamps —
/// those two lit in the game's own Yes/No colours, the other six dark — and everything goes back
/// exactly as it was when the prompt closes.
///
/// Owning all eight, rather than only darkening the six, matters: the lamps underneath belong to
/// whatever menu is showing, and menus differ. Category select offers only NEXT, so it never lights
/// lamp 4 at all, and leaving that lamp alone left BACK unlit while the prompt was asking a
/// yes-or-no question. Colours come from CommonButtonObject.LedColors32, which resolves
/// LedColors.Red and .Blue through the game's LED settings, so nothing is hardcoded.
///
/// Three things make this harder than writing the lamps once.
///
/// The prompt lives on the monitor with no player entered — monitor 1 on a 1P cabinet — so writing
/// to MechaManager.LedIf[promptMonitor] drives the side whose buttons do not physically exist. The
/// lamps the player sees belong to the other device, so every device is written.
///
/// A lamp cannot be read back: Bd15070_4IF exposes only setters and _setColor is private. The
/// colours to restore therefore have to be captured on their way in, and every writer has to be
/// caught or the captured state goes stale. SetColorButton delegates to SetColor, the timeline
/// track in DB/LedBlock calls SetColor directly, and SetColorMulti writes all eight at once —
/// ButtonLedReset is just SetColorMulti(black). Missing that last one left a lamp recorded as lit
/// after the game had switched it off, so restoring lit it back up.
///
/// Nothing is recorded while the prompt is open. PleaseWaitMonitor.ResetLEDColor fires its own
/// repaint before the prompt finishes closing; recording that would hand the player a countdown
/// colour instead of the menu lighting they had.
/// </summary>
public static class FreedomExitLeds
{
    private const byte LampCount = 8;

    private const byte ConfirmLamp = 3;

    private const byte CancelLamp = 4;

    private static readonly Color32 Off = new(0, 0, 0, 255);

    /// <summary>Last colour each device was asked for, per lamp, from before the prompt opened.</summary>
    private static readonly Dictionary<Bd15070_4IF, Color32[]> Requested = new();

    /// <summary>Lamp + setter pairs already reported, so the log stays to one line each.</summary>
    private static readonly HashSet<(byte, string)> LateWrites = [];

    private static bool _promptOpen;

    /// <summary>Set while this class writes, so its own writes are not mistaken for requests.</summary>
    private static bool _applying;

    private static MelonPreferences_Entry<bool> _enabled;

    public static void LoadPreferences(MelonPreferences_Category category)
    {
        _enabled = category.CreateEntry(
            "DimLedsOnTerminationPrompt", true,
            description: "While the Freedom Mode termination prompt is up, light only the two " +
                         "buttons that answer it — NEXT and BACK — and turn the rest off. The " +
                         "previous lighting comes back when the prompt closes.");
    }

    private static bool Active => _promptOpen && _enabled is { Value: true };

    /// <summary>What a lamp shows while the prompt owns it.</summary>
    private static Color32 PromptColour(byte ledPos) => ledPos switch
    {
        ConfirmLamp => CommonButtonObject.LedColors32(CommonButtonObject.LedColors.Red),
        CancelLamp => CommonButtonObject.LedColors32(CommonButtonObject.LedColors.Blue),
        _ => Off,
    };

    /// <summary>Single-lamp writes: SetColorButton delegates here, and DB/LedBlock calls it directly.</summary>
    [HarmonyPrefix]
    [HarmonyPatch(typeof(Bd15070_4IF), nameof(Bd15070_4IF.SetColor))]
    public static void PreSetColor(Bd15070_4IF __instance, byte ledPos, ref Color32 color)
    {
        if (_applying || __instance == null || ledPos >= LampCount) return;

        if (Active)
        {
            LogLateWrite(ledPos, color, "SetColor");
            color = PromptColour(ledPos);
            return;
        }

        Lamps(__instance)[ledPos] = color;
    }

    /// <summary>The press flash. Held down while the prompt is up, never recorded: it is transient.</summary>
    [HarmonyPrefix]
    [HarmonyPatch(typeof(Bd15070_4IF), nameof(Bd15070_4IF.SetColorSwitch))]
    public static void PreSetColorSwitch(byte ledPos, ref Color32 color)
    {
        if (_applying || ledPos >= LampCount || !Active) return;

        LogLateWrite(ledPos, color, "SetColorSwitch");
        color = PromptColour(ledPos);
    }

    /// <summary>
    /// Bulk writes hit all eight lamps at once, so they cannot be filtered per lamp on the way in.
    /// Outside the prompt they are recorded for every lamp — ButtonLedReset is SetColorMulti(black),
    /// and missing it was what left a stale colour to restore. Inside the prompt they are allowed
    /// through and then overwritten.
    /// </summary>
    [HarmonyPostfix]
    [HarmonyPatch(typeof(Bd15070_4IF), nameof(Bd15070_4IF.SetColorMulti))]
    public static void PostSetColorMulti(Bd15070_4IF __instance, Color32 color) => RecordBulk(__instance, color);

    [HarmonyPostfix]
    [HarmonyPatch(typeof(Bd15070_4IF), nameof(Bd15070_4IF.SetColorMultiFade))]
    public static void PostSetColorMultiFade(Bd15070_4IF __instance, Color32 color) => RecordBulk(__instance, color);

    private static void RecordBulk(Bd15070_4IF device, Color32 color)
    {
        if (_applying || device == null) return;

        if (!Active)
        {
            var lamps = Lamps(device);
            for (byte lamp = 0; lamp < LampCount; lamp++) lamps[lamp] = color;
            return;
        }

        LogLateWrite(0, color, "SetColorMulti");
        WriteLamps(device, restore: false);
    }

    [HarmonyPostfix]
    [HarmonyPatch(typeof(PleaseWaitMonitor), nameof(PleaseWaitMonitor.SetTerminationCheck))]
    public static void PostSetTerminationCheck()
    {
        if (_enabled is not { Value: true }) return;

        _promptOpen = true;
        LateWrites.Clear();
        WriteEveryDevice(restore: false);
        MelonLogger.Msg("Termination prompt owns the lamps: NEXT and BACK lit, the rest off.");
    }

    [HarmonyPostfix]
    [HarmonyPatch(typeof(PleaseWaitMonitor), nameof(PleaseWaitMonitor.SetCountDown))]
    public static void PostSetCountDown()
    {
        if (!_promptOpen) return;

        _promptOpen = false;
        WriteEveryDevice(restore: true);
        MelonLogger.Msg("Termination prompt closed: lamps restored.");
    }

    private static Color32[] Lamps(Bd15070_4IF device)
    {
        if (Requested.TryGetValue(device, out var lamps)) return lamps;

        lamps = new Color32[LampCount];
        Requested[device] = lamps;
        return lamps;
    }

    /// <summary>
    /// Every device, not just the prompt's own monitor: the prompt runs on the side with no player
    /// and the visible lamps are on the other one.
    /// </summary>
    private static void WriteEveryDevice(bool restore)
    {
        var devices = MechaManager.LedIf;
        if (devices == null) return;

        foreach (var device in devices)
        {
            if (device != null) WriteLamps(device, restore);
        }
    }

    private static void WriteLamps(Bd15070_4IF device, bool restore)
    {
        var lamps = restore && Requested.TryGetValue(device, out var recorded) ? recorded : null;

        _applying = true;
        try
        {
            for (byte lamp = 0; lamp < LampCount; lamp++)
            {
                device.SetColorButton(lamp, lamps != null ? lamps[lamp] : PromptColour(lamp));
            }
        }
        finally
        {
            _applying = false;
        }
    }

    /// <summary>Names any path that tries to repaint while the prompt owns the lamps, once each.</summary>
    private static void LogLateWrite(byte ledPos, Color32 color, string setter)
    {
        if (color is { r: 0, g: 0, b: 0 }) return;
        if (!LateWrites.Add((ledPos, setter))) return;

        MelonLogger.Msg($"Overrode a late {setter} on lamp {ledPos} ({color.r},{color.g},{color.b}).");
    }
}
