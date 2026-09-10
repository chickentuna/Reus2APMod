using System.Collections.Concurrent;
using System.Collections.Generic;
using Reus2Archipelago.Archipelago;

namespace Reus2Archipelago;

/// <summary>
/// Main-thread boundary between game patches and the Archipelago network client.
/// Gameplay item handlers will be added here once the AP world contract is fixed.
/// </summary>
internal static class ApState
{
    private static readonly HashSet<string> CheckedLocations = new HashSet<string>();
    private static readonly HashSet<int> ReceivedItemIndexes = new HashSet<int>();
    private static readonly ConcurrentQueue<PendingItem> PendingItems =
        new ConcurrentQueue<PendingItem>();
    private static readonly ConcurrentQueue<PendingDisconnect> PendingDisconnects =
        new ConcurrentQueue<PendingDisconnect>();

    private static ArchipelagoClient client;

    internal static bool Connected => client?.Connected == true;

    internal static void AdoptSession(
        ArchipelagoClient newClient,
        IEnumerable<string> alreadyChecked)
    {
        ClearPending();
        client = newClient;
        CheckedLocations.Clear();
        ReceivedItemIndexes.Clear();

        if (alreadyChecked != null)
        {
            foreach (var location in alreadyChecked)
                CheckedLocations.Add(location);
        }

        Plugin.Log.LogInfo(
            $"Archipelago session adopted with {CheckedLocations.Count} completed checks.");
    }

    internal static void ReleaseSession(ArchipelagoClient source)
    {
        if (!ReferenceEquals(client, source))
            return;

        client = null;
        ClearPending();
    }

    internal static void QueueReceivedItem(
        ArchipelagoClient source,
        int index,
        string itemName)
    {
        PendingItems.Enqueue(new PendingItem(source, index, itemName));
    }

    internal static void QueueDisconnect(ArchipelagoClient source, string reason)
    {
        PendingDisconnects.Enqueue(new PendingDisconnect(source, reason));
    }

    /// <summary>Called by the plugin Update loop on Unity's main thread.</summary>
    internal static void DrainNetworkEvents()
    {
        while (PendingDisconnects.TryDequeue(out var disconnect))
        {
            if (!ReferenceEquals(client, disconnect.Source))
                continue;

            Plugin.Log.LogWarning(
                $"Archipelago connection closed: {disconnect.Reason ?? "unknown reason"}");
            client = null;
        }

        while (PendingItems.TryDequeue(out var item))
        {
            if (!ReferenceEquals(client, item.Source)
                || !ReceivedItemIndexes.Add(item.Index))
                continue;

            // Receiving is intentionally non-destructive in this foundation. Later item
            // handlers will rebuild game unlocks from this replayed, indexed stream.
            Plugin.Log.LogInfo(
                $"Received item #{item.Index}: {item.Name ?? "<unnamed>"} "
                + "(no gameplay handler yet).");
        }
    }

    internal static bool SendCheck(string locationName)
    {
        if (string.IsNullOrWhiteSpace(locationName)
            || CheckedLocations.Contains(locationName)
            || client == null)
            return false;

        if (!client.SendCheck(locationName))
            return false;

        CheckedLocations.Add(locationName);
        Plugin.Log.LogInfo($"Sent check: {locationName}");
        return true;
    }

    internal static void SendGoal()
    {
        client?.SendGoal();
    }

    private static void ClearPending()
    {
        while (PendingItems.TryDequeue(out _))
        {
        }

        while (PendingDisconnects.TryDequeue(out _))
        {
        }
    }

    private readonly struct PendingItem
    {
        internal PendingItem(ArchipelagoClient source, int index, string name)
        {
            Source = source;
            Index = index;
            Name = name;
        }

        internal ArchipelagoClient Source { get; }
        internal int Index { get; }
        internal string Name { get; }
    }

    private readonly struct PendingDisconnect
    {
        internal PendingDisconnect(ArchipelagoClient source, string reason)
        {
            Source = source;
            Reason = reason;
        }

        internal ArchipelagoClient Source { get; }
        internal string Reason { get; }
    }
}
