using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using BepInEx;
using Newtonsoft.Json;

namespace Reus2Archipelago.Game;

/// <summary>
/// Ties a Reus 2 profile to one Archipelago slot. A fresh profile is claimed and cleared on first
/// contact; a profile with progress, or one claimed by another slot, is refused so a real save is
/// never overwritten. Bindings are stored beside the BepInEx config.
/// </summary>
internal static class ProfileBinding
{
    private sealed class Record
    {
        public string Seed;
        public string Slot;
        public int EonBoostsApplied;
    }

    private static readonly string Folder = Path.Combine(Paths.ConfigPath, "Reus2Archipelago");
    private static readonly Dictionary<string, Record> Cache = new Dictionary<string, Record>();

    internal static bool IsBound(object profile) => profile != null && Read(profile) != null;

    internal static bool EnsureBound(object profile, string seed, string slot)
    {
        var record = Read(profile);
        if (record != null)
        {
            if (record.Seed == seed && record.Slot == slot)
                return true;
            Plugin.Log.LogError(
                $"Profile '{ProfileName(profile)}' belongs to another Archipelago seed or slot. Switch to a new profile.");
            return false;
        }

        if (!IsFresh(profile))
        {
            Plugin.Log.LogError(
                $"Profile '{ProfileName(profile)}' already has progress. Create a new profile for this Archipelago slot.");
            return false;
        }

        Clear(profile);
        Write(profile, new Record { Seed = seed, Slot = slot });
        Plugin.Log.LogInfo($"Profile '{ProfileName(profile)}' is now bound to slot {slot}.");
        return true;
    }

    internal static int EonBoostsApplied(object profile) => Read(profile)?.EonBoostsApplied ?? 0;

    internal static void SetEonBoostsApplied(object profile, int count)
    {
        var record = Read(profile);
        if (record == null)
            return;
        record.EonBoostsApplied = count;
        Write(profile, record);
    }

    private static string ProfileName(object profile)
        => profile is UnityEngine.Object unityObject ? unityObject.name : GameAccess.Get(profile, "name") as string ?? "profile";

    private static bool IsFresh(object profile)
    {
        var level = GameAccess.Get(profile, "profileLevel") as int? ?? 0;
        var sessions = GameAccess.Get(profile, "idToSessionData") as IEnumerable;
        return level <= 1 && (sessions == null || !sessions.Cast<object>().Any());
    }

    // Vanilla level-one grants; everything the slot owns is re-added from received items.
    private static void Clear(object profile)
    {
        foreach (var field in new[] { "bioticumData", "giantData", "biomeData", "characterData", "unlockedAbilities" })
            (GameAccess.Get(profile, field) as IList)?.Clear();
    }

    private static Record Read(object profile)
    {
        var name = ProfileName(profile);
        if (Cache.TryGetValue(name, out var cached))
            return cached;

        Record record = null;
        var path = PathFor(name);
        try
        {
            if (File.Exists(path))
                record = JsonConvert.DeserializeObject<Record>(File.ReadAllText(path));
        }
        catch (Exception e)
        {
            Plugin.Log.LogError($"Could not read profile binding {path}: {e.Message}");
        }
        Cache[name] = record;
        return record;
    }

    private static void Write(object profile, Record record)
    {
        var name = ProfileName(profile);
        Directory.CreateDirectory(Folder);
        File.WriteAllText(PathFor(name), JsonConvert.SerializeObject(record, Formatting.Indented));
        Cache[name] = record;
    }

    private static string PathFor(string profileName) => Path.Combine(Folder, profileName + ".binding.json");
}
