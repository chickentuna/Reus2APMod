using System;
using System.Linq;
using System.Reflection;
using HarmonyLib;

namespace Reus2Archipelago.Game;

/// <summary>
/// Answers, in one line each, whether Harmony detours run at all in this process, which 0Harmony is
/// loaded, and whether the assembly the patches are applied to is the one the running game executes.
/// </summary>
internal static class HarmonySelfTest
{
    private static bool postfixRan;

    internal static void Run(Harmony harmony)
    {
        var runtime = typeof(Harmony).Assembly;
        Plugin.Log.LogInfo($"Harmony {runtime.GetName().Version} from {runtime.Location}");

        try
        {
            harmony.Patch(AccessTools.Method(typeof(HarmonySelfTest), nameof(Target)),
                postfix: new HarmonyMethod(AccessTools.Method(typeof(HarmonySelfTest), nameof(AfterTarget))));
            Target();
            Plugin.Log.LogInfo(postfixRan
                ? "Harmony self-test: a detour on the plugin's own method ran."
                : "Harmony self-test: a detour on the plugin's own method did not run.");
        }
        catch (Exception e)
        {
            Plugin.Log.LogError($"Harmony self-test failed: {e}");
        }

        var folder = System.IO.Path.GetDirectoryName(typeof(HarmonySelfTest).Assembly.Location);
        var beside = System.IO.Directory.GetFiles(folder, "*.dll").Select(System.IO.Path.GetFileName).ToList();
        Plugin.Log.LogInfo($"Beside the plugin: {string.Join(", ", beside)}");
        var clashes = beside.Where(f => f.StartsWith("0Harmony") || f.StartsWith("BepInEx")
                                        || f.StartsWith("Mono.Cecil") || f.StartsWith("MonoMod")
                                        || f.StartsWith("UnityEngine") || f.StartsWith("Assembly-CSharp")).ToList();
        if (clashes.Any())
            Plugin.Log.LogError($"These belong to the host, and a second copy here takes the detours "
                                + $"away from the running game: {string.Join(", ", clashes)}");

        var loaded = AppDomain.CurrentDomain.GetAssemblies()
            .Where(a => a.GetName().Name == "Assembly-CSharp").ToList();
        Plugin.Log.LogInfo($"Assembly-CSharp loaded {loaded.Count} time(s): "
            + string.Join(" | ", loaded.Select(a => $"{a.Location} mvid={a.ManifestModule.ModuleVersionId}")));
    }

    /// <summary>Compares the assembly a live game object came from with the one the patches went to.
    /// Called once the game is running, since it needs an object the game itself made.</summary>
    internal static void CompareAssemblies()
    {
        var profile = GameAccess.CurrentProfile;
        if (profile == null)
            return;
        var live = profile.GetType().Assembly;
        var patched = GameAccess.TypeNamed("GameplayController")?.Assembly;
        Plugin.Log.LogInfo($"Live profile assembly {(ReferenceEquals(live, patched) ? "is" : "IS NOT")} "
            + $"the patched one: live={live.Location} mvid={live.ManifestModule.ModuleVersionId}, "
            + $"patched={patched?.Location} mvid={patched?.ManifestModule.ModuleVersionId}");

        var method = AccessTools.Method(GameAccess.TypeNamed("GameplayController"), "OnBioticumPlaced");
        var info = method == null ? null : Harmony.GetPatchInfo(method);
        Plugin.Log.LogInfo(info == null
            ? "GameplayController.OnBioticumPlaced carries no patch info."
            : $"GameplayController.OnBioticumPlaced has {info.Postfixes.Count} postfix(es) from "
              + $"{string.Join(", ", info.Owners)}; Harmony knows {Harmony.GetAllPatchedMethods().Count()} patched methods.");
    }

    private static void Target()
    {
    }

    private static void AfterTarget() => postfixRan = true;
}
