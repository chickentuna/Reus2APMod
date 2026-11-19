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

    /// <summary>
    /// A copy of a game sprite with a small Archipelago logo on a dark disc, or a black logo on a
    /// light disc when blackened, in its upper right corner. The sprite is copied through a render
    /// texture because game sprites sit in atlases whose textures are not readable.
    /// </summary>
    internal static Sprite WithLogo(Sprite original, bool blackened = false)
    {
        var logo = Load()?.texture;
        if (original == null || logo == null)
            return null;

        RenderTexture target = null;
        var previous = RenderTexture.active;
        try
        {
            var rect = original.textureRect;
            var source = original.texture;
            var width = Mathf.RoundToInt(rect.width);
            var height = Mathf.RoundToInt(rect.height);

            target = RenderTexture.GetTemporary(width, height, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Default);
            Graphics.Blit(source, target,
                new Vector2(rect.width / source.width, rect.height / source.height),
                new Vector2(rect.x / source.width, rect.y / source.height));

            var texture = new Texture2D(width, height, TextureFormat.RGBA32, mipChain: false)
            {
                hideFlags = HideFlags.HideAndDontSave,
                filterMode = source.filterMode,
                wrapMode = TextureWrapMode.Clamp,
            };
            RenderTexture.active = target;
            texture.ReadPixels(new Rect(0, 0, width, height), 0, 0);
            var pixels = texture.GetPixels32();

            // Texture rows start at the bottom, so the upper edge is a high y.
            var size = Mathf.Min(width, height);
            var radius = size * 0.22f;
            var centerX = width - radius - size * 0.02f;
            var centerY = height - radius - size * 0.02f;
            var logoRadius = radius * 0.82f;
            var disc = blackened
                ? new Color(1f, 1f, 1f, 0.95f)
                : new Color(0.08f, 0.08f, 0.08f, 0.9f);

            for (var y = Mathf.Max(0, Mathf.FloorToInt(centerY - radius - 1)); y < height; y++)
            {
                for (var x = Mathf.Max(0, Mathf.FloorToInt(centerX - radius - 1)); x < width; x++)
                {
                    var dx = x - centerX;
                    var dy = y - centerY;
                    var cover = Mathf.Clamp01(radius - Mathf.Sqrt(dx * dx + dy * dy) + 0.5f);
                    if (cover <= 0f)
                        continue;

                    var index = y * width + x;
                    Color pixel = Color.Lerp(pixels[index], disc, disc.a * cover);
                    pixel.a = Mathf.Max(pixels[index].a / 255f, cover);

                    var u = 0.5f + dx / (2f * logoRadius);
                    var v = 0.5f + dy / (2f * logoRadius);
                    if (u >= 0f && u <= 1f && v >= 0f && v <= 1f)
                    {
                        var mark = logo.GetPixelBilinear(u, v);
                        var markColor = blackened ? Color.black : new Color(mark.r, mark.g, mark.b, 1f);
                        pixel = Color.Lerp(pixel, markColor, mark.a);
                    }

                    pixels[index] = pixel;
                }
            }

            texture.SetPixels32(pixels);
            texture.Apply();

            var pivot = new Vector2(original.pivot.x / original.rect.width, original.pivot.y / original.rect.height);
            var badged = Sprite.Create(texture, new Rect(0f, 0f, width, height), pivot, original.pixelsPerUnit,
                0, SpriteMeshType.FullRect, original.border);
            badged.name = original.name + " (Archipelago)";
            badged.hideFlags = HideFlags.HideAndDontSave;
            return badged;
        }
        catch (Exception e)
        {
            Plugin.Log.LogWarning($"Could not badge sprite {original.name}: {e.Message}");
            return null;
        }
        finally
        {
            RenderTexture.active = previous;
            if (target != null)
                RenderTexture.ReleaseTemporary(target);
        }
    }
}
