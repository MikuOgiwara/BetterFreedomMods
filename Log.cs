using MelonLoader;

namespace BetterFreedomMode;

/// <summary>
/// Routine logging is off by default. It earned its place during development — nearly every fix in
/// this mod came from a log line rather than a guess — so it is kept behind a switch rather than
/// deleted: on a cabinet, commented-out logging helps nobody. Set VerboseLogging in
/// MelonPreferences.cfg to bring it back when something needs diagnosing.
///
/// Warnings and errors are never suppressed. They fire rarely and always mean a patch declined to
/// do its job, which is exactly what someone reporting a problem needs to see.
/// </summary>
internal static class Log
{
    private static MelonPreferences_Entry<bool> _verbose;

    internal static void LoadPreferences(MelonPreferences_Category category)
    {
        _verbose = category.CreateEntry(
            "VerboseLogging", false,
            description: "Log what each patch does, credit by credit. Off by default; turn it on " +
                         "before reporting a problem.");
    }

    /// <summary>Routine progress. Silent unless VerboseLogging is on.</summary>
    internal static void Info(string message)
    {
        if (_verbose is { Value: true }) MelonLogger.Msg(message);
    }

    /// <summary>A patch declining to act, or a setting being overridden. Always shown.</summary>
    internal static void Warn(string message) => MelonLogger.Warning(message);

    /// <summary>A patch that could not be applied at all. Always shown.</summary>
    internal static void Error(string message) => MelonLogger.Error(message);
}
