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
    [JsonProperty("progressive_micro")] public NamedItem ProgressiveMicro;
    [JsonProperty("eras")] public EraEntry Eras;
    [JsonProperty("filler")] public List<NamedItem> Filler = new List<NamedItem>();
    [JsonProperty("request_rewards")] public List<RequestEntry> RequestRewards = new List<RequestEntry>();
    [JsonProperty("shop_drafts")] public List<ShopEntry> ShopDrafts = new List<ShopEntry>();
    [JsonProperty("turning_points")] public List<string> TurningPoints = new List<string>();
    [JsonProperty("shop_options")] public List<string> ShopOptions = new List<string>();

    private Dictionary<string, BioticumEntry> bioticumByGuid;
    private Dictionary<string, string> comboLocations;

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
        data.comboLocations = data.Biotica
            .SelectMany(b => b.Rules.Select(r => (key: ComboKey(b.Guid, r.RuleIndex), r.Location)))
            .ToDictionary(p => p.key, p => p.Location);
        Instance = data;
        Plugin.Log.LogInfo($"Archipelago data for Reus 2 {data.GameVersion}: {data.Biotica.Count} biotica.");
        return true;
    }

    internal BioticumEntry BioticumForGuid(string guid)
        => guid != null && bioticumByGuid.TryGetValue(guid, out var entry) ? entry : null;

    internal string ComboLocation(string guid, int ruleIndex)
        => comboLocations.TryGetValue(ComboKey(guid, ruleIndex), out var location) ? location : null;

    /// <summary>The location for the index-th draft-paying request of a level, if it is among the
    /// first <paramref name="enabled"/> the slot turned into checks.</summary>
    internal string RequestLocation(int level, int index, int enabled)
    {
        var position = RequestRewards.FindIndex(r => r.Level == level && r.Index == index);
        return position >= 0 && position < enabled ? RequestRewards[position].Location : null;
    }

    internal string ShopLocation(int index, int enabled)
        => index >= 1 && index <= enabled && index <= ShopDrafts.Count ? ShopDrafts[index - 1].Location : null;

    private static string ComboKey(string guid, int ruleIndex) => guid + "#" + ruleIndex;
}

internal sealed class BioticumEntry
{
    [JsonProperty("guid")] public string Guid;
    [JsonProperty("item")] public string Item;
    [JsonProperty("build_location")] public string BuildLocation;
    [JsonProperty("rules")] public List<RuleEntry> Rules = new List<RuleEntry>();
}

internal sealed class RuleEntry
{
    [JsonProperty("rule_index")] public int RuleIndex;
    [JsonProperty("location")] public string Location;
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
    [JsonProperty("order")] public int Order;
}

internal sealed class NamedItem
{
    [JsonProperty("item")] public string Item;
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
