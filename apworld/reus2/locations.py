from typing import Dict

from BaseClasses import Location

from .game_data import DATA


class Reus2Location(Location):
    game = "Reus 2"


def location_table() -> Dict[str, int]:
    """Every name and id, holding both the per-rule and the per-bioticum combo locations whatever
    `combo_checks` is: `Reus2World.location_name_to_id` is set on the class, where the options of a
    slot cannot be read."""
    table = {}
    for b in DATA["biotica"]:
        table[b["build_location"]] = b["build_location_id"]
        table.update({rule["location"]: rule["location_id"] for rule in b["rules"]})
        if b["rules"]:
            table[b["all_combos_location"]] = b["all_combos_location_id"]
    for key in ("request_rewards", "shop_drafts"):
        table.update({entry["location"]: entry["location_id"] for entry in DATA[key]})
    return table
