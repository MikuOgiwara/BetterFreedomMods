using Manager;

namespace BetterFreedomMode;

/// <summary>
/// Several things Freedom Mode skips are decided by a GameManager.IsFreedomMode test sitting in the
/// middle of an expression, where Harmony has nothing to prefix or postfix. Patching the getter does
/// not work: Mono inlines these two-line accessors into their callers, and an inlined caller never
/// reaches the detour. That was measured — a call counter on GameManager.IsFreedomMapSkip stayed at
/// zero through a credit that provably calls it.
///
/// What does work is changing the value there is to read. IsFreedomMode has a public setter, and an
/// inlined getter compiles to a read of the same backing field, so clearing the flag for the
/// duration of one method is seen by every caller however it was compiled.
///
/// One shared depth counter, deliberately: each feature keeping its own would let two overlapping
/// windows each believe they own the flag, and the inner one's restore would re-enable it while the
/// outer was still running. Everything here runs on the Unity main thread, so a plain counter is
/// enough.
///
/// Callers must restore from a Harmony finalizer rather than a postfix: a postfix is skipped when
/// the patched method throws, and leaving this flag false would silently disable Freedom Mode.
/// </summary>
internal static class FreedomFlagScope
{
    private static int _depth;

    /// <summary>What IsFreedomMode was when the outermost window opened.</summary>
    private static bool _saved;

    /// <summary>True while the flag is being held false, for callers that want to log it.</summary>
    internal static bool IsHeld => _depth > 0 && _saved;

    internal static void Enter(bool enabled)
    {
        if (_depth++ > 0) return;

        _saved = GameManager.IsFreedomMode;
        if (_saved && enabled) GameManager.IsFreedomMode = false;
    }

    internal static void Exit()
    {
        if (--_depth > 0) return;
        if (!_saved) return;

        GameManager.IsFreedomMode = true;
        _saved = false;
    }
}
