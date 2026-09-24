using System.Collections.Generic;
using System.Linq;
using System.Reflection.Emit;
using HarmonyLib;
using MelonLoader;
using Monitor;

namespace BetterFreedomMode;

/// <summary>
/// Character levels are granted by the client, not the server: MapResultMonitor.ViewUpdate works out
/// how many levels the credit is worth and writes them into UserData.CharaList, which then goes up
/// in the UserAll upload. In Freedom Mode that amount is capped:
///
///     if (IsFreedomMode) {
///         if (IsCallAwake[_monitorID]) {
///             num5 = (int)GameManager.GetMaxTrackCount();
///             if (num5 &gt;= 6) num5 = 6;              // this
///             flag3 = true;
///         }
///     } else if (IsFinalTrack(MusicTrackNumber)) {
///         num5 = (int)(GetMaxTrackCount() + PlayedLongMusicNum);   // no cap
///     }
///
/// A stock Freedom credit runs ten minutes, so four to six tracks, and the cap almost never binds.
/// It only bites when an infinite-timer mod lets the session run long — past six tracks the levels
/// stop coming. Raising it is a balance decision, not a bug fix: a normal credit grants three or
/// four levels, so an uncapped thirty-track session grants thirty.
///
/// Nothing downstream breaks from a larger number. The level is clamped to _levelMax (999999) both
/// before and after the addition, CalcLevelToAwake recomputes Awakening from scratch by counting
/// every threshold at or below the new level — so crossing several at once still lands correctly —
/// and ClacReincarnationLevel handles the 10000-level rollover.
///
/// The replacement is itself capped at 99. Past that the game misbehaves — found by testing, not
/// from the source — so a larger configured value is clamped rather than honoured. 99 levels in one
/// credit is far beyond any real session anyway.
///
/// The cap sits mid-method in a ViewUpdate of some four thousand lines, so there is no prefix or
/// postfix to hang this on; it needs a transpiler. The difficulty is telling this constant apart
/// from its neighbours: "call GetMaxTrackCount followed by a 6" also describes the
/// `GetMaxTrackCount() >= 6` tests at two other points in the same method. What is unique to the
/// clamp is its shape — two Ldc_I4_6 either side of a single conditional branch, the compare and
/// the assignment. A plain comparison has only one. The patch refuses to touch anything unless that
/// shape appears exactly once.
/// </summary>
public static class FreedomLevelBonusCap
{
    /// <summary>
    /// Known limitation, established by testing on a cabinet: above 99 the game does not cope with
    /// the level gain. Configured values are clamped to this rather than passed through.
    /// </summary>
    internal const int HardMax = 99;

    internal const int StockCap = 6;

    /// <summary>The cap actually in force, after clamping. Shared with the display fix.</summary>
    internal static int EffectiveCap
    {
        get
        {
            var configured = _cap?.Value ?? StockCap;
            return configured < 1 ? 1 : configured > HardMax ? HardMax : configured;
        }
    }

    private static MelonPreferences_Entry<int> _cap;

    public static void LoadPreferences(MelonPreferences_Category category)
    {
        _cap = category.CreateEntry(
            "FreedomLevelBonusCap", HardMax,
            description: "Most character levels a single Freedom Mode credit may grant. The stock " +
                         "value is 6, which only matters past six tracks in a session. Do not go " +
                         "above 99: the game misbehaves past that, so higher values are clamped. " +
                         "Read once at startup.");
    }

    [HarmonyTranspiler]
    [HarmonyPatch(typeof(MapResultMonitor), nameof(MapResultMonitor.ViewUpdate))]
    public static IEnumerable<CodeInstruction> RaiseTheCap(IEnumerable<CodeInstruction> instructions)
    {
        var codes = instructions.ToList();
        var configured = _cap?.Value ?? StockCap;
        var cap = EffectiveCap;

        if (cap != configured)
        {
            Log.Warn($"Freedom level bonus cap of {configured} is out of range; using " +
                                $"{cap}. Above {HardMax} the game does not cope with the gain.");
        }

        if (cap == StockCap)
        {
            Log.Info($"Freedom level bonus left at the stock cap of {StockCap}.");
            return codes;
        }

        var matches = new List<int>();
        for (var i = 0; i + 2 < codes.Count; i++)
        {
            if (codes[i].opcode != OpCodes.Ldc_I4_6) continue;
            if (codes[i + 2].opcode != OpCodes.Ldc_I4_6) continue;
            if (codes[i + 1].opcode.FlowControl != FlowControl.Cond_Branch) continue;

            matches.Add(i);
        }

        if (matches.Count != 1)
        {
            Log.Error($"Freedom level bonus cap not raised: expected exactly one " +
                              $"compare-and-clamp on 6 in ViewUpdate, found {matches.Count}. " +
                              "The game build probably differs from the one this was written for; " +
                              "leaving the method untouched.");
            return codes;
        }

        // Rewritten in place rather than replaced, so any labels or exception blocks these
        // instructions carry stay attached to them.
        foreach (var index in new[] { matches[0], matches[0] + 2 })
        {
            codes[index].opcode = OpCodes.Ldc_I4;
            codes[index].operand = cap;
        }

        Log.Info($"Freedom level bonus cap changed from {StockCap} to {cap}.");
        return codes;
    }
}
