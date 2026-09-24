using System;
using System.Collections.Generic;
using MelonLoader;

namespace BetterFreedomMode;

public class Main : MelonMod
{
    public const string Version = "1.0.0";
    public const string Description = "Better Freedom Mode";
    public const string Author = "Mikuwu";

    /// <summary>
    /// Every patch set, applied one at a time. Verified identical across CiRCLE PLUS and Magical
    /// Code — same types, same signatures, same behaviour — so there is no per-version branch here;
    /// a build that ever diverges is caught by the isolation below rather than by a version check.
    /// </summary>
    private static readonly Type[] PatchSets =
    [
        typeof(FreedomExitAnyButton),
        typeof(FreedomConfirmOnMainMonitor),
        typeof(FreedomImmediateExit),
        typeof(FreedomReportAsNormalPlay),
        typeof(FreedomMapSystem),
        typeof(FreedomAreaSelect),
        typeof(FreedomTicketSelect),
        typeof(FreedomExitLeds),
        typeof(FreedomLevelBonusCap),
        typeof(FreedomLevelBonusDisplay),
    ];

    public override void OnInitializeMelon()
    {
        var category = MelonPreferences.CreateCategory("BetterFreedomMode");
        Log.LoadPreferences(category);
        FreedomExitAnyButton.LoadPreferences(category);
        FreedomConfirmOnMainMonitor.LoadPreferences(category);
        FreedomReportAsNormalPlay.LoadPreferences(category);
        FreedomMapSystem.LoadPreferences(category);
        FreedomAreaSelect.LoadPreferences(category);
        FreedomTicketSelect.LoadPreferences(category);
        FreedomImmediateExit.LoadPreferences(category);
        FreedomExitLeds.LoadPreferences(category);
        FreedomLevelBonusCap.LoadPreferences(category);

        // AssemblyInfo declares [HarmonyDontPatchAll], so nothing is patched until here.
        //
        // One at a time, each in its own try. PatchAll throws when a target method is missing, and
        // a single throw out here would leave every later set unpatched — the mod half-applied,
        // with only a stack trace to say so. Isolated, an unfamiliar game build costs one feature
        // and names it, while the rest keep working.
        var failed = new List<string>();
        foreach (var patchSet in PatchSets)
        {
            try
            {
                HarmonyInstance.PatchAll(patchSet);
            }
            catch (Exception e)
            {
                failed.Add(patchSet.Name);
                Log.Error($"{patchSet.Name} could not be applied and is disabled: {e.Message}");
            }
        }

        if (failed.Count == 0)
        {
            MelonLogger.Msg($"v{Version} ready: {PatchSets.Length} patch sets applied.");
            return;
        }

        Log.Warn($"v{Version} started with {failed.Count} of {PatchSets.Length} patch sets " +
                 $"disabled: {string.Join(", ", failed.ToArray())}. The game build is probably " +
                 "not one this was written against.");
    }
}
