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
        Patching.Apply(harmony, "ChatMessage", "SerializeData", typeof(ApChat), postfix: nameof(AfterSerializeData));
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

    /// <summary>A save stores the leader as a definition ID, which the definition database cannot
    /// resolve on load, so the leader comes back null and the portrait would throw. It is restored
    /// here from the city index.</summary>
    private static void AfterSerializeData(ChatMessage __instance)
    {
        var memory = __instance.cityMemory;
        if (memory == null || memory.leaderCharacter != null)
            return;

        var badge = (Badge)(BaseCityIndex - memory.cityIndex);
        if (badge >= Badge.None && badge <= Badge.Trap)
            memory.leaderCharacter = Leader(badge);
    }
}
