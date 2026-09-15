using System;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;
using Reus2Archipelago.Archipelago;
using Reus2Archipelago.Game;
using Reus2Archipelago.Patches;

namespace Reus2Archipelago;

[BepInPlugin(MyPluginInfo.PLUGIN_GUID, MyPluginInfo.PLUGIN_NAME, MyPluginInfo.PLUGIN_VERSION)]
public sealed class Plugin : BaseUnityPlugin
{
    internal static ManualLogSource Log { get; private set; }
    internal static ArchipelagoClient Client { get; private set; }

    /// <summary>A slot is configured: gameplay patches are installed, and the current profile is
    /// bound to the slot on connect.</summary>
    internal static bool ApMode { get; private set; }

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
            "Player slot name. Setting one enables Archipelago mode: drafts, profile levels, "
            + "alternate modes and challenges are turned off. Leave empty to play vanilla.");
        password = Config.Bind("Archipelago", "Password", "",
            "Optional room password.");
        autoConnect = Config.Bind("Archipelago", "AutoConnect", true,
            "Connect at startup when a slot name is configured.");

        enableDefinitionDump = Config.Bind("Diagnostics", "EnableDefinitionDump", false,
            "Write the live definition catalogue to JSON once it is available.");
        enableProbes = Config.Bind("Diagnostics", "EnableProbes", true,
            "Log every milestone, era feat, placement, combo, request, purchase and unlock as it "
            + "happens, without changing progression, together with a tally of which patched methods "
            + "ever ran and a snapshot of the profile and session. "
            + "Writes BepInEx/plugins/reus2_probe.log.");
        DefinitionDumper.MaxDepth = Config.Bind("Dump", "MaxDepth", 4,
            "How deep to follow nested objects such as objective filters before writing a placeholder.").Value;
        DefinitionDumper.OutputPath = Config.Bind("Dump", "OutputPath", "",
            "Where to write the JSON. Empty means BepInEx/plugins/reus2_definitions.json.").Value;

        ApMode = !string.IsNullOrWhiteSpace(slot.Value);
        Log.LogInfo($"{MyPluginInfo.PLUGIN_NAME} {MyPluginInfo.PLUGIN_VERSION} loaded.");
        harmony = new Harmony(MyPluginInfo.PLUGIN_GUID);
        HarmonySelfTest.Run(harmony);
        HarmonySelfTest.SecondOpinion(harmony);

        if (enableDefinitionDump.Value)
        {
            DefinitionDumper.Status("awake");
            HookDatabaseRegistration();
        }

        if (enableProbes.Value)
            GameProbes.Install(harmony);

        if (ApMode && ApData.Load())
        {
            ProgressionPatches.Install(harmony);
            CheckPatches.Install(harmony);
        }

        MainThreadRunner.Create(AutoConnect);
    }

    private void AutoConnect()
    {
        if (!autoConnect.Value)
        {
            Log.LogInfo("Archipelago auto-connect is disabled.");
            return;
        }

        if (!ApMode)
        {
            Log.LogInfo("Set Archipelago.Slot in the BepInEx config to play with Archipelago.");
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

    // Fallback for the case where the database registration hook never fires.
    private void Update()
    {
        if (!enableDefinitionDump.Value)
            return;
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

    /// <summary>Reus 2 destroys this component once the chainloader finishes, which is why its
    /// Update never ticks. The patches and the session belong to the static Harmony instance and to
    /// MainThreadRunner's own object, both of which outlive it, so nothing is torn down here.</summary>
    private void OnDestroy()
        => Log.LogWarning("Plugin component destroyed; patches and the Archipelago session stay.");
}
