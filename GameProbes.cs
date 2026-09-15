using System;
using System.Reflection;
using HarmonyLib;
using Reus2Archipelago.Game;
using Reus2Archipelago.Patches;
using UnityEngine;

namespace Reus2Archipelago;

/// <summary>
/// Observe-only Harmony patches on every game event that is a candidate check or item grant, plus
/// the tally that says which of them ever ran. Nothing here changes progression: each probe writes
/// a line naming the definitions involved and raises a count.
///
/// The point is the trace. One played session has to answer what the decompiled source leaves
/// ambiguous — whether a detour on a two-statement method such as Giant.OnBioticumPlaced is reached
/// at all, whether era feats are re-earned on every run, and what the vanilla unlock stream looks
/// like — before any of it is wired up for real.
///
/// Targets are resolved by name so the plugin still needs no compile-time reference to
/// Assembly-CSharp, and a target that has moved in a patch disables its own probe rather than
/// breaking the plugin.
/// </summary>
internal static class GameProbes
{
    private const float DumpSeconds = 60f;

    /// <summary>Every RuleState subclass with its own CalculateActivity. RuleState declares it
    /// abstract, so there is no single method to patch.</summary>
    private static readonly string[] RuleStates =
    {
        "BaseTriggerConditionRuleState", "BiomeForEachRuleState", "ConditionSlotRuleState",
        "OnEachSlotForEachRuleState", "SimpleBiomeBonusRuleState", "SimpleSlotRuleState",
        "SlotBonusRuleState", "SlotBuffRuleState", "SlotForEachRuleState"
    };

    private static float nextDump;

    internal static void Install(Harmony harmony)
    {
        ProbeTally.Enabled = true;
        ProbeLog.Open();
        nextDump = Time.realtimeSinceStartup + DumpSeconds;

        InstallUnlocks(harmony);
        InstallPlacement(harmony);
        InstallCombos(harmony);
        InstallRequests(harmony);
        InstallShopAndAspects(harmony);
        InstallLifecycle(harmony);
    }

    /// <summary>Called every frame from MainThreadRunner.</summary>
    internal static void Tick()
    {
        if (!ProbeTally.Enabled || Time.realtimeSinceStartup < nextDump)
            return;
        nextDump = Time.realtimeSinceStartup + DumpSeconds;
        ProbeTally.Dump($"every {DumpSeconds:0} seconds");
        StateSnapshot.Dump($"every {DumpSeconds:0} seconds");
    }

    private static void InstallUnlocks(Harmony harmony)
    {
        Patch(harmony, "Milestone", "AchieveMilestone",
            prefix: nameof(BeforeMilestone), postfix: nameof(AfterMilestone));
        Patch(harmony, "EraFeatController", "OnEraFeatAchieved", postfix: nameof(AfterEraFeat));
        Patch(harmony, "EraFeatController", "StartTrackingMainFeat", postfix: nameof(AfterMainFeatStarted));
        Patch(harmony, "MetaGameProfile.UserProfile", "UnlockBioticum", postfix: nameof(AfterUnlockBioticum));
        Patch(harmony, "MetaGameProfile.UserProfile", "UnlockTurningPoint", postfix: nameof(AfterArguments));
        Patch(harmony, "MetaGameProfile.UserProfile", "LevelUpProfile", postfix: nameof(AfterLevelUp));
    }

    /// <summary>The whole placement chain, from the action that finishes the animation down to the
    /// two-statement methods it calls, so the tally shows exactly where the calls stop arriving.</summary>
    private static void InstallPlacement(Harmony harmony)
    {
        Patch(harmony, "PlaceBioticumAction", "OnPlacementCompleted", postfix: nameof(AfterPlacementCompleted));
        Patch(harmony, "BioticumSlot", "CompleteGrowth", postfix: nameof(AfterCompleteGrowth));
        Patch(harmony, "Giant", "OnBioticumPlaced", postfix: nameof(AfterArguments));
        Patch(harmony, "City", "OnBioticumPlaced", postfix: nameof(AfterArguments));
        Patch(harmony, "GameplayController", "OnBioticumPlaced", postfix: nameof(AfterArguments));
    }

    private static void InstallCombos(Harmony harmony)
    {
        Patch(harmony, "RuleState", "set_IsActive", postfix: nameof(AfterRuleActivity));
        Patch(harmony, "RuleState", "UpdateActivity", postfix: nameof(AfterUpdateActivity));
        Patch(harmony, "RuleState", "CalculateActivity", postfix: nameof(AfterCalculateActivity));
        foreach (var ruleState in RuleStates)
            Patch(harmony, ruleState, "CalculateActivity", postfix: nameof(AfterCalculateActivity));
    }

    private static void InstallRequests(Harmony harmony)
    {
        Patch(harmony, "Request", "GiveRewards", postfix: nameof(AfterGiveRewards));
        Patch(harmony, "EraFeatController", "GiveRewardForQuest", postfix: nameof(AfterQuestReward));
        Patch(harmony, "EraFeatController", "GainsDraftForRequest", postfix: nameof(AfterGainsDraft));
        Patch(harmony, "RequestController", "OnRequestCompleted", postfix: nameof(AfterRequestCompleted));
        Patch(harmony, "EraFeatController", "OnRequestCompleted", postfix: nameof(AfterQuestCounted));
    }

    private static void InstallShopAndAspects(Harmony harmony)
    {
        Patch(harmony, "GameplayShop", "BuyOption", postfix: nameof(AfterBuyOption));
        Patch(harmony, "AspectController", "DrawInitialSet", postfix: nameof(AfterAspectCall));
        Patch(harmony, "AspectController", "DrawSetForEra", postfix: nameof(AfterAspectCall));
        Patch(harmony, "AspectController", "UnlockAspectShopOption", postfix: nameof(AfterAspectCall));
        Patch(harmony, "AspectController", "GrantAspectCharge", postfix: nameof(AfterAspectCall));
        Patch(harmony, "AspectController", "ConsumeAspectCharge", postfix: nameof(AfterAspectCall));
    }

    private static void InstallLifecycle(Harmony harmony)
    {
        Patch(harmony, "EraFeatController", "OnWorldStageControllerActivated", postfix: nameof(AfterPlanetLoaded));
        Patch(harmony, "WorldStageController", "ChangeCurrentWorldStage", postfix: nameof(AfterEraChange));
        Patch(harmony, "WorldStageController", "EndEra", postfix: nameof(AfterEraChange));
        Patch(harmony, "WorldStageController", "CompleteTurningPoint", postfix: nameof(AfterEraChange));
        Patch(harmony, "EraFeatController", "OnLevelUpWorldStage", postfix: nameof(AfterArguments));
        Patch(harmony, "Session", "WinSession", postfix: nameof(AfterSessionEnded));
        Patch(harmony, "Session", "GameOver", postfix: nameof(AfterSessionEnded));
        Patch(harmony, "GameManager", "OnSessionCompleted", postfix: nameof(AfterSessionEnded));
    }

    private static void Patch(Harmony harmony, string typeName, string methodName,
        string prefix = null, string postfix = null)
        => Patching.Apply(harmony, typeName, methodName, typeof(GameProbes), prefix, postfix);

    // ---- placement -------------------------------------------------------------------------

    private static void AfterPlacementCompleted(MethodBase __originalMethod, object __instance)
        => Observe(__originalMethod, () =>
            $"bioticum={Naming.Of(GameAccess.Get(__instance, "bioticum"))}"
            + $" giant={Naming.Of(GameAccess.Get(__instance, "giant"))}"
            + $" slotHolds={Naming.Of(GameAccess.Get(GameAccess.Get(__instance, "targetSlot"), "BioticumDefinition"))}");

    private static void AfterCompleteGrowth(MethodBase __originalMethod, object __instance, object __result)
        => Observe(__originalMethod, () =>
            $"grown={Naming.Of(__result)}"
            + $" slotHolds={Naming.Of(GameAccess.Get(__instance, "BioticumDefinition"))}");

    // ---- combos ----------------------------------------------------------------------------

    // Rule activity is recalculated for the placement preview as well as for placed biotica, so the
    // line says which of the two this call was.
    private static void AfterRuleActivity(MethodBase __originalMethod, object __instance, object[] __args)
        => Observe(__originalMethod, () =>
        {
            var context = GameAccess.Get(__instance, "slotContext");
            var preview = GameAccess.Get(context, "PreviewedBioticum");
            return $"value={Naming.Of(__args != null && __args.Length > 0 ? __args[0] : null)}"
                   + $" rule={Naming.Of(GameAccess.Get(__instance, "Rule"))}"
                   + $" bioticum={Naming.Of(GameAccess.Get(GameAccess.Get(context, "slot"), "BioticumDefinition"))}"
                   + $" previewed={Naming.Of(preview)}";
        }, quiet: true);

    private static void AfterUpdateActivity(MethodBase __originalMethod, object __instance)
        => Observe(__originalMethod, () => $"rule={Naming.Of(GameAccess.Get(__instance, "Rule"))}"
                                           + $" isActive={GameAccess.Get(__instance, "IsActive")}", quiet: true);

    private static void AfterCalculateActivity(MethodBase __originalMethod, object __instance, bool __result)
        => Observe(__originalMethod, () => $"rule={Naming.Of(GameAccess.Get(__instance, "Rule"))}"
                                           + $" calculated={__result}", quiet: true);

    // ---- requests --------------------------------------------------------------------------

    private static void AfterGiveRewards(MethodBase __originalMethod, object __instance)
        => Observe(__originalMethod, () =>
        {
            var controller = GameAccess.Get(__instance, "controller");
            return $"request={Naming.Of(GameAccess.Get(__instance, "definition"))}"
                   + $" level={Naming.Count(GameAccess.Get(controller, "completedRequests"))}"
                   + $" city={GameAccess.Get(GameAccess.Get(controller, "city"), "Name")}";
        });

    private static void AfterQuestReward(MethodBase __originalMethod, object __instance, object[] __args)
        => Observe(__originalMethod, () =>
            $"level={Naming.Of(__args != null && __args.Length > 0 ? __args[0] : null)}"
            + $" completedForEachLevel={Naming.Each(GameAccess.Get(__instance, "completedQuestForEachLevel"))}");

    private static void AfterGainsDraft(MethodBase __originalMethod, object[] __args, bool __result)
        => Observe(__originalMethod, () =>
            $"level={Naming.Of(__args != null && __args.Length > 0 ? __args[0] : null)} gainsDraft={__result}",
            quiet: true);

    private static void AfterRequestCompleted(MethodBase __originalMethod, object __instance)
        => Observe(__originalMethod, () =>
            $"city={GameAccess.Get(GameAccess.Get(__instance, "city"), "Name")}"
            + $" completedRequests={Naming.Count(GameAccess.Get(__instance, "completedRequests"))}");

    private static void AfterQuestCounted(MethodBase __originalMethod, object __instance, object[] __args)
        => Observe(__originalMethod, () =>
            $"completer={GameAccess.Get(GameAccess.Get(__args != null && __args.Length > 0 ? __args[0] : null, "city"), "Name")}"
            + $" completedForEachLevel={Naming.Each(GameAccess.Get(__instance, "completedQuestForEachLevel"))}"
            + $" questPressure={GameAccess.Get(__instance, "questPressure")}");

    // ---- shop and aspects ------------------------------------------------------------------

    private static void AfterBuyOption(MethodBase __originalMethod, object __instance, object[] __args)
        => Observe(__originalMethod, () =>
            $"{Arguments(__originalMethod, __args)} | buyCount={Naming.Each(GameAccess.Get(__instance, "buyCount"))}");

    private static void AfterAspectCall(MethodBase __originalMethod, object __instance, object[] __args)
        => Observe(__originalMethod, () =>
            $"{Arguments(__originalMethod, __args)}"
            + $" | latestUnlockedAspect={Naming.Of(GameAccess.Get(__instance, "latestUnlockedAspect"))}"
            + $" | inventory={Naming.Count(GameAccess.Get(__instance, "aspectInventory"))} types");

    // ---- session lifecycle -----------------------------------------------------------------

    private static void AfterPlanetLoaded(MethodBase __originalMethod)
    {
        Observe(__originalMethod, () => "planet session loaded");
        ProbeTally.Dump("planet load");
        StateSnapshot.Dump("planet load");
    }

    private static void AfterEraChange(MethodBase __originalMethod, object __instance)
        => Observe(__originalMethod, () =>
            $"worldStage={Naming.Of(GameAccess.Get(__instance, "currentWorldStage"))}"
            + $" era={GameAccess.Get(__instance, "CurrentEra")}"
            + $" turningPoint={Naming.Of(GameAccess.Get(__instance, "CurrentTurningPoint"))}"
            + $" completedTurningPoints={Naming.Count(GameAccess.Get(__instance, "completedTurningPoints"))}");

    private static void AfterSessionEnded(MethodBase __originalMethod)
    {
        Observe(__originalMethod, () =>
        {
            var session = GameAccess.Get(GameAccess.SessionContext, "CurrentSession");
            return $"session={GameAccess.SessionId} finished={GameAccess.Get(session, "IsFinished")}"
                   + $" planetIsLost={GameAccess.Get(session, "PlanetIsLost")}";
        });
        ProbeTally.Dump($"session end through {ProbeTally.Key(__originalMethod)}");
    }

    // ---- unlocks ---------------------------------------------------------------------------

    // Milestones are the persistent, achievement-backed checks. The prefix records whether this
    // one had already been achieved, which is what separates a real check from a replayed one.
    private static void BeforeMilestone(object __instance, out bool __state)
        => __state = GameAccess.Get(__instance, "achieved") is bool achieved && achieved;

    private static void AfterMilestone(MethodBase __originalMethod, object __instance, bool __state)
        => Observe(__originalMethod, () =>
        {
            var definition = GameAccess.Get(__instance, "definition");
            return $"{(__state ? "re-triggered" : "achieved")}: {Naming.Of(definition)}"
                   + $" | category={GameAccess.Get(definition, "category")}"
                   + $" | would send check: \"Milestone: {NameOf(definition)}\"";
        });

    private static void AfterEraFeat(MethodBase __originalMethod, object[] __args)
        => Observe(__originalMethod, () =>
        {
            var feat = __args != null && __args.Length > 0 ? __args[0] : null;
            var definition = GameAccess.Get(feat, "definition");
            return $"achieved: {Naming.Of(definition)}"
                   + $" | hasEverBeenAchieved={GameAccess.Get(feat, "hasEverBeenAchieved")}"
                   + $" | difficulty={GameAccess.Get(definition, "difficulty")}"
                   + $" | would send check: \"Era Feat: {NameOf(definition)}\"";
        });

    // A new era began and its main feat is now tracked.
    private static void AfterMainFeatStarted(MethodBase __originalMethod, object __instance)
        => Observe(__originalMethod, () =>
            $"main feat={Naming.Of(GameAccess.Get(GameAccess.Get(__instance, "mainEraFeat"), "definition"))}");

    private static void AfterUnlockBioticum(MethodBase __originalMethod, object[] __args, bool __result)
        => Observe(__originalMethod, () =>
            $"{Arguments(__originalMethod, __args)} | unlocked={__result}");

    private static void AfterLevelUp(MethodBase __originalMethod, object __instance)
        => Observe(__originalMethod, () =>
            $"level={GameAccess.Get(__instance, "ProfileLevel")}"
            + $" | xpBudget={GameAccess.Get(__instance, "RemainingXPBudget")}"
            + $" | apexPoints={GameAccess.Get(__instance, "ApexUnlockPoints")}");

    // ---- shared ----------------------------------------------------------------------------

    private static void AfterArguments(MethodBase __originalMethod, object[] __args)
        => Observe(__originalMethod, () => Arguments(__originalMethod, __args));

    private static void Observe(MethodBase original, Func<string> detail, bool quiet = false)
        => ProbeTally.Record(typeof(GameProbes), original, detail, quiet);

    private static string Arguments(MethodBase original, object[] args)
    {
        if (args == null || args.Length == 0)
            return "no arguments";
        var parameters = original?.GetParameters();
        var parts = new string[args.Length];
        for (var index = 0; index < args.Length; index++)
        {
            var name = parameters != null && index < parameters.Length ? parameters[index].Name : $"arg{index}";
            parts[index] = $"{name}={Naming.Of(args[index])}";
        }
        return string.Join(", ", parts);
    }

    private static string NameOf(object definition)
        => definition is UnityEngine.Object o ? o.name : definition?.ToString() ?? "<null>";
}
