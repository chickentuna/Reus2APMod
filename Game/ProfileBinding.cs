using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;

namespace Reus2Archipelago.Game;

/// <summary>
/// Ties each profile in the Archipelago save folder to one seed and slot, so a planet never mixes two
/// multiworlds. The binding is stored in the profile's own folder.
/// </summary>
internal static class ProfileBinding
{
    private const string FileName = "archipelago.json";

    private sealed class Record
    {
        public string Seed;
        public string Slot;
        public int EonBoostsApplied;
    }

    private static readonly Dictionary<string, Record> Cache = new Dictionary<string, Record>();
    private static object warnedProfile;

    internal static bool IsBound(object profile) => ProfileIsolation.IsArchipelagoProfile(profile) && Read(profile) != null;

    /// <summary>
    /// True when the current profile is this slot's. Otherwise, from the main menu, switches to the
    /// slot's profile or creates one — an unbound current profile is simply claimed — and returns false
    /// so the caller retries against the new current profile.
    /// </summary>
    internal static bool EnsureBound(object profile, string seed, string slot)
    {
        if (!ProfileIsolation.IsArchipelagoProfile(profile))
        {
            if (!ReferenceEquals(warnedProfile, profile))
                Plugin.Log.LogError($"Refusing to use profile '{ProfileName(profile)}': it is not an Archipelago profile.");
            warnedProfile = profile;
            return false;
        }

        var record = Read(profile);
        if (record != null && Matches(record, seed, slot))
            return true;

        var install = GameAccess.Install;
        var own = (GameAccess.Get(install, "UserProfiles") as IEnumerable)?.Cast<object>()
            .FirstOrDefault(p => !ReferenceEquals(p, profile) && Read(p) is Record r && Matches(r, seed, slot));

        if (own == null && record == null)
        {
            Clear(profile);
            Write(profile, new Record { Seed = seed, Slot = slot });
            Plugin.Log.LogInfo($"Profile '{ProfileName(profile)}' is now slot {slot}'s.");
            return true;
        }

        if (GameAccess.Gameplay != null)
        {
            if (!ReferenceEquals(warnedProfile, profile))
                Plugin.Log.LogWarning($"This planet belongs to another Archipelago slot. Return to the main menu to play slot {slot}.");
            warnedProfile = profile;
            return false;
        }

        var switchOnly = GameAccess.EnumValue("ProfileSwitchMode", "SwitchOnly");
        if (own != null)
        {
            GameAccess.Call(install, "SwitchToUserProfile", own, switchOnly);
            Plugin.Log.LogInfo($"Switched to profile '{ProfileName(own)}' for slot {slot}.");
        }
        else
        {
            var created = GameAccess.Call(install, "CreateAndSwitchToFreshProfile", switchOnly);
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
