using System.Collections;
using HarmonyLib;
using Reus2Archipelago.Archipelago;
using Reus2Archipelago.Game;

namespace Reus2Archipelago.Patches;

/// <summary>
/// Sends checks: a bioticum's first build, a combo rule's first activation, and the draft-paying
/// request rewards and shop draft purchases within a planet that the slot turned into checks.
/// </summary>
internal static class CheckPatches
{
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
    private static void AfterBioticumPlaced(object[] __args)
    {
        if (!ItemApplier.ProfileBound || __args == null || __args.Length == 0)
            return;
        var guid = GameAccess.DefinitionId(__args[0]);
        var entry = ApData.Instance.BioticumForGuid(guid);
        if (entry == null)
        {
            Plugin.Log.LogWarning($"Placed a bioticum the data does not hold: {guid ?? "no id"}.");
            return;
        }
        ApState.SendCheck(entry.BuildLocation);
    }

    // Rule states are also generated for the placement preview; only a placed bioticum counts.
    private static void AfterRuleActivityChanged(object __instance, bool value)
    {
        if (!value || !ItemApplier.ProfileBound)
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
            var location = ApData.Instance.ComboLocation(guid, index);
            if (location != null)
                ApState.SendCheck(location);
            return;
        }
    }

    // Runs before the reward is paid and before the request is counted, so the reward's number is
    // the level's completed count plus one.
    private static void BeforeRequestReward(object __instance, int level)
    {
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

    private static void BeforeShopPurchase(object __instance, object[] __args)
    {
        if (!ItemApplier.ProfileBound || __args == null || __args.Length == 0)
            return;
        var option = __args[0];
        if (option == null || !option.ToString().StartsWith("Draft"))
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
}
