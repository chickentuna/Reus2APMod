using System;
using System.Collections.Generic;
using HarmonyLib;
using Reus2Archipelago.Patches;
using UnityEngine;

namespace Reus2Archipelago.Game;

/// <summary>
/// Posts Archipelago messages to the planet's chat log under an Archipelago "leader" whose portrait
/// is the Archipelago icon, with a dot in the colour Archipelago gives the item's classification. A
/// chat message keeps only a CityMemory, never the city, so no city has to exist for it.
/// </summary>
internal static class ApChat
{
    internal enum Badge
    {
        None,
        Progression,
        Useful,
        Filler,
        Trap,
    }

    /// <summary>No real city has an index this low, so the messages stay out of every leader's
    /// filter. Each badge takes the index below it, which is how a loaded message finds its leader
    /// again.</summary>
    private const int BaseCityIndex = -1000;

    private const string CityName = "Archipelago";

    /// <summary>The colours Archipelago's own text client gives each classification.</summary>
    private static readonly Dictionary<Badge, Color> BadgeColors = new Dictionary<Badge, Color>
    {
        [Badge.Progression] = new Color32(175, 153, 239, 255),
        [Badge.Useful] = new Color32(109, 139, 232, 255),
        [Badge.Filler] = new Color32(0, 238, 238, 255),
        [Badge.Trap] = new Color32(250, 128, 114, 255),
    };

    private static readonly Dictionary<Badge, CharacterDefinition> Leaders = new Dictionary<Badge, CharacterDefinition>();

    internal static void Install(Harmony harmony)
    {
        Patching.Apply(harmony, "CityMemory", "Serialize", typeof(ApChat), prefix: nameof(SerializeApMemory));
    }

    /// <summary>Adds a message to the chat log. False outside a planet, where there is no log.</summary>
    internal static bool Push(string text, ChatMessageStyle style = ChatMessageStyle.GoodImportant, Badge badge = Badge.None)
    {
        if (GameAccess.Gameplay == null)
            return false;

        try
        {
            var drama = AbbeyBehaviour.SessionContext.GameplayController.dramaController;
            var chatLog = AccessTools.Field(typeof(DramaController), "chatLog").GetValue(drama);
            var createNew = AccessTools.Method(chatLog.GetType(), "CreateNew",
                new[] { typeof(Action<ChatMessage>), typeof(ChatMessage) });

            Action<ChatMessage> fill = message =>
            {
                message.cityMemory = Memory(badge);
                message.style = style;
                message.message = text;
            };
            createNew.Invoke(chatLog, new object[] { fill, null });

            // Raises ChatLogChanged, which is all AddChatMessage does after its delay.
            drama.OnChatMessageTransformed(null);
            return true;
        }
        catch (Exception e)
        {
            Plugin.Log.LogError($"Posting to the chat log failed: {e}");
            return false;
        }
    }

    private static CityMemory Memory(Badge badge) => new CityMemory
    {
        cityIndex = BaseCityIndex - (int)badge,
        cityName = CityName,
        leaderCharacter = Leader(badge),
        cityColor = Color.white,
    };

    /// <summary>A leader made at runtime: the chat log only draws its portraitIcon.</summary>
    private static CharacterDefinition Leader(Badge badge)
    {
        if (Leaders.TryGetValue(badge, out var leader) && leader != null)
            return leader;

        leader = ScriptableObject.CreateInstance<CharacterDefinition>();
        leader.name = $"{CityName} {badge}";
        leader.hideFlags = HideFlags.HideAndDontSave;
        leader.portraitIcon = BadgeColors.TryGetValue(badge, out var color) ? ApIcon.WithBadge(color) : ApIcon.Load();
        Leaders[badge] = leader;
        return leader;
    }

    /// <summary>
    /// CityMemory.Serialize for an Archipelago message, both saving and loading. A save stores a
    /// leader as its definition ID, and the runtime-made leader has none, so the load would throw
    /// looking up an empty ID and fail the whole session. The leader is therefore never written, and
    /// on load it comes back from the city index, which is read first. Every other memory is serialized
    /// as the original does. A save written before this still holds the empty leader, which is skipped
    /// the same way.
    /// </summary>
    private static bool SerializeApMemory(CityMemory __instance, ISerializationArchive archive)
    {
        archive.Serialize("cityIndex", ref __instance.cityIndex);
        archive.Serialize("cityName", ref __instance.cityName);
        var badge = (Badge)(BaseCityIndex - __instance.cityIndex);
        if (badge >= Badge.None && badge <= Badge.Trap)
            __instance.leaderCharacter = Leader(badge);
        else
            archive.SerializeDefinition("leaderCharacter", ref __instance.leaderCharacter);
        archive.Serialize("cityColor", ref __instance.cityColor);
        return false;
    }
}
