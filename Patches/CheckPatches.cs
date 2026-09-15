using System;
using System.Collections;
using System.Reflection;
using HarmonyLib;
using Reus2Archipelago.Archipelago;
using Reus2Archipelago.Game;

namespace Reus2Archipelago.Patches;

/// <summary>
/// Sends checks: a bioticum's first build, its combo rules — one check each or one for all of them
/// together, as combo_checks says — and the draft-paying request rewards and shop draft purchases
/// within a planet that the slot turned into checks.
/// </summary>
internal static class CheckPatches
{
    private const int ComboChecksNone = 0;
    private const int ComboChecksPerRule = 1;

    internal static void Install(Harmony harmony)
    {
        var owner = typeof(CheckPatches);
        Patching.Apply(harmony, "GameplayController", "OnBioticumPlaced", owner, postfix: nameof(AfterBioticumPlaced));
        Patching.Apply(harmony, "RuleState", "set_IsActive", owner, postfix: nameof(AfterRuleActivityChanged));
        Patching.Apply(harmony, "EraFeatController", "GiveRewardForQuest", owner, prefix: nameof(BeforeRequestReward));
        Patching.Apply(harmony, "GameplayShop", "BuyOption", owner, prefix: nameof(BeforeShopPurchase));
    }

    // Mono inlines Giant.OnBioticumPlaced, two statements long, into PlaceBioticumAction, so the
    // detour holds on this one, which the same placement completion calls with the definition.
    private static void AfterBioticumPlaced(MethodBase __originalMethod, object[] __args)
    {
        var bioticum = __args == null || __args.Length == 0 ? null : __args[0];
        Record(__originalMethod, () => $"bound={ItemApplier.ProfileBound} bioticum={Naming.Of(bioticum)}");
        if (!ItemApplier.ProfileBound || bioticum == null)
            return;
        var guid = GameAccess.DefinitionId(bioticum);
        var entry = ApData.Instance.BioticumForGuid(guid);
        if (entry == null)
        {
            Plugin.Log.LogWarning($"Placed a bioticum the data does not hold: {guid ?? "no id"}.");
            return;
        }
        ApState.SendCheck(entry.BuildLocation);
    }

    // Rule states are also generated for the placement preview; only a placed bioticum counts.
    private static void AfterRuleActivityChanged(MethodBase __originalMethod, object __instance, bool value)
    {
        Record(__originalMethod, () => $"value={value} bound={ItemApplier.ProfileBound}", quiet: true);
        if (!value || !ItemApplier.ProfileBound)
            return;
        var mode = ApState.SlotInt("combo_checks", ComboChecksNone);
        if (mode == ComboChecksNone)
            return;
        var context = GameAccess.Get(__instance, "slotContext");
        if (context == null || GameAccess.Get(context, "PreviewedBioticum") != null)
            return;

        var definition = GameAccess.Get(GameAccess.Get(context, "slot"), "BioticumDefinition");
        var guid = GameAccess.DefinitionId(definition);
        if (guid == null || !(GameAccess.Get(definition, "rules") is IList rules))
            return;

        var rule = GameAccess.Get(__instance, "RuleDefinitionBase");
        for (var index = 0; index < rules.Count; index++)
        {
            if (!ReferenceEquals(rules[index], rule))
                continue;
            if (ApData.Instance.ComboRule(guid, index) is RuleEntry combo)
            {
                if (mode == ComboChecksPerRule)
                    ApState.SendCheck(combo.Location);
                else
                    SendWhenEveryRuleActivated(guid, index);
            }
            return;
        }
    }

    // A bioticum's rules are activated over several placements and several planets, so the profile
    // keeps the indexes that have fired and the check waits for the last of them.
    private static void SendWhenEveryRuleActivated(string guid, int ruleIndex)
    {
        var bioticum = ApData.Instance.BioticumForGuid(guid);
        if (bioticum?.AllCombosLocation == null)
            return;
        var activated = ProfileBinding.AddActivatedComboRule(GameAccess.CurrentProfile, guid, ruleIndex);
        if (activated >= bioticum.Rules.Count)
            ApState.SendCheck(bioticum.AllCombosLocation);
    }

    // Runs before the reward is paid and before the request is counted, so the reward's number is
    // the level's completed count plus one.
    private static void BeforeRequestReward(MethodBase __originalMethod, object __instance, int level)
    {
        Record(__originalMethod, () => $"level={level} bound={ItemApplier.ProfileBound}");
        if (!ItemApplier.ProfileBound)
            return;
        if (!(GameAccess.Call(__instance, "GainsDraftForRequest", level) is bool gainsDraft && gainsDraft))
            return;
        if (!(GameAccess.Get(__instance, "completedQuestForEachLevel") is IList completed) || level >= completed.Count)
            return;

        var index = (int)completed[level] + 1;
        var location = ApData.Instance.RequestLocation(level, index, ApState.SlotInt("request_reward_checks", 0));
        if (location != null)
            ApState.SendCheck(location);
    }

    private static void BeforeShopPurchase(MethodBase __originalMethod, object __instance, object[] __args)
    {
        var option = __args == null || __args.Length == 0 ? null : __args[0];
        Record(__originalMethod, () => $"option={Naming.Of(option)} bound={ItemApplier.ProfileBound}");
        if (!ItemApplier.ProfileBound || option == null || !option.ToString().StartsWith("Draft"))
            return;
        if (!(GameAccess.Get(GameAccess.Call(__instance, "CanBuy", option), "Item1") is bool affordable && affordable))
            return;

        var bought = 0;
        if (GameAccess.Get(__instance, "buyCount") is IEnumerable purchases)
        {
            foreach (var purchase in purchases)
            {
                if (GameAccess.Get(purchase, "Key")?.ToString().StartsWith("Draft") == true
                    && GameAccess.Get(purchase, "Value") is int count)
                    bought += count;
            }
        }

        var location = ApData.Instance.ShopLocation(bought + 1, ApState.SlotInt("shop_draft_checks", 0));
        if (location != null)
            ApState.SendCheck(location);
    }

    private static void Record(MethodBase original, Func<string> detail, bool quiet = false)
        => ProbeTally.Record(typeof(CheckPatches), original, detail, quiet);
}
