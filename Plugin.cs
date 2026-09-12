using System;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;
using Reus2Archipelago.Archipelago;

namespace Reus2Archipelago;

[BepInPlugin(MyPluginInfo.PLUGIN_GUID, MyPluginInfo.PLUGIN_NAME, MyPluginInfo.PLUGIN_VERSION)]
public sealed class Plugin : BaseUnityPlugin
{
    internal static ManualLogSource Log { get; private set; }
    internal static ArchipelagoClient Client { get; private set; }

    private ConfigEntry<string> server;
    private ConfigEntry<int> port;
    private ConfigEntry<string> slot;
    private ConfigEntry<string> password;
    private ConfigEntry<bool> autoConnect;
    private ConfigEntry<bool> enableDefinitionDump;
    private ConfigEntry<bool> enableProbes;

    private Harmony harmony;

    private void Awake()
    {
        Log = Logger;
        Client = new ArchipelagoClient();

        server = Config.Bind("Archipelago", "Server", "localhost",
            "Archipelago server hostname.");
        port = Config.Bind("Archipelago", "Port", 38281,
            "Archipelago server port.");
        slot = Config.Bind("Archipelago", "Slot", "",
            "Player slot name. Leave empty to disable auto-connect.");
        password = Config.Bind("Archipelago", "Password", "",
            "Optional room password.");
        autoConnect = Config.Bind("Archipelago", "AutoConnect", true,
            "Connect at startup when a slot name is configured.");

        enableDefinitionDump = Config.Bind("Diagnostics", "EnableDefinitionDump", false,
            "Write the live definition catalogue to JSON once it is available.");
        enableProbes = Config.Bind("Diagnostics", "EnableProbes", true,
            "Log every milestone, era feat and unlock as it happens, without changing progression. "
            + "Writes BepInEx/plugins/reus2_probe.log.");
        DefinitionDumper.MaxDepth = Config.Bind("Dump", "MaxDepth", 4,
            "How deep to follow nested objects such as objective filters before writing a placeholder.").Value;
        DefinitionDumper.OutputPath = Config.Bind("Dump", "OutputPath", "",
            "Where to write the JSON. Empty means BepInEx/plugins/reus2_definitions.json.").Value;

        Log.LogInfo($"{MyPluginInfo.PLUGIN_NAME} {MyPluginInfo.PLUGIN_VERSION} loaded.");
        harmony = new Harmony(MyPluginInfo.PLUGIN_GUID);

        if (enableDefinitionDump.Value)
        {
            DefinitionDumper.Status("awake");
            HookDatabaseRegistration();
        }

        if (enableProbes.Value)
            GameProbes.Install(harmony);
    }

    private void Start()
    {
        if (!autoConnect.Value)
        {
            Log.LogInfo("Archipelago auto-connect is disabled.");
            return;
        }

        if (string.IsNullOrWhiteSpace(slot.Value))
        {
            Log.LogInfo(
                "Set Archipelago.Slot in the BepInEx config to enable auto-connect.");
            return;
        }

        var error = Client.Connect(server.Value, port.Value, slot.Value, password.Value);
        if (error != null)
            Log.LogError($"Archipelago connection failed: {error}");
    }

    /// <summary>
    /// Dumps the moment the game registers its definition database, which is the first point the
    /// catalogue is complete. Update is not reliable here — this plugin's Update never ticks in
    /// Reus 2 — so the Harmony hook is the primary trigger.
    /// </summary>
    private void HookDatabaseRegistration()
    {
        try
        {
            var databaseType = AccessTools.TypeByName("DefinitionDatabase");
            var register = AccessTools.Method(databaseType, "RegisterInstance");
            if (register == null)
            {
                Log.LogWarning("DefinitionDatabase.RegisterInstance not found; falling back to Update.");
                DefinitionDumper.Status("hook target missing");
                return;
            }

            harmony.Patch(register,
                postfix: new HarmonyMethod(AccessTools.Method(typeof(Plugin), nameof(AfterDatabaseRegistered))));
            DefinitionDumper.Status("hooked DefinitionDatabase.RegisterInstance");
        }
        catch (Exception e)
        {
            Log.LogError($"Definition dump hook failed: {e}");
            DefinitionDumper.Status($"hook failed: {e.Message}");
        }
    }

    private static void AfterDatabaseRegistered()
    {
        DefinitionDumper.Status("RegisterInstance fired");
        try
        {
            DefinitionDumper.DumpOnce();
        }
        catch (Exception e)
        {
            DefinitionDumper.Status($"FAILED: {e}");
            Log.LogError(e.ToString());
        }
    }

    private void Update()
    {
        ApState.DrainNetworkEvents();

        // Fallback for the case where the database registration hook never fires.
        if (enableDefinitionDump.Value)
        {
            try
            {
                if (DefinitionDumper.Ready())
                    DefinitionDumper.DumpOnce();
            }
            catch (Exception e)
            {
                DefinitionDumper.Status($"FAILED: {e}");
                Log.LogError(e.ToString());
            }
        }
    }

    private void OnDestroy()
    {
        Client?.Disconnect();
        harmony?.UnpatchSelf();
    }
}
