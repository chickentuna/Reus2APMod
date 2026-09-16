using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Newtonsoft.Json;

// Fields below are filled by Newtonsoft from reus2.json, which the compiler cannot see.
#pragma warning disable CS0649

namespace Reus2Archipelago.Archipelago;

/// <summary>
/// The item and location tables shared with the apworld, read from the embedded reus2.json that
/// tools/build_ap_data.py generates. Game definitions are matched by GUID.
/// </summary>
internal sealed class ApData
{
    private const string ResourceName = "Reus2Archipelago.reus2.json";

    internal static ApData Instance { get; private set; }

    [JsonProperty("game_version")] public string GameVersion;
    [JsonProperty("biotica")] public List<BioticumEntry> Biotica = new List<BioticumEntry>();
    [JsonProperty("giants")] public List<GiantEntry> Giants = new List<GiantEntry>();
    [JsonProperty("biomes")] public List<GuidItem> Biomes = new List<GuidItem>();
    [JsonProperty("abilities")] public List<AbilityEntry> Abilities = new List<AbilityEntry>();
    [JsonProperty("characters")] public List<GuidItem> Characters = new List<GuidItem>();
    [JsonProperty("micros")] public List<MicroEntry> Micros = new List<MicroEntry>();
    [JsonProperty("eras")] public EraEntry Eras;
    [JsonProperty("unlocks")] public List<UnlockEntry> Unlocks = new List<UnlockEntry>();
    [JsonProperty("filler")] public List<FillerEntry> Filler = new List<FillerEntry>();
    [JsonProperty("request_rewards")] public List<RequestEntry> RequestRewards = new List<RequestEntry>();
    [JsonProperty("shop_drafts")] public List<ShopEntry> ShopDrafts = new List<ShopEntry>();
    /// <summary>Granted from the start: every turning point, and the shop options that are not
    /// <see cref="Unlocks"/>.</summary>
    [JsonProperty("turning_points")] public List<string> TurningPoints = new List<string>();
    [JsonProperty("shop_options")] public List<string> ShopOptions = new List<string>();

    private Dictionary<string, BioticumEntry> bioticumByGuid;
    private Dictionary<string, List<string>> bioticumItems;
    private Dictionary<string, RuleEntry> comboRules;

    internal static bool Load()
    {
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(ResourceName);
        if (stream == null)
        {
            Plugin.Log.LogError($"Embedded resource {ResourceName} is missing; rebuild the plugin.");
            return false;
        }

        using var reader = new StreamReader(stream);
        var data = JsonConvert.DeserializeObject<ApData>(reader.ReadToEnd());
        data.bioticumByGuid = data.Biotica.ToDictionary(b => b.Guid);
        data.bioticumItems = data.Biotica.ToDictionary(b => b.Guid, b => new List<string> { b.Item });
        var bioticumByName = data.Biotica.ToDictionary(b => b.Name);
        foreach (var apex in data.Biotica.Where(b => b.ApexItem != null))
        {
            data.bioticumItems[apex.Guid].Add(apex.ApexItem);
            foreach (var cohort in apex.Cohorts)
                data.bioticumItems[bioticumByName[cohort].Guid].Add(apex.ApexItem);
        }
        data.comboRules = data.Biotica
            .SelectMany(b => b.Rules.Select(r => (key: ComboKey(b.Guid, r.RuleIndex), rule: r)))
            .ToDictionary(p => p.key, p => p.rule);
        Instance = data;
        Plugin.Log.LogInfo($"Archipelago data for Reus 2 {data.GameVersion}: {data.Biotica.Count} biotica.");
        return true;
    }

    internal BioticumEntry BioticumForGuid(string guid)
        => guid != null && bioticumByGuid.TryGetValue(guid, out var entry) ? entry : null;

    /// <summary>The items that grant a bioticum: its own item, and the apex item of every apex
    /// bioticum that lists it as a cohort. A seed's pool holds one kind or the other.</summary>
    internal List<string> ItemsGranting(string guid) => bioticumItems[guid];

    /// <summary>The rule at that index of a bioticum, or null for the rules the data leaves out,
    /// which send no check of their own and do not count towards the bioticum's All Combos.</summary>
    internal RuleEntry ComboRule(string guid, int ruleIndex)
        => comboRules.TryGetValue(ComboKey(guid, ruleIndex), out var rule) ? rule : null;

    /// <summary>The location for the index-th draft-paying request of a level, if it is among the
    /// first <paramref name="enabled"/> the slot turned into checks.</summary>
    internal string RequestLocation(int level, int index, int enabled)
    {
        if (level < 3)
        {
            var position = RequestRewards.FindIndex(r => r.Level == level && r.Index == index);
            if (position < 0)
            {
                return null;
            }
            return RequestRewards[position].Location;
        }

        return index <= enabled ? "Level 1 Request Reward " + index : null;
    }

    internal string ShopLocation(int index, int enabled)
        => index >= 1 && index <= enabled && index <= ShopDrafts.Count ? ShopDrafts[index - 1].Location : null;

    private static string ComboKey(string guid, int ruleIndex) => guid + "#" + ruleIndex;
}

internal sealed class BioticumEntry
{
    [JsonProperty("guid")] public string Guid;
    [JsonProperty("name")] public string Name;
    [JsonProperty("item")] public string Item;
    [JsonProperty("cohorts")] public List<string> Cohorts = new List<string>();
    [JsonProperty("apex_item")] public string ApexItem;
    [JsonProperty("build_location")] public string BuildLocation;
    [JsonProperty("all_combos_location")] public string AllCombosLocation;
    [JsonProperty("rules")] public List<RuleEntry> Rules = new List<RuleEntry>();
}

internal sealed class RuleEntry
{
    [JsonProperty("rule_index")] public int RuleIndex;
    [JsonProperty("location")] public string Location;

    /// <summary>The stack max_combos asks this rule to reach. 0 asks only that it activate, which is
    /// what the data writes for a rule the game does not cap and for one whose cap counts something
    /// a planet cannot reach.</summary>
    [JsonProperty("max_stacks")] public int MaxStacks;
}

internal sealed class GiantEntry
{
    [JsonProperty("guid")] public string Guid;
    [JsonProperty("item")] public string Item;
    [JsonProperty("role")] public string Role;
}

internal sealed class GuidItem
{
    [JsonProperty("guid")] public string Guid;
    [JsonProperty("item")] public string Item;
}

internal sealed class AbilityEntry
{
    [JsonProperty("item")] public string Item;
    [JsonProperty("guids")] public List<string> Guids = new List<string>();
}

internal sealed class MicroEntry
{
    [JsonProperty("guid")] public string Guid;
    [JsonProperty("item")] public string Item;
}

/// <summary>An item standing in for a profile-level unlock: the shop options it grants.</summary>
internal sealed class UnlockEntry
{
    [JsonProperty("item")] public string Item;
    [JsonProperty("shop_options")] public List<string> ShopOptions = new List<string>();
}

/// <summary>A filler item: the player resource it adds to in the planet, and by how much.</summary>
internal sealed class FillerEntry
{
    [JsonProperty("item")] public string Item;
    [JsonProperty("resource")] public string Resource;
    [JsonProperty("amount")] public float Amount;
}

internal sealed class EraEntry
{
    [JsonProperty("item")] public string Item;
    [JsonProperty("base")] public List<string> Base = new List<string>();
    [JsonProperty("eras")] public List<string> Eras = new List<string>();
}

internal sealed class RequestEntry
{
    [JsonProperty("location")] public string Location;
    [JsonProperty("level")] public int Level;
    [JsonProperty("index")] public int Index;
}

internal sealed class ShopEntry
{
    [JsonProperty("location")] public string Location;
    [JsonProperty("index")] public int Index;
}
