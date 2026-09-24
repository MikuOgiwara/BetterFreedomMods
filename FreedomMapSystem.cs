using System;
using HarmonyLib;
using Manager;
using MelonLoader;
using Process;

namespace BetterFreedomMode;

/// <summary>
/// GameManager.IsFreedomMapSkip() is hardcoded to true whenever Freedom Mode is on, and six places
/// read it. They want different answers, so the override is allowed only where it is wanted — a
/// whitelist. An earlier version did the opposite, overriding everywhere except one known caller,
/// and that put the map result screen back after every single track: ResultProcess line 1300 uses
/// the same call to skip MapResultProcess mid-credit, which stock Freedom Mode deliberately does.
///
///   NextTrackProcess.OnStart 210/214  map and character select after a mid-credit unlock -> want false
///   NotesListManager         96       IsMapBonus(), the boost markers on songs            -> want false
///   NextTrackProcess         342      pauses the Freedom clock between tracks             -> want true
///   ResultProcess            1300     skips the map result screen after each track        -> want true,
///                                     except on the track that ends the credit (see below)
///   UnlockProcess            117      same, on the unlock path                            -> want true
///
/// The method is not reliably inlined by Mono: a call counter showed zero calls through one credit
/// and one call through the next. So the override cannot be counted on to fire — but it definitely
/// can, which is exactly why scoping it correctly matters.
/// </summary>
public static class FreedomMapSystem
{
    /// <summary>Depth of a whitelisted caller; the override only applies above zero.</summary>
    private static int _allowedDepth;

    private static int _flips;

    private static MelonPreferences_Entry<bool> _enabled;

    public static void LoadPreferences(MelonPreferences_Category category)
    {
        _enabled = category.CreateEntry(
            "EnableMapSystemInFreedom", true,
            description: "In Freedom Mode, bring back the map/character select screens after a " +
                         "mid-credit unlock and the map-bonus markers on songs. The map result " +
                         "screen stays at the end of the credit only, as in the stock game.");
    }

    private static void Enter() => _allowedDepth++;

    private static void Exit() => _allowedDepth--;

    [HarmonyPostfix]
    [HarmonyPatch(typeof(GameManager), nameof(GameManager.IsFreedomMapSkip))]
    public static void PostIsFreedomMapSkip(ref bool __result)
    {
        if (_allowedDepth <= 0) return;
        if (_enabled is not { Value: true }) return;
        // false already means "not Freedom Mode"; nothing to re-enable.
        if (!__result) return;

        _flips++;
        __result = false;
    }

    [HarmonyPrefix]
    [HarmonyPatch(typeof(NextTrackProcess), "OnStart")]
    public static void PreNextTrackOnStart() => Enter();

    /// <summary>Finalizer, not postfix: the depth must unwind even if the method throws.</summary>
    [HarmonyFinalizer]
    [HarmonyPatch(typeof(NextTrackProcess), "OnStart")]
    public static Exception FinNextTrackOnStart(Exception __exception)
    {
        Exit();
        if (GameManager.IsFreedomMode && (GameManager.NextMapSelect || GameManager.NextCharaSelect))
        {
            Log.Info("Freedom mode unlock: routing to " +
                            $"{(GameManager.NextMapSelect ? "map" : "character")} select.");
        }

        return __exception;
    }

    /// <summary>
    /// ResultProcess.ToNextProcess line 1300 skips MapResultProcess on every track, so stock Freedom
    /// Mode never shows the map progress screen at all — not even at the end. Allow it through on
    /// the transition that ends the credit only, which is what IsFreedomTimeUp marks. __state
    /// carries the decision to the finalizer so the depth unwinds exactly as it was raised.
    /// </summary>
    [HarmonyPrefix]
    [HarmonyPatch(typeof(ResultProcess), "ToNextProcess")]
    public static void PreResultToNextProcess(out bool __state)
    {
        __state = GameManager.IsFreedomMode && GameManager.IsFreedomTimeUp;
        if (__state) Enter();
    }

    [HarmonyFinalizer]
    [HarmonyPatch(typeof(ResultProcess), "ToNextProcess")]
    public static Exception FinResultToNextProcess(bool __state, Exception __exception)
    {
        if (__state) Exit();
        return __exception;
    }

    [HarmonyPrefix]
    [HarmonyPatch(typeof(NotesListManager), "CreateNormalNotesList")]
    public static void PreCreateNormalNotesList() => Enter();

    [HarmonyFinalizer]
    [HarmonyPatch(typeof(NotesListManager), "CreateNormalNotesList")]
    public static Exception FinCreateNormalNotesList(Exception __exception)
    {
        Exit();
        return __exception;
    }

    /// <summary>Proves where the map result screen actually opens, and in what state.</summary>
    [HarmonyPostfix]
    [HarmonyPatch(typeof(MapResultProcess), "OnStart")]
    public static void PostMapResultOnStart()
    {
        Log.Info($"MapResultProcess opened (freedom={GameManager.IsFreedomMode}, " +
                        $"track={GameManager.MusicTrackNumber}, timeUp={GameManager.IsFreedomTimeUp}). " +
                        $"IsFreedomMapSkip overridden {_flips}x, only inside whitelisted callers.");
    }
}
