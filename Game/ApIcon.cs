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

    private static Sprite Load()
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
}
