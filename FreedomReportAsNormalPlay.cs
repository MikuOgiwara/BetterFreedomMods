using HarmonyLib;
using MelonLoader;
using Net.VO.Mai2;

namespace BetterFreedomMode;

/// <summary>
/// The reward systems people expect in Freedom Mode — map distance (KM), character awakening XP,
/// bonus tickets, event and mission progress — are not disabled in the client. The client is only
/// a display layer for them: UserData.UpdateTotalAwake() merely re-sums CharaList[].Awakening, and
/// the only "Distance +=" in the whole game is in PlInformationProcess, a debug tool. The real
/// values arrive from the server (GetUserMapApi and friends).
///
/// What the server keys off is the playlog, which declares the credit as Freedom:
///
///     result.playMode = 1;               // ExportUserGamePlaylog and ExportUserPlaylog
///     result.isFreedomMode = true;       // ExportUserPlaylog
///
/// So this reports Freedom credits as ordinary ones and lets the server award everything normally.
///
/// The trade-off, chosen deliberately by the operator of the server this talks to: playlogs no
/// longer record which credits were played in Freedom Mode, and that distinction cannot be
/// recovered afterwards. The alternative is to treat playMode == 1 as a normal credit in the
/// server's own reward rules, which keeps the playlog honest — prefer that if the server is ever
/// changed to care about the flag.
///
/// The credit cost (GameCostEnoughFreedom) is left truthful: it feeds bookkeeping, not rewards.
/// </summary>
public static class FreedomReportAsNormalPlay
{
    private static MelonPreferences_Entry<bool> _enabled;
    private static int _gamePlaylogRewrites;
    private static int _trackPlaylogRewrites;

    public static void LoadPreferences(MelonPreferences_Category category)
    {
        _enabled = category.CreateEntry(
            "ReportFreedomAsNormalPlay", true,
            description: "Upload Freedom Mode playlogs as ordinary credits (playMode 0, " +
                         "isFreedomMode false) so the server awards map distance, character XP, " +
                         "bonus tickets and event progress as usual. Turn off to keep playlogs " +
                         "truthful and handle playMode == 1 in the server's reward rules instead.");
    }

    [HarmonyPostfix]
    [HarmonyPatch(typeof(VOExtensions), nameof(VOExtensions.ExportUserGamePlaylog))]
    // UserGamePlaylog is a struct: without ref, the postfix would mutate a copy and do nothing.
    public static void PostExportUserGamePlaylog(ref UserGamePlaylog __result)
    {
        if (_enabled is not { Value: true }) return;
        if (__result.playMode != 1) return;

        __result.playMode = 0;
        _gamePlaylogRewrites++;
        Log.Info($"Credit playlog #{_gamePlaylogRewrites}: playMode 1 -> 0 " +
                        $"(playCredit={__result.playCredit}, useTicketId={__result.useTicketId}, " +
                        $"playTrack={__result.playTrack}).");
    }

    [HarmonyPostfix]
    [HarmonyPatch(typeof(VOExtensions), nameof(VOExtensions.ExportUserPlaylog))]
    // UserPlaylog is a struct: see above.
    public static void PostExportUserPlaylog(ref UserPlaylog __result)
    {
        if (_enabled is not { Value: true }) return;
        if (!__result.isFreedomMode && __result.playMode != 1) return;

        __result.isFreedomMode = false;
        if (__result.playMode == 1) __result.playMode = 0;
        _trackPlaylogRewrites++;
        Log.Info($"Track playlog #{_trackPlaylogRewrites}: isFreedomMode -> false, playMode -> 0.");
    }

    /// <summary>
    /// Counted separately so the log shows whether BOTH exports ran. The server may key its rewards
    /// off the per-credit playlog, the per-track ones, or both; if one of these counters stays at
    /// zero, that export never went through this code and the flag it carries was never rewritten.
    /// </summary>
    [HarmonyPostfix]
    [HarmonyPatch(typeof(Process.MusicSelectProcess), "OnStart")]
    public static void PostMusicSelectOnStart()
    {
        if (_gamePlaylogRewrites == 0 && _trackPlaylogRewrites == 0) return;
        Log.Info($"Previous credit rewrote {_gamePlaylogRewrites} credit playlog(s) and " +
                        $"{_trackPlaylogRewrites} track playlog(s).");
        _gamePlaylogRewrites = 0;
        _trackPlaylogRewrites = 0;
    }
}
