using System;
using System.Reflection;
using HarmonyLib;
using Reus2Archipelago.Game;

namespace Reus2Archipelago.Patches;

/// <summary>
/// Replaces vanilla progression on a profile bound to an Archipelago slot: no profile XP or level
/// rewards, no drafts, and no in-run biotica beyond what was received. While a slot is configured,
/// new planets are Normal mode only and challenges are unavailable.
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

    private static void Record(MethodBase original, Func<string> detail, bool quiet = false)
        => ProbeTally.Record(typeof(ProgressionPatches), original, detail, quiet);
}
