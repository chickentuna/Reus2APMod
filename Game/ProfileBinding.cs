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
        public Dictionary<string, Dictionary<string, int>> FillerAppliedBySession =
            new Dictionary<string, Dictionary<string, int>>();
        public List<string> FinalEraSessions = new List<string>();
        public List<string> GoalBiotica = new List<string>();
        public Dictionary<string, int> ShopDraftPurchasesByOption = new Dictionary<string, int>();
        public int ItemsAnnounced;
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

    /// <summary>How many of a filler item a planet has been paid, keyed by its session ID: a save keeps
    /// the ID, so a reload is not paid again, and a new planet starts at none.</summary>
    internal static int FillerApplied(object profile, string session, string item)
    {
        var record = Read(profile);
        return record != null && record.FillerAppliedBySession.TryGetValue(session, out var items)
                              && items.TryGetValue(item, out var count)
            ? count
            : 0;
    }

    internal static void SetFillerApplied(object profile, string session, string item, int count)
    {
        var record = Read(profile);
        if (record == null)
            return;
        if (!record.FillerAppliedBySession.TryGetValue(session, out var items))
            record.FillerAppliedBySession[session] = items = new Dictionary<string, int>();
        items[item] = count;
        Write(profile, record);
    }

    internal static int FinalEraCompletions(object profile)
    {
        var record = Read(profile);
        return record?.FinalEraSessions?.Count ?? 0;
    }

    internal static IEnumerable<string> GoalBiotica(object profile, string seed, string slot)
    {
        var record = Read(profile);
        if (record == null || !Matches(record, seed, slot) || record.GoalBiotica == null)
            return Enumerable.Empty<string>();
        return record.GoalBiotica;
    }

    internal static void RecordGoalBioticum(object profile, string seed, string slot, string name)
    {
        var record = Read(profile);
        if (record == null || !Matches(record, seed, slot) || string.IsNullOrWhiteSpace(name))
            return;
        if (record.GoalBiotica == null)
            record.GoalBiotica = new List<string>();
        if (record.GoalBiotica.Contains(name))
            return;

        record.GoalBiotica.Add(name);
        Write(profile, record);
    }

    /// <summary>Records one completed Final Era per saved planet session and returns its stable
    /// one-based check number; an existing session keeps its original number.</summary>
    internal static int RecordFinalEraCompletion(object profile, string session)
    {
        var record = Read(profile);
        if (record == null)
            return 0;
        if (record.FinalEraSessions == null)
            record.FinalEraSessions = new List<string>();

        var existing = record.FinalEraSessions.FindIndex(id => id == session);
        if (existing >= 0)
            return existing + 1;

        record.FinalEraSessions.Add(session);
        Write(profile, record);
        return record.FinalEraSessions.Count;
    }

    /// <summary>How many received items, counted from the start of the slot's item stream, the chat
    /// log has already told the player about.</summary>
    internal static int ItemsAnnounced(object profile) => Read(profile)?.ItemsAnnounced ?? 0;

    internal static int ShopDraftPurchases(object profile, string option)
    {
        var purchases = Read(profile)?.ShopDraftPurchasesByOption;
        return purchases != null && purchases.TryGetValue(option, out var count) ? Math.Max(0, count) : 0;
    }

    internal static void RecordShopDraftPurchase(object profile, string option)
    {
        var record = Read(profile);
        if (record == null)
            return;
        if (record.ShopDraftPurchasesByOption == null)
            record.ShopDraftPurchasesByOption = new Dictionary<string, int>();
        record.ShopDraftPurchasesByOption.TryGetValue(option, out var count);
        record.ShopDraftPurchasesByOption[option] = count + 1;
        Write(profile, record);
    }

    internal static void SetItemsAnnounced(object profile, int count)
    {
        var record = Read(profile);
        if (record == null || record.ItemsAnnounced == count)
            return;
        record.ItemsAnnounced = count;
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
