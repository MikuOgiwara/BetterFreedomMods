using System;
using HarmonyLib;
using Manager;
using MelonLoader;
using Process;
using Process.LoginBonus;

namespace BetterFreedomMode;

/// <summary>
/// At the start of a credit the stock game shows AREA SELECT (RegionalSelectProcess), where you
/// pick your next area and see the km left to the next reward. Freedom Mode is routed past it:
///
///     SimpleSettingProcess -> LoginBonusProcess -> line 204:
///         (IsCourseMode || IsFreedomMode) ? new GetPresentProcess(...) : new RegionalSelectProcess(...)
///
/// CircleFinalResultProcess line 283 carries the same ternary on the other entry path.
///
/// The test sits mid-ternary, so there is nothing to prefix or postfix. Patching the IsFreedomMode
/// *getter* does not work either: Mono inlines these two-line accessors into their callers, and an
/// inlined caller never reaches the Harmony detour. That was measured, not assumed — a call counter
/// on GameManager.IsFreedomMapSkip stayed at zero through a credit that provably calls it.
///
/// So instead of intercepting the read, this changes what there is to read: IsFreedomMode has a
/// public setter, and an inlined getter compiles down to a read of the same backing field. The flag
/// is set false for the duration of the routing method and restored afterwards.
///
/// Restoring matters more than anything else here — leaving the flag false would disable every
/// other part of this mod and confuse the game itself — so the restore runs from a Harmony
/// finalizer, which executes even if the patched method throws.
///
/// Each of the two files reads IsFreedomMode exactly once (verified with grep -c), so nothing else
/// in them changes behaviour. Code they call into is not audited, hence the read counter below.
///
/// Presents are not lost: GetPresentProcess already runs earlier in the chain, from
/// UnlockMusicProcess. Swapping this one for AREA SELECT reproduces the normal-mode flow.
///
/// Not covered: CodeReadProcess line 931 carries the same ternary inside TimeUpCoroutine(), whose
/// body runs in a compiler-generated MoveNext rather than in the method Harmony would patch. It is
/// only reachable when IsGotoCodeRead is set.
/// </summary>
public static class FreedomAreaSelect
{
    /// <summary>Proves the OnUpdate patches actually applied; logged once, not per frame.</summary>
    private static bool _loggedFirstWindow;

    private static MelonPreferences_Entry<bool> _enabled;

    public static void LoadPreferences(MelonPreferences_Category category)
    {
        _enabled = category.CreateEntry(
            "AreaSelectInFreedom", true,
            description: "Show the AREA SELECT screen in Freedom Mode, as the game does for a " +
                         "normal credit, instead of routing straight past it.");
    }

    private static void Enter()
    {
        var on = _enabled is { Value: true };
        FreedomFlagScope.Enter(on);

        if (!on || _loggedFirstWindow || !FreedomFlagScope.IsHeld) return;
        _loggedFirstWindow = true;
        Log.Info("Routing window is live: IsFreedomMode reads false while the credit-start " +
                        "screens choose the next process.");
    }

    private static void Exit() => FreedomFlagScope.Exit();

    [HarmonyPrefix]
    [HarmonyPatch(typeof(global::CircleFinalResultProcess), "OnUpdate")]
    public static void PreCircleFinalResultOnUpdate() => Enter();

    /// <summary>Finalizer, not postfix: the flag must be restored even if OnUpdate throws.</summary>
    [HarmonyFinalizer]
    [HarmonyPatch(typeof(global::CircleFinalResultProcess), "OnUpdate")]
    public static Exception FinCircleFinalResultOnUpdate(Exception __exception)
    {
        Exit();
        return __exception;
    }

    [HarmonyPrefix]
    [HarmonyPatch(typeof(LoginBonusProcess), "OnUpdate")]
    public static void PreLoginBonusOnUpdate() => Enter();

    [HarmonyFinalizer]
    [HarmonyPatch(typeof(LoginBonusProcess), "OnUpdate")]
    public static Exception FinLoginBonusOnUpdate(Exception __exception)
    {
        Exit();
        return __exception;
    }

    /// <summary>Did the screen we are after actually open?</summary>
    [HarmonyPostfix]
    [HarmonyPatch(typeof(RegionalSelectProcess), "OnStart")]
    public static void PostRegionalSelectOnStart()
    {
        Log.Info("AREA SELECT opened.");
    }
}
