using System;
using System.Collections;
using System.Collections.Generic;
using HarmonyLib;

namespace Reus2Archipelago.Game;

/// <summary>
/// Diagnostics.EnableTextProbe: for each text the game passes to MacroSettings.L that an override line
/// names, logs once the key the game infers for it, what the translation table holds under that key
/// and under the text itself, and what L returned. That shows where an override that does not appear
/// on screen went.
/// </summary>
internal static class TextProbe
{
    internal static bool Enabled;

    private static readonly HashSet<string> Logged = new HashSet<string>(StringComparer.Ordinal);

    internal static void AfterL(string original, string __result)
    {
        if (!Enabled || original == null || !TextOverrides.Sources.Contains(original) || !Logged.Add(original))
            return;

        try
        {
            var settings = GameAccess.TypeNamed("MacroSettings");
            var inferred = AccessTools.Method(settings, "InferKey")?.Invoke(null, new object[] { original }) as string;
            var current = AccessTools.Field(settings, "currentTranslation")?.GetValue(null);
            var table = GameAccess.Get(current, "keyToTranslation") as IDictionary;

            Plugin.Log.LogInfo($"[text probe] L(\"{Show(original)}\")"
                               + $" | inferred key: {(inferred == null ? "none" : $"\"{Show(inferred)}\"")}"
                               + $" | table[inferred]: {Lookup(table, inferred)}"
                               + $" | table[text]: {Lookup(table, original)}"
                               + $" | returned: \"{Show(__result)}\"");
        }
        catch (Exception e)
        {
            Plugin.Log.LogWarning($"[text probe] failed for \"{Show(original)}\": {e.Message}");
        }
    }

    private static string Lookup(IDictionary table, string key)
    {
        if (table == null)
            return "no table";
        if (key == null)
            return "-";
        return table.Contains(key) ? $"\"{Show(table[key] as string)}\"" : "missing";
    }

    private static string Show(string text) => TextEscaping.Escape(TextOverrides.Shorten(text ?? ""));
}
