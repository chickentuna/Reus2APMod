from typing import Dict

from BaseClasses import Location

from .game_data import DATA


class Reus2Location(Location):
    game = "Reus 2"


def location_table() -> Dict[str, int]:
    table = {}
    for b in DATA["biotica"]:
        table[b["build_location"]] = b["build_location_id"]
        table.update({rule["location"]: rule["location_id"] for rule in b["rules"]})
    for key in ("request_rewards", "shop_drafts"):
        table.update({entry["location"]: entry["location_id"] for entry in DATA[key]})
    return table
