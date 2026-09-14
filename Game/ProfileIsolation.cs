using System.IO;
using System.Linq;
using HarmonyLib;
using Reus2Archipelago.Patches;
using UnityEngine;

namespace Reus2Archipelago.Game;

/// <summary>
/// Keeps Archipelago profiles apart from the player's own. Archipelago profiles have their own ids
/// (ap_1, ap_2…, in folders beside the vanilla profile_* ones). While a slot is configured only those
/// are loaded or created, and the install file, which records the last profile used, is never
/// written, so the player's vanilla profiles and planets are never loaded or saved.
/// </summary>
internal static class ProfileIsolation
{
    internal const string IdPrefix = "ap_";

    internal static bool IsArchipelagoProfile(object profile)
        => (profile as UnityEngine.Object)?.name?.StartsWith(IdPrefix) == true;

    private static System.Reflection.PropertyInfo gameManagerInstance;
    private static bool gameManagerReady;

    internal static void Install(Harmony harmony)
    {
        var owner = typeof(ProfileIsolation);
        // First, so the patches below can safely run SaveFileManager's initialiser (see below).
        Patching.Apply(harmony, "AbbeyBehaviour", "GetDebugToggle", owner, prefix: nameof(NoTogglesBeforeGameManager));
        Patching.Apply(harmony, "Install", "LoadAllProfiles", owner, prefix: nameof(LoadArchipelagoProfiles));
        Patching.Apply(harmony, "Install", "CreateAndSwitchToFreshProfile", owner, prefix: nameof(CreateArchipelagoProfile));
        Patching.Apply(harmony, "Install", "WriteInstallToDisk", owner, prefix: nameof(Skip));
        Patching.Apply(harmony, "GameManager", "ClearMetaGame", owner, prefix: nameof(Skip));
    }

    // Patching a method that reads SaveFileManager runs SaveFileManager's initialiser, which asks
    // GetDebugToggle for the save folder. GetDebugToggle dereferences GameManager.Instance, which does
    // not exist yet at plugin load; answering "off" until it does gives the normal save folder.
    private static bool NoTogglesBeforeGameManager(ref bool __result)
    {
        if (gameManagerReady)
            return true;
        gameManagerInstance ??= AccessTools.Property(GameAccess.TypeNamed("GameManager"), "Instance");
        if (gameManagerInstance?.GetValue(null) != null)
        {
            gameManagerReady = true;
            return true;
        }
        __result = false;
        return false;
    }

    private static string SaveFolder => Application.persistentDataPath;

    private static bool LoadArchipelagoProfiles(object __instance)
    {
        foreach (var directory in new DirectoryInfo(SaveFolder).GetDirectories(IdPrefix + "*", SearchOption.TopDirectoryOnly))
        {
            var file = directory.GetFiles(directory.Name).FirstOrDefault();
            if (file != null)
                GameAccess.Call(__instance, "LoadUserProfile", file.FullName);
        }
        Plugin.Log.LogInfo("Loaded Archipelago profiles only; vanilla profiles are hidden.");
        return false;
    }

    private static bool CreateArchipelagoProfile(object __instance, object[] __args, ref object __result)
    {
        var number = 0;
        string id;
        do
        {
            number++;
            id = IdPrefix + number;
        }
        while (Directory.Exists(Path.Combine(SaveFolder, id)));

        var profile = GameAccess.Call(__instance, "CreateNewUserProfile", id);
        GameAccess.Call(profile, "SetName", $"Archipelago {number}");
        GameAccess.Call(__instance, "SwitchToUserProfile", profile, __args[0]);
        __result = profile;
        Plugin.Log.LogInfo($"Created Archipelago profile {id}.");
        return false;
    }

    private static bool Skip() => false;
}
