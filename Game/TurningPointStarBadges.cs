using System.Collections.Generic;
using Reus2Archipelago.Archipelago;
using UnityEngine;
using UnityEngine.UI;

namespace Reus2Archipelago.Game;

/// <summary>Replaces only the unearned, dark Turning Point stars with the Archipelago logo.</summary>
internal static class TurningPointStarBadges
{
    private static readonly Dictionary<Image, Sprite> Originals = new Dictionary<Image, Sprite>();

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

        if (!dirty)
            return;
        dirty = false;

        foreach (var display in UnityEngine.Object.FindObjectsOfType<TurningPointDisplayUI>())
            Refresh(display);
    }

    internal static void Refresh(object display)
    {
        if (!(display is TurningPointDisplayUI turningPointDisplay)
            || turningPointDisplay.starRatingUI == null
            || turningPointDisplay.starRatingUI.stars == null)
            return;

        var bound = ApState.HasSession && ItemApplier.ProfileBound && ApData.Instance != null;
        var turningPoint = GameAccess.Get(GameAccess.Get(turningPointDisplay, "props"), "turningPoint");
        var guid = bound ? GameAccess.DefinitionId(GameAccess.Get(turningPoint, "Definition")) : null;
        var stars = turningPointDisplay.starRatingUI.stars;
        for (var index = 0; index < stars.Length; index++)
        {
            var star = stars[index];
            var entry = guid == null ? null : ApData.Instance.TurningPointStarForTurningPoint(guid, index + 1);
            var open = bound && entry != null && ApState.IsOpen(entry.Location);
            Show(star?.shadow, open);
        }
    }

    private static void Show(Image image, bool badge)
    {
        if (image == null)
            return;

        if (!Originals.TryGetValue(image, out var original))
        {
            if (!badge)
                return;
            Originals[image] = original = image.sprite;
        }

        if (!badge)
        {
            image.sprite = original;
            return;
        }

        var logo = ApIcon.Load();
        if (logo != null)
            image.sprite = logo;
    }
}
