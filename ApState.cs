using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using Archipelago.MultiClient.Net.Enums;
using Newtonsoft.Json.Linq;
using Reus2Archipelago.Archipelago;
using Reus2Archipelago.Game;

namespace Reus2Archipelago;

/// <summary>
/// Main-thread boundary between game patches and the Archipelago network client: received items,
/// completed checks, slot data and the goal.
/// </summary>
internal static class ApState
{
    private static readonly HashSet<string> SeedLocations = new HashSet<string>();
    private static readonly HashSet<string> CheckedLocations = new HashSet<string>();
    private static readonly HashSet<string> UnsentChecks = new HashSet<string>();
    private static readonly SortedDictionary<int, string> Received = new SortedDictionary<int, string>();
    private static readonly HashSet<string> SlotComboRuleIds = new HashSet<string>();
    private static readonly ConcurrentQueue<PendingItem> PendingItems =
        new ConcurrentQueue<PendingItem>();
    private static readonly ConcurrentQueue<PendingDisconnect> PendingDisconnects =
        new ConcurrentQueue<PendingDisconnect>();

    private static ArchipelagoClient client;
    private static Dictionary<string, object> slotData = new Dictionary<string, object>();
    private static bool goalSent;
    private static bool slotComboRuleIdsProvided;
    private static bool finalEraChecksRestored;
    private static int finalEraCompletionCount;

    internal static bool Connected => client?.Connected == true;

    /// <summary>A slot has been logged into since the game started; its items and slot data stay
    /// known after a disconnect.</summary>
    internal static bool HasSession { get; private set; }

    internal static string Seed { get; private set; }

    internal static string SlotName { get; private set; }

    internal static IEnumerable<string> ReceivedItems => Received.Values;

    /// <summary>The seed's goal_builds, or 0 before a slot has been logged into.</summary>
    internal static int GoalBuilds { get; private set; }

    /// <summary>The seed's three-star Discovery goal, or 0 for older seeds.</summary>
    internal static int GoalFinalEras { get; private set; }

    internal static int FinalEraCheckCount { get; private set; }

    internal static int FinalErasCompleted => finalEraCompletionCount;

    internal static bool IceAgeEnabled { get; private set; }

    internal static bool EvergladesEnabled { get; private set; }

    internal static bool GrasslandsEnabled { get; private set; }

    internal static bool JurassicEnabled { get; private set; }

    internal static int ProjectChecks { get; private set; }

    internal static bool TurningPointChecksEnabled { get; private set; }

    /// <summary>Different biotica built so far towards the goal, updated whenever a check is sent.</summary>
    internal static int BuiltCount { get; private set; }

    /// <summary>The item the last sent check held, who it was for, and whether that is this slot.
    /// Null until a check goes out with the seed scouted.</summary>
    internal static string LastSentItem { get; private set; }

    internal static string LastSentTo { get; private set; }

    internal static bool LastSentToSelf { get; private set; }

    internal static void AdoptSession(
        ArchipelagoClient newClient,
        IEnumerable<string> seedLocations,
        IEnumerable<string> alreadyChecked,
        string slot,
        string seed,
        Dictionary<string, object> newSlotData)
    {
        ClearPending();
        client = newClient;
        SeedLocations.Clear();
        CheckedLocations.Clear();
        Received.Clear();
        ApNotices.Reset();

        if (seedLocations != null)
            SeedLocations.UnionWith(seedLocations);

        if (alreadyChecked != null)
        {
            foreach (var location in alreadyChecked)
                CheckedLocations.Add(location);
        }

        SlotName = slot;
        Seed = seed;
        slotData = newSlotData ?? new Dictionary<string, object>();
        SlotComboRuleIds.Clear();
        slotComboRuleIdsProvided = false;
        if (slotData.TryGetValue("combo_rule_ids", out var comboValue) && comboValue is JArray comboRuleIds)
        {
            slotComboRuleIdsProvided = true;
            foreach (var token in comboRuleIds)
            {
                if (token.Type == JTokenType.String)
                    SlotComboRuleIds.Add(token.Value<string>());
                else
                    Plugin.Log.LogWarning("Ignoring a non-string entry in slot data combo_rule_ids.");
            }
        }
        GoalBuilds = SlotInt("goal_builds", 0);
        GoalFinalEras = SlotInt("goal_final_eras", 0);
        FinalEraCheckCount = SlotInt("final_era_check_count", 0);
        IceAgeEnabled = SlotBool("ice_age", false);
        EvergladesEnabled = SlotBool("everglades", false);
        GrasslandsEnabled = SlotBool("grasslands", false);
        JurassicEnabled = SlotBool("jurassic", false);
        ProjectChecks = SlotInt("project_checks", 0);
        TurningPointChecksEnabled = SlotBool("turning_point_checks", false);
        finalEraCompletionCount = 0;
        finalEraChecksRestored = false;
        HasSession = true;
        goalSent = false;
        ItemApplier.MarkDirty();

        Plugin.Log.LogInfo(
            $"Archipelago session adopted with {CheckedLocations.Count} completed checks.");
        Plugin.Log.LogInfo("Slot data: " + string.Join(", ", slotData
            .Where(pair => !(pair.Value is JArray || pair.Value is JObject))
            .Select(pair => $"{pair.Key}={pair.Value}")));
        foreach (var key in new[] { "bioticum_checks", "bioticum_items", "goal_builds",
                     "goal_final_eras", "final_era_check_count", "ice_age", "everglades", "grasslands",
                     "jurassic", "project_checks", "turning_point_checks", "combo_rule_ids" })
        {
            if (!slotData.ContainsKey(key))
                Plugin.Log.LogWarning($"Slot data has no {key}; this seed was generated by an older "
                                      + "apworld and the mod is falling back to its default.");
        }

        foreach (var location in UnsentChecks.ToList())
            SendCheck(location);
        CheckGoal();
        MarkLocationBadgesDirty();
    }

    /// <summary>A location of this slot's seed that has been neither sent nor kept to send. A name
    /// the seed does not hold, such as a rule the slot's options leave out, is never open.</summary>
    internal static bool IsOpen(string locationName)
        => locationName != null
           && SeedLocations.Contains(locationName)
           && !CheckedLocations.Contains(locationName)
           && !UnsentChecks.Contains(locationName);

    internal static bool HasLocation(string locationName)
        => locationName != null && SeedLocations.Contains(locationName);

    internal static void ReleaseSession(ArchipelagoClient source)
    {
        if (!ReferenceEquals(client, source))
            return;

        client = null;
        ClearPending();
    }

    internal static void QueueReceivedItem(
        ArchipelagoClient source,
        int index,
        string itemName,
        string senderName,
        bool fromSelf,
        long location,
        ItemFlags flags)
    {
        PendingItems.Enqueue(new PendingItem(source, index, itemName, senderName, fromSelf, location, flags));
    }

    internal static void QueueDisconnect(ArchipelagoClient source, string reason)
    {
        PendingDisconnects.Enqueue(new PendingDisconnect(source, reason));
    }

    /// <summary>Called every frame on Unity's main thread.</summary>
    internal static void DrainNetworkEvents()
    {
        while (PendingDisconnects.TryDequeue(out var disconnect))
        {
            if (!ReferenceEquals(client, disconnect.Source))
                continue;

            Plugin.Log.LogWarning(
                $"Archipelago connection closed: {disconnect.Reason ?? "unknown reason"}");
            client = null;
        }

        while (PendingItems.TryDequeue(out var item))
        {
            if (!ReferenceEquals(client, item.Source) || Received.ContainsKey(item.Index))
                continue;

            Received[item.Index] = item.Name;
            var from = item.SenderName ?? (item.FromSelf ? "this slot" : "no player");
            Plugin.Log.LogInfo($"Received item #{item.Index}: {item.Name ?? "<unnamed>"} from {from}");
            var flags = item.Location > 0 ? item.Flags
                : item.Location == StartingInventoryLocation ? StartingItemFlags(item.Name)
                : (ItemFlags?)null;
            ApNotices.ItemReceived(item.Index, item.Name, item.SenderName, item.FromSelf, flags);
            ItemApplier.MarkDirty();
        }
    }

    /// <summary>Sends a check, or keeps it until the next connection when offline.</summary>
    internal static bool SendCheck(string locationName)
    {
        if (string.IsNullOrWhiteSpace(locationName) || CheckedLocations.Contains(locationName))
            return false;
        if (SeedLocations.Count > 0 && !SeedLocations.Contains(locationName))
            return false;

        if (client == null || !client.SendCheck(locationName))
        {
            if (UnsentChecks.Add(locationName))
            {
                Plugin.Log.LogInfo($"Check kept until the next connection: {locationName}");
                MarkLocationBadgesDirty();
            }
            return false;
        }

        UnsentChecks.Remove(locationName);
        CheckedLocations.Add(locationName);
        MarkLocationBadgesDirty();

        // What that check just handed out. Unknown until the connect-time scout lands, so a check
        // sent in the first seconds of a session has no names to show.
        var known = client.TryDescribeCheck(locationName, out var itemName, out var receiverName, out var forSelf,
            out var flags);
        LastSentItem = known ? itemName : null;
        LastSentTo = known ? receiverName : null;
        LastSentToSelf = known && forSelf;

        if (known)
            ApNotices.CheckSent(itemName, receiverName, forSelf, flags);

        Plugin.Log.LogInfo(known
            ? $"Sent check: {locationName} — {LastSentItem} for {(forSelf ? "you" : LastSentTo)}"
            : $"Sent check: {locationName}");
        CheckGoal();
        return true;
    }

    private static void MarkLocationBadgesDirty()
    {
        BioticaBadges.MarkDirty();
        ProjectBadges.MarkDirty();
        TurningPointStarBadges.MarkDirty();
    }

    /// <summary>Restores completion checks saved with the bound profile after reconnecting or
    /// restarting the game.</summary>
    internal static void RestoreFinalEraChecks(object profile)
    {
        if (finalEraChecksRestored || !ItemApplier.ProfileBound)
            return;

        finalEraChecksRestored = true;
        finalEraCompletionCount = ProfileBinding.FinalEraCompletions(profile);
        var count = Math.Min(finalEraCompletionCount, FinalEraCheckCount);
        for (var index = 1; index <= count; index++)
        {
            var location = Archipelago.ApData.Instance.FinalEraLocation(index);
            if (location != null)
                SendCheck(location);
        }
        CheckGoal();
    }

    /// <summary>Persists a completed Discovery session before sending its numbered location.</summary>
    internal static void RecordFinalEraCompletion(string session)
    {
        if (string.IsNullOrWhiteSpace(session) || !HasSession || !ItemApplier.ProfileBound)
            return;

        var profile = GameAccess.CurrentProfile;
        var index = ProfileBinding.RecordFinalEraCompletion(profile, session);
        if (index == 0)
        {
            Plugin.Log.LogWarning("Could not persist a three-star Discovery completion for the bound profile.");
            return;
        }

        finalEraCompletionCount = ProfileBinding.FinalEraCompletions(profile);
        if (index <= FinalEraCheckCount)
        {
            var location = Archipelago.ApData.Instance.FinalEraLocation(index);
            if (location != null)
                SendCheck(location);
        }
        else
        {
            Plugin.Log.LogWarning(
                $"Discovery completion #{index} exceeds this seed's {FinalEraCheckCount} Final Era checks.");
        }
        CheckGoal();
    }

    /// <summary>The location the server names for an item of the slot's start inventory.</summary>
    private const long StartingInventoryLocation = -2;

    /// <summary>The classification the seed gave a start inventory item, which the server sends
    /// without flags; null when the slot data does not say.</summary>
    private static ItemFlags? StartingItemFlags(string itemName)
    {
        if (itemName == null || !(slotData.TryGetValue("starting_item_flags", out var value) && value is JObject flags))
            return null;
        return flags.TryGetValue(itemName, out var token) ? (ItemFlags)token.Value<int>() : (ItemFlags?)null;
    }

    internal static int SlotInt(string key, int fallback)
    {
        if (!slotData.TryGetValue(key, out var value) || value == null)
            return fallback;
        try
        {
            return value is JToken token ? token.Value<int>() : Convert.ToInt32(value);
        }
        catch
        {
            return fallback;
        }
    }

    private static bool SlotBool(string key, bool fallback)
    {
        if (!slotData.TryGetValue(key, out var value) || value == null)
            return fallback;
        if (value is JValue token)
        {
            if (token.Type == JTokenType.Boolean)
                return token.Value<bool>();
            if (token.Type == JTokenType.Integer)
                return token.Value<long>() != 0;
            if (token.Type == JTokenType.String)
            {
                var tokenText = token.Value<string>();
                if (bool.TryParse(tokenText, out var tokenResult))
                    return tokenResult;
                if (int.TryParse(tokenText, out var tokenInt))
                    return tokenInt != 0;
            }
        }
        if (value is bool flag)
            return flag;
        if (value is int intValue)
            return intValue != 0;
        if (value is long longValue)
            return longValue != 0;
        if (value is string text && bool.TryParse(text, out var result))
            return result;
        if (value is string numericText && int.TryParse(numericText, out var numericResult))
            return numericResult != 0;

        Plugin.Log.LogWarning($"Slot data {key} is not a Boolean; using {fallback}.");
        return fallback;
    }

    internal static bool IsDlcEnabled(string dlc)
    {
        if (string.IsNullOrEmpty(dlc))
            return true;
        if (dlc == "Ice Age")
            return IceAgeEnabled;
        if (dlc == "Everglades")
            return EvergladesEnabled;
        if (dlc == "Grasslands")
            return GrasslandsEnabled;
        if (dlc == "Jurassic")
            return JurassicEnabled;
        Plugin.Log.LogWarning($"Unknown DLC '{dlc}' in Archipelago data; leaving it disabled.");
        return false;
    }

    /// <summary>Counts a bioticum toward the goal even when the seed has no location for it.</summary>
    internal static void RecordGoalBioticum(Archipelago.BioticumEntry bioticum)
    {
        if (bioticum == null || !HasSession || !ItemApplier.ProfileBound
            || bioticum.Level > SlotInt("max_biotica_level", int.MaxValue)
            || !IsDlcEnabled(bioticum.Dlc))
            return;

        ProfileBinding.RecordGoalBioticum(GameAccess.CurrentProfile, Seed, SlotName, bioticum.Name);
        BioticaBadges.MarkDirty();
        CheckGoal();
    }

    internal static bool IsComboRuleEnabled(string guid, int ruleIndex)
    {
        var id = $"{guid}#{ruleIndex}";
        if (slotComboRuleIdsProvided)
            return SlotComboRuleIds.Contains(id);
        var legacyIds = Archipelago.ApData.Instance?.BaseComboRuleIds;
        return legacyIds == null || legacyIds.Count == 0 || legacyIds.Contains(id);
    }

    /// <summary>Biotica the slot has checked. A bioticum whose location is its combos was built to
    /// get there, so both families count, and a bioticum is counted once.</summary>
    /// <summary>A bioticum above the slot's max_biotica_level has no item or location and is held
    /// from the start.</summary>
    internal static bool HeldFromStart(Archipelago.BioticumEntry bioticum)
        => bioticum.Level > SlotInt("max_biotica_level", int.MaxValue);

    private static int BioticaBuilt()
    {
        var data = Archipelago.ApData.Instance;
        if (data == null)
            return CheckedLocations.Count(location => location.StartsWith("Build "));
        var recorded = GoalBioticaCounted();
        var maxLevel = SlotInt("max_biotica_level", int.MaxValue);
        return data.Biotica.Count(b => b.Level <= maxLevel && recorded.Contains(b.Name));
    }

    /// <summary>Names counted toward the bioticum goal, including checks restored from the server.</summary>
    internal static HashSet<string> GoalBioticaCounted()
    {
        var counted = new HashSet<string>(
            ProfileBinding.GoalBiotica(GameAccess.CurrentProfile, Seed, SlotName));
        var data = Archipelago.ApData.Instance;
        if (data == null)
            return counted;

        foreach (var bioticum in data.Biotica)
        {
            if (CheckedLocations.Contains(bioticum.BuildLocation)
                || (bioticum.AllCombosLocation != null && CheckedLocations.Contains(bioticum.AllCombosLocation)))
                counted.Add(bioticum.Name);
        }
        return counted;
    }

    private static void CheckGoal()
    {
        BuiltCount = BioticaBuilt();
        if (goalSent || client == null)
            return;
        var bioticumGoal = SlotInt("goal_builds", int.MaxValue);
        var finalEraGoal = SlotInt("goal_final_eras", 0);
        if (BuiltCount < bioticumGoal || finalEraCompletionCount < finalEraGoal)
            return;

        client.SendGoal();
        goalSent = true;
        Plugin.Log.LogInfo($"Goal complete: {bioticumGoal} biotica built and "
                           + $"{finalEraGoal} three-star Discovery eras completed.");
    }

    private static void ClearPending()
    {
        while (PendingItems.TryDequeue(out _))
        {
        }

        while (PendingDisconnects.TryDequeue(out _))
        {
        }
    }

    private readonly struct PendingItem
    {
        internal PendingItem(ArchipelagoClient source, int index, string name, string senderName, bool fromSelf,
            long location, ItemFlags flags)
        {
            Source = source;
            Index = index;
            Name = name;
            SenderName = senderName;
            FromSelf = fromSelf;
            Location = location;
            Flags = flags;
        }

        internal ArchipelagoClient Source { get; }
        internal int Index { get; }
        internal string Name { get; }
        internal string SenderName { get; }
        internal bool FromSelf { get; }
        /// <summary>The location the item was found at; 0 or below for none, as the start inventory
        /// and the server's /send, whose flags the server leaves out.</summary>
        internal long Location { get; }
        internal ItemFlags Flags { get; }
    }

    private readonly struct PendingDisconnect
    {
        internal PendingDisconnect(ArchipelagoClient source, string reason)
        {
            Source = source;
            Reason = reason;
        }

        internal ArchipelagoClient Source { get; }
        internal string Reason { get; }
    }
}
