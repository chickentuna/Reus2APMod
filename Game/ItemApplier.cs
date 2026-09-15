using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Reus2Archipelago.Archipelago;

namespace Reus2Archipelago.Game;

/// <summary>
/// Rebuilds the bound profile's unlocks from the full stream of received items, so the profile always
/// matches the slot, and grants per-planet items (Micros, Eon) inside the running planet. Runs on the
/// main thread whenever items arrive, the profile changes, or a planet loads.
/// </summary>
internal static class ItemApplier
{
    private const float EonPerBoost = 5f;
    private const int MicroUnlocksOff = 0;
    private const int MicroUnlocksProgressive = 2;

    private static bool dirty = true;
    private static object lastProfile;
    private static object checkedProfile;
    private static bool checkedBound;

    internal static void MarkDirty() => dirty = true;

    /// <summary>Whether the current profile belongs to an Archipelago slot; gates every gameplay patch.</summary>
    internal static bool ProfileBound
    {
        get
        {
            var profile = GameAccess.CurrentProfile;
            if (!ReferenceEquals(profile, checkedProfile))
            {
                checkedProfile = profile;
                checkedBound = ProfileBinding.IsBound(profile);
            }
            return checkedBound;
        }
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
        if (!dirty || profile == null || !ApState.HasSession || !GameAccess.DefinitionsReady)
            return;
        dirty = false;

        try
        {
            if (!ProfileBinding.EnsureBound(profile, ApState.Seed, ApState.SlotName))
            {
                dirty = true;
                return;
            }
            checkedProfile = profile;
            checkedBound = true;

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
            SetUnlocked(profile, "GetBioticumData", bioticum.Guid,
                data.ItemsGranting(bioticum.Guid).Any(received.ContainsKey));
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

        ApplyEras(profile, received.TryGetValue(data.Eras.Item, out var eras) ? eras : 0);
        GrantMissing(profile, "unlockedTurningPoints", data.TurningPoints.Select(GameAccess.Definition));
        GrantShopOptions(profile, data.ShopOptions);
        GameAccess.Call(profile, "InvalidateSaveFile");
    }

    private static void ApplyPlanet(object profile, Dictionary<string, int> received)
    {
        var gameplay = GameAccess.Gameplay;
        if (gameplay == null)
            return;

        GameAccess.Set(GameAccess.Get(GameAccess.SessionContext, "Scenario"), "draftsLocked", true);

        var aspects = GameAccess.Get(gameplay, "aspectController");
        var unlocks = ApState.SlotInt("micro_unlocks", MicroUnlocksOff);
        var sold = UnlockedMicros(received, unlocks).ToList();
        foreach (var micro in sold)
        {
            var definition = GameAccess.Definition(micro.Guid);
            if (aspects != null && definition != null)
                GameAccess.Call(aspects, "UnlockAspectShopOption", definition);
        }
        GrantCharges(profile, aspects, received, unlocks == MicroUnlocksOff ? ApData.Instance.Micros : sold);

        var boosts = received.TryGetValue(ApData.Instance.Filler[0].Item, out var count) ? count : 0;
        var applied = ProfileBinding.EonBoostsApplied(profile);
        if (boosts <= applied)
            return;
        var bonusEon = GameAccess.EnumValue("PlayerResource", "BonusEon");
        for (var i = applied; i < boosts; i++)
            GameAccess.Call(gameplay, "ChangePlayerResource", bonusEon, EonPerBoost);
        ProfileBinding.SetEonBoostsApplied(profile, boosts);
    }

    /// <summary>The Micro types to pass to UnlockAspectShopOption. With micro_unlocks off the game
    /// draws the run's types itself.</summary>
    private static IEnumerable<MicroEntry> UnlockedMicros(Dictionary<string, int> received, int unlocks)
    {
        var data = ApData.Instance;
        if (unlocks == MicroUnlocksOff)
            return Enumerable.Empty<MicroEntry>();
        if (unlocks == MicroUnlocksProgressive)
        {
            var count = received.TryGetValue(data.ProgressiveMicro.Item, out var n) ? n : 0;
            return data.Micros.OrderBy(m => m.Order).Take(count);
        }
        return data.Micros.Where(m => received.ContainsKey(m.Item));
    }

    /// <summary>Calls GrantAspectCharge for each Micro the number of its charge items received minus
    /// what the running session has already been granted, then records the new total against
    /// Session.SessionID, so a reload of the same save adds nothing and a new planet adds them again.
    /// <paramref name="usable"/> holds the types the shop sells; a charge for a type outside it is
    /// granted on the tick the type's own item arrives.</summary>
    private static void GrantCharges(object profile, object aspects, Dictionary<string, int> received,
                                     IEnumerable<MicroEntry> usable)
    {
        var session = GameAccess.SessionId;
        if (aspects == null || session == null)
            return;

        var granted = ProfileBinding.MicroChargesGranted(profile, session);
        var changed = false;
        foreach (var micro in usable)
        {
            var wanted = received.TryGetValue(micro.ChargeItem, out var n) ? n : 0;
            var already = granted.TryGetValue(micro.Guid, out var g) ? g : 0;
            var definition = GameAccess.Definition(micro.Guid);
            if (wanted <= already || definition == null)
                continue;
            for (var i = already; i < wanted; i++)
                GameAccess.Call(aspects, "GrantAspectCharge", definition);
            granted[micro.Guid] = wanted;
            changed = true;
        }
        if (changed)
            ProfileBinding.SetMicroChargesGranted(profile, session, granted);
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
    }

    private static Dictionary<string, int> Count(IEnumerable<string> names)
    {
        var counts = new Dictionary<string, int>();
        foreach (var name in names)
            counts[name] = counts.TryGetValue(name, out var n) ? n + 1 : 1;
        return counts;
    }
}
