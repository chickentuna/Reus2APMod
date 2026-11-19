using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using HarmonyLib;
using Reus2Archipelago.Archipelago;
using Reus2Archipelago.Game;

namespace Reus2Archipelago.Patches;

/// <summary>
/// Sends checks: a bioticum's first build and the moment every combo rule of one of its placements
/// is active at once, as bioticum_checks says, and the draft-paying request rewards and shop draft
/// purchases across planets. A three-star Discovery milestone
/// is counted only when its session wins.
/// </summary>
internal static class CheckPatches
{
    private const int BuildOnly = 0;
    private const int WhenSynergized = 1;
    private const int BuildAndSynergized = 2;
    private const int ProjectBuildOnly = 1;
    private const int ProjectWhenSynergized = 2;
    private const string FinalEra = "Era_4_Discovery";
    private const int ThreeStars = 3;
    private static readonly HashSet<string> ThreeStarFinalEraSessions = new HashSet<string>();
    private static readonly HashSet<object> ActiveRuleStates =
        new HashSet<object>(ObjectReferenceComparer.Instance);
    private static readonly Dictionary<object, object> RuleByState =
        new Dictionary<object, object>(ObjectReferenceComparer.Instance);
    private static readonly Dictionary<object, int> ActiveRuleCounts =
        new Dictionary<object, int>(ObjectReferenceComparer.Instance);
    private static readonly HashSet<object> ProjectRuleDefinitions =
        new HashSet<object>(ObjectReferenceComparer.Instance);
    private static readonly HashSet<object> ActiveProjects =
        new HashSet<object>(ObjectReferenceComparer.Instance);
    private static string RuleTrackingSessionId;

    internal static void Install(Harmony harmony)
    {
        var owner = typeof(CheckPatches);
        Patching.Apply(harmony, "GameplayController", "OnBioticumPlaced", owner, postfix: nameof(AfterBioticumPlaced));
        Patching.Apply(harmony, "RuleState", "set_IsActive", owner, postfix: nameof(AfterRuleActivityChanged));
        Patching.Apply(harmony, "BioticumSlot", "OnRuleActivityChanged", owner, postfix: nameof(AfterSlotRuleActivity));
        Patching.Apply(harmony, "BioticumSlot", "OnForEachRuleCountChanged", owner, postfix: nameof(AfterSlotForEachCount));
        Patching.Apply(harmony, "EraFeatController", "GiveRewardForQuest", owner, prefix: nameof(BeforeRequestReward));
        Patching.Apply(harmony, "GameplayShop", "BuyOption", owner,
            prefix: nameof(BeforeShopPurchase), postfix: nameof(AfterShopPurchase));
        Patching.Apply(harmony, "GameplayShop", "GetPrice", owner, postfix: nameof(AfterShopPrice));
        Patching.Apply(harmony, "Milestone", "AchieveMilestone", owner, postfix: nameof(AfterMilestoneAchieved));
        Patching.Apply(harmony, "Session", "WinSession", owner, postfix: nameof(AfterSessionWon));
        Patching.Apply(harmony, "CityProject", "Activate", owner, postfix: nameof(AfterProjectActivated));
        Patching.Apply(harmony, "CityProject", "Deactivate", owner, postfix: nameof(AfterProjectDeactivated));
        Patching.Apply(harmony, "WorldStageController", "CompleteTurningPoint", owner,
            prefix: nameof(BeforeTurningPointCompleted), postfix: nameof(AfterTurningPointCompleted));
        Patching.Apply(harmony, "TurningPointDisplayUI", "RenderRankings", owner,
            postfix: nameof(AfterTurningPointRankings));
    }

    private static void AfterTurningPointRankings(object __instance)
        => TurningPointStarBadges.Refresh(__instance);

    private static void AfterMilestoneAchieved(MethodBase __originalMethod, object __instance)
    {
        Record(__originalMethod, () => $"milestone={Naming.Of(GameAccess.Get(__instance, "definition"))}");
        if (!ItemApplier.ProfileBound)
            return;

        if (ApState.TurningPointChecksEnabled)
        {
            var milestoneId = GameAccess.DefinitionId(GameAccess.Get(__instance, "definition"));
            var star = ApData.Instance.TurningPointStarForMilestone(milestoneId);
            if (star != null && ApState.IsDlcEnabled(star.Dlc))
                ApState.SendCheck(star.Location);
        }

        var session = GameAccess.Get(GameAccess.SessionContext, "CurrentSession");
        var sessionId = GameAccess.Get(session, "SessionID") as string;
        var parameters = GameAccess.Get(session, "StartParameters");
        var gameplay = GameAccess.Gameplay;
        var stageController = GameAccess.Get(gameplay, "worldStageController");
        if (string.IsNullOrEmpty(sessionId)
            || GameAccess.Get(parameters, "finalEra")?.ToString() != FinalEra
            || GameAccess.Get(stageController, "CurrentEra")?.ToString() != FinalEra
            || !IsThreeStarCurrentTurningPoint(__instance, stageController))
            return;

        ThreeStarFinalEraSessions.Add(sessionId);
        Plugin.Log.LogInfo($"Three-star Discovery turning point completed in session {sessionId}.");
    }

    private static bool IsThreeStarCurrentTurningPoint(object milestone, object stageController)
    {
        var definition = GameAccess.Get(milestone, "definition");
        if (GameAccess.Get(definition, "category")?.ToString() != "TurningPoint"
            || !(GameAccess.Get(definition, "starsRequirement") is int stars)
            || stars != ThreeStars)
            return false;

        var current = GameAccess.Get(stageController, "CurrentTurningPoint");
        var currentDefinition = GameAccess.Get(current, "Definition")
                                ?? GameAccess.Get(current, "definition")
                                ?? current;
        var currentId = GameAccess.DefinitionId(currentDefinition);
        if (currentId == null || !(GameAccess.Get(definition, "turningPointRequirement") is IEnumerable requirements))
            return false;

        foreach (var requirement in requirements)
        {
            var requiredDefinition = GameAccess.Get(requirement, "Definition")
                                    ?? GameAccess.Get(requirement, "definition")
                                    ?? requirement;
            if (GameAccess.DefinitionId(requiredDefinition) == currentId)
                return true;
        }
        return false;
    }

    private static void AfterSessionWon(MethodBase __originalMethod)
    {
        var session = GameAccess.Get(GameAccess.SessionContext, "CurrentSession");
        var sessionId = GameAccess.Get(session, "SessionID") as string;
        Record(__originalMethod, () => $"session={sessionId} finished={GameAccess.Get(session, "IsFinished")}");
        if (string.IsNullOrEmpty(sessionId) || !ThreeStarFinalEraSessions.Remove(sessionId))
            return;

        if (!(GameAccess.Get(session, "IsFinished") is bool finished && finished)
            || GameAccess.Get(session, "PlanetIsLost") is bool lost && lost
            || GameAccess.Get(GameAccess.Get(session, "StartParameters"), "finalEra")?.ToString() != FinalEra)
            return;

        ApState.RecordFinalEraCompletion(sessionId);
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
        // Under when_synergized a bioticum that has combo rules carries one location, its combos, so
        // its build sends nothing; one with no rule keeps its build.
        var checks = ApState.SlotInt("bioticum_checks", BuildOnly);
        var hasSynergyCheck = entry.Rules.Any(rule => ApState.IsComboRuleEnabled(guid, rule.RuleIndex));
        if (checks == WhenSynergized && hasSynergyCheck)
            return;
        ApState.SendCheck(entry.BuildLocation);
        ApState.RecordGoalBioticum(entry);
    }

    // Rule states are also generated for the placement preview; only a placed bioticum counts.
    private static void AfterRuleActivityChanged(MethodBase __originalMethod, object __instance, bool value)
    {
        Record(__originalMethod, () => $"value={value} bound={ItemApplier.ProfileBound}", quiet: true);
        TrackRuleActivity(__instance, value);
        if (!value)
            return;
        SendCombo(__instance, "rule activity");
        var changedRule = RuleDefinition(__instance);
        if (changedRule != null && ProjectRuleDefinitions.Contains(changedRule))
            CheckActiveProjectSynergies(changedRule);
    }

    private static void AfterProjectActivated(MethodBase __originalMethod, object __instance)
    {
        Record(__originalMethod, () => $"project={Naming.Of(GameAccess.Get(__instance, "Definition"))}");
        if (!ItemApplier.ProfileBound || ApState.ProjectChecks == 0)
            return;

        EnsureRuleTrackingSession();
        var entry = ApData.Instance.ProjectForGuid(
            GameAccess.DefinitionId(GameAccess.Get(__instance, "Definition")));
        if (entry == null || !ProjectDlcEnabled(entry))
            return;

        if (ApState.ProjectChecks == ProjectBuildOnly)
        {
            ApState.SendCheck(entry.Location);
            return;
        }

        var rules = ProjectRules(__instance);
        ActiveProjects.Add(__instance);
        foreach (var rule in rules)
            ProjectRuleDefinitions.Add(rule);
        if (rules.Count == 0)
            ApState.SendCheck(entry.Location);
        else
            CheckProjectSynergy(__instance, entry, rules);
    }

    private static void AfterProjectDeactivated(object __instance)
    {
        ActiveProjects.Remove(__instance);
        ProjectRuleDefinitions.Clear();
        foreach (var project in ActiveProjects)
        {
            foreach (var rule in ProjectRules(project))
                ProjectRuleDefinitions.Add(rule);
        }
    }

    private static void CheckActiveProjectSynergies(object changedRule)
    {
        foreach (var project in ActiveProjects.ToArray())
        {
            if (GameAccess.Get(project, "IsArchived") is bool archived && archived)
                continue;

            var rules = ProjectRules(project);
            if (!rules.Any(rule => ReferenceEquals(rule, changedRule)))
                continue;

            var entry = ApData.Instance.ProjectForGuid(
                GameAccess.DefinitionId(GameAccess.Get(project, "Definition")));
            if (entry != null && ProjectDlcEnabled(entry))
                CheckProjectSynergy(project, entry, rules);
        }
    }

    private static bool ProjectDlcEnabled(ProjectEntry entry)
    {
        if (!string.IsNullOrEmpty(entry.Dlc))
            return ApState.IsDlcEnabled(entry.Dlc);
        return entry.Dlcs.Count == 0 || entry.Dlcs.Any(ApState.IsDlcEnabled);
    }

    private static void CheckProjectSynergy(object project, ProjectEntry entry, List<object> rules)
    {
        if (ApState.ProjectChecks != ProjectWhenSynergized)
            return;
        foreach (var rule in rules)
        {
            if (!ActiveRuleCounts.TryGetValue(rule, out var count) || count == 0)
                return;
        }
        ApState.SendCheck(entry.Location);
    }

    private static List<object> ProjectRules(object project)
    {
        var result = new List<object>();
        if (!(GameAccess.Get(project, "Rules") is IEnumerable rules))
            return result;

        foreach (var rule in rules)
        {
            if (rule != null && !rule.GetType().Name.StartsWith("Fake", StringComparison.Ordinal))
                result.Add(rule);
        }
        return result;
    }

    private static void TrackRuleActivity(object state, bool active)
    {
        EnsureRuleTrackingSession();
        var context = GameAccess.Get(state, "slotContext");
        if (context != null
            && (GameAccess.Get(context, "PreviewedBioticum") != null
                || GameAccess.Get(GameAccess.Get(context, "slot"), "IsFutureSlot") is bool future && future))
        {
            if (ActiveRuleStates.Remove(state) && RuleByState.TryGetValue(state, out var previousPreviewRule))
            {
                DecrementActiveRule(previousPreviewRule);
                RuleByState.Remove(state);
            }
            return;
        }

        if (ActiveRuleStates.Remove(state) && RuleByState.TryGetValue(state, out var previousRule))
        {
            DecrementActiveRule(previousRule);
            RuleByState.Remove(state);
        }
        if (!active)
            return;

        var rule = RuleDefinition(state);
        if (rule == null)
            return;
        ActiveRuleStates.Add(state);
        RuleByState[state] = rule;
        ActiveRuleCounts[rule] = ActiveRuleCounts.TryGetValue(rule, out var count) ? count + 1 : 1;
    }

    private static object RuleDefinition(object state)
        => GameAccess.Get(state, "Rule") ?? GameAccess.Get(state, "RuleDefinitionBase");

    private static void DecrementActiveRule(object rule)
    {
        if (!ActiveRuleCounts.TryGetValue(rule, out var count))
            return;
        if (count <= 1)
            ActiveRuleCounts.Remove(rule);
        else
            ActiveRuleCounts[rule] = count - 1;
    }

    private static void EnsureRuleTrackingSession()
    {
        var sessionId = GameAccess.SessionId;
        if (sessionId == RuleTrackingSessionId)
            return;

        RuleTrackingSessionId = sessionId;
        ActiveRuleStates.Clear();
        RuleByState.Clear();
        ActiveRuleCounts.Clear();
        ProjectRuleDefinitions.Clear();
        ActiveProjects.Clear();
    }

    private static void BeforeTurningPointCompleted(object __instance, out string __state)
    {
        var current = GameAccess.Get(__instance, "CurrentTurningPoint");
        var definition = GameAccess.Get(current, "Definition") ?? GameAccess.Get(current, "definition") ?? current;
        __state = GameAccess.DefinitionId(definition);
    }

    private static void AfterTurningPointCompleted(MethodBase __originalMethod, string __state)
    {
        Record(__originalMethod, () => $"turningPoint={__state}", quiet: true);
        if (!ItemApplier.ProfileBound || !ApState.TurningPointChecksEnabled || string.IsNullOrEmpty(__state))
            return;

        var star = ApData.Instance.TurningPointStarForTurningPoint(__state, 1);
        if (star != null && ApState.IsDlcEnabled(star.Dlc))
            ApState.SendCheck(star.Location);
    }

    /// <summary>Resolves a rule state to the bioticum it belongs to and sends its combos location when
    /// every combo rule of that placement is active at this moment. Three callbacks reach this; a
    /// location is only ever sent once.</summary>
    private static void SendCombo(object ruleState, string source)
    {
        if (ruleState == null || !ItemApplier.ProfileBound)
            return;
        var mode = ApState.SlotInt("bioticum_checks", BuildOnly);
        if (mode == BuildOnly)
            return;

        var context = GameAccess.Get(ruleState, "slotContext");
        if (context == null)
        {
            Bail(ruleState, $"{source}: the rule has no slot context");
            return;
        }
        if (GameAccess.Get(context, "PreviewedBioticum") != null)
            return;

        var slot = GameAccess.Get(context, "slot");
        // Ordering a build grows the bioticum in the slot's future copy until the giant finishes, and
        // that copy's rules activate at once; the game skips it for its own triggers the same way.
        if (GameAccess.Get(slot, "IsFutureSlot") is bool future && future)
            return;
        var definition = GameAccess.Get(slot, "BioticumDefinition");
        var guid = GameAccess.DefinitionId(definition);
        if (guid == null)
        {
            Bail(ruleState, $"{source}: the slot holds no bioticum definition");
            return;
        }
        // BioticumDefinition.rules is a ReferenceList, which carries IList<T> but not IList, so it is
        // walked as a plain sequence; its position is the rule index the data was built with.
        if (!(GameAccess.Get(definition, "rules") is IEnumerable rules))
        {
            Bail(ruleState, $"{source}: the definition has no rules list");
            return;
        }

        var name = ApData.Instance.BioticumForGuid(guid)?.Name ?? guid;
        var rule = GameAccess.Get(ruleState, "RuleDefinitionBase");
        var index = -1;
        foreach (var candidate in rules)
        {
            index++;
            if (!ReferenceEquals(candidate, rule))
                continue;
            if (ApState.IsComboRuleEnabled(guid, index) && ApData.Instance.ComboRule(guid, index) != null)
                SendWhenEveryRuleActive(slot, guid, rules);
            else
                Bail(ruleState, $"{source}: rule {index} of {name} carries no location");
            return;
        }
        Bail(ruleState, $"{source}: the rule is not in {name}'s rules list");
    }

    private static readonly HashSet<string> BailReasons = new HashSet<string>();

    /// <summary>Says once why an activation sent nothing; a rule recalculates every frame.</summary>
    private static void Bail(object ruleState, string reason)
    {
        var key = $"{ruleState?.GetType().Name}: {reason}";
        if (BailReasons.Add(key))
            Plugin.Log.LogWarning($"Combo activation sent no check — {key}");
    }

    /// <summary>The slot's own activity callback, which PuzzleBonusHandler subscribes for a placed
    /// bioticum. A ForEach rule is not routed here: its count goes to OnForEachRuleCountChanged.</summary>
    private static void AfterSlotRuleActivity(MethodBase __originalMethod, object[] __args)
    {
        Record(__originalMethod, () => $"state={__args?[0]} bound={ItemApplier.ProfileBound}", quiet: true);
        if (__args == null || __args.Length < 2 || !(__args[0] is bool state) || !state)
            return;
        SendCombo(__args[1], "slot activity");
    }

    /// <summary>The slot's callback for a ForEach rule, which counts rather than flips active, and
    /// carries 229 of the 397 combo rules.</summary>
    private static void AfterSlotForEachCount(MethodBase __originalMethod, object[] __args)
    {
        Record(__originalMethod, () => $"diff={__args?[1]} bound={ItemApplier.ProfileBound}", quiet: true);
        if (__args == null || __args.Length < 3 || !(__args[1] is int diff) || diff <= 0)
            return;
        SendCombo(__args[2], "slot for-each count");
    }

    /// <summary>Sends the combos location when, on this slot, the state of every rule the data keeps
    /// is active right now. A counting rule is active while its count is above 0. The last rule to
    /// turn active is the one that sends, whatever order they fire in.</summary>
    private static void SendWhenEveryRuleActive(object slot, string guid, IEnumerable rules)
    {
        var bioticum = ApData.Instance.BioticumForGuid(guid);
        if (bioticum?.AllCombosLocation == null)
            return;
        if (!(GameAccess.Get(GameAccess.Get(slot, "BonusState"), "AllRuleStates") is IEnumerable states))
            return;

        var active = new List<object>();
        foreach (var state in states)
        {
            if (GameAccess.Get(state, "IsActive") is bool on && on)
                active.Add(GameAccess.Get(state, "RuleDefinitionBase"));
        }

        var index = -1;
        foreach (var rule in rules)
        {
            index++;
            if (!ApState.IsComboRuleEnabled(guid, index) || ApData.Instance.ComboRule(guid, index) == null)
                continue;
            if (!active.Exists(definition => ReferenceEquals(definition, rule)))
                return;
        }
        ApState.SendCheck(bioticum.AllCombosLocation);
        ApState.RecordGoalBioticum(bioticum);
    }

    // Level 1 to 3 rewards use the next open global check; level 4 keeps its per-planet index.
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
        var location = level < 3
            ? ApData.Instance.RequestLocations(level).FirstOrDefault(ApState.IsOpen)
            : ApData.Instance.RequestLocation(level, index, ApState.SlotInt("request_reward_checks", 0));
        if (location != null)
            ApState.SendCheck(location);
    }

    private static void AfterShopPrice(
        MethodBase __originalMethod,
        ShopPriceOption option,
        ref (CityStat, int) __result)
    {
        if (!ItemApplier.ProfileBound || !IsDraftOption(option))
            return;

        var count = ProfileBinding.ShopDraftPurchases(GameAccess.CurrentProfile, option.ToString());
        var price = 5 + count * 10;
        __result = (__result.Item1, price);
        Record(__originalMethod, () => $"option={option} purchases={count} price={price}", quiet: true);
    }

    private static void BeforeShopPurchase(
        MethodBase __originalMethod,
        object __instance,
        object[] __args,
        out int __state)
    {
        __state = -1;
        var option = __args != null && __args.Length > 0 && __args[0] is ShopPriceOption value
            ? value
            : (ShopPriceOption?)null;
        Record(__originalMethod, () => $"option={Naming.Of(option)} bound={ItemApplier.ProfileBound}");
        if (!ItemApplier.ProfileBound || !option.HasValue || !IsDraftOption(option.Value))
            return;

        __state = ShopPurchaseCount(__instance, option.Value);
    }

    private static void AfterShopPurchase(object __instance, object[] __args, int __state)
    {
        if (__state < 0 || !ItemApplier.ProfileBound
            || __args == null || __args.Length == 0
            || !(__args[0] is ShopPriceOption option) || !IsDraftOption(option)
            || ShopPurchaseCount(__instance, option) <= __state)
            return;

        ProfileBinding.RecordShopDraftPurchase(GameAccess.CurrentProfile, option.ToString());
        var location = ApData.Instance.ShopLocations().FirstOrDefault(ApState.IsOpen);
        if (location != null)
            ApState.SendCheck(location);
    }

    private static int ShopPurchaseCount(object shop, ShopPriceOption option)
    {
        if (!(GameAccess.Get(shop, "buyCount") is IEnumerable purchases))
            return 0;

        foreach (var purchase in purchases)
            if (GameAccess.Get(purchase, "Key") is ShopPriceOption key && key == option
                && GameAccess.Get(purchase, "Value") is int count)
                return count;

        return 0;
    }

    private static bool IsDraftOption(ShopPriceOption option)
        => option == ShopPriceOption.DraftPop
           || option == ShopPriceOption.DraftTech
           || option == ShopPriceOption.DraftWealth;

    private static void Record(MethodBase original, Func<string> detail, bool quiet = false)
        => ProbeTally.Record(typeof(CheckPatches), original, detail, quiet);

    private sealed class ObjectReferenceComparer : IEqualityComparer<object>
    {
        internal static readonly ObjectReferenceComparer Instance = new ObjectReferenceComparer();

        public new bool Equals(object left, object right) => ReferenceEquals(left, right);
        public int GetHashCode(object value) => RuntimeHelpers.GetHashCode(value);
    }
}
