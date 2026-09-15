using System.Reflection;
using HarmonyLib;

namespace Reus2Archipelago.Game;

/// <summary>
/// The same target as one of the manual patches, applied through an attribute class instead, so a
/// run says whether the two ways of asking Harmony for a detour differ.
/// </summary>
[HarmonyPatch]
internal static class AttributeCheck
{
    internal static int Hits;

    internal static MethodBase TargetMethod()
        => AccessTools.Method(GameAccess.TypeNamed("GameplayShop"), "BuyOption");

    private static void Postfix()
    {
        Hits++;
        Plugin.Log.LogInfo($"Attribute patch on GameplayShop.BuyOption ran ({Hits}).");
    }
}
