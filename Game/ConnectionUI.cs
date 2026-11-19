using UnityEngine;
using UnityEngine.InputSystem;

namespace Reus2Archipelago.Game;

/// <summary>
/// Connection panel, drawn with IMGUI so it needs no prefab and works on any screen. It opens by
/// itself on the main menu while there is no connection, and F1 toggles it.
/// </summary>
internal static class ConnectionUI
{
    private const int WindowId = 0x52455553;
    private const float ReferenceHeight = 1080f;
    private const float AutoShowDelaySeconds = 2f;

    private static readonly Vector2 Size = new Vector2(380f, 270f);

    private static Rect window = new Rect(40f, 40f, Size.x, Size.y);
    private static bool visible;
    private static bool autoShown;
    private static bool loaded;
    private static string status = "";
    private static bool statusIsError;

    private static string host;
    private static string port;
    private static string slot;
    private static string password;

    /// <summary>Called from Update.</summary>
    internal static void Tick()
    {
        var keyboard = Keyboard.current;
        if (keyboard != null && keyboard.f1Key.wasPressedThisFrame)
            visible = !visible;

        if (!autoShown && Time.realtimeSinceStartup > AutoShowDelaySeconds && GameAccess.Gameplay == null)
        {
            autoShown = true;
            if (!Plugin.Client.Connected)
                visible = true;
        }
    }

    /// <summary>Called from OnGUI.</summary>
    internal static void Draw()
    {
        if (!visible)
            return;
        if (!loaded)
        {
            loaded = true;
            host = Plugin.Server.Value;
            port = Plugin.Port.Value.ToString();
            slot = Plugin.Slot.Value;
            password = Plugin.Password.Value;
        }

        var scale = Mathf.Max(1f, Screen.height / ReferenceHeight);
        var matrix = GUI.matrix;
        GUI.matrix = Matrix4x4.Scale(new Vector3(scale, scale, 1f));
        window = GUI.Window(WindowId, window, DrawWindow, "Archipelago");
        GUI.matrix = matrix;
    }

    private static void DrawWindow(int id)
    {
        var connected = Plugin.Client.Connected;

        GUILayout.Space(4f);
        host = LabelledField("Server", host);
        port = LabelledField("Port", port);
        slot = LabelledField("Slot name", slot);
        password = LabelledField("Password", password);

        GUILayout.Space(8f);
        GUILayout.BeginHorizontal();
        if (GUILayout.Button(connected ? "Reconnect" : "Connect", GUILayout.Height(26f)))
            Connect();
        if (connected && GUILayout.Button("Disconnect", GUILayout.Height(26f)))
        {
            Plugin.Client.Disconnect();
            SetStatus("Disconnected.", isError: false);
        }
        if (GUILayout.Button("Close", GUILayout.Width(70f), GUILayout.Height(26f)))
            visible = false;
        GUILayout.EndHorizontal();

        GUILayout.Space(6f);
        if (connected)
        {
            GUILayout.Label($"Connected as {ApState.SlotName}.");
            if (ApState.GoalBuilds > 0)
                GUILayout.Label($"Goal: {ApState.BuiltCount} / {ApState.GoalBuilds} biotica.");
            if (ApState.GoalFinalEras > 0)
                GUILayout.Label($"Discovery at 3 stars: {ApState.FinalErasCompleted} / {ApState.GoalFinalEras}.");
        }
        else if (status.Length > 0)
        {
            var colour = GUI.color;
            GUI.color = statusIsError ? new Color(1f, 0.5f, 0.5f) : Color.white;
            GUILayout.Label(status);
            GUI.color = colour;
        }

        GUILayout.Label("Toggle with F1");
        GUI.DragWindow(new Rect(0f, 0f, Size.x, 20f));
    }

    private static string LabelledField(string label, string value)
    {
        GUILayout.BeginHorizontal();
        GUILayout.Label(label, GUILayout.Width(90f));
        var result = GUILayout.TextField(value ?? "");
        GUILayout.EndHorizontal();
        return result;
    }

    private static void Connect()
    {
        if (string.IsNullOrWhiteSpace(slot))
        {
            SetStatus("Slot name is required.", isError: true);
            return;
        }
        if (!int.TryParse(port, out var portNumber) || portNumber <= 0 || portNumber > 65535)
        {
            SetStatus("Port must be a number between 1 and 65535.", isError: true);
            return;
        }

        var error = Plugin.Connect(host.Trim(), portNumber, slot.Trim(), password);
        if (error != null)
        {
            SetStatus(error, isError: true);
            return;
        }
        SetStatus("Connected.", isError: false);
        visible = false;
    }

    private static void SetStatus(string message, bool isError)
    {
        status = message;
        statusIsError = isError;
        if (isError)
            Plugin.Log.LogWarning($"[connection] {message}");
    }
}
