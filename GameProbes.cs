using System;
using System.IO;
using System.Reflection;
using HarmonyLib;

namespace Reus2Archipelago;

/// <summary>
/// Observe-only Harmony patches on every game event that is a candidate check or item grant.
/// Nothing here changes progression: each probe writes a line naming the definition involved and,
/// for checks, the AP location name that would be sent.
///
/// The point is the trace. A single played session answers the questions the decompiled source
/// leaves ambiguous — whether era feats are re-earned on every run, which milestones fire in
/// ordinary play, and what the vanilla unlock stream looks like — before any of it is wired up
/// for real.
///
/// Targets are resolved by name so the plugin still needs no compile-time reference to
/// Assembly-CSharp, and a target that has moved in a patch disables its own probe rather than
/// breaking the plugin.
/// </summary>
internal static class GameProbes
{
    private const BindingFlags Members =
        BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;

    private static string logPath;

    internal static void Install(Harmony harmony)
    {
        logPath = Path.Combine(BepInEx.Paths.PluginPath, "reus2_probe.log");
        Write($"--- probes installed {DateTime.Now:yyyy-MM-dd HH:mm:ss} ---");

        Patch(harmony, "Milestone", "AchieveMilestone",
            prefix: nameof(BeforeMilestone), postfix: nameof(AfterMilestone));
        Patch(harmony, "EraFeatController", "OnEraFeatAchieved", postfix: nameof(AfterEraFeat));
        Patch(harmony, "EraFeatController", "OnWorldStageControllerActivated", postfix: nameof(AfterSessionLoaded));
        Patch(harmony, "EraFeatController", "StartTrackingMainFeat", postfix: nameof(AfterMainFeatStarted));
        Patch(harmony, "MetaGameProfile.UserProfile", "UnlockBioticum", postfix: nameof(AfterUnlockBioticum));
        Patch(harmony, "MetaGameProfile.UserProfile", "UnlockTurningPoint", postfix: nameof(AfterUnlockTurningPoint));
        Patch(harmony, "MetaGameProfile.UserProfile", "LevelUpProfile", postfix: nameof(AfterLevelUp));
    }

    private static void Patch(Harmony harmony, string typeName, string methodName,
        string prefix = null, string postfix = null)
    {
        try
        {
            var type = AccessTools.TypeByName(typeName);
            var method = type == null ? null : AccessTools.Method(type, methodName);
            if (method == null)
            {
                Plugin.Log.LogWarning($"Probe target missing: {typeName}.{methodName}");
                Write($"probe target missing: {typeName}.{methodName}");
                return;
            }

            harmony.Patch(method,
                prefix: prefix == null ? null : new HarmonyMethod(AccessTools.Method(typeof(GameProbes), prefix)),
                postfix: postfix == null ? null : new HarmonyMethod(AccessTools.Method(typeof(GameProbes), postfix)));
            Write($"probing {typeName}.{methodName}");
        }
        catch (Exception e)
        {
            Plugin.Log.LogWarning($"Probe on {typeName}.{methodName} failed: {e.Message}");
            Write($"probe failed: {typeName}.{methodName}: {e.Message}");
        }
    }

    // Milestones are the persistent, achievement-backed checks. The prefix records whether this
    // one had already been achieved, which is what separates a real check from a replayed one.
    private static void BeforeMilestone(object __instance, out bool __state)
    {
        __state = Field<bool>(__instance, "achieved");
    }

    private static void AfterMilestone(object __instance, bool __state)
    {
        var definition = Field<object>(__instance, "definition");
        var fresh = !__state;
        Write($"MILESTONE {(fresh ? "achieved" : "re-triggered")}: {Describe(definition)}"
              + $" | category={Field<object>(definition, "category")}"
              + $" | would send check: \"Milestone: {NameOf(definition)}\"");
    }

    private static void AfterEraFeat(object[] __args)
    {
        var feat = __args != null && __args.Length > 0 ? __args[0] : null;
        var definition = Field<object>(feat, "definition");
        Write($"ERA FEAT achieved: {Describe(definition)}"
              + $" | hasEverBeenAchieved={Field<bool>(feat, "hasEverBeenAchieved")}"
              + $" | difficulty={Field<object>(definition, "difficulty")}"
              + $" | would send check: \"Era Feat: {NameOf(definition)}\"");
    }

    // A planet was started or loaded. Fires once per session, so repeats across runs read clearly.
    private static void AfterSessionLoaded()
    {
        Write("=== planet session loaded ===");
    }

    // A new era began and its main feat is now tracked.
    private static void AfterMainFeatStarted(object __instance)
    {
        var mainFeat = Field<object>(__instance, "mainEraFeat");
        Write($"--- era started, main feat: {Describe(Field<object>(mainFeat, "definition"))} ---");
    }

    private static void AfterUnlockBioticum(object[] __args, bool __result)
    {
        if (!__result) return;
        var bioticum = __args != null && __args.Length > 0 ? __args[0] : null;
        Write($"UNLOCK bioticum: {Describe(bioticum)}");
    }

    private static void AfterUnlockTurningPoint(object[] __args)
    {
        var turningPoint = __args != null && __args.Length > 0 ? __args[0] : null;
        Write($"UNLOCK turning point: {Describe(turningPoint)}");
    }

    private static void AfterLevelUp(object __instance)
    {
        Write($"PROFILE level up -> {Field<object>(__instance, "profileLevel")}"
              + $" | xpBudget={Field<object>(__instance, "remainingXPBudget")}"
              + $" | apexPoints={Field<object>(__instance, "apexUnlockPoints")}");
    }

    private static string Describe(object definition)
    {
        if (definition == null) return "<null>";
        var id = definition.GetType().GetProperty("ID", Members)?.GetValue(definition) as string;
        return $"{NameOf(definition)} [{id}]";
    }

    private static string NameOf(object definition)
        => definition is UnityEngine.Object o ? o.name : definition?.ToString() ?? "<null>";

    private static T Field<T>(object target, string name)
    {
        if (target == null) return default;
        try
        {
            var value = AccessTools.Field(target.GetType(), name)?.GetValue(target);
            return value is T typed ? typed : default;
        }
        catch
        {
            return default;
        }
    }

    /// <summary>Straight-through write: the BepInEx log buffers, and a probe trace is worthless if
    /// closing the game loses its tail.</summary>
    private static void Write(string line)
    {
        Plugin.Log.LogInfo($"[probe] {line}");
        try
        {
            File.AppendAllText(logPath, $"{DateTime.Now:HH:mm:ss.fff}  {line}\n");
        }
        catch
        {
            // The BepInEx log still has the line.
        }
    }
}
