from typing import Dict, List

from BaseClasses import Location, LocationProgressType

from .game_data import DATA
from .options import BioticumChecks


class Reus2Location(Location):
    game = "Reus 2"


def final_era_location_count(goal: int) -> int:
    return min(len(DATA["final_era_checks"]), max(6, goal + 2))


def shop_draft_location_count(item_count: int, non_shop_location_count: int, available: int,
                              excluded_location_count: int = 0) -> int:
    """Keep 30 non-excluded filler slots after placing every important item."""
    count = max(15, item_count - non_shop_location_count + excluded_location_count + 30)
    if count > available:
        raise ValueError(f"shop safeguard needs {count} locations, but only {available} are defined")
    return count


def mark_locations_excluded(locations, excluded_names: set) -> None:
    for location in locations:
        if location.name in excluded_names:
            location.progress_type = LocationProgressType.EXCLUDED


def progressive_era_early_locations(turning_points, request_rewards, shop_drafts, biotica,
                                    bioticum_checks: int) -> set:
    """Active checks suitable for Progressive Era items; other worlds remain valid placements."""
    names = {check["location"] for check in turning_points if check["era"] == "Era_1_Genesis"}
    names.update(reward["location"] for reward in request_rewards if reward["level"] < 2)
    names.update(draft["location"] for draft in shop_drafts[:6])
    for bioticum in biotica:
        if bioticum["level"] != 1:
            continue
        combos = bioticum["rules"]
        if combos and bioticum_checks != BioticumChecks.option_build_only:
            names.add(bioticum["all_combos_location"])
        if not (combos and bioticum_checks == BioticumChecks.option_when_synergized):
            names.add(bioticum["build_location"])
    return names


def restrict_item_to_locations(locations, item_name: str, allowed_names: set) -> None:
    """Restrict an item in this world's locations without limiting placements in other worlds."""
    for location in locations:
        if location.name in allowed_names:
            continue
        previous_rule = location.item_rule
        location.item_rule = lambda item, rule=previous_rule: (
            item.name != item_name and rule(item))


def biotica_with_locations(max_level: int, only_max_level: bool, data=DATA) -> List[dict]:
    """Biotica that have build or synergy locations for these options."""
    return [b for b in data["biotica"]
            if b["level"] <= max_level and (not only_max_level or b["level"] == max_level)]


def turning_point_checks(data, final_era_goal: int) -> List[dict]:
    """Keep all non-Final-Era checks, and at most `final_era_goal - 1` Final Era Turning Points."""
    final_era_turning_points = max(0, final_era_goal - 1)
    seen_final_era_turning_points = set()
    checks = []
    for check in data["turning_point_stars"]:
        if check["era"] != "Era_4_Discovery":
            checks.append(check)
            continue
        turning_point = check["turning_point_guid"]
        if turning_point not in seen_final_era_turning_points:
            if len(seen_final_era_turning_points) >= final_era_turning_points:
                continue
            seen_final_era_turning_points.add(turning_point)
        checks.append(check)
    return checks


def location_table() -> Dict[str, int]:
    """Every name and id, holding the build and Synergized locations whatever `bioticum_checks` is:
    `Reus2World.location_name_to_id` is set on the class, where the options of a slot cannot be read."""
    table = {}
    for b in DATA["biotica"]:
        table[b["build_location"]] = b["build_location_id"]
        if b["rules"]:
            table[b["all_combos_location"]] = b["all_combos_location_id"]
    for key in ("request_rewards", "shop_drafts"):
        table.update({entry["location"]: entry["location_id"] for entry in DATA[key]})
    table.update({entry["location"]: entry["location_id"] for entry in DATA["final_era_checks"]})
    for key in ("projects", "turning_point_stars"):
        table.update({entry["location"]: entry["location_id"] for entry in DATA[key]})
    return table
