from typing import Dict, List

from BaseClasses import Item, ItemClassification

from .game_data import DATA


class Reus2Item(Item):
    game = "Reus 2"


def _micros_needed_by_logic() -> set:
    return {rule["micro"] for b in DATA["biotica"] for rule in b["rules"] if rule["kind"] == "micro"}


def item_table() -> Dict[str, int]:
    table = {b["item"]: b["item_id"] for b in DATA["biotica"]}
    for key in ("giants", "biomes", "abilities", "characters", "micros", "filler"):
        table.update({entry["item"]: entry["item_id"] for entry in DATA[key]})
    table[DATA["progressive_micro"]["item"]] = DATA["progressive_micro"]["item_id"]
    table[DATA["eras"]["item"]] = DATA["eras"]["item_id"]
    return table


def classifications() -> Dict[str, ItemClassification]:
    progression = ItemClassification.progression
    useful = ItemClassification.useful
    needed_micros = _micros_needed_by_logic()
    result = {b["item"]: progression for b in DATA["biotica"]}
    result.update({g["item"]: progression for g in DATA["giants"]})
    result.update({b["item"]: progression for b in DATA["biomes"]})
    result.update({a["item"]: useful for a in DATA["abilities"]})
    result.update({c["item"]: useful for c in DATA["characters"]})
    result.update({m["item"]: progression if m["name"] in needed_micros else useful for m in DATA["micros"]})
    result.update({f["item"]: ItemClassification.filler for f in DATA["filler"]})
    result[DATA["progressive_micro"]["item"]] = progression
    result[DATA["eras"]["item"]] = progression
    return result


def item_groups() -> Dict[str, List[str]]:
    return {
        "Biotica": [b["item"] for b in DATA["biotica"]],
        "Giants": [g["item"] for g in DATA["giants"]],
        "Biomes": [b["item"] for b in DATA["biomes"]],
        "Powers": [a["item"] for a in DATA["abilities"]],
        "Characters": [c["item"] for c in DATA["characters"]],
        "Micros": [m["item"] for m in DATA["micros"]] + [DATA["progressive_micro"]["item"]],
    }
