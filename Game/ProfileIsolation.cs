using System.IO;
using UnityEngine;

namespace Reus2Archipelago.Game;

/// <summary>
/// Archipelago profiles have their own ids, profile_ap1, profile_ap2…, in folders beside the game's
/// profile_1, profile_2… ones. Install.LoadAllProfiles enumerates profile_*, so it loads these on a
/// later launch, and Install.CreateAndSwitchToFreshProfile numbers its own profiles profile_N, which
/// cannot collide with profile_apN.
/// </summary>
internal static class ProfileIsolation
{
    internal const string IdPrefix = "profile_ap";

    internal static bool IsArchipelagoProfile(object profile)
        => (profile as UnityEngine.Object)?.name?.StartsWith(IdPrefix) == true;

    /// <summary>Creates an Archipelago profile with the lowest number whose folder does not exist,
    /// switches to it, and returns it.</summary>
    internal static object CreateAndSwitchToFreshProfile(object install)
    {
        var number = 0;
        string id;
        do
        {
            number++;
            id = IdPrefix + number;
        }
        while (Directory.Exists(Path.Combine(Application.persistentDataPath, id)));

        var profile = GameAccess.Call(install, "CreateNewUserProfile", id);
        GameAccess.Call(profile, "SetName", $"Archipelago {number}");
        GameAccess.Call(install, "SwitchToUserProfile", profile, GameAccess.EnumValue("ProfileSwitchMode", "SwitchOnly"));
        return profile;
    }
}
