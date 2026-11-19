"""Evaluates every apworld location rule with a full inventory and lists the ones that still fail.

A location that fails here can never be reached, which Archipelago reports as "Location Accessibility
requirements not fulfilled". Runs without an Archipelago install by stubbing the modules the rules
import.

Usage:  python tools/check_logic.py
"""

import importlib.util
import itertools
import sys
import types
from pathlib import Path

WORLD = Path(__file__).resolve().parent.parent / "apworld" / "reus2"


def load_world_modules():
    base = types.ModuleType("BaseClasses")
    base.CollectionState = object
    base.Item = type("Item", (), {})
    base.ItemClassification = type("ItemClassification", (), {"progression": 1, "useful": 2, "filler": 0})
    base.Location = type("Location", (), {})
    base.LocationProgressType = type("LocationProgressType", (), {"EXCLUDED": 2})
    options = types.ModuleType("Options")
    for name in ("Choice", "Range", "PerGameCommonOptions", "StartInventoryPool", "Toggle", "DefaultOnToggle"):
        setattr(options, name, type(name, (), {}))
    sys.modules["BaseClasses"] = base
    sys.modules["Options"] = options

    package = types.ModuleType("reus2")
    package.__path__ = [str(WORLD)]
    sys.modules["reus2"] = package
    loaded = {}
    for name in ("game_data", "options", "items", "locations", "rules"):
        spec = importlib.util.spec_from_file_location(f"reus2.{name}", WORLD / f"{name}.py")
        module = importlib.util.module_from_spec(spec)
        sys.modules[f"reus2.{name}"] = module
        spec.loader.exec_module(module)
        loaded[name] = module
    return loaded


class FullInventory:
    def has(self, name, player, count=1):
        return True

    def has_any(self, names, player):
        return bool(names)

    def has_all(self, names, player):
        return True


class MissingFinalEra(FullInventory):
    def has(self, name, player, count=1):
        if name == "Progressive Era" and count >= 2:
            return False
        return True


class MissingProgressiveEras(FullInventory):
    def has(self, name, player, count=1):
        if name == "Progressive Era" and count > 0:
            return False
        return True


def main():
    modules = load_world_modules()
    data = modules["game_data"].DATA
    game_data = modules["game_data"]
    options = modules["options"]
    assert options.GoalBuilds.default == 200
    assert options.GoalBuilds.range_end >= len(data["biotica"])
    assert options.GoalFinalEras.default == 4
    assert options.GoalFinalEras.range_start == 0
    assert options.GoalFinalEras.range_end == 15
    assert options.IceAge.default is False
    assert options.Everglades.default is False
    assert options.Grasslands.default is False
    assert options.Jurassic.default is False
    assert options.ProjectChecks.default == options.ProjectChecks.option_build_only
    assert options.TurningPointChecks.default is True
    assert options.OnlyMaxLevelBiotica.default is True
    assert len(data["final_era_checks"]) == 22
    assert len({entry["location_id"] for entry in data["final_era_checks"]}) == 22
    assert len(data["biotica"]) == 531
    assert len(data["characters"]) == 24
    assert len(data["giants"]) == 10
    assert len(data["biomes"]) == 10
    assert len(data["dlc_turning_points"]) == 13
    assert len(data["projects"]) == 140
    assert len(data["turning_point_stars"]) == 96
    assert len(data["request_rewards"]) == 22
    assert len(data["shop_drafts"]) == 512
    assert sum(reward["level"] < 3 for reward in data["request_rewards"]) == 16
    assert [sum(reward["level"] == level for reward in data["request_rewards"]) for level in range(4)] == [
        4, 6, 6, 6]
    assert options.RequestRewardChecks.default == 6
    assert options.RequestRewardChecks.range_end == 6
    assert {entry["star"] for entry in data["turning_point_stars"]} == {1, 2, 3}
    assert all(entry["visual_name"] != "Construction Site" for entry in data["projects"])
    assert len({entry["guid"] for entry in data["projects"]}) == len(data["projects"])
    assert len({entry["location_id"] for entry in data["projects"]}) == len(data["projects"])
    assert all(entry["milestone_guid"] for entry in data["turning_point_stars"] if entry["star"] > 1)
    all_dlcs = {"Ice Age", "Everglades", "Grasslands", "Jurassic"}
    direct_dlc_data = dict(data)
    direct_dlc_data["projects"] = [{"dlc": "Ice Age", "dlcs": ["Everglades"]}]
    assert not game_data.select_data(direct_dlc_data, False, True)["projects"]
    assert game_data.select_data(direct_dlc_data, True, False)["projects"] == direct_dlc_data["projects"]
    for key in ("biotica", "giants", "biomes", "characters", "micros", "projects", "turning_point_stars"):
        assert {entry["dlc"] for entry in data[key] if entry.get("dlc")} <= all_dlcs
    locations = modules["locations"]
    assert locations.biotica_with_locations(2, False, data) == [
        b for b in data["biotica"] if b["level"] <= 2]
    assert locations.biotica_with_locations(2, True, data) == [
        b for b in data["biotica"]
        if b["level"] == 2 or (b["level"] <= 2 and locations.has_evolve_rule(b))]
    checks = options.BioticumChecks
    starting_biomes = {
        giant["first_biome"] for giant in data["giants"] if giant.get("first_biome")
    }
    active_early_locations = locations.progressive_era_early_locations(
        data["turning_point_stars"], data["request_rewards"], data["shop_drafts"],
        data["biotica"], checks.option_build_only, starting_biomes, False)
    genesis_turning_points = {
        check["location"] for check in data["turning_point_stars"] if check["era"] == "Era_1_Genesis"
    }
    later_turning_points = {
        check["location"] for check in data["turning_point_stars"] if check["era"] != "Era_1_Genesis"
    }
    assert genesis_turning_points <= active_early_locations
    assert not later_turning_points & active_early_locations
    assert {reward["location"] for reward in data["request_rewards"] if reward["level"] < 2} <= \
        active_early_locations
    assert {draft["location"] for draft in data["shop_drafts"][:6]} <= active_early_locations
    assert data["shop_drafts"][6]["location"] not in active_early_locations
    level_one_biotica = [b for b in data["biotica"] if b["level"] == 1]
    starting_level_one = [
        b for b in level_one_biotica if starting_biomes.intersection(b["biomes"])
    ]
    assert {b["build_location"] for b in starting_level_one} <= active_early_locations
    assert not {b["build_location"] for b in level_one_biotica if b not in starting_level_one} \
        & active_early_locations
    assert not {p["location"] for p in data["projects"]} & active_early_locations
    both_biotica_checks = locations.progressive_era_early_locations(
        [], [], [], level_one_biotica, checks.option_build_and_synergized, starting_biomes, False)
    assert {b["all_combos_location"] for b in starting_level_one if b["rules"]} \
        <= both_biotica_checks

    base_data = game_data.selected_data()
    base_starting_biomes = {
        giant["first_biome"] for giant in base_data["giants"] if giant.get("first_biome")
    }
    base_level_one = [b for b in base_data["biotica"] if b["level"] == 1]
    base_early = locations.progressive_era_early_locations(
        [], [], [], base_level_one, checks.option_build_only, base_starting_biomes, False)
    base_starting_level_one = [
        b for b in base_level_one if base_starting_biomes.intersection(b["biomes"])
    ]
    assert {b["build_location"] for b in base_starting_level_one} <= base_early
    assert not {b["build_location"] for b in base_level_one if b not in base_starting_level_one} \
        & base_early

    evolve_biotica = [b for b in data["biotica"] if locations.has_evolve_rule(b)]
    assert evolve_biotica
    assert all(b in locations.biotica_with_locations(3, True, data) for b in evolve_biotica)

    class TestItem:
        def __init__(self, name):
            self.name = name

    early_location = types.SimpleNamespace(name="Genesis TP", item_rule=lambda item: True)
    late_location = types.SimpleNamespace(name="Ancient TP", item_rule=lambda item: True)
    locations.restrict_item_to_locations(
        [early_location, late_location], "Progressive Era", {"Genesis TP"})
    assert early_location.item_rule(TestItem("Progressive Era"))
    assert not late_location.item_rule(TestItem("Progressive Era"))
    assert late_location.item_rule(TestItem("Filler"))

    no_eras = MissingProgressiveEras()
    assert all(
        modules["rules"].Reus2Rules.turning_point_reachable(
            types.SimpleNamespace(data=data, player=1, era_item=data["eras"]["item"]), no_eras, check)
        for check in data["turning_point_stars"] if check["era"] == "Era_2_Ancient"
    )
    assert not any(
        modules["rules"].Reus2Rules.turning_point_reachable(
            types.SimpleNamespace(data=data, player=1, era_item=data["eras"]["item"]), no_eras, check)
        for check in data["turning_point_stars"] if check["era"] == "Era_3_Medieval"
    )
    item_table = modules["items"].item_table()
    assert all(entry["item"] in item_table for key in ("biotica", "giants", "biomes", "characters", "micros")
               for entry in data[key])
    assert locations.final_era_location_count(0) == 6
    assert locations.final_era_location_count(4) == 6
    assert locations.final_era_location_count(options.MAX_FINAL_ERA_GOAL) == 17
    assert locations.final_era_location_count(20) == 22
    assert locations.shop_draft_location_count(326, 332, 512) == 24
    assert locations.shop_draft_location_count(326, 332, 512, 15) == 39
    assert locations.shop_draft_location_count(0, 332, 512) == 15
    final_era_names = {check["location"] for check in data["final_era_checks"]}
    final_era_names.update(
        check["location"] for check in locations.turning_point_checks(data, 4)
        if check["era"] == "Era_4_Discovery")
    excluded = types.SimpleNamespace(name="Final Era", progress_type=0)
    ordinary = types.SimpleNamespace(name="Ordinary", progress_type=0)
    locations.mark_locations_excluded([excluded, ordinary], final_era_names | {"Final Era"})
    assert excluded.progress_type == modules["locations"].LocationProgressType.EXCLUDED
    assert ordinary.progress_type == 0
    for final_era_goal in (0, 1, 4, options.MAX_FINAL_ERA_GOAL):
        limited_checks = locations.turning_point_checks(data, final_era_goal)
        final_era_checks = [check for check in limited_checks if check["era"] == "Era_4_Discovery"]
        all_final_era_turning_points = {
            check["turning_point_guid"] for check in data["turning_point_stars"]
            if check["era"] == "Era_4_Discovery"
        }
        expected_final_era_count = min(max(0, final_era_goal - 1), len(all_final_era_turning_points)) * 3
        assert len(final_era_checks) == expected_final_era_count
        assert [check for check in limited_checks if check["era"] != "Era_4_Discovery"] == [
            check for check in data["turning_point_stars"] if check["era"] != "Era_4_Discovery"]
        assert all(
            sum(check["turning_point_guid"] == turning_point for check in final_era_checks) == 3
            for turning_point in {check["turning_point_guid"] for check in final_era_checks}
        )
    location_table = locations.location_table()
    assert all(entry["location"] in location_table
               for key in ("request_rewards", "shop_drafts") for entry in data[key])
    assert all(entry["location"] in location_table
               for entry in data["final_era_checks"])
    assert all(b["build_location"] in location_table for b in data["biotica"])
    assert all(b["all_combos_location"] in location_table
               for b in data["biotica"] if b["rules"])
    assert all(entry["location"] in location_table for entry in data["projects"])
    assert all(entry["location"] in location_table for entry in data["turning_point_stars"])

    checks = options.BioticumChecks
    values = (checks.option_build_only, checks.option_when_synergized, checks.option_build_and_synergized)
    dlc_selections = tuple(itertools.product((False, True), repeat=4))
    expected_biotica_counts = {
        (False, False, False, False): 353, (False, False, False, True): 419,
        (False, False, True, False): 355, (False, False, True, True): 421,
        (False, True, False, False): 412, (False, True, False, True): 478,
        (False, True, True, False): 414, (False, True, True, True): 480,
        (True, False, False, False): 404, (True, False, False, True): 470,
        (True, False, True, False): 406, (True, False, True, True): 472,
        (True, True, False, False): 463, (True, True, False, True): 529,
        (True, True, True, False): 465, (True, True, True, True): 531,
    }
    expected_project_counts = {
        (False, False, False, False): 106, (False, False, False, True): 118,
        (False, False, True, False): 115, (False, False, True, True): 126,
        (False, True, False, False): 121, (False, True, False, True): 131,
        (False, True, True, False): 130, (False, True, True, True): 139,
        (True, False, False, False): 114, (True, False, False, True): 121,
        (True, False, True, False): 121, (True, False, True, True): 128,
        (True, True, False, False): 127, (True, True, False, True): 133,
        (True, True, True, False): 134, (True, True, True, True): 140,
    }
    expected_turning_point_star_counts = {
        selection: 57 + 12 * selection[0] + 9 * selection[1] + 9 * selection[2] + 9 * selection[3]
        for selection in dlc_selections
    }
    vanilla_data = game_data.selected_data()
    vanilla_rule_ids = sorted(
        f'{bioticum["guid"]}#{rule["rule_index"]}'
        for bioticum in vanilla_data["biotica"]
        for rule in bioticum["rules"]
    )
    assert data["base_combo_rule_ids"] == vanilla_rule_ids

    failures = []
    rules_module = modules["rules"]
    modes = []
    for ice_age, everglades, grasslands, jurassic in dlc_selections:
        selected = game_data.selected_data(ice_age, everglades, grasslands, jurassic)
        enabled = ({name for name, flag in (
            ("Ice Age", ice_age), ("Everglades", everglades),
            ("Grasslands", grasslands), ("Jurassic", jurassic)) if flag})
        selection = (f"ice_age={ice_age} everglades={everglades} "
                     f"grasslands={grasslands} jurassic={jurassic}")
        if len(selected["biotica"]) != expected_biotica_counts[
                (ice_age, everglades, grasslands, jurassic)]:
            failures.append((selection, "data", "unexpected active biotica count"))
        selection_tuple = (ice_age, everglades, grasslands, jurassic)
        if len(selected["projects"]) != expected_project_counts[selection_tuple]:
            failures.append((selection, "projects", "unexpected active project count"))
        if len(selected["turning_point_stars"]) != expected_turning_point_star_counts[selection_tuple]:
            failures.append((selection, "Turning Point stars", "unexpected active check count"))
        limited_turning_points = locations.turning_point_checks(selected, 4)
        selected_final_era_tps = {
            check["turning_point_guid"] for check in selected["turning_point_stars"]
            if check["era"] == "Era_4_Discovery"
        }
        if sum(check["era"] == "Era_4_Discovery" for check in limited_turning_points) != \
                min(3, len(selected_final_era_tps)) * 3:
            failures.append((selection, "Final Era Turning Points", "wrong number of checks for goal 4"))
        for key in ("biotica", "giants", "biomes", "characters", "micros", "projects", "turning_point_stars"):
            for entry in selected[key]:
                if entry.get("dlc") and entry["dlc"] not in enabled:
                    failures.append((selection, key, "disabled DLC content was included"))
                elif not entry.get("dlc") and entry.get("dlcs") and not set(entry["dlcs"]) & enabled:
                    failures.append((selection, key, "disabled DLC content was included"))
        active_item_table = modules["items"].item_table(selected)
        if any(entry["item"] not in active_item_table
               for key in ("biotica", "giants", "biomes", "characters", "micros")
               for entry in selected[key]):
            failures.append((selection, "items", "an active DLC item has no Archipelago id"))
        active_rule_ids = {
            f'{bioticum["guid"]}#{rule["rule_index"]}'
            for bioticum in selected["biotica"]
            for rule in bioticum["rules"]
        }
        if (not any((ice_age, everglades, grasslands, jurassic))
                and sorted(active_rule_ids) != data["base_combo_rule_ids"]):
            failures.append((selection, "combo rules", "legacy combo-rule fallback differs from vanilla data"))

        for max_level, bioticum_items, bioticum_checks in (
                (max_level, bioticum_items, bioticum_checks)
                for max_level in (1, 2, 3)
                for bioticum_items in (0, 1)
                for bioticum_checks in values):
            mode = (f"{selection} max_biotica_level={max_level} "
                    f"bioticum_items={bioticum_items} bioticum_checks={bioticum_checks}")
            modes.append(mode)
            world = types.SimpleNamespace(player=1, data=selected, options=types.SimpleNamespace(
                max_biotica_level=types.SimpleNamespace(value=max_level),
                bioticum_items=types.SimpleNamespace(value=bioticum_items),
                bioticum_checks=types.SimpleNamespace(value=bioticum_checks),
                only_max_level_biotica=types.SimpleNamespace(value=False)))
            rules = rules_module.Reus2Rules(world)
            state = FullInventory()
            biotica = modules["items"].biotica_up_to(max_level, selected)
            for bioticum in biotica:
                combos = bioticum["rules"]
                if not (combos and bioticum_checks == checks.option_when_synergized):
                    if not rules.can_build(state, bioticum):
                        failures.append((mode, bioticum["build_location"], "build"))
                if combos and bioticum_checks != checks.option_build_only:
                    if not rules.can_combo_all(state, bioticum):
                        failures.append((mode, bioticum["all_combos_location"], "all combos"))
            for reward in data["request_rewards"]:
                if not rules.request_reward_ok(state, reward):
                    failures.append((mode, reward["location"], "request reward"))
            for draft in data["shop_drafts"]:
                if not rules.shop_draft_ok(state, draft):
                    failures.append((mode, draft["location"], "shop draft"))
            for project in selected["projects"]:
                if not rules.project_reachable(state, project):
                    failures.append((mode, project["location"], "project"))
                if project["level"] >= 3 and rules.project_reachable(MissingFinalEra(), project):
                    failures.append((mode, project["location"], "project reachable without its era item"))
            for turning_point_star in selected["turning_point_stars"]:
                if not rules.turning_point_reachable(state, turning_point_star):
                    failures.append((mode, turning_point_star["location"], "Turning Point star"))
                if (turning_point_star["era"] == "Era_4_Discovery"
                        and rules.turning_point_reachable(MissingFinalEra(), turning_point_star)):
                    failures.append((mode, turning_point_star["location"],
                                     "Turning Point star reachable without Discovery access"))
            for final_era in data["final_era_checks"]:
                if not rules.final_era_reachable(state):
                    failures.append((mode, final_era["location"], "final era"))
            if not rules.builds_reachable(state, len(biotica)):
                failures.append((mode, "goal", "not every build reachable"))
            highest_goal = min(options.GoalBuilds.range_end, len(biotica))
            if not rules.goal_reachable(state, highest_goal):
                failures.append((mode, "goal", f"the highest goal, {highest_goal}, is not reachable"))
            if not rules.goal_reachable(state, highest_goal, options.GoalFinalEras.range_end):
                failures.append((mode, "goal", "the highest combined goal is not reachable"))
            if rules.final_era_reachable(MissingFinalEra()):
                failures.append((mode, "final era", "reachable without both Progressive Era items"))
            if rules.goal_reachable(MissingFinalEra(), 0, 1):
                failures.append((mode, "goal", "combined goal is reachable without Discovery access"))

            strict_world = types.SimpleNamespace(player=1, data=selected, options=types.SimpleNamespace(
                max_biotica_level=types.SimpleNamespace(value=max_level),
                bioticum_items=types.SimpleNamespace(value=bioticum_items),
                bioticum_checks=types.SimpleNamespace(value=bioticum_checks),
                only_max_level_biotica=types.SimpleNamespace(value=True)))
            strict_rules = rules_module.Reus2Rules(strict_world)
            if not strict_rules.builds_reachable(state, len(biotica)):
                failures.append((mode, "max-level goal", "omitted biotica below the max level"))
            if strict_rules.builds_reachable(state, len(biotica) + 1):
                failures.append((mode, "max-level goal", "counted biotica above the max level"))
            if not strict_rules.goal_reachable(state, len(biotica)):
                failures.append((mode, "max-level goal", "goal omitted biotica below the max level"))
            if strict_rules.goal_reachable(state, len(biotica) + 1):
                failures.append((mode, "max-level goal", "goal counted biotica above the max level"))
            for reward in data["request_rewards"]:
                if not strict_rules.request_reward_ok(state, reward):
                    failures.append((mode, reward["location"], "max-level option affected request access"))

    if failures:
        print(f"{len(failures)} unreachable:")
        for mode, location, reason in failures:
            print(f"  [{mode}] {location}  ({reason})")
        sys.exit(1)
    print(f"every location is reachable with a full inventory, in all {len(modes)} DLC, max_biotica_level,"
          " bioticum_items and bioticum_checks combinations")


if __name__ == "__main__":
    main()
