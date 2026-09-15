using System;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using HarmonyLib;
using HarmonyLib.Tools;

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
        HarmonyFileLog.FileWriterPath = System.IO.Path.Combine(BepInEx.Paths.PluginPath, "reus2_harmony.log");
        Logger.ChannelFilter = Logger.LogChannel.All;

        var runtime = typeof(Harmony).Assembly;
        Plugin.Log.LogInfo($"Harmony {runtime.GetName().Version} from {runtime.Location}");

        try
        {
            harmony.Patch(AccessTools.Method(typeof(HarmonySelfTest), nameof(Target)),
                postfix: new HarmonyMethod(AccessTools.Method(typeof(HarmonySelfTest), nameof(AfterTarget))));
            CallTarget();
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
    /// <summary>Applies the attribute-class patch and turns HarmonyX's own tracing on for two
    /// targets, so the log carries the replacement IL and the detour steps for them.</summary>
    internal static void SecondOpinion(Harmony harmony)
    {
        try
        {
            harmony.CreateClassProcessor(typeof(AttributeCheck)).Patch();
            Plugin.Log.LogInfo($"Attribute patch applied to {AttributeCheck.TargetMethod()?.FullDescription() ?? "nothing"}.");
        }
        catch (Exception e)
        {
            Plugin.Log.LogError($"Attribute patch failed: {e}");
        }
    }

    /// <summary>Calls two patched game methods from the plugin. A count that rises here while the
    /// game's own calls never arrive means the game runs a different method than the patched one.</summary>
    internal static void SelfInvoke()
    {
        var session = GameAccess.Get(GameAccess.SessionContext, "CurrentSession");
        var feats = GameAccess.Get(session, "eraFeatController");
        if (feats == null)
            return;
        var before = ProbeTally.CountFor("EraFeatController.GainsDraftForRequest");
        GameAccess.Call(feats, "GainsDraftForRequest", 0);
        var after = ProbeTally.CountFor("EraFeatController.GainsDraftForRequest");
        Plugin.Log.LogInfo(after > before
            ? "Self-invoke: calling a patched game method reached the probe."
            : "Self-invoke: calling a patched game method did not reach the probe.");
    }

    internal static void CompareAssemblies()
    {
        var gameplay = GameAccess.Gameplay;
        if (gameplay != null)
        {
            var liveType = gameplay.GetType();
            var namedType = GameAccess.TypeNamed("GameplayController");
            Plugin.Log.LogInfo($"Live GameplayController type {(ReferenceEquals(liveType, namedType) ? "is" : "IS NOT")} "
                + $"the patched one: live={liveType.AssemblyQualifiedName}, patched={namedType?.AssemblyQualifiedName}");
        }

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

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void CallTarget() => Target();

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void Target() => sideEffect++;

    private static int sideEffect;

    private static void AfterTarget() => postfixRan = true;
}
