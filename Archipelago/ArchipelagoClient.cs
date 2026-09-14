using System;
using System.Collections.Generic;
using Archipelago.MultiClient.Net;
using Archipelago.MultiClient.Net.Enums;
using Archipelago.MultiClient.Net.Helpers;

namespace Reus2Archipelago.Archipelago;

/// <summary>
/// Owns the Archipelago session. Game-facing code goes through <see cref="ApState"/>
/// so network callbacks never touch Unity objects directly.
/// </summary>
internal sealed class ArchipelagoClient
{
    internal const string GameName = "Reus 2";

    private ArchipelagoSession session;

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
            ApState.AdoptSession(this, CheckedLocationNames(), slot, session.RoomState.Seed, success.SlotData);

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

    internal void SendGoal()
    {
        if (Connected)
            session.SetGoalAchieved();
    }

    private IEnumerable<string> CheckedLocationNames()
    {
        foreach (var id in session.Locations.AllLocationsChecked)
        {
            var name = session.Locations.GetLocationNameFromId(id, GameName);
            if (!string.IsNullOrWhiteSpace(name))
                yield return name;
        }
    }

    private void OnItemReceived(IReceivedItemsHelper helper)
    {
        while (helper.Any())
        {
            var item = helper.DequeueItem();
            ApState.QueueReceivedItem(this, helper.Index, item.ItemName);
        }
    }

    private void OnSocketClosed(string reason)
    {
        ApState.QueueDisconnect(this, reason);
    }
}
