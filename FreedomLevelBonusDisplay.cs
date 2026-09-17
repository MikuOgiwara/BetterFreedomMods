using System.Collections.Generic;
using HarmonyLib;
using MAI2.Util;
using Manager;
using Manager.MaiStudio.CardTypeName;
using Manager.UserDatas;
using MelonLoader;
using Monitor.MapResult;
using Process;

namespace BetterFreedomMode;

/// <summary>
/// A second cap on the same number, in Phase3CommonMember.Initialize:
///
///     if (IsFreedomMode) {
///         num = (int)GetMaxTrackCount();
///         if (num >= 6) num = 6;
///         levelBounusIconDispInfo.point = (uint)num;
///     }
///
/// This one is display only — its num is local and feeds the three boxes on the level-up screen
/// (TRACK / PASS / TICKET), never a character level. FreedomLevelBonusCap handles the cap that
/// actually grants levels, over in MapResultMonitor. Left alone, the two disagree: the screen
/// announces TRACK 6 while seven levels are handed out.
///
/// Rather than transpile a second constant, the boxes are recomputed in a postfix. Initialize
/// receives card_type and ticket_rate, so the arithmetic can mirror the original exactly with the
/// cap raised — pass grade doubles it for a Gold Pass, then the ticket rate scales it with the
/// game's own round-up-from-a-tenth rule.
/// </summary>
public static class FreedomLevelBonusDisplay
{
    /// <summary>Character levels as they stood before the grant, for the delta log below.</summary>
    private static readonly Dictionary<int, uint> LevelsBefore = new();

    [HarmonyPostfix]
    [HarmonyPatch(typeof(Phase3CommonMember), nameof(Phase3CommonMember.Initialize))]
    public static void PostInitialize(Phase3CommonMember __instance, List<UserChara> info,
                                      Table card_type, float ticket_rate)
    {
        LevelsBefore.Clear();
        if (info != null)
        {
            foreach (var chara in info) LevelsBefore[chara.ID] = chara.Level;
        }

        if (!GameManager.IsFreedomMode) return;

        var cap = FreedomLevelBonusCap.EffectiveCap;
        if (cap == FreedomLevelBonusCap.StockCap) return;

        var boxes = __instance._dispInfo;
        if (boxes == null || boxes.Count < 3) return;

        var tracks = (int)GameManager.GetMaxTrackCount();
        if (tracks > cap) tracks = cap;

        boxes[0].point = boxes[0].total = (uint)tracks;

        var total = tracks * (card_type == Table.GoldPass ? 2 : 1);
        boxes[1].total = (uint)total;

        if (ticket_rate > 1f)
        {
            var scaled = total * ticket_rate;
            var whole = (int)scaled;
            total = whole + (scaled - whole >= 0.1f ? 1 : 0);
        }

        boxes[2].total = (uint)total;
        MelonLogger.Msg($"Level-up screen shows {tracks} tracks, {total} levels after bonuses.");
    }

    /// <summary>
    /// Ground truth on what was actually granted. Reading a level off a photo cannot tell six from
    /// seven; this prints the before and after for every character the credit touched.
    /// </summary>
    [HarmonyPostfix]
    [HarmonyPatch(typeof(MapResultProcess), nameof(MapResultProcess.OnRelease))]
    public static void PostMapResultRelease()
    {
        if (LevelsBefore.Count == 0) return;

        for (var monitor = 0; monitor < 2; monitor++)
        {
            var user = Singleton<UserDataManager>.Instance?.GetUserData(monitor);
            if (user is not { IsEntry: true } || user.CharaList == null) continue;

            foreach (var chara in user.CharaList)
            {
                if (!LevelsBefore.TryGetValue(chara.ID, out var before)) continue;
                if (chara.Level == before) continue;

                MelonLogger.Msg($"Character {chara.ID}: level {before} -> {chara.Level} " +
                                $"(+{chara.Level - before}).");
            }
        }

        LevelsBefore.Clear();
    }
}
