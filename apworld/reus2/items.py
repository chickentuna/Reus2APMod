from collections import defaultdict
from typing import Dict, List

from BaseClasses import Item, ItemClassification

from .game_data import DATA


class Reus2Item(Item):
    game = "Reus 2"


def _micros_needed_by_logic() -> set:
    return {rule["micro"] for b in DATA["biotica"] for rule in b["rules"] if rule["kind"] == "micro"}


def apex_items() -> List[str]:
    return [b["apex_item"] for b in DATA["biotica"] if "apex_item" in b]


MICROS_ITEM = "Micros"
SECONDARY_BIOMES_ITEM = "Secondary Biomes"


def filler_items() -> List[str]:
    """What a filler slot can hold."""
    return [f["item"] for f in DATA["filler"]]


def common_filler_items() -> List[str]:
    """The filler items drawn at random, which leaves out the ones with a set share."""
    return [f["item"] for f in DATA["filler"] if "share" not in f]


def rare_filler_counts(slots: int) -> Dict[str, int]:
    """How many of each rare filler item a seed with that many filler slots holds."""
    return {f["item"]: int(slots * f["share"]) for f in DATA["filler"] if "share" in f}


def items_granting_biotica(apex: bool) -> Dict[str, List[str]]:
    """For each bioticum name, the items that grant it. An apex item grants its own bioticum and the
    cohorts it brings, so a cohort brought by several apexes has several."""
    granting: Dict[str, List[str]] = defaultdict(list)
    if apex:
        for bioticum in DATA["biotica"]:
            if "apex_item" in bioticum:
                for name in [bioticum["name"]] + bioticum["cohorts"]:
                    granting[name].append(bioticum["apex_item"])
    for bioticum in DATA["biotica"]:
        if bioticum["name"] not in granting:
            granting[bioticum["name"]] = [bioticum["item"]]
    return dict(granting)


def bioticum_pool_items(apex: bool) -> List[str]:
    """Every item that grants a bioticum, once each."""
    granted = items_granting_biotica(apex).values()
    return list(dict.fromkeys(item for items in granted for item in items))


def item_table() -> Dict[str, int]:
    table = {b["item"]: b["item_id"] for b in DATA["biotica"]}
    table.update({b["apex_item"]: b["apex_item_id"] for b in DATA["biotica"] if "apex_item" in b})
    for key in ("giants", "biomes", "abilities", "characters", "micros", "unlocks", "filler"):
        table.update({entry["item"]: entry["item_id"] for entry in DATA[key]})
    table[DATA["eras"]["item"]] = DATA["eras"]["item_id"]
    return table


def classifications() -> Dict[str, ItemClassification]:
    progression = ItemClassification.progression
    useful = ItemClassification.useful
    useful_progression = ItemClassification.useful | ItemClassification.progression
    needed_micros = _micros_needed_by_logic()
    result = {b["item"]: progression for b in DATA["biotica"]}
    result.update({item: progression for item in apex_items()})
    result.update({g["item"]: progression for g in DATA["giants"]})
    result.update({b["item"]: useful_progression for b in DATA["biomes"]})
    result.update({a["item"]: useful for a in DATA["abilities"]})
    result.update({c["item"]: useful for c in DATA["characters"]})
    result.update({m["item"]: progression if m["name"] in needed_micros else useful for m in DATA["micros"]})
    result.update({u["item"]: useful for u in DATA["unlocks"]})
    result[MICROS_ITEM] = progression if needed_micros else useful
    result[SECONDARY_BIOMES_ITEM] = progression
    result.update({item: ItemClassification.filler for item in filler_items()})
    result[DATA["eras"]["item"]] = useful_progression
    result["Upgrade Slots"] = useful_progression
    return result


def item_groups() -> Dict[str, List[str]]:
    return {
        "Biotica": [b["item"] for b in DATA["biotica"]] + apex_items(),
        "Apex Biotica": apex_items(),
        "Giants": [g["item"] for g in DATA["giants"]],
        "Biomes": [b["item"] for b in DATA["biomes"]],
        "Powers": [a["item"] for a in DATA["abilities"]],
        "Characters": [c["item"] for c in DATA["characters"]],
        "Micro Types": [m["item"] for m in DATA["micros"]],
        "Unlocks": [u["item"] for u in DATA["unlocks"]],
        "Filler": filler_items(),
    }
