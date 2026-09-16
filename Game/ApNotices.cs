using System;
using System.Collections.Generic;
using System.Linq;
using Archipelago.MultiClient.Net.Enums;

namespace Reus2Archipelago.Game;

/// <summary>
/// Tells the player in the chat log what Archipelago did: items received from other players, the
/// player's own items found at their locations, and items sent to others. Messages wait until a
/// planet of the bound profile is open, and received items the profile has already been told about
/// are not repeated when the server replays the item stream on connect.
/// </summary>
internal static class ApNotices
{
    /// <summary>More waiting items than this are summed up in one line rather than listed.</summary>
    private const int ListedAtOnce = 3;

    private static readonly SortedDictionary<int, ReceivedItem> Received = new SortedDictionary<int, ReceivedItem>();
    private static readonly List<(string Text, ItemFlags Flags)> Pending = new List<(string, ItemFlags)>();

    /// <summary>A new session replays every item from index 0, so nothing from the last one is kept.</summary>
    internal static void Reset()
    {
        Received.Clear();
        Pending.Clear();
    }

    internal static void ItemReceived(int index, string itemName, string senderName, bool fromSelf, ItemFlags flags)
    {
        Received[index] = new ReceivedItem(itemName, senderName, fromSelf, flags);
    }

    internal static void CheckSent(string itemName, string receiverName, bool forSelf, ItemFlags flags)
    {
        if (forSelf)
            Pending.Add((Text("You found your {0}!", itemName), flags));
        else
            Pending.Add((Text("You sent {0} to {1}.", itemName, receiverName), flags));
    }

    /// <summary>Called every frame on the main thread.</summary>
    internal static void Tick()
    {
        if ((Received.Count == 0 && Pending.Count == 0) || GameAccess.Gameplay == null || !ItemApplier.ProfileBound)
            return;

        AnnounceReceived(GameAccess.CurrentProfile);

        foreach (var message in Pending)
            ApChat.Push(message.Text, StyleFor(message.Flags), BadgeFor(message.Flags));
        Pending.Clear();
    }

    /// <summary>The chat log's filters are positive, minor, warnings and crises: items that help are
    /// positive, filler is minor, and a trap is a warning. The badge on the portrait is what tells
    /// progression from useful.</summary>
    private static ChatMessageStyle StyleFor(ItemFlags flags)
    {
        if ((flags & ItemFlags.Trap) != 0)
            return ChatMessageStyle.Warning;
        if ((flags & (ItemFlags.Advancement | ItemFlags.NeverExclude)) != 0)
            return ChatMessageStyle.GoodImportant;
        return ChatMessageStyle.Neutral;
    }

    private static ApChat.Badge BadgeFor(ItemFlags flags)
    {
        if ((flags & ItemFlags.Trap) != 0)
            return ApChat.Badge.Trap;
        if ((flags & ItemFlags.Advancement) != 0)
            return ApChat.Badge.Progression;
        if ((flags & ItemFlags.NeverExclude) != 0)
            return ApChat.Badge.Useful;
        return ApChat.Badge.Filler;
    }

    private static void AnnounceReceived(object profile)
    {
        if (Received.Count == 0)
            return;

        var announced = ProfileBinding.ItemsAnnounced(profile);
        // The player's own items were announced as they were found.
        var fresh = Received
            .Where(pair => pair.Key >= announced && !pair.Value.FromSelf)
            .Select(pair => pair.Value)
            .ToList();

        if (fresh.Count > ListedAtOnce)
        {
            // Classed as the best of what it stands for; a trap only when nothing among them helps.
            var flags = fresh.Aggregate(ItemFlags.None, (all, item) => all | item.Flags);
            if ((flags & ItemFlags.Advancement) != 0)
                flags = ItemFlags.Advancement;
            else if ((flags & ItemFlags.NeverExclude) != 0)
                flags = ItemFlags.NeverExclude;
            Pending.Add((Text("{0} items arrived while you were away.", fresh.Count), flags));
        }
        else
        {
            foreach (var item in fresh)
                Pending.Add((Text("{0} sent you {1}!", item.SenderName, item.ItemName), item.Flags));
        }

        ProfileBinding.SetItemsAnnounced(profile, Math.Max(announced, Received.Keys.Last() + 1));
        Received.Clear();
    }

    /// <summary>Goes through the game's translation, so reus2_text_overrides.tsv can reword these
    /// lines by their English text.</summary>
    private static string Text(string english, params object[] args)
    {
        try
        {
            return MacroSettings.L(english, args);
        }
        catch
        {
            return string.Format(english, args);
        }
    }

    private readonly struct ReceivedItem
    {
        internal ReceivedItem(string itemName, string senderName, bool fromSelf, ItemFlags flags)
        {
            ItemName = itemName ?? "an item";
            SenderName = senderName ?? "Someone";
            FromSelf = fromSelf;
            Flags = flags;
        }

        internal string ItemName { get; }
        internal string SenderName { get; }
        internal bool FromSelf { get; }
        internal ItemFlags Flags { get; }
    }
}
