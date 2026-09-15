using System;
using HarmonyLib;
using Reus2Archipelago.Game;

namespace Reus2Archipelago.Patches;

internal static class Patching
{
    /// <summary>Patches a game method found by type and method name. A missing target is logged and
    /// skipped, so one moved method does not stop the others from loading.</summary>
    internal static void Apply(Harmony harmony, string typeName, string methodName, Type owner,
        string prefix = null, string postfix = null)
    {
        try
        {
            var type = GameAccess.TypeNamed(typeName);
            var method = type == null ? null : AccessTools.Method(type, methodName);
            if (method == null)
            {
                Report($"Patch target missing: {typeName}.{methodName}", error: true);
                return;
            }

            if (method.IsAbstract)
            {
                Report($"Patch target {typeName}.{methodName} is abstract and has no body to detour; "
                       + "each override is patched instead.", error: false);
                return;
            }

            if (!ProbeTally.Register(method, owner, $"{prefix}+{postfix}"))
            {
                Report($"{typeName}.{methodName} resolves to {ProbeTally.Key(method)}, "
                       + "which this patch is already on.", error: false);
                return;
            }

            harmony.Patch(method,
                prefix: prefix == null ? null : new HarmonyMethod(AccessTools.Method(owner, prefix)),
                postfix: postfix == null ? null : new HarmonyMethod(AccessTools.Method(owner, postfix)));
            Report($"Patched {method.DeclaringType?.FullName}.{methodName}", error: false);
        }
        catch (Exception e)
        {
            Report($"Patching {typeName}.{methodName} failed: {e}", error: true);
        }
    }

    private static void Report(string line, bool error)
    {
        if (error)
            Plugin.Log.LogError(line);
        else
            Plugin.Log.LogInfo(line);
        ProbeLog.Append(line);
    }
}
