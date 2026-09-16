using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text;
using BepInEx;
using HarmonyLib;

namespace Reus2Archipelago.Game;

/// <summary>
/// Replacement strings for the game's translation table, read from reus2_text_overrides.tsv and,
/// for one locale only, reus2_text_overrides.&lt;locale&gt;.tsv, both shipped next to the mod's DLL
/// from the repository's assets folder. Each line is a tab between what to replace and what to
/// show instead; the left side is either column of the dump, a translation key or the English
/// source it is inferred from.
/// </summary>
internal static class TextOverrides
{
    private const string BaseName = "reus2_text_overrides";

    /// <summary>Lines whose left side was not a key: they are English sources, which can only be
    /// resolved once MacroSettings has read the key table.</summary>
    private static readonly Dictionary<string, string> Unresolved = new Dictionary<string, string>(StringComparer.Ordinal);

    /// <summary>The mod's own folder: the override files ship next to the DLL.</summary>
    private static string Folder => Path.GetDirectoryName(typeof(Plugin).Assembly.Location) ?? Paths.PluginPath;

    internal static string CommonPath => Path.Combine(Folder, BaseName + ".tsv");

    internal static string LocalePath(string locale) => Path.Combine(Folder, $"{BaseName}.{locale}.tsv");

    /// <summary>True when there is something to apply, which is what decides whether the text
    /// patches are installed at all.</summary>
    internal static bool AnyFilePresent()
    {
        if (File.Exists(CommonPath))
            return true;
        foreach (var file in Directory.GetFiles(Folder, BaseName + ".*.tsv"))
            if (file != null)
                return true;
        return false;
    }

    /// <summary>Overwrites the loaded translations for that locale. The dictionary is the game's own
    /// <c>keyToTranslation</c>, which <c>MacroSettings.Translate</c> reads.</summary>
    internal static void Apply(object translation, string locale)
    {
        if (GameAccess.Get(translation, "keyToTranslation") is not IDictionary table)
        {
            Plugin.Log.LogWarning("Translation table not reachable; text overrides are not applied.");
            return;
        }

        Unresolved.Clear();
        var entries = Read(CommonPath);
        foreach (var entry in Read(LocalePath(locale)))
            entries[entry.Key] = entry.Value;
        Write(table, entries, locale);
    }

    /// <summary>Applies the lines that named an English source, once the key table exists. The game
    /// reads the key table after the locale, so on the first load those lines wait for this.</summary>
    internal static void ApplyDeferred()
    {
        if (Unresolved.Count == 0)
            return;

        var settingsType = GameAccess.TypeNamed("MacroSettings");
        var current = AccessTools.Field(settingsType, "currentTranslation")?.GetValue(null);
        if (GameAccess.Get(current, "keyToTranslation") is not IDictionary table)
            return;

        var pending = new Dictionary<string, string>(Unresolved, StringComparer.Ordinal);
        Unresolved.Clear();
        Write(table, pending, AccessTools.Property(settingsType, "CurrentLocale")?.GetValue(null) as string ?? "");
    }

    private static void Write(IDictionary table, Dictionary<string, string> entries, string locale)
    {
        if (entries.Count == 0)
            return;

        var keyByEnglish = KeyByEnglish();
        var knownKeys = keyByEnglish == null ? null : new HashSet<string>(keyByEnglish.Values, StringComparer.Ordinal);
        var replaced = 0;
        var deferred = 0;
        var unknown = new List<string>();

        foreach (var entry in entries)
        {
            var key = entry.Key;
            if (!table.Contains(key) && knownKeys?.Contains(key) != true)
            {
                // A line that names the English source instead of the key.
                if (keyByEnglish == null)
                {
                    Unresolved[key] = entry.Value;
                    deferred++;
                    continue;
                }

                // Neither: MacroSettings.L still looks a text up by itself when it has no key, which
                // is how the mod's own messages are reworded.
                if (keyByEnglish.TryGetValue(key, out var inferred))
                    key = inferred;
                else
                    unknown.Add(key);
            }

            table[key] = entry.Value;
            replaced++;
        }

        if (replaced > 0)
            Plugin.Log.LogInfo($"Applied {replaced} text overrides for '{locale}'.");
        if (deferred > 0)
            Plugin.Log.LogInfo($"{deferred} overrides wait for the key table to load.");
        foreach (var line in unknown)
            Plugin.Log.LogInfo($"\"{Shorten(line)}\" matches no game text; added as a text of its own.");
    }

    /// <summary>English source to translation key, or null while MacroSettings has not read the key
    /// table yet.</summary>
    private static Dictionary<string, string> KeyByEnglish()
    {
        var keys = AccessTools.Field(GameAccess.TypeNamed("MacroSettings"), "translationKeys")?.GetValue(null);
        if (keys == null)
            return null;

        var map = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var pair in GameAccess.Pairs(keys))
            if (pair.Value is string key)
                map[pair.Key] = key;
        return map.Count == 0 ? null : map;
    }

    private static Dictionary<string, string> Read(string path)
    {
        var entries = new Dictionary<string, string>(StringComparer.Ordinal);
        if (!File.Exists(path))
            return entries;

        try
        {
            var line = 0;
            foreach (var raw in ReadLines(path))
            {
                line++;
                if (string.IsNullOrWhiteSpace(raw) || raw.StartsWith("#", StringComparison.Ordinal))
                    continue;

                var tab = raw.IndexOf('\t');
                if (tab <= 0)
                {
                    Plugin.Log.LogWarning($"{Path.GetFileName(path)} line {line} has no tab separator; skipped.");
                    continue;
                }

                var key = raw.Substring(0, tab).Trim();
                var text = raw.Substring(tab + 1);
                var nextTab = text.IndexOf('\t');
                if (nextTab >= 0)
                    text = text.Substring(0, nextTab);
                entries[TextEscaping.Unescape(key)] = TextEscaping.Unescape(text);
            }

            Plugin.Log.LogInfo($"Read {entries.Count} text overrides from {Path.GetFileName(path)}.");
        }
        catch (Exception e)
        {
            Plugin.Log.LogError($"Reading {path} failed: {e.Message}");
        }

        return entries;
    }

    /// <summary>Reads the file even while a spreadsheet holds it open, which is how these files are
    /// usually edited.</summary>
    private static IEnumerable<string> ReadLines(string path)
    {
        var lines = new List<string>();
        using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
        using (var reader = new StreamReader(stream, Encoding.UTF8))
        {
            string line;
            while ((line = reader.ReadLine()) != null)
                lines.Add(line);
        }

        return lines;
    }

    private static string Shorten(string line) => line.Length <= 60 ? line : line.Substring(0, 57) + "...";
}
