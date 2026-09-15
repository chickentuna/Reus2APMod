using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;

namespace Reus2Archipelago.Game;

/// <summary>
/// What the profile and the running planet hold right now: unlock counts, the shop options the
/// profile carries, the parameters the session was started with, the aspect inventory, and how many
/// biotica GameplayController.GetAllAvailableBiotica returns. Read-only throughout.
/// </summary>
internal static class StateSnapshot
{
    internal static void Dump(string reason)
    {
        try
        {
            ProbeLog.Write($"=== state ({reason}) ===");
            foreach (var line in Lines())
                ProbeLog.Write($"  {line}");
            ProbeLog.Write("=== end of state ===");
        }
        catch (Exception e)
        {
            ProbeLog.Fault("state snapshot", e);
        }
    }

    private static IEnumerable<string> Lines()
    {
        var profile = GameAccess.CurrentProfile;
        foreach (var line in Profile(profile))
            yield return line;

        var session = GameAccess.Get(GameAccess.SessionContext, "CurrentSession");
        if (session == null)
        {
            yield return "session: none (outside a planet)";
            yield break;
        }
        foreach (var line in Session(session))
            yield return line;
    }

    private static IEnumerable<string> Profile(object profile)
    {
        if (profile == null)
        {
            yield return "profile: none";
            yield break;
        }

        var name = (profile as UnityEngine.Object)?.name;
        yield return $"profile: id={name} name={GameAccess.Get(profile, "VisualName")}"
                     + $" bound={ItemApplier.ProfileBound} slot={ApState.SlotName ?? "none"}"
                     + $" seed={ApState.Seed ?? "none"} connected={ApState.Connected}";

        yield return $"unlocked: biotica={Naming.Count(GameAccess.Get(profile, "UnlockedBiotica"))}"
                     + $" biomes={Naming.Count(GameAccess.Get(profile, "UnlockedBiomes"))}"
                     + $" characters={Naming.Count(GameAccess.Get(profile, "UnlockedCharacters"))}"
                     + $" giants={Naming.Count(GameAccess.Get(profile, "AvailableGiants"))}"
                     + $" turningPoints={Naming.Count(GameAccess.Get(profile, "UnlockedTurningPoints"))}"
                     + $" abilities={Naming.Count(GameAccess.Get(profile, "UnlockedAbilities"))}";

        yield return $"giant roles: {GiantRoles(profile)}";
        yield return $"shop options ({Naming.Count(GameAccess.Get(profile, "unlockedShopOptions"))}): "
                     + Naming.Each(GameAccess.Get(profile, "unlockedShopOptions"));
        yield return $"eras: {Naming.Each(GameAccess.Get(profile, "UnlockedEras"))}"
                     + $" | finalEraSelected={GameAccess.Get(profile, "finalEraSelected")}"
                     + $" | profileLevel={GameAccess.Get(profile, "ProfileLevel")}"
                     + $" | apexUnlockPoints={GameAccess.Get(profile, "ApexUnlockPoints")}";
    }

    /// <summary>Each giant the profile holds data for, with the roles unlocked on it. A giant with
    /// no unlocked role is locked.</summary>
    private static string GiantRoles(object profile)
    {
        if (!(GameAccess.Get(profile, "giantData") is IEnumerable giants))
            return "unreadable";
        var entries = giants.Cast<object>()
            .Select(g => $"{Naming.Of(GameAccess.Get(g, "giantDefinition"))}"
                         + $"={Naming.Each(GameAccess.Get(g, "unlockedRoles"))}")
            .ToList();
        return entries.Count == 0 ? "none" : string.Join(" | ", entries);
    }

    private static IEnumerable<string> Session(object session)
    {
        var parameters = GameAccess.Get(session, "StartParameters");
        yield return $"session: id={GameAccess.SessionId} finished={GameAccess.Get(session, "IsFinished")}"
                     + $" planetIsLost={GameAccess.Get(session, "PlanetIsLost")}"
                     + $" playMode={GameAccess.Get(parameters, "playMode")}"
                     + $" draftMode={GameAccess.Get(parameters, "draftMode")}"
                     + $" horizonMode={GameAccess.Get(parameters, "horizonMode")}"
                     + $" finalEra={GameAccess.Get(parameters, "finalEra")}";

        var scenario = GameAccess.Get(session, "Scenario");
        var scenarioDefinition = GameAccess.Get(scenario, "BaseDefinition");
        yield return $"scenario: {Naming.Of(scenarioDefinition)}"
                     + $" isTutorial={GameAccess.Get(scenarioDefinition, "isTutorial")}"
                     + $" draftsLocked={GameAccess.Get(scenario, "draftsLocked")}"
                     + $" shopLocked={GameAccess.Get(scenario, "shopLocked")}"
                     + $" bioticaLocked={GameAccess.Get(scenario, "bioticaLocked")}";

        var gameplay = GameAccess.Gameplay;
        if (gameplay == null)
        {
            yield return "gameplay: no GameplayController";
            yield break;
        }

        yield return $"aspects: {AspectInventory(GameAccess.Get(gameplay, "aspectController"))}";
        yield return $"shop buyCount: {BuyCount(GameAccess.Get(gameplay, "gameplayShop"))}";
        yield return $"GetAllAvailableBiotica: {AvailableBiotica(gameplay)}";
    }

    /// <summary>Every aspect type in the inventory with its charge count. An entry that does not
    /// use charges keeps the -1 it was created with.</summary>
    private static string AspectInventory(object aspects)
    {
        if (!(GameAccess.Get(aspects, "aspectInventory") is IEnumerable inventory))
            return "unreadable";
        var entries = Pairs(inventory)
            .Select(pair => $"{Naming.Of(pair.Key)} charges={GameAccess.Get(pair.Value, "Charges")}"
                            + $" usesCharges={GameAccess.Get(pair.Value, "UsesCharges")}")
            .ToList();
        return entries.Count == 0 ? "none" : string.Join(" | ", entries);
    }

    private static string BuyCount(object shop)
    {
        if (!(GameAccess.Get(shop, "buyCount") is IEnumerable purchases))
            return "unreadable";
        var entries = Pairs(purchases).Select(pair => $"{Naming.Of(pair.Key)}={pair.Value}").ToList();
        return entries.Count == 0 ? "nothing bought" : string.Join(", ", entries);
    }

    /// <summary>GenericDictionary implements only the generic IDictionary, so its entries are read
    /// through the Key and Value of each KeyValuePair it yields.</summary>
    private static IEnumerable<(object Key, object Value)> Pairs(IEnumerable dictionary)
        => dictionary.Cast<object>()
            .Select(entry => (GameAccess.Get(entry, "Key"), GameAccess.Get(entry, "Value")));

    /// <summary>ProgressionPatches replaces this result on a bound profile, so the number is what
    /// the game is actually offered, not what vanilla would have offered.</summary>
    private static string AvailableBiotica(object gameplay)
    {
        var available = GameAccess.Call(gameplay, "GetAllAvailableBiotica");
        var count = Naming.Count(available);
        return count < 0 ? "unreadable" : $"{count} biotica (the value after the mod's postfix)";
    }
}
