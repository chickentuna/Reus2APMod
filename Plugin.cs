using System;
using System.IO;
using BepInEx;
using BepInEx.Logging;
using HarmonyLib;

namespace Reus2Dump;

[BepInPlugin(MyPluginInfo.PLUGIN_GUID, MyPluginInfo.PLUGIN_NAME, MyPluginInfo.PLUGIN_VERSION)]
public class Plugin : BaseUnityPlugin
{
    internal static new ManualLogSource Logger;

    private void Awake()
    {
        Logger = base.Logger;
        DefinitionDumper.MaxDepth = Config.Bind("Dump", "MaxDepth", 4,
            "How deep to follow nested objects such as objective filters before writing a placeholder.").Value;
        DefinitionDumper.OutputPath = Config.Bind("Dump", "OutputPath", "",
            "Where to write the JSON. Empty means BepInEx/plugins/reus2_definitions.json.").Value;

        Logger.LogInfo($"Plugin path: {Paths.PluginPath}");
        DefinitionDumper.Status("awake");
        Logger.LogInfo($"Status file writable: {File.Exists(Path.Combine(Paths.PluginPath, "reus2dump_status.txt"))}");

        HookDatabaseRegistration();
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
                Logger.LogWarning("DefinitionDatabase.RegisterInstance not found; falling back to Update.");
                DefinitionDumper.Status("hook target missing");
                return;
            }

            new Harmony(MyPluginInfo.PLUGIN_GUID).Patch(register,
                postfix: new HarmonyMethod(AccessTools.Method(typeof(Plugin), nameof(AfterDatabaseRegistered))));
            DefinitionDumper.Status("hooked DefinitionDatabase.RegisterInstance");
        }
        catch (Exception e)
        {
            Logger.LogError($"Hook failed: {e}");
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
            Logger.LogError(e.ToString());
        }
    }

    // Fallback for the case where the hook never fires.
    private void Update()
    {
        frame++;
        if (frame == 1) DefinitionDumper.Status("update ticking");

        try
        {
            if (!DefinitionDumper.Ready()) return;
            DefinitionDumper.DumpOnce();
        }
        catch (Exception e)
        {
            DefinitionDumper.Status($"FAILED: {e}");
            Logger.LogError(e.ToString());
        }
        enabled = false;
    }

    private int frame;
}
