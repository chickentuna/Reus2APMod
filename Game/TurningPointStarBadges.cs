using System.Collections.Generic;
using System.Linq;
using Reus2Archipelago.Archipelago;
using UnityEngine;
using UnityEngine.UI;

namespace Reus2Archipelago.Game;

/// <summary>Colors open Turning Point star symbols and marks pictures with unfinished checks.</summary>
internal static class TurningPointStarBadges
{
    private static readonly Dictionary<Image, ImageOriginal> Originals = new Dictionary<Image, ImageOriginal>();
    private static readonly Dictionary<object, Sprite> TurningPointOriginals = new Dictionary<object, Sprite>();
    private static readonly Dictionary<object, Sprite> TurningPointBadges = new Dictionary<object, Sprite>();

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

        if (!dirty || !GameAccess.DefinitionsReady)
            return;
        dirty = false;

        foreach (var display in UnityEngine.Object.FindObjectsOfType<TurningPointDisplayUI>())
            Refresh(display);
        RefreshTurningPointImages(bound);
    }

    internal static void Refresh(object display)
    {
        if (!(display is TurningPointDisplayUI turningPointDisplay)
            || turningPointDisplay.starRatingUI == null
            || turningPointDisplay.starRatingUI.stars == null)
            return;

        var bound = ApState.HasSession && ItemApplier.ProfileBound && ApData.Instance != null;
        var turningPoint = GameAccess.Get(GameAccess.Get(turningPointDisplay, "props"), "turningPoint");
        var definition = GameAccess.Get(turningPoint, "Definition");
        var guid = bound ? GameAccess.DefinitionId(definition) : null;
        var stars = turningPointDisplay.starRatingUI.stars;
        for (var index = 0; index < stars.Length; index++)
        {
            var star = stars[index];
            var entry = guid == null ? null : ApData.Instance.TurningPointStarForTurningPoint(guid, index + 1);
            var open = bound && entry != null && ApState.IsOpen(entry.Location);
            Show(star?.shadow, open);
        }
    }

    private static void RefreshTurningPointImages(bool bound)
    {
        var data = ApData.Instance;
        if (data == null)
            return;

        foreach (var definition in GameAccess.DefinitionsOfType("TurningPointDefinition"))
        {
            var guid = GameAccess.DefinitionId(definition);
            var hasOpenCheck = bound && data.TurningPointStars
                .Where(entry => entry.TurningPointGuid == guid)
                .Any(entry => ApState.IsOpen(entry.Location));
            ShowTurningPointImage(definition, hasOpenCheck);
        }
    }

    private static void ShowTurningPointImage(object definition, bool badge)
    {
        if (!TurningPointOriginals.TryGetValue(definition, out var original))
        {
            original = GameAccess.Get(definition, "fullImage") as Sprite;
            if (original == null)
                return;
            TurningPointOriginals[definition] = original;
        }

        if (!badge)
        {
            GameAccess.Set(definition, "fullImage", original);
            return;
        }

        if (!TurningPointBadges.TryGetValue(definition, out var sprite))
            TurningPointBadges[definition] = sprite = ApIcon.WithLogo(original);
        GameAccess.Set(definition, "fullImage", sprite != null ? sprite : original);
    }

    private static void Show(Image image, bool badge)
    {
        if (image == null)
            return;

        if (!Originals.TryGetValue(image, out var original))
        {
            if (!badge)
                return;
            Originals[image] = original = new ImageOriginal(image.sprite, image.color);
        }

        if (!badge)
        {
            image.sprite = original.Sprite;
            image.color = original.Color;
            return;
        }

        var logo = ApIcon.Load();
        if (logo != null)
        {
            image.sprite = logo;
            image.color = Color.white;
        }
    }

    private readonly struct ImageOriginal
    {
        internal ImageOriginal(Sprite sprite, Color color)
        {
            Sprite = sprite;
            Color = color;
        }

        internal Sprite Sprite { get; }
        internal Color Color { get; }
    }
}
