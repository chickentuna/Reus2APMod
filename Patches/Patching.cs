using System;
using HarmonyLib;

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
            var type = AccessTools.TypeByName(typeName);
            var method = type == null ? null : AccessTools.Method(type, methodName);
            if (method == null)
            {
                Plugin.Log.LogError($"Patch target missing: {typeName}.{methodName}");
                return;
            }

            harmony.Patch(method,
                prefix: prefix == null ? null : new HarmonyMethod(AccessTools.Method(owner, prefix)),
                postfix: postfix == null ? null : new HarmonyMethod(AccessTools.Method(owner, postfix)));
        }
        catch (Exception e)
        {
            Plugin.Log.LogError($"Patching {typeName}.{methodName} failed: {e}");
        }
    }
}
