using System.IO;
using HarmonyLib;
using Reus2Archipelago.Patches;
using UnityEngine;

namespace Reus2Archipelago.Game;

/// <summary>
/// Gives Archipelago mode its own save folder, so the player's vanilla profiles and planets are never
/// loaded or written while a slot is configured. Every Reus 2 save path — profiles, planets, the install
/// file and the metagame log — derives from UserProfile.Reus2SaveFolder, so redirecting that getter
/// isolates them all. Clearing the slot brings the player's own saves back.
/// </summary>
internal static class SaveRoot
{
    internal static string Folder { get; private set; }

    internal static void Install(Harmony harmony)
    {
        Folder = Path.Combine(Application.persistentDataPath, "Archipelago");
        Directory.CreateDirectory(Folder);
        Patching.Apply(harmony, "MetaGameProfile.UserProfile", "get_Reus2SaveFolder", typeof(SaveRoot),
            postfix: nameof(RedirectSaveFolder));

        // A static field computed once from the save folder, so it is pointed at the new one directly.
        AccessTools.Field(GameAccess.TypeNamed("SaveFileManager"), "InstallSavePath")
            ?.SetValue(null, Path.Combine(Folder, "install"));
        Plugin.Log.LogInfo($"Archipelago saves are kept in {Folder}");
    }

    private static void RedirectSaveFolder(ref string __result) => __result = Folder;
}
