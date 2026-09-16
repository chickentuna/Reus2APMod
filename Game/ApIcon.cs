using System;
using System.IO;
using System.Reflection;
using UnityEngine;

namespace Reus2Archipelago.Game;

/// <summary>
/// Puts the Archipelago icon on the draft point resource, which every draft count and draft button
/// draws from, since a draft is an Archipelago location now.
/// </summary>
internal static class ApIcon
{
    private const string ResourceName = "Reus2Archipelago.archipelago.png";

    private static Sprite sprite;
    private static bool applied;

    internal static void Apply()
    {
        if (applied)
            return;
        var draftPoints = GameAccess.Resource("DraftPoints");
        if (draftPoints == null || Load() == null)
            return;

        applied = true;
        // sprite is private on Stat, which PlayerResource derives from, so the write goes through
        // GameAccess, which walks base types.
        GameAccess.Set(draftPoints, "sprite", sprite);
        Plugin.Log.LogInfo($"Draft points now show the Archipelago icon: "
                           + $"{GameAccess.Get(draftPoints, "sprite")}");
    }

    internal static Sprite Load()
    {
        if (sprite != null)
            return sprite;
        try
        {
            using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(ResourceName);
            if (stream == null)
            {
                Plugin.Log.LogWarning($"Embedded resource {ResourceName} is missing; rebuild the plugin.");
                return null;
            }
            using var memory = new MemoryStream();
            stream.CopyTo(memory);

            var texture = new Texture2D(2, 2, TextureFormat.RGBA32, mipChain: false);
            if (!texture.LoadImage(memory.ToArray()))
            {
                Plugin.Log.LogWarning("The Archipelago icon could not be decoded.");
                return null;
            }
            texture.hideFlags = HideFlags.HideAndDontSave;
            sprite = Sprite.Create(texture, new Rect(0f, 0f, texture.width, texture.height),
                new Vector2(0.5f, 0.5f), 100f);
            sprite.hideFlags = HideFlags.HideAndDontSave;
            return sprite;
        }
        catch (Exception e)
        {
            Plugin.Log.LogError($"Loading the Archipelago icon failed: {e}");
            return null;
        }
    }

    /// <summary>The icon with a dot of that colour in its lower right corner, ringed in a dark
    /// outline so it reads against the logo's own colours.</summary>
    internal static Sprite WithBadge(Color color)
    {
        var source = Load()?.texture;
        if (source == null)
            return null;

        try
        {
            var width = source.width;
            var height = source.height;
            var pixels = source.GetPixels32();

            // Texture rows start at the bottom, so a low y is the lower edge.
            var centerX = width * 0.79f;
            var centerY = height * 0.21f;
            var radius = width * 0.15f;
            var outline = width * 0.05f;
            var ring = new Color(0.08f, 0.08f, 0.08f, 1f);

            for (var y = 0; y < height; y++)
            {
                for (var x = 0; x < width; x++)
                {
                    var distance = Mathf.Sqrt((x - centerX) * (x - centerX) + (y - centerY) * (y - centerY));
                    var cover = Mathf.Clamp01(radius + outline - distance + 0.5f);
                    if (cover <= 0f)
                        continue;

                    // One pixel of blend between fill and ring, and between ring and logo.
                    var fill = Color.Lerp(ring, color, Mathf.Clamp01(radius - distance + 0.5f));
                    var index = y * width + x;
                    pixels[index] = Color.Lerp(pixels[index], fill, cover);
                }
            }

            var texture = new Texture2D(width, height, TextureFormat.RGBA32, mipChain: false)
            {
                hideFlags = HideFlags.HideAndDontSave,
            };
            texture.SetPixels32(pixels);
            texture.Apply();

            var badged = Sprite.Create(texture, new Rect(0f, 0f, width, height), new Vector2(0.5f, 0.5f), 100f);
            badged.hideFlags = HideFlags.HideAndDontSave;
            return badged;
        }
        catch (Exception e)
        {
            Plugin.Log.LogError($"Drawing the icon badge failed: {e}");
            return Load();
        }
    }

}
