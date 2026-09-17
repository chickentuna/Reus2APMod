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

    /// <summary>A slot has been connected to or configured: gameplay patches are installed, and the
    /// current profile is bound to the slot on connect.</summary>
    internal static bool ApMode { get; private set; }

    internal static ConfigEntry<string> Server { get; private set; }
    internal static ConfigEntry<int> Port { get; private set; }
    internal static ConfigEntry<string> Slot { get; private set; }
    internal static ConfigEntry<string> Password { get; private set; }
    private static Harmony patches;
    private ConfigEntry<bool> autoConnect;
    private ConfigEntry<bool> enableDefinitionDump;
    private ConfigEntry<bool> enableProbes;
    private ConfigEntry<bool> enableTextDump;
    private ConfigEntry<bool> enableTextProbe;
    private ConfigEntry<bool> enableTextOverrides;

    /// <summary>Reus 2 destroys this component once the chainloader finishes, which is why the
    /// plugin's Update never ticks and MainThreadRunner drives the per-frame work from its own
    /// object. Harmony holds the detours it installed, so they survive that destruction.</summary>
    private Harmony harmony;

    private void Awake()
    {
        Log = Logger;
        Client = new ArchipelagoClient();

        Server = Config.Bind("Archipelago", "Server", "archipelago.gg",
            "Archipelago server hostname.");
        Port = Config.Bind("Archipelago", "Port", 38281,
            "Archipelago server port.");
        Slot = Config.Bind("Archipelago", "Slot", "",
            "Player slot name, also set from the connection panel (F1). Once one is set or connected "
            + "to, Archipelago mode is on: drafts, profile levels, alternate modes and challenges are "
            + "turned off. Leave empty to play vanilla.");
        Password = Config.Bind("Archipelago", "Password", "",
            "Optional room password.");
        autoConnect = Config.Bind("Archipelago", "AutoConnect", true,
            "Connect at startup when a slot name is configured.");

        enableDefinitionDump = Config.Bind("Diagnostics", "EnableDefinitionDump", false,
            "Write the live definition catalogue to JSON once it is available.");
        enableProbes = Config.Bind("Diagnostics", "EnableProbes", false,
            "Log every milestone, era feat, placement, combo, request, purchase and unlock as it "
            + "happens, without changing progression, together with a tally of which patched methods "
            + "ever ran and a snapshot of the profile and session. "
            + "Writes BepInEx/plugins/reus2_probe.log.");
        enableTextProbe = Config.Bind("Diagnostics", "EnableTextProbe", false,
            "Log, for each text an override line names, the key the game looks it up by, what the "
            + "translation table holds, and what the game shows. Writes to BepInEx/LogOutput.log.");
        enableTextDump = Config.Bind("Diagnostics", "EnableTextDump", false,
            "Write the active language's translation table to BepInEx/plugins/reus2_texts_<locale>.tsv.");
        enableTextOverrides = Config.Bind("Text", "EnableOverrides", true,
            "Replace game texts from reus2_text_overrides.tsv and reus2_text_overrides.<locale>.tsv, "
            + "shipped next to the mod's DLL, when either file is present.");

        DefinitionDumper.MaxDepth = Config.Bind("Dump", "MaxDepth", 4,
            "How deep to follow nested objects such as objective filters before writing a placeholder.").Value;
        DefinitionDumper.OutputPath = Config.Bind("Dump", "OutputPath", "",
            "Where to write the JSON. Empty means BepInEx/plugins/reus2_definitions.json.").Value;

        Log.LogInfo($"{MyPluginInfo.PLUGIN_NAME} {MyPluginInfo.PLUGIN_VERSION} loaded.");
        harmony = new Harmony(MyPluginInfo.PLUGIN_GUID);
        patches = harmony;

        if (enableDefinitionDump.Value)
        {
            DefinitionDumper.Status("awake");
            HookDatabaseRegistration();
        }

        if (enableProbes.Value)
            GameProbes.Install(harmony);

        var applyTextOverrides = enableTextOverrides.Value && TextOverrides.AnyFilePresent();
        if (applyTextOverrides || enableTextDump.Value)
            TextPatches.Install(harmony, applyTextOverrides, enableTextDump.Value, enableTextProbe.Value);

        // A planet saved with Archipelago messages must load even with no slot configured.
        ApChat.Install(harmony);

        if (!string.IsNullOrWhiteSpace(Slot.Value))
            EnterApMode();

        MainThreadRunner.Create(AutoConnect);
    }

    /// <summary>Loads the seed data and installs the gameplay patches, once.</summary>
    private static bool EnterApMode()
    {
        if (ApMode)
            return true;
        if (!ApData.Load())
            return false;
        ProgressionPatches.Install(patches);
        CheckPatches.Install(patches);
        ApMode = true;
        return true;
    }

    /// <summary>Connects to a slot, remembering the details for the next launch. Returns the error,
    /// or null once connected.</summary>
    internal static string Connect(string host, int port, string slot, string password)
    {
        Server.Value = host;
        Port.Value = port;
        Slot.Value = slot;
        Password.Value = password;
        if (!EnterApMode())
            return "The mod's Archipelago data failed to load; see BepInEx/LogOutput.log.";
        return Client.Connect(host, port, slot, password);
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
            Log.LogInfo("Connect to a slot from the connection panel (F1) to play with Archipelago.");
            return;
        }

        var error = Client.Connect(Server.Value, Port.Value, Slot.Value, Password.Value);
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
}
