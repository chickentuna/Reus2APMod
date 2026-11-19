using System.Collections.Generic;
using System.Linq;
using Reus2Archipelago.Archipelago;
using UnityEngine;

namespace Reus2Archipelago.Game;

/// <summary>
/// Marks open bioticum locations with the Archipelago logo, and uncounted goal biotica without a
/// location with a black logo. Every biotica UI draws from the definition's placeholderSprite, so
/// swapping that one field badges the build menu, draft cards, tooltips and slot icons at once.
/// Vanilla icons come back when the profile is not the slot's.
/// </summary>
internal static class BioticaBadges
{
    private static readonly Dictionary<BioticumDefinition, Sprite> Originals = new Dictionary<BioticumDefinition, Sprite>();
    private static readonly Dictionary<BioticumDefinition, Sprite> Badged = new Dictionary<BioticumDefinition, Sprite>();
    private static readonly Dictionary<BioticumDefinition, Sprite> Blackened = new Dictionary<BioticumDefinition, Sprite>();

    private static bool dirty = true;
    private static bool lastBound;

    internal static void MarkDirty() => dirty = true;

    /// <summary>Called every frame on the main thread.</summary>
    internal static void Tick()
    {
        var bound = ApState.HasSession && ItemApplier.ProfileBound;
        if (bound != lastBound)
        {
            lastBound = bound;
            dirty = true;
        }

        if (!dirty || ApData.Instance == null || !GameAccess.DefinitionsReady)
            return;
        dirty = false;

        var badged = 0;
        var goalOnly = 0;
        var countedGoalBiotica = bound ? ApState.GoalBioticaCounted() : new HashSet<string>();
        foreach (var entry in ApData.Instance.Biotica)
        {
            if (GameAccess.Definition(entry.Guid) is not BioticumDefinition definition)
                continue;

            var locations = LocationsOf(entry).ToList();
            var open = bound && locations.Any(ApState.IsOpen);
            var inGoalWithoutLocation = bound
                && entry.Level <= ApState.SlotInt("max_biotica_level", int.MaxValue)
                && ApState.IsDlcEnabled(entry.Dlc)
                && !locations.Any(ApState.HasLocation)
                && !countedGoalBiotica.Contains(entry.Name);
            Show(definition, open, inGoalWithoutLocation);
            if (open)
                badged++;
            else if (inGoalWithoutLocation)
                goalOnly++;
        }

        Plugin.Log.LogInfo($"Biotica icons: {badged} marked with open Archipelago locations, "
                            + $"{goalOnly} marked as goal-only.");
    }

    private static IEnumerable<string> LocationsOf(BioticumEntry entry)
    {
        yield return entry.BuildLocation;
        yield return entry.AllCombosLocation;
    }

    private static void Show(BioticumDefinition definition, bool badge, bool blackened)
    {
        if (!Originals.TryGetValue(definition, out var original))
        {
            if (!badge && !blackened)
                return;
            Originals[definition] = original = definition.placeholderSprite;
        }

        if (!badge && !blackened)
        {
            definition.placeholderSprite = original;
            return;
        }

        var cache = blackened ? Blackened : Badged;
        if (!cache.TryGetValue(definition, out var sprite))
            cache[definition] = sprite = ApIcon.WithLogo(original, blackened);

        // A sprite that could not be copied keeps its vanilla icon.
        definition.placeholderSprite = sprite != null ? sprite : original;
    }
}
