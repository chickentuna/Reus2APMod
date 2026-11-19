using System.Collections.Generic;
using Reus2Archipelago.Archipelago;
using UnityEngine;

namespace Reus2Archipelago.Game;

/// <summary>Marks project icons whose Archipelago checks are still open.</summary>
internal static class ProjectBadges
{
    private static readonly Dictionary<ProjectDefinition, Sprite> Originals =
        new Dictionary<ProjectDefinition, Sprite>();
    private static readonly Dictionary<ProjectDefinition, Sprite> Badged =
        new Dictionary<ProjectDefinition, Sprite>();

    private static bool dirty = true;
    private static bool lastBound;

    internal static void MarkDirty() => dirty = true;

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
        foreach (var entry in ApData.Instance.Projects)
        {
            if (GameAccess.Definition(entry.Guid) is not ProjectDefinition definition)
                continue;

            var open = bound && ApState.IsOpen(entry.Location);
            Show(definition, open);
            if (open)
                badged++;
        }

        Plugin.Log.LogInfo($"Project icons: {badged} marked with open Archipelago locations.");
    }

    private static void Show(ProjectDefinition definition, bool badge)
    {
        if (!Originals.TryGetValue(definition, out var original))
        {
            if (!badge)
                return;
            Originals[definition] = original = definition.UISprite;
        }

        if (!badge)
        {
            definition.UISprite = original;
            return;
        }

        if (!Badged.TryGetValue(definition, out var sprite))
            Badged[definition] = sprite = ApIcon.WithLogo(original);

        definition.UISprite = sprite != null ? sprite : original;
    }
}
