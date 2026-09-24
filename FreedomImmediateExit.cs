using HarmonyLib;
using MAI2.Util;
using Manager;
using MelonLoader;
using Process;
using Util;

namespace BetterFreedomMode;

/// <summary>
/// ForcedTerminationForFreedomMode only zeroes the clock. MusicSelectProcess then sees
/// GetFreedomModeMSec() &lt;= 0 and calls TimerCountUp(), which is the *selection timeout*
/// handler: it force-picks the highlighted song and ends in OnGameStart(). So ending a
/// Freedom credit always makes you play one more song first.
///
/// This skips that forced song and goes straight to NextTrackProcess, the stock
/// "Freedom Mode is over" screen: with IsFreedomTimeUp set it runs the TIME UP animation,
/// calls CheckAchieveCreditTotal() and routes to the correct ending process.
///
/// The ending chain assumes one invariant that entering from music select breaks:
/// MusicTrackNumber equals the number of *finished* tracks. Straight after a track that holds;
/// but the NextTrackProcess that followed it already incremented MusicTrackNumber for the
/// selection we are now leaving, so it is one ahead of the score log. Everything downstream
/// sizes itself off that number and reads past the end of the log:
///
///   - NextTrackProcess.CheckAchieveTrack() reads the score log at MusicTrackNumber - 1;
///     GetGameScore returns null and GetGhostScore dereferences it.
///   - PhotoEditProcess.OnStart() sizes its arrays from GetMaxTrackCount(), which in Freedom
///     Mode is MusicTrackNumber, then fills them from the shorter score log.
///
/// So restore the invariant at the point of divergence rather than patching each consumer:
/// MusicTrackNumber is rewound to the real finished-track count before the transition.
///
/// One thing still cannot be made to work with nothing played at all:
/// CheckAchieveCreditTotal() -> AchieveCreditData.Create calls .Min() over GetGameScores(),
/// which throws on an empty sequence. The credit totals are real, so that cannot be skipped —
/// with zero tracks played we decline the shortcut and let the stock forced song run.
/// </summary>
public static class FreedomImmediateExit
{
    /// <summary>True from the moment we divert until the next music select. Also gates the CheckAchieveTrack skip.</summary>
    private static bool _exiting;

    /// <summary>
    /// True from the divert until the credit really ends (GameManager.Clear). Separate from _exiting,
    /// which a new MusicSelectProcess clears: this one must outlive any bounce back to music select,
    /// because that is exactly where the credit would otherwise restart itself. See PreStartTimer.
    /// </summary>
    private static bool _creditIsEnding;

    private static MelonPreferences_Entry<bool> _showMapResult;

    public static void LoadPreferences(MelonPreferences_Category category)
    {
        _showMapResult = category.CreateEntry(
            "MapResultOnFreedomExit", true,
            description: "Show the map progress (km) screen once when a Freedom credit ends by " +
                         "Aime. Stock Freedom Mode never shows it: ResultProcess skips it on every " +
                         "track, the last one included.");
    }

    /// <summary>Each music select re-arms, so a second Freedom credit can exit too.</summary>
    [HarmonyPostfix]
    [HarmonyPatch(typeof(MusicSelectProcess), "OnStart")]
    public static void PostMusicSelectOnStart()
    {
        _exiting = false;
    }

    [HarmonyPrefix]
    [HarmonyPatch(typeof(MusicSelectProcess), "TimerCountUp")]
    public static bool PreTimerCountUp(MusicSelectProcess __instance)
    {
        if (!GameManager.IsFreedomMode) return true;
        if (GameManager.GetFreedomModeMSec() > 0) return true;
        if (_exiting) return false;

        // The credit totals cannot be built from an empty score log; see the class remarks.
        if (!Singleton<GamePlayManager>.Instance.IsPlayLog())
        {
            Log.Info("No track played yet; letting the stock forced song run so the credit can total up.");
            return true;
        }

        var container = Traverse.Create(__instance).Field("container").GetValue<ProcessDataContainer>();
        if (container?.processManager == null)
        {
            // Never leave the player stuck: fall back to the stock forced song.
            Log.Warn("Could not reach the process manager; letting the stock forced song run.");
            return true;
        }

        _exiting = true;
        GameManager.IsFreedomTimeUp = true;

        // NextTrackProcess.OnStart bounces to NextTrackMode.NeedAwake -- which does
        // AddProcess(new MusicSelectProcess(...)) -- whenever a player's character awakening has
        // neither been played nor been queued. In a stock Freedom credit the clock expires during a
        // track, so that track's MapResult sees IsFreedomTimeUp and plays the awakening. Exiting by
        // Aime happens after the last MapResult has already run with the flag still false, so the
        // awakening never happened and the game sends us back for another track. Mark it handled.
        var mapMaster = Singleton<MapMaster>.Instance;
        if (mapMaster?.IsCallAwake != null && mapMaster.IsNeedAwake != null)
        {
            for (var i = 0; i < mapMaster.IsCallAwake.Length && i < 2; i++)
            {
                var user = Singleton<UserDataManager>.Instance.GetUserData(i);
                if (!user.IsEntry || user.IsGuest()) continue;
                if (mapMaster.IsCallAwake[i] || mapMaster.IsNeedAwake[i]) continue;

                mapMaster.IsCallAwake[i] = true;
                Log.Info($"Marking player {i}'s character awakening as handled so the ending " +
                                "does not bounce back to music select for another track.");
            }
        }

        _creditIsEnding = true;

        // Restore the invariant the whole ending chain sizes itself from.
        var finishedTracks = (uint)Singleton<GamePlayManager>.Instance.GetScoreListCount();
        if (GameManager.MusicTrackNumber != finishedTracks)
        {
            Log.Info($"Rewinding MusicTrackNumber {GameManager.MusicTrackNumber} -> {finishedTracks} " +
                            "to match the finished-track count.");
            GameManager.MusicTrackNumber = finishedTracks;
        }

        // MusicSelectProcess.GameStart() normally does this on the way out.
        SoundManager.PreviewEnd();
        SoundManager.StopBGM(2);
        container.processManager.ClearTimeoutAction();

        // MusicSelectMonitor turned the sub-monitor character strip on (message 20020) when the
        // selection opened, and the stock flow turns it back off on the way to the ending: first in
        // GameProcess when a song starts, then in ResultProcess. We skip both, so without this the
        // strip stays up through the whole ending chain and renders as empty level boxes.
        for (var monitor = 0; monitor < 2; monitor++)
        {
            container.processManager.SendMessage(
                new Message(ProcessType.CommonProcess, 20020, monitor, false));
        }

        // Stock Freedom never reaches MapResultProcess -- ResultProcess skips it on every track,
        // and on an Aime exit IsFreedomTimeUp is still false while that runs -- so the km screen has
        // to be put into the chain here. MapResultProcess adds NextTrackProcess itself afterwards,
        // so the ending continues exactly as before.
        if (_showMapResult is { Value: true })
        {
            container.processManager.AddProcess(
                new FadeProcess(container, __instance, new MapResultProcess(container)), 50);
            Log.Info("Freedom mode over at music select: skipping the forced last song, " +
                            "via the map result screen.");
            return false;
        }

        // Same shape as the nine stock call sites; the FadeProcess base releases __instance for us.
        container.processManager.AddProcess(new NextTrackProcess(container, __instance), 50);
        Log.Info("Freedom mode over at music select: skipping the forced last song.");
        return false;
    }

    /// <summary>
    /// Second half of the trap. MusicSelectProcess restarts the whole Freedom credit with
    ///
    ///     if (!IsFreedomCountDown &amp;&amp; MusicTrackNumber == 1) StartFreedomModeTimer(...)
    ///
    /// and StartFreedomModeTimer clears IsFreedomTimeUp. Both conditions hold after we exit: the
    /// clock reaching zero cleared IsFreedomCountDown, and the rewind above put MusicTrackNumber
    /// back to 1 when only one track was played. Anything that returns to music select would then
    /// silently start a fresh credit and the player could never leave. Refuse until the credit is
    /// genuinely over.
    /// </summary>
    [HarmonyPrefix]
    [HarmonyPatch(typeof(GameManager), nameof(GameManager.StartFreedomModeTimer))]
    public static bool PreStartFreedomModeTimer()
    {
        if (!_creditIsEnding) return true;

        Log.Warn("Refusing to restart the Freedom clock: this credit is already ending.");
        return false;
    }

    /// <summary>The real credit boundary; Initialize -> Clear runs from AdvertiseProcess and EntryProcess.</summary>
    [HarmonyPostfix]
    [HarmonyPatch(typeof(GameManager), nameof(GameManager.Clear))]
    public static void PostGameManagerClear()
    {
        _creditIsEnding = false;
        _exiting = false;
    }

    /// <summary>
    /// With MusicTrackNumber rewound this would no longer crash, but it would re-score a track
    /// its own NextTrackProcess already evaluated. Nothing finished on this transition, so skip it.
    /// Only on the exit we ourselves diverted; the stock flow is untouched.
    /// </summary>
    [HarmonyPrefix]
    [HarmonyPatch(typeof(NextTrackProcess), "CheckAchieveTrack")]
    public static bool PreCheckAchieveTrack()
    {
        if (!_exiting) return true;
        Log.Info("Skipping per-track achievement check: no track finished on this transition.");
        return false;
    }
}
