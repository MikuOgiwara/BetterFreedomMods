using MelonLoader;

namespace BetterFreedomMode;

public class Main : MelonMod
{
    public const string Version = "2.3.0";
    public const string Description = "Better Freedom Mode";
    public const string Author = "Mikuwu";

    public override void OnInitializeMelon()
    {
        var category = MelonPreferences.CreateCategory("BetterFreedomMode");
        FreedomExitAnyButton.LoadPreferences(category);
        FreedomConfirmOnMainMonitor.LoadPreferences(category);
        FreedomReportAsNormalPlay.LoadPreferences(category);
        FreedomMapSystem.LoadPreferences(category);
        FreedomAreaSelect.LoadPreferences(category);
        FreedomTicketSelect.LoadPreferences(category);
        FreedomImmediateExit.LoadPreferences(category);

        // AssemblyInfo declares [HarmonyDontPatchAll], so nothing is patched until here.
        HarmonyInstance.PatchAll(typeof(FreedomExitAnyButton));
        HarmonyInstance.PatchAll(typeof(FreedomConfirmOnMainMonitor));
        HarmonyInstance.PatchAll(typeof(FreedomImmediateExit));
        HarmonyInstance.PatchAll(typeof(FreedomReportAsNormalPlay));
        HarmonyInstance.PatchAll(typeof(FreedomMapSystem));
        HarmonyInstance.PatchAll(typeof(FreedomAreaSelect));
        HarmonyInstance.PatchAll(typeof(FreedomTicketSelect));
        MelonLogger.Msg("Freedom mode: any button confirms, confirmation is on the visible monitor, " +
                        "ending the credit skips the forced last song, credits are reported to the " +
                        "server as normal play, and area select, the map system and ticket select " +
                        "are back.");
    }
}
