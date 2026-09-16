using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;

namespace Reus2Archipelago.Game;

/// <summary>
/// Ties each Archipelago profile to one seed and slot, so a planet never mixes two multiworlds. The
/// binding is stored in the profile's own folder.
/// </summary>
internal static class ProfileBinding
{
    private const string FileName = "archipelago.json";

    private sealed class Record
    {
        public string Seed;
        public string Slot;
        public int EonBoostsApplied;
        public int MicroChargesApplied;
        public int ItemsAnnounced;
        public Dictionary<string, List<int>> ActivatedComboRules = new Dictionary<string, List<int>>();
        public Dictionary<string, Dictionary<int, int>> ComboRuleStacks =
            new Dictionary<string, Dictionary<int, int>>();
    }

    private static readonly Dictionary<string, Record> Cache = new Dictionary<string, Record>();
    private static object warnedProfile;

    internal static bool IsBound(object profile) => ProfileIsolation.IsArchipelagoProfile(profile) && Read(profile) != null;

    /// <summary>
    /// True when the current profile is this slot's. Otherwise, from the main menu, switches to the
    /// slot's Archipelago profile or creates one — an unbound Archipelago profile is simply claimed —
    /// and returns false so the caller retries against the new current profile.
    /// </summary>
    internal static bool EnsureBound(object profile, string seed, string slot)
    {
        var archipelago = ProfileIsolation.IsArchipelagoProfile(profile);
        var record = archipelago ? Read(profile) : null;
        if (record != null && Matches(record, seed, slot))
            return true;

        var install = GameAccess.Install;
        var own = (GameAccess.Get(install, "UserProfiles") as IEnumerable)?.Cast<object>()
            .FirstOrDefault(p => !ReferenceEquals(p, profile) && ProfileIsolation.IsArchipelagoProfile(p)
                                 && Read(p) is Record r && Matches(r, seed, slot));

        if (archipelago && own == null && record == null)
        {
            Clear(profile);
            Write(profile, new Record { Seed = seed, Slot = slot });
            Plugin.Log.LogInfo($"Profile '{ProfileName(profile)}' is now slot {slot}'s.");
            return true;
        }

        if (GameAccess.Gameplay != null)
        {
            if (!ReferenceEquals(warnedProfile, profile))
                Plugin.Log.LogWarning($"This planet is not slot {slot}'s. Return to the main menu to play slot {slot}.");
            warnedProfile = profile;
            return false;
        }

        if (own != null)
        {
            GameAccess.Call(install, "SwitchToUserProfile", own, GameAccess.EnumValue("ProfileSwitchMode", "SwitchOnly"));
            Plugin.Log.LogInfo($"Switched to profile '{ProfileName(own)}' for slot {slot}.");
        }
        else
        {
            var created = ProfileIsolation.CreateAndSwitchToFreshProfile(install);
            Plugin.Log.LogInfo($"Created profile '{ProfileName(created)}' for slot {slot}.");
        }
        return false;
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

    /// <summary>Micro Charge items already paid out, each once, to the planet open when it arrived.</summary>
    internal static int MicroChargesApplied(object profile) => Read(profile)?.MicroChargesApplied ?? 0;

    internal static void SetMicroChargesApplied(object profile, int count)
    {
        var record = Read(profile);
        if (record == null)
            return;
        record.MicroChargesApplied = count;
        Write(profile, record);
    }

    /// <summary>How many received items, counted from the start of the slot's item stream, the chat
    /// log has already told the player about.</summary>
    internal static int ItemsAnnounced(object profile) => Read(profile)?.ItemsAnnounced ?? 0;

    internal static void SetItemsAnnounced(object profile, int count)
    {
        var record = Read(profile);
        if (record == null || record.ItemsAnnounced == count)
            return;
        record.ItemsAnnounced = count;
        Write(profile, record);
    }

    /// <summary>Notes a bioticum's rule index as activated and returns how many of that bioticum's
    /// rule indexes have been activated, over every planet the slot has played.</summary>
    internal static int AddActivatedComboRule(object profile, string guid, int ruleIndex)
    {
        var record = Read(profile);
        if (record == null)
            return 0;
        if (!record.ActivatedComboRules.TryGetValue(guid, out var indexes))
            record.ActivatedComboRules[guid] = indexes = new List<int>();
        if (indexes.Contains(ruleIndex))
            return indexes.Count;
        indexes.Add(ruleIndex);
        Write(profile, record);
        return indexes.Count;
    }

    /// <summary>Notes the stack a bioticum's rule has reached, keeping the highest that rule has
    /// reached on any planet the slot has played, and returns the stack of each of its rules.</summary>
    internal static Dictionary<int, int> AddComboRuleStack(object profile, string guid, int ruleIndex, int stack)
    {
        var record = Read(profile);
        if (record == null)
            return new Dictionary<int, int>();
        if (!record.ComboRuleStacks.TryGetValue(guid, out var stacks))
            record.ComboRuleStacks[guid] = stacks = new Dictionary<int, int>();
        if (stacks.TryGetValue(ruleIndex, out var highest) && highest >= stack)
            return stacks;
        stacks[ruleIndex] = stack;
        Write(profile, record);
        return stacks;
    }

    private static bool Matches(Record record, string seed, string slot) => record.Seed == seed && record.Slot == slot;

    private static string ProfileName(object profile) => (profile as UnityEngine.Object)?.name ?? "profile";

    // Vanilla level-one grants; everything the slot owns is re-added from received items.
    private static void Clear(object profile)
    {
        foreach (var field in new[] { "bioticumData", "giantData", "biomeData", "characterData", "unlockedAbilities" })
            (GameAccess.Get(profile, field) as IList)?.Clear();
    }

    private static string PathFor(object profile)
    {
        var directory = GameAccess.Get(profile, "UserProfileDirectory") as string;
        return directory == null ? null : Path.Combine(directory, FileName);
    }

    private static Record Read(object profile)
    {
        var path = PathFor(profile);
        if (path == null)
            return null;
        if (Cache.TryGetValue(path, out var cached))
            return cached;

        Record record = null;
        try
        {
            if (File.Exists(path))
                record = JsonConvert.DeserializeObject<Record>(File.ReadAllText(path));
        }
        catch (Exception e)
        {
            Plugin.Log.LogError($"Could not read profile binding {path}: {e.Message}");
        }
        Cache[path] = record;
        return record;
    }

    private static void Write(object profile, Record record)
    {
        var path = PathFor(profile);
        if (path == null)
            return;
        Directory.CreateDirectory(Path.GetDirectoryName(path));
        File.WriteAllText(path, JsonConvert.SerializeObject(record, Formatting.Indented));
        Cache[path] = record;
    }
}
