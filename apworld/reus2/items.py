from collections import defaultdict
from typing import Dict, List

from BaseClasses import Item, ItemClassification

from .game_data import DATA


class Reus2Item(Item):
    game = "Reus 2"


def _micros_needed_by_logic(data=DATA) -> set:
    return {rule["micro"] for b in data["biotica"] for rule in b["rules"] if rule["kind"] == "micro"}


def apex_items(data=DATA) -> List[str]:
    return [b["apex_item"] for b in data["biotica"] if "apex_item" in b]


MICROS_ITEM = "Unlock: Micros"
SECONDARY_BIOMES_ITEM = "Unlock: Secondary Biomes"


def filler_items(data=DATA) -> List[str]:
    """What a filler slot can hold."""
    return [f["item"] for f in data["filler"]]


def common_filler_items(data=DATA) -> List[str]:
    """The filler items drawn at random, which leaves out the ones with a set share."""
    return [f["item"] for f in data["filler"] if "share" not in f]


def rare_filler_counts(slots: int, data=DATA) -> Dict[str, int]:
    """How many of each rare filler item a seed with that many filler slots holds."""
    return {f["item"]: int(slots * f["share"]) for f in data["filler"] if "share" in f}


def biotica_up_to(max_level: int, data=DATA) -> List[dict]:
    """The biotica a seed with that Max Biotica Level holds."""
    return [b for b in data["biotica"] if b["level"] <= max_level]


def items_granting_biotica(apex: bool, max_level: int, data=DATA) -> Dict[str, List[str]]:
    """For each bioticum up to `max_level`, the items that grant it. An apex item grants its own
    bioticum and the cohorts it brings, so a cohort brought by several apexes has several; an apex
    above `max_level` is left out."""
    biotica = biotica_up_to(max_level, data)
    kept = {b["name"] for b in biotica}
    granting: Dict[str, List[str]] = defaultdict(list)
    if apex:
        for bioticum in biotica:
            if "apex_item" in bioticum:
                for name in [bioticum["name"]] + bioticum["cohorts"]:
                    if name in kept:
                        granting[name].append(bioticum["apex_item"])
    for bioticum in biotica:
        if bioticum["name"] not in granting:
            granting[bioticum["name"]] = [bioticum["item"]]
    return dict(granting)


def bioticum_pool_items(apex: bool, max_level: int, data=DATA) -> List[str]:
    """Every item that grants a bioticum, once each."""
    granted = items_granting_biotica(apex, max_level, data).values()
    return list(dict.fromkeys(item for items in granted for item in items))


def item_table(data=DATA) -> Dict[str, int]:
    table = {b["item"]: b["item_id"] for b in data["biotica"]}
    table.update({b["apex_item"]: b["apex_item_id"] for b in data["biotica"] if "apex_item" in b})
    for key in ("giants", "biomes", "abilities", "characters", "micros", "unlocks", "filler"):
        table.update({entry["item"]: entry["item_id"] for entry in data[key]})
    table[data["eras"]["item"]] = data["eras"]["item_id"]
    return table


def classifications(data=DATA) -> Dict[str, ItemClassification]:
    progression = ItemClassification.progression
    useful = ItemClassification.useful
    useful_progression = ItemClassification.useful | ItemClassification.progression
    needed_micros = _micros_needed_by_logic(data)
    result = {b["item"]: progression for b in data["biotica"]}
    result.update({item: progression for item in apex_items(data)})
    result.update({g["item"]: progression for g in data["giants"]})
    result.update({b["item"]: useful_progression for b in data["biomes"]})
    result.update({a["item"]: useful for a in data["abilities"]})
    result.update({c["item"]: useful for c in data["characters"]})
    result.update({m["item"]: progression if m["name"] in needed_micros else useful for m in data["micros"]})
    result.update({u["item"]: useful for u in data["unlocks"]})
    result[MICROS_ITEM] = progression if needed_micros else useful
    result[SECONDARY_BIOMES_ITEM] = progression
    result.update({item: ItemClassification.filler for item in filler_items(data)})
    result[data["eras"]["item"]] = useful_progression
    result["Terraform: Upgrade Slots"] = useful_progression
    return result


def item_groups(data=DATA) -> Dict[str, List[str]]:
    return {
        "Biotica": [b["item"] for b in data["biotica"]] + apex_items(data),
        "Apex Biotica": apex_items(data),
        "Giants": [g["item"] for g in data["giants"]],
        "Biomes": [b["item"] for b in data["biomes"]],
        "Powers": [a["item"] for a in data["abilities"]],
        "Characters": [c["item"] for c in data["characters"]],
        "Micro Types": [m["item"] for m in data["micros"]],
        "Unlocks": [u["item"] for u in data["unlocks"]],
        "Filler": filler_items(data),
    }
