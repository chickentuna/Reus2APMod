using System;
using System.Collections.Generic;
using System.Linq;
using Archipelago.MultiClient.Net;
using Archipelago.MultiClient.Net.Enums;
using Archipelago.MultiClient.Net.Helpers;
using Archipelago.MultiClient.Net.Models;

namespace Reus2Archipelago.Archipelago;

/// <summary>
/// Owns the Archipelago session. Game-facing code goes through <see cref="ApState"/>
/// so network callbacks never touch Unity objects directly.
/// </summary>
internal sealed class ArchipelagoClient
{
    internal const string GameName = "Reus 2";

    private ArchipelagoSession session;
    private int nextItemIndex;

    /// <summary>What each of this slot's locations holds, scouted once at connect so a check can
    /// name its item without a round trip. Filled from a background task, hence the lock.</summary>
    private readonly Dictionary<long, ScoutedItemInfo> scouted = new Dictionary<long, ScoutedItemInfo>();
    private readonly object scoutLock = new object();

    internal bool Connected => session?.Socket?.Connected == true;

    internal string Connect(string host, int port, string slot, string password)
    {
        Disconnect();

        try
        {
            session = ArchipelagoSessionFactory.CreateSession(host, port);
            var result = session.TryConnectAndLogin(
                GameName,
                slot,
                ItemsHandlingFlags.AllItems,
                password: string.IsNullOrWhiteSpace(password) ? null : password);

            if (result is LoginFailure failure)
            {
                var reason = string.Join("; ", failure.Errors);
                session.Socket.DisconnectAsync();
                session = null;
                return string.IsNullOrWhiteSpace(reason) ? "Connection refused" : reason;
            }

            var success = (LoginSuccessful)result;
            ApState.AdoptSession(this, LocationNames(session.Locations.AllLocations), LocationNames(session.Locations.AllLocationsChecked),
                slot, session.RoomState.Seed, success.SlotData);

            nextItemIndex = 0;
            ScoutSeed();
            session.Items.ItemReceived += OnItemReceived;
            session.Socket.SocketClosed += OnSocketClosed;

            // Items received before the event subscription are retained by the helper.
            OnItemReceived(session.Items);

            Plugin.Log.LogInfo($"Connected to {host}:{port} as {slot}.");
            return null;
        }
        catch (Exception exception)
        {
            try
            {
                session?.Socket?.DisconnectAsync();
            }
            catch
            {
                // Preserve the original connection error.
            }

            session = null;
            ApState.ReleaseSession(this);
            return exception.GetBaseException().Message;
        }
    }

    internal void Disconnect()
    {
        var activeSession = session;
        if (activeSession == null)
            return;

        session = null;
        try
        {
            activeSession.Items.ItemReceived -= OnItemReceived;
            activeSession.Socket.SocketClosed -= OnSocketClosed;
            activeSession.Socket.DisconnectAsync();
        }
        catch (Exception exception)
        {
            Plugin.Log.LogWarning($"Error while disconnecting: {exception.Message}");
        }

        ApState.ReleaseSession(this);
    }

    internal bool SendCheck(string locationName)
    {
        if (!Connected)
            return false;

        var id = session.Locations.GetLocationIdFromName(GameName, locationName);
        if (id <= 0)
        {
            Plugin.Log.LogError(
                $"Unknown location '{locationName}'; the client and AP world names may differ.");
            return false;
        }

        session.Locations.CompleteLocationChecks(id);
        return true;
    }

    /// <summary>What a location holds and who it is for. False before the scout lands, and for a
    /// location this slot does not own.</summary>
    internal bool TryDescribeCheck(string locationName, out string itemName, out string receiverName,
        out bool forSelf, out ItemFlags flags)
    {
        itemName = null;
        receiverName = null;
        forSelf = false;
        flags = ItemFlags.None;

        if (!Connected)
            return false;

        var id = session.Locations.GetLocationIdFromName(GameName, locationName);
        ScoutedItemInfo item;
        lock (scoutLock)
        {
            if (id <= 0 || !scouted.TryGetValue(id, out item))
                return false;
        }

        itemName = item.ItemDisplayName;
        var receiver = item.Player;
        receiverName = string.IsNullOrWhiteSpace(receiver?.Alias) ? receiver?.Name : receiver.Alias;
        forSelf = receiver != null && receiver.Slot == session.ConnectionInfo.Slot;
        flags = item.Flags;
        return true;
    }

    /// <summary>Asks the server what every location of this slot holds. HintCreationPolicy.None
    /// keeps it off the hint list, so nothing is announced to the other players.</summary>
    private void ScoutSeed()
    {
        lock (scoutLock)
            scouted.Clear();

        var locations = session.Locations.AllLocations.ToArray();
        if (locations.Length == 0)
            return;

        session.Locations.ScoutLocationsAsync(HintCreationPolicy.None, locations)
            .ContinueWith(task =>
            {
                if (task.IsFaulted || task.Result == null)
                {
                    Plugin.Log.LogWarning(
                        $"Scouting the seed failed; checks cannot name their item: {task.Exception?.GetBaseException().Message}");
                    return;
                }

                lock (scoutLock)
                    foreach (var entry in task.Result)
                        scouted[entry.Key] = entry.Value;
                Plugin.Log.LogInfo($"Scouted {task.Result.Count} locations.");
            });
    }

    internal void SendGoal()
    {
        if (Connected)
            session.SetGoalAchieved();
    }

    private IEnumerable<string> LocationNames(IEnumerable<long> ids)
    {
        foreach (var id in ids)
        {
            var name = session.Locations.GetLocationNameFromId(id, GameName);
            if (!string.IsNullOrWhiteSpace(name))
                yield return name;
        }
    }

    private void OnItemReceived(IReceivedItemsHelper helper)
    {
        // helper.Index is the number of items received, which is the same for every item in
        // this loop, so the position each one is dequeued at is what tells two copies apart.
        while (helper.Any())
        {
            var item = helper.DequeueItem();
            var sender = item.Player;
            var fromOwnSlot = sender != null && sender.Slot == session?.ConnectionInfo.Slot;
            // Only an item at one of this slot's locations was found here; the server's /send and the
            // start inventory come from no location (-1 and -2) and name this slot as the sender.
            ApState.QueueReceivedItem(this, nextItemIndex++, item.ItemName,
                fromOwnSlot ? null : string.IsNullOrWhiteSpace(sender?.Alias) ? sender?.Name : sender.Alias,
                fromOwnSlot && item.LocationId > 0,
                item.Flags);
        }
    }

    private void OnSocketClosed(string reason)
    {
        ApState.QueueDisconnect(this, reason);
    }
}
