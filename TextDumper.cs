using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text;
using BepInEx;
using HarmonyLib;
using Reus2Archipelago.Game;
using UnityEngine;

namespace Reus2Archipelago;

/// <summary>
/// Writes the game's translation tables to BepInEx/plugins/reus2_texts_&lt;locale&gt;.tsv: one line
/// per translation key, with the English source the game infers the key from and the text the
/// active locale shows. The key column is what <see cref="TextOverrides"/> matches on, so an
/// override line is this file's first and third columns.
///
/// The tables are <c>MacroSettings.translationKeys</c> (English to key) and the per-locale
/// TextAsset in <c>MacroSettings.translationFiles</c>, both read by reflection.
/// </summary>
internal static class TextDumper
{
    /// <summary>The game separates a key from its text with this, and starts an entry on any line
    /// carrying one; a line without it continues the entry above.</summary>
    private const char Delimiter = '';

    private static bool done;

    internal static void DumpOnce()
    {
        if (done) return;
        done = true;

        var settingsType = GameAccess.TypeNamed("MacroSettings");
        if (settingsType == null)
        {
            Plugin.Log.LogError("MacroSettings not found — no texts dumped.");
            return;
        }

        var locale = AccessTools.Property(settingsType, "CurrentLocale")?.GetValue(null) as string ?? "en";
        var keyByEnglish = GameAccess.Pairs(AccessTools.Field(settingsType, "translationKeys")?.GetValue(null));
        var englishByKey = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var pair in keyByEnglish)
            if (pair.Value is string key && !englishByKey.ContainsKey(key))
                englishByKey[key] = pair.Key;

        var texts = LocaleTexts(settingsType, locale);
        var path = Path.Combine(Paths.PluginPath, $"reus2_texts_{locale}.tsv");

        var lines = new SortedDictionary<string, string>(StringComparer.Ordinal);
        foreach (var entry in texts)
            lines[entry.Key] = $"{TextEscaping.Escape(entry.Key)}\t{TextEscaping.Escape(englishByKey.TryGetValue(entry.Key, out var source) ? source : "")}\t{TextEscaping.Escape(entry.Value)}";

        // Keys the game can infer but the locale file has no line for: shown in English in game,
        // and still overridable.
        foreach (var pair in englishByKey)
            if (!lines.ContainsKey(pair.Key))
                lines[pair.Key] = $"{TextEscaping.Escape(pair.Key)}\t{TextEscaping.Escape(pair.Value)}\t";

        try
        {
            var output = new StringBuilder();
            output.AppendLine("# key\tEnglish source\ttext in this locale");
            output.AppendLine($"# Reus 2 {Application.version}, locale '{locale}', {lines.Count} keys.");
            output.AppendLine("# To replace a text, put its key or English source, a tab, and the new text");
            output.AppendLine("# into assets/reus2_text_overrides.tsv in the mod's repository.");
            foreach (var line in lines.Values)
                output.AppendLine(line);
            File.WriteAllText(path, output.ToString(), Encoding.UTF8);
            Plugin.Log.LogInfo($"Wrote {lines.Count} texts to {path}");
        }
        catch (Exception e)
        {
            Plugin.Log.LogError($"Writing {path} failed: {e}");
        }
    }

    /// <summary>The locale's key-to-text table, parsed from its TextAsset the way the game's own
    /// reader does.</summary>
    private static Dictionary<string, string> ParseTable(string content)
    {
        var entries = new Dictionary<string, string>(StringComparer.Ordinal);
        if (string.IsNullOrEmpty(content))
            return entries;

        var entry = string.Empty;
        foreach (var line in content.Split('\n'))
        {
            var text = line.TrimEnd('\r');
            if (text.IndexOf(Delimiter) >= 0)
            {
                Store(entry);
                entry = string.Empty;
            }
            else if (!string.IsNullOrEmpty(text.Trim()))
            {
                entry += "\n";
            }

            if (!string.IsNullOrWhiteSpace(text))
                entry += text;
        }

        Store(entry);
        return entries;

        void Store(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw))
                return;
            var parts = raw.Split(Delimiter);
            if (parts.Length >= 2 && !entries.ContainsKey(parts[0]))
                entries[parts[0]] = parts[1];
        }
    }

    private static Dictionary<string, string> LocaleTexts(Type settingsType, string locale)
    {
        var settings = AccessTools.Method(settingsType, "GetMacroSettings")?.Invoke(null, null);
        var files = GameAccess.Get(settings, "translationFiles");
        foreach (var pair in GameAccess.Pairs(files))
        {
            if (pair.Key != locale)
                continue;
            if (pair.Value is TextAsset asset)
                return ParseTable(asset.text);
        }

        Plugin.Log.LogWarning($"No translation file for locale '{locale}'; only inferred keys are dumped.");
        return new Dictionary<string, string>(StringComparer.Ordinal);
    }
}
