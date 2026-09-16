using System;
using System.Collections;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using Reus2Archipelago.Game;

namespace Reus2Archipelago.Patches;

/// <summary>
/// Replaces vanilla progression on a profile bound to an Archipelago slot: no profile XP or level
/// rewards, no drafts, no in-run biotica beyond what was received, and only the Micro types received
/// rather than the ones the game draws. While a slot is configured, new planets are Normal
/// mode only and challenges are unavailable.
/// </summary>
internal static class ProgressionPatches
{
    private const string UserProfile = "MetaGameProfile.UserProfile";

    internal static void Install(Harmony harmony)
    {
        var owner = typeof(ProgressionPatches);
        Patching.Apply(harmony, UserProfile, "AddXP", owner, prefix: nameof(RunUnlessBound));
        Patching.Apply(harmony, UserProfile, "RewardUnlocksForLevel", owner, prefix: nameof(RunUnlessBound));
        Patching.Apply(harmony, "GameplayController", "GetAllAvailableBiotica", owner, postfix: nameof(AllReceivedBiotica));
        Patching.Apply(harmony, "GameplayController", "UnlockBioticum", owner, prefix: nameof(OnlyReceivedBioticum));
        Patching.Apply(harmony, "NewGameViewModel", "SetPlayMode", owner, prefix: nameof(ForceStandardMode));
        Patching.Apply(harmony, "NewGameViewModel", "SetDraftMode", owner, prefix: nameof(ForceStandardMode));
        Patching.Apply(harmony, "NewGameViewModel", "SetHorizonMode", owner, prefix: nameof(ForceStandardMode));
        Patching.Apply(harmony, "CommunityChallengeUI", "OnChallengeSelected", owner, prefix: nameof(BlockChallenge));
        Patching.Apply(harmony, "EraFeatController", "OnWorldStageControllerActivated", owner, postfix: nameof(PlanetLoaded));
        Patching.Apply(harmony, "AspectController", "DrawSetForEra", owner, prefix: nameof(SkipDrawnMicroType));
        Patching.Apply(harmony, "AspectController", "DrawInitialSet", owner, postfix: nameof(AfterInitialMicros));
        Patching.Apply(harmony, "GiantAbility", "IsLocked", owner, postfix: nameof(LockUnreceivedBiome));
        Patching.Apply(harmony, "GameplayShop", "IsOptionUnlocked", owner, postfix: nameof(LockSpentDraftShop));
        Patching.Apply(harmony, "GameplayShop", "CanBuy", owner, postfix: nameof(LockSpentDraftShop));
    }

    // IsLocked answers a biome power from the giant's first and second biome alone and returns before
    // it reads the planet's locked abilities, so a biome whose item has not arrived is locked here.
    // IsLockedForMetaGame reads the profile's unlocked biomes, which the received items set; the
    // ability buttons read IsLocked to decide whether they can be clicked.
    private static void LockUnreceivedBiome(MethodBase __originalMethod, GiantAbility __instance, ref (bool, string) __result)
    {
        if (__result.Item1 || !(__instance.Definition is PlaceBiomeAbilityDefinition biome) || !ItemApplier.ProfileBound)
            return;
        if (!biome.IsLockedForMetaGame())
            return;
        __result = (true, MacroSettings.L("{0} has not arrived from Archipelago yet.", biome.biomeType.GetRichName()));
        Record(__originalMethod, () => $"biome={biome.biomeType.biomeType} locked", quiet: true);
    }

    // Drafts are locked, so buying one only sends a Shop Draft check. Once none of the seed's is left to
    // send, the draft options are shown locked (IsOptionUnlocked) and cannot be bought (CanBuy, which
    // BuyOption and the buttons read).
    private static void LockSpentDraftShop(MethodBase __originalMethod, ShopPriceOption option, ref (bool, string) __result)
    {
        if (!__result.Item1 || !option.ToString().StartsWith("Draft") || !ItemApplier.ProfileBound || !ApState.HasSession)
            return;
        if (Archipelago.ApData.Instance.ShopDrafts.Any(draft => ApState.IsOpen(draft.Location)))
            return;
        __result = (false, MacroSettings.L("Every Archipelago check in this shop has been sent."));
        Record(__originalMethod, () => $"option={option} locked", quiet: true);
    }

    private static bool RunUnlessBound(MethodBase __originalMethod)
    {
        var run = !ItemApplier.ProfileBound;
        Record(__originalMethod, () => run ? "original runs" : "original skipped");
        return run;
    }

    // Every received bioticum is placeable: a bound profile's unlocked set is exactly what it received.
    private static void AllReceivedBiotica(MethodBase __originalMethod, ref object __result)
    {
        if (ItemApplier.ProfileBound)
            __result = GameAccess.Get(GameAccess.CurrentProfile, "UnlockedBiotica");
        var replaced = __result;
        Record(__originalMethod, () => $"bound={ItemApplier.ProfileBound} biotica={Naming.Count(replaced)}",
            quiet: true);
    }

    private static bool OnlyReceivedBioticum(MethodBase __originalMethod, object[] __args)
    {
        var run = !ItemApplier.ProfileBound || ItemApplier.IsReceivedBioticum(__args[0]);
        Record(__originalMethod, () => $"bioticum={Naming.Of(__args[0])} original {(run ? "runs" : "skipped")}");
        return run;
    }

    // The first value of each mode enum is the standard one: Normal, FullDraft, Standard.
    private static void ForceStandardMode(MethodBase __originalMethod, object[] __args)
    {
        if (__args.Length > 0 && __args[0] is Enum mode)
            __args[0] = Enum.ToObject(mode.GetType(), 0);
        Record(__originalMethod, () => $"mode={Naming.Of(__args.Length > 0 ? __args[0] : null)}");
    }

    private static bool BlockChallenge(MethodBase __originalMethod)
    {
        Plugin.Log.LogInfo("Daily and weekly challenges are unavailable while an Archipelago slot is configured.");
        Record(__originalMethod, () => "original skipped");
        return false;
    }

    private static void PlanetLoaded(MethodBase __originalMethod)
    {
        ItemApplier.MarkDirty();
        Record(__originalMethod, () => "items will be reapplied");
    }

    // DrawSetForEra draws one more Micro type from the era pools and names it in the era
    // introduction. The received items decide which types the player has, so the draw is skipped and
    // clearing latestUnlockedAspect leaves the panel out of the era introduction.
    private static bool SkipDrawnMicroType(MethodBase __originalMethod, object __instance)
    {
        var run = !ItemApplier.ProfileBound;
        if (!run)
            GameAccess.Set(__instance, "latestUnlockedAspect", null);
        Record(__originalMethod, () => run ? "original runs" : "original skipped");
        return run;
    }

    // DrawInitialSet gives a new planet each giant's own Micro and draws it one starter type; the
    // starter is taken back out, and the received types are added when the items are applied.
    private static void AfterInitialMicros(MethodBase __originalMethod, AspectController __instance)
    {
        if (ItemApplier.ProfileBound)
        {
            ItemApplier.DropDrawnMicroTypes(__instance);
            ItemApplier.MarkDirty();
        }
        Record(__originalMethod, () => $"bound={ItemApplier.ProfileBound}");
    }

    private static void Record(MethodBase original, Func<string> detail, bool quiet = false)
        => ProbeTally.Record(typeof(ProgressionPatches), original, detail, quiet);
}
