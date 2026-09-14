from collections import defaultdict

from BaseClasses import CollectionState

from .game_data import DATA
from .options import MicroMode


class Reus2Rules:
    """Placement logic.

    A bioticum is placeable in a biome when the player has it, has a giant of its type, has the biome
    and a giant able to terraform it, has reached the era its level needs, and — if it costs
    biodiversity — can reach that cost with zero-cost biotica placed in the same biome, each counting
    for its level.
    """

    def __init__(self, world) -> None:
        self.player = world.player
        self.progressive_micros = world.options.micro_mode.value == MicroMode.option_progressive
        self.biotica = {b["item"]: b for b in DATA["biotica"]}
        self.giants_of_type = defaultdict(list)
        self.terraformers = defaultdict(list)
        for giant in DATA["giants"]:
            self.giants_of_type[giant["type"]].append(giant["item"])
            for biome in (giant["first_biome"], giant["second_biome"]):
                if biome:
                    self.terraformers[biome].append(giant["item"])
        self.biome_item = {b["name"]: b["item"] for b in DATA["biomes"]}
        self.providers = defaultdict(list)
        for bioticum in DATA["biotica"]:
            if bioticum["cost"] == 0:
                for biome in bioticum["biomes"]:
                    self.providers[biome].append(bioticum)
        self.era_item = DATA["eras"]["item"]
        self.micro_item = {m["name"]: m["item"] for m in DATA["micros"]}
        self.micro_order = {m["name"]: m["order"] for m in DATA["micros"]}
        self.progressive_micro = DATA["progressive_micro"]["item"]

    def level_ok(self, state: CollectionState, level: int) -> bool:
        return state.has(self.era_item, self.player, level - 1)

    def has_giant_for(self, state: CollectionState, bioticum) -> bool:
        return any(state.has_any(self.giants_of_type[t], self.player) for t in bioticum["types"])

    def biome_open(self, state: CollectionState, biome: str) -> bool:
        return state.has(self.biome_item[biome], self.player) and state.has_any(self.terraformers[biome], self.player)

    def biodiversity_ok(self, state: CollectionState, bioticum, biome: str) -> bool:
        needed = bioticum["cost"]
        if needed <= 0:
            return True
        total = 0
        for provider in self.providers[biome]:
            if provider is bioticum:
                continue
            if (state.has(provider["item"], self.player) and self.level_ok(state, provider["level"])
                    and self.has_giant_for(state, provider)):
                total += provider["level"]
                if total >= needed:
                    return True
        return False

    def can_place(self, state: CollectionState, bioticum, biome: str) -> bool:
        return (state.has(bioticum["item"], self.player)
                and self.level_ok(state, bioticum["level"])
                and self.has_giant_for(state, bioticum)
                and self.biome_open(state, biome)
                and self.biodiversity_ok(state, bioticum, biome))

    def can_build(self, state: CollectionState, bioticum) -> bool:
        return any(self.can_place(state, bioticum, biome) for biome in bioticum["biomes"])

    def can_combo(self, state: CollectionState, bioticum, rule) -> bool:
        biomes = [rule["require_biome"]] if rule.get("require_biome") else bioticum["biomes"]
        return any(self.can_place(state, bioticum, biome) and self.requirement_met(state, bioticum, rule, biome)
                   for biome in biomes)

    def requirement_met(self, state: CollectionState, bioticum, rule, biome: str) -> bool:
        kind = rule["kind"]
        if kind == "none":
            return True
        if kind == "biome":
            return self.biome_open(state, rule["biome"])
        if kind == "micro":
            return self.has_micro(state, rule["micro"])
        in_biome = None if rule.get("any_biome") else biome
        allow_self = rule.get("allow_self", False)
        return all(self.enough_candidates(state, bioticum, group, rule["count"], in_biome, allow_self)
                   for group in rule["groups"])

    def enough_candidates(self, state: CollectionState, bioticum, group, count: int, biome, allow_self: bool) -> bool:
        found = 0
        for name in group:
            other = self.biotica.get(name)
            if other is None or (other is bioticum and not allow_self):
                continue
            if biome is None:
                placeable = self.can_build(state, other)
            else:
                placeable = biome in other["biomes"] and self.can_place(state, other, biome)
            if placeable:
                found += 1
                if found >= count:
                    return True
        return False

    def has_micro(self, state: CollectionState, micro: str) -> bool:
        if self.progressive_micros:
            return state.has(self.progressive_micro, self.player, self.micro_order[micro] + 1)
        return state.has(self.micro_item[micro], self.player)

    def request_reward_ok(self, state: CollectionState, reward) -> bool:
        return state.has(self.era_item, self.player, reward["level"])

    def shop_draft_ok(self, state: CollectionState, draft) -> bool:
        index = draft["index"]
        if index <= 2:
            return True
        return state.has(self.era_item, self.player, 1 if index <= 5 else 2)

    def builds_reachable(self, state: CollectionState, goal: int) -> bool:
        found = 0
        for bioticum in DATA["biotica"]:
            if self.can_build(state, bioticum):
                found += 1
                if found >= goal:
                    return True
        return False
