using System.Collections.Generic;
using System.Linq;
using Reus2Archipelago.Archipelago;
using UnityEngine;

namespace Reus2Archipelago.Game;

/// <summary>
/// Marks with the Archipelago logo the icon of every bioticum that still has a location of this
/// seed left to send: its build, a combo rule, or all its combos. Every biotica UI draws from the
/// definition's placeholderSprite, so swapping that one field badges the build menu, draft cards,
/// tooltips and slot icons at once. Vanilla icons come back when the profile is not the slot's.
/// </summary>
internal static class BioticaBadges
{
    private static readonly Dictionary<BioticumDefinition, Sprite> Originals = new Dictionary<BioticumDefinition, Sprite>();
    private static readonly Dictionary<BioticumDefinition, Sprite> Badged = new Dictionary<BioticumDefinition, Sprite>();

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
        foreach (var entry in ApData.Instance.Biotica)
        {
            if (GameAccess.Definition(entry.Guid) is not BioticumDefinition definition)
                continue;

            var open = bound && LocationsOf(entry).Any(ApState.IsOpen);
            Show(definition, open);
            if (open)
                badged++;
        }

        Plugin.Log.LogInfo($"Biotica icons: {badged} marked with open Archipelago locations.");
    }

    private static IEnumerable<string> LocationsOf(BioticumEntry entry)
    {
        yield return entry.BuildLocation;
        yield return entry.AllCombosLocation;
        foreach (var rule in entry.Rules)
            yield return rule.Location;
    }

    private static void Show(BioticumDefinition definition, bool badge)
    {
        if (!Originals.TryGetValue(definition, out var original))
        {
            if (!badge)
                return;
            Originals[definition] = original = definition.placeholderSprite;
        }

        if (!badge)
        {
            definition.placeholderSprite = original;
            return;
        }

        if (!Badged.TryGetValue(definition, out var sprite))
            Badged[definition] = sprite = ApIcon.WithLogo(original);

        // A sprite that could not be copied keeps its vanilla icon.
        definition.placeholderSprite = sprite != null ? sprite : original;
    }
}
