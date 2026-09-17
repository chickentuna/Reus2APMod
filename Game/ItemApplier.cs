using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Reus2Archipelago.Archipelago;

namespace Reus2Archipelago.Game;

/// <summary>
/// Rebuilds the bound profile's unlocks from the full stream of received items, so the profile always
/// matches the slot, and pays out filler items and Micro types inside the running planet. Runs on the
/// main thread whenever items arrive, the profile changes, or a planet loads.
/// </summary>
internal static class ItemApplier
{
    /// <summary>The shop options that buy the next Micro type of an era, which only items give.</summary>
    private static readonly string[] MicroTypeShopOptions = { "Micro1", "Micro2", "Micro3" };

    private static bool dirty = true;
    private static object lastProfile;
    private static object lastGameplay;
    private static int pooledFrom;
    private static object checkedProfile;
    private static bool checkedBound;
    private static bool logged;

    internal static void MarkDirty() => dirty = true;

    /// <summary>Whether the current profile belongs to an Archipelago slot; gates every gameplay patch.</summary>
    internal static bool ProfileBound
    {
        get
        {
            var profile = GameAccess.CurrentProfile;
            if (!ReferenceEquals(profile, checkedProfile))
                RecordBound(profile, ProfileBinding.IsBound(profile));
            return checkedBound;
        }
    }

    /// <summary>Every gameplay patch and every check reads ProfileBound, so the log says which way
    /// it went each time it changes.</summary>
    private static void RecordBound(object profile, bool bound)
    {
        checkedProfile = profile;
        if (bound == checkedBound && logged)
            return;
        checkedBound = bound;
        logged = true;
        var name = (profile as UnityEngine.Object)?.name ?? "no profile";
        Plugin.Log.LogInfo(bound
            ? $"Profile '{name}' is bound to slot {ApState.SlotName}: checks are sent and progression is replaced."
            : $"Profile '{name}' is not bound to slot {ApState.SlotName}: no checks are sent.");
    }

    internal static bool IsReceivedBioticum(object definition)
    {
        var state = definition == null ? null : GameAccess.Call(GameAccess.CurrentProfile, "GetBioticumData", definition);
        return GameAccess.Get(state, "unlocked") is bool unlocked && unlocked;
    }

    internal static void Tick()
    {
        var profile = GameAccess.CurrentProfile;
        if (!ReferenceEquals(profile, lastProfile))
        {
            lastProfile = profile;
            dirty = true;
        }
        var gameplay = GameAccess.Gameplay;
        if (!ReferenceEquals(gameplay, lastGameplay))
        {
            lastGameplay = gameplay;
            pooledFrom = 0;
            dirty = true;
        }
        HoldPlanetPool(gameplay);
        if (!dirty || profile == null || !ApState.HasSession || !GameAccess.DefinitionsReady)
            return;
        dirty = false;

        try
        {
            ApIcon.Apply();

            if (!ProfileBinding.EnsureBound(profile, ApState.Seed, ApState.SlotName))
            {
                dirty = true;
                return;
            }
            RecordBound(profile, true);

            var received = Count(ApState.ReceivedItems);
            ApplyProfile(profile, received);
            ApplyPlanet(profile, received);
        }
        catch (Exception e)
        {
            Plugin.Log.LogError($"Applying Archipelago items failed: {e}");
        }
    }

    private static void ApplyProfile(object profile, Dictionary<string, int> received)
    {
        var data = ApData.Instance;

        foreach (var bioticum in data.Biotica)
            SetUnlocked(profile, "GetBioticumData", bioticum.Guid, Holds(bioticum, received));
        foreach (var biome in data.Biomes)
            SetUnlocked(profile, "GetBiomeData", biome.Guid, received.ContainsKey(biome.Item));
        foreach (var character in data.Characters)
            SetUnlocked(profile, "GetCharacterData", character.Guid, received.ContainsKey(character.Item));

        foreach (var giant in data.Giants)
        {
            var definition = GameAccess.Definition(giant.Guid);
            if (definition == null || !(GameAccess.Get(GameAccess.Call(profile, "GetGiantData", definition), "unlockedRoles") is IList roles))
                continue;
            roles.Clear();
            if (received.ContainsKey(giant.Item))
                roles.Add(Enum.Parse(GameAccess.ElementType(roles), giant.Role));
        }

        if (GameAccess.Get(profile, "unlockedAbilities") is IList abilities)
        {
            foreach (var ability in data.Abilities)
            {
                foreach (var definition in ability.Guids.Select(GameAccess.Definition).Where(d => d != null))
                {
                    while (abilities.Contains(definition))
                        abilities.Remove(definition);
                    if (received.ContainsKey(ability.Item))
                        abilities.Add(definition);
                }
            }
        }

        // The tutorial scenario waits for the player to spend a draft, and drafts are locked.
        GameAccess.Set(profile, "tutorialCheckValue", false);

        ApplyEras(profile, received.TryGetValue(data.Eras.Item, out var eras) ? eras : 0);
        GrantMissing(profile, "unlockedTurningPoints", data.TurningPoints.Select(GameAccess.Definition));
        GrantShopOptions(profile, data.ShopOptions);
        ApplyUnlocks(profile, received);
        GameAccess.Call(profile, "InvalidateSaveFile");
    }

    private static bool Holds(BioticumEntry bioticum, Dictionary<string, int> received)
        => ApState.HeldFromStart(bioticum) || ApData.Instance.ItemsGranting(bioticum.Guid).Any(received.ContainsKey);

    /// <summary>GameplayController.UnlockBaseBiotica fills masteredBiotica with each legal biome's
    /// base biotica, and GetAllAvailableBiotica returns that list, so the run's pool is written from
    /// the received items instead. Returns how many it holds.</summary>
    private static int SetPlanetPool(object gameplay, Dictionary<string, int> received)
    {
        if (!(GameAccess.Get(gameplay, "masteredBiotica") is IList pool))
            return -1;

        var wanted = ApData.Instance.Biotica
            .Where(b => Holds(b, received))
            .Select(b => GameAccess.Definition(b.Guid))
            .Where(d => d != null)
            .ToList();

        pool.Clear();
        foreach (var definition in wanted)
            pool.Add(definition);
        pooledFrom = pool.Count;
        return pool.Count;
    }

    /// <summary>Evolving, shop purchases and era grants add to masteredBiotica while a planet runs,
    /// so the pool is compared every frame and rewritten when it grows.</summary>
    private static void HoldPlanetPool(object gameplay)
    {
        if (gameplay == null || pooledFrom == 0 || !ProfileBound)
            return;
        if (GameAccess.Get(gameplay, "masteredBiotica") is IList pool && pool.Count != pooledFrom)
            dirty = true;
    }

    private static void ApplyPlanet(object profile, Dictionary<string, int> received)
    {
        var gameplay = GameAccess.Gameplay;
        if (gameplay == null)
            return;

        var scenario = GameAccess.Get(GameAccess.SessionContext, "Scenario");
        GameAccess.Set(scenario, "draftsLocked", true);
        AddReceivedMicroTypes(GameAccess.Get(gameplay, "aspectController"), received);
        Plugin.Log.LogInfo($"Planet: draftsLocked={GameAccess.Get(scenario, "draftsLocked")}"
                           + $", pool={SetPlanetPool(gameplay, received)} biotica from items.");
        Plugin.Log.LogInfo($"Aspect inventory: {AspectInventory(GameAccess.Get(gameplay, "aspectController"))}");

        var session = GameAccess.SessionId;
        if (session == null)
            return;
        foreach (var filler in ApData.Instance.Filler)
            ApplyFiller(profile, gameplay, session, filler, received.TryGetValue(filler.Item, out var count) ? count : 0);
    }

    /// <summary>Pays this planet every filler item of one kind received so far that it has not been
    /// paid yet, so a new planet starts with the whole stash and a running one gets each new arrival.</summary>
    private static void ApplyFiller(object profile, object gameplay, string session, FillerEntry filler, int received)
    {
        var owed = received - ProfileBinding.FillerApplied(profile, session, filler.Item);
        if (owed <= 0)
            return;
        var total = owed * filler.Amount;
        GameAccess.Call(gameplay, "ChangePlayerResource", GameAccess.Resource(filler.Resource), total);
        ProfileBinding.SetFillerApplied(profile, session, filler.Item, received);
        Plugin.Log.LogInfo($"{filler.Item} x{owed}: +{total} {filler.Resource}.");
    }

    /// <summary>The game draws no Micro types of its own on a bound profile, so a type whose item has
    /// arrived is added to the inventory, where a charge can be spent on it.</summary>
    private static void AddReceivedMicroTypes(object aspects, Dictionary<string, int> received)
    {
        if (aspects == null)
            return;
        foreach (var micro in ApData.Instance.Micros.Where(m => received.ContainsKey(m.Item)))
        {
            var definition = GameAccess.Definition(micro.Guid);
            if (definition != null)
                GameAccess.Call(aspects, "UnlockAspectShopOption", definition);
        }
    }

    /// <summary>Every entry the run's aspect inventory holds, with its charges, so a Micro on screen
    /// can be matched to what put it there.</summary>
    private static string AspectInventory(object aspects)
    {
        if (!(GameAccess.Get(aspects, "aspectInventory") is IEnumerable inventory))
            return "unreadable";
        var lines = inventory.Cast<object>().Select(entry =>
        {
            var name = GameAccess.Get(GameAccess.Get(entry, "Key"), "name");
            var value = GameAccess.Get(entry, "Value");
            return $"{name ?? "?"}(charges={GameAccess.Get(value, "Charges") ?? "?"}"
                   + $" usesCharges={GameAccess.Get(value, "UsesCharges") ?? "?"})";
        }).ToList();

        var giants = GameAccess.Get(aspects, "giantAspect") as IEnumerable;
        var baseAspects = giants?.Cast<object>()
            .Select(entry => $"{GameAccess.Get(GameAccess.Get(GameAccess.Get(entry, "Value"), "Definition"), "name")} (a giant's own)")
            .ToList();
        if (baseAspects != null)
            lines.AddRange(baseAspects);
        return lines.Count == 0 ? "empty" : string.Join(", ", lines);
    }

    /// <summary>Takes out of a new planet's inventory the Micro type AspectController.DrawInitialSet
    /// draws for it, which leaves only the types received from items. Each giant's own Micro is kept
    /// apart from the inventory and stays.</summary>
    internal static void DropDrawnMicroTypes(AspectController aspects)
    {
        if (!(GameAccess.Get(aspects, "aspectInventory") is GenericDictionary<BioticumAspectDefinition, BioticumAspectInventoryEntry> inventory))
            return;
        var received = new HashSet<string>(ApState.ReceivedItems);
        var drawn = inventory.Keys
            .Where(type => !ApData.Instance.Micros.Any(m => received.Contains(m.Item) && m.Guid == type.ID))
            .ToList();
        foreach (var type in drawn)
            inventory.Remove(type);
        if (drawn.Count > 0)
            Plugin.Log.LogInfo($"Removed the Micro types the game drew for this planet: {string.Join(", ", drawn.Select(Naming.Of))}");
    }

    private static void ApplyEras(object profile, int received)
    {
        if (!(GameAccess.Get(profile, "unlockedEras") is IList eras))
            return;
        var eraType = GameAccess.ElementType(eras);
        var names = ApData.Instance.Eras.Base.Concat(ApData.Instance.Eras.Eras.Take(received)).ToList();
        eras.Clear();
        foreach (var name in names)
            eras.Add(Enum.Parse(eraType, name));

        var latest = Convert.ToInt32(Enum.Parse(eraType, names[names.Count - 1])) - 1;
        if (GameAccess.Get(profile, "finalEraSelected") is int selected && selected > latest)
            GameAccess.Set(profile, "finalEraSelected", latest);
    }

    private static void SetUnlocked(object profile, string accessor, string guid, bool unlocked)
    {
        var definition = GameAccess.Definition(guid);
        var state = definition == null ? null : GameAccess.Call(profile, accessor, definition);
        if (state != null)
            GameAccess.Set(state, "unlocked", unlocked);
    }

    private static void GrantMissing(object profile, string listField, IEnumerable<object> definitions)
    {
        if (!(GameAccess.Get(profile, listField) is IList list))
            return;
        foreach (var definition in definitions)
        {
            if (definition != null && !list.Contains(definition))
                list.Add(definition);
        }
    }

    private static void GrantShopOptions(object profile, IEnumerable<string> names)
    {
        if (!(GameAccess.Get(profile, "unlockedShopOptions") is IList options))
            return;
        var optionType = GameAccess.ElementType(options);
        foreach (var name in names)
        {
            var value = Enum.Parse(optionType, name);
            if (!options.Contains(value))
                options.Add(value);
        }
        foreach (var name in MicroTypeShopOptions)
        {
            if (Enum.IsDefined(optionType, name))
                options.Remove(Enum.Parse(optionType, name));
        }
    }

    /// <summary>The profile-level unlocks that are items: each one's shop options are on the profile
    /// exactly while its item has been received.</summary>
    private static void ApplyUnlocks(object profile, Dictionary<string, int> received)
    {
        if (!(GameAccess.Get(profile, "unlockedShopOptions") is IList options))
            return;
        var optionType = GameAccess.ElementType(options);

        foreach (var unlock in ApData.Instance.Unlocks)
        {
            var held = received.ContainsKey(unlock.Item);
            foreach (var value in unlock.ShopOptions.Select(name => Enum.Parse(optionType, name)))
            {
                if (held && !options.Contains(value))
                    options.Add(value);
                else if (!held)
                {
                    while (options.Contains(value))
                        options.Remove(value);
                }
            }
        }
    }

    private static Dictionary<string, int> Count(IEnumerable<string> names)
    {
        var counts = new Dictionary<string, int>();
        foreach (var name in names)
            counts[name] = counts.TryGetValue(name, out var n) ? n + 1 : 1;
        return counts;
    }
}
