from collections import defaultdict

from BaseClasses import CollectionState, ItemClassification

from .items import MICROS_ITEM, SECONDARY_BIOMES_ITEM, biotica_up_to, classifications, items_granting_biotica
from .options import BioticumChecks, BioticumItems

# How many biotica must be synergized before a request of each level (stored from 0) can be completed,
# since cities only ask for and reward what biotica working at full strength produce.
REQUEST_BIOTICA = {0: 8, 1: 12, 2: 16, 3: 24}
# The same for buying a shop draft, by the draft's number: its price is paid in city resources.
SHOP_DRAFT_BIOTICA = ((2, 8), (5, 12), (10, 16))


class Reus2Rules:
    """Placement logic.

    A bioticum is placeable in a biome when the player has an item granting it, has a giant of its
    type, has the biome and a giant able to terraform it (as that giant's second biome only with
    Secondary Biomes), has reached the era its level needs, and — if it costs biodiversity — can
    reach that cost with zero-cost biotica placed in the same biome, each counting for its level.
    """

    def __init__(self, world) -> None:
        self.data = world.data
        self.player = world.player
        self.synergy_counts = world.options.bioticum_checks.value == BioticumChecks.option_when_synergized
        self.max_level = world.options.max_biotica_level.value
        self.granting = items_granting_biotica(
            world.options.bioticum_items.value == BioticumItems.option_apex, self.max_level, self.data)
        self.biotica = {b["name"]: b for b in self.data["biotica"]}
        self.giants_of_type = defaultdict(list)
        self.first_terraformers = defaultdict(list)
        self.second_terraformers = defaultdict(list)
        for giant in self.data["giants"]:
            self.giants_of_type[giant["type"]].append(giant["item"])
            if giant["first_biome"]:
                self.first_terraformers[giant["first_biome"]].append(giant["item"])
            if giant["second_biome"]:
                self.second_terraformers[giant["second_biome"]].append(giant["item"])
        self.biome_item = {b["name"]: b["item"] for b in self.data["biomes"]}
        self.providers = defaultdict(list)
        for bioticum in self.data["biotica"]:
            if bioticum["cost"] == 0:
                for biome in bioticum["biomes"]:
                    self.providers[biome].append(bioticum)
        self.era_item = self.data["eras"]["item"]
        self.upgrade_item = "Terraform: Upgrade Slots"
        self.micro_item = {m["name"]: m["item"] for m in self.data["micros"]}
        both = classifications(self.data)
        self.key_items = sorted(name for name, flags in both.items()
                                if flags & ItemClassification.progression and flags & ItemClassification.useful)

    def has_bioticum(self, state: CollectionState, bioticum) -> bool:
        """A bioticum above Max Biotica Level is held from the start."""
        return bioticum["level"] > self.max_level or state.has_any(self.granting[bioticum["name"]], self.player)

    def level_ok(self, state: CollectionState, level: int) -> bool:
        """A bioticum above level 1 needs a slot of its level, and Upgrade Slots is what raises one;
        the one ability serves both levels."""
        return (state.has(self.era_item, self.player, level - 1)
                and (level < 2 or state.has(self.upgrade_item, self.player)))

    def has_giant_for(self, state: CollectionState, bioticum) -> bool:
        return any(state.has_any(self.giants_of_type[t], self.player) for t in bioticum["types"])

    def biome_open(self, state: CollectionState, biome: str) -> bool:
        """The game locks every giant's second biome until the Secondary Biomes unlock."""
        if not state.has(self.biome_item[biome], self.player):
            return False
        return (state.has_any(self.first_terraformers[biome], self.player)
                or (state.has(SECONDARY_BIOMES_ITEM, self.player)
                    and state.has_any(self.second_terraformers[biome], self.player)))

    def biodiversity_ok(self, state: CollectionState, bioticum, biome: str) -> bool:
        needed = bioticum["cost"]
        if needed <= 0:
            return True
        total = 0
        for provider in self.providers[biome]:
            if provider is bioticum:
                continue
            if (self.has_bioticum(state, provider) and self.level_ok(state, provider["level"])
                    and self.has_giant_for(state, provider)):
                total += provider["level"]
                if total >= needed:
                    return True
        return False

    def can_place(self, state: CollectionState, bioticum, biome: str) -> bool:
        return (self.has_bioticum(state, bioticum)
                and self.level_ok(state, bioticum["level"])
                and self.has_giant_for(state, bioticum)
                and self.biome_open(state, biome)
                and self.biodiversity_ok(state, bioticum, biome))

    def can_build(self, state: CollectionState, bioticum) -> bool:
        return any(self.can_place(state, bioticum, biome) for biome in bioticum["biomes"])

    def can_combo_all(self, state: CollectionState, bioticum) -> bool:
        """Every combo rule active at once on one placement, so all of them in the one biome it is
        placed in. A rule that names a biome decides that biome, and two naming different biomes
        can never be active together."""
        rules = bioticum["rules"]
        named = {rule["require_biome"] for rule in rules if rule.get("require_biome")}
        if len(named) > 1:
            return False
        biomes = sorted(named) or bioticum["biomes"]
        return any(self.can_place(state, bioticum, biome)
                   and all(self.requirement_met(state, bioticum, rule, biome) for rule in rules)
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
        """A group names each bioticum once, and the player may place a bioticum as many times as
        there are slots, so a count beyond the group's size is reached with further copies of one
        already there and asks for no further item."""
        others = [other for other in (self.biotica.get(name) for name in group)
                  if other is not None and (allow_self or other is not bioticum)]
        if not others:
            return False
        found = 0
        for other in others:
            if (self.can_build(state, other) if biome is None
                    else biome in other["biomes"] and self.can_place(state, other, biome)):
                found += 1
                if found >= min(count, len(others)):
                    return True
        return False

    def has_micro(self, state: CollectionState, micro: str) -> bool:
        """A Micro type is placeable once the Micros unlock and its own item have arrived. Charges
        are filler and the game pays some out every era once Micros are unlocked, so they never gate
        a location."""
        return state.has_all((MICROS_ITEM, self.micro_item[micro]), self.player)

    def request_reward_ok(self, state: CollectionState, reward) -> bool:
        """Levels 1 to 3 need one Progressive Era per level above the first. Level 4 needs every item
        classed both progression and useful. Every level needs its count of synergized biotica."""
        level = reward["level"]
        unlocked = (state.has(self.era_item, self.player, min(level, len(self.data["eras"]["eras"])))
                    and (level < 3 or state.has_all(self.key_items, self.player)))
        return unlocked and self.synergized_reachable(state, REQUEST_BIOTICA[level])

    def shop_draft_ok(self, state: CollectionState, draft) -> bool:
        # Every global shop check after the first ten can come from the first purchase on a new planet.
        index = 1 if draft["index"] > 10 else draft["index"]
        biotica = next(count for last, count in SHOP_DRAFT_BIOTICA if index <= last)
        eras = 0 if index <= 2 else 1 if index <= 5 else 2
        return (state.has(self.era_item, self.player, eras)
                and self.synergized_reachable(state, biotica))

    def builds_reachable(self, state: CollectionState, goal: int) -> bool:
        found = 0
        for bioticum in biotica_up_to(self.max_level, self.data):
            if self.can_build(state, bioticum):
                found += 1
                if found >= goal:
                    return True
        return False

    def goal_reachable(self, state: CollectionState, bioticum_goal: int, final_era_goal: int = 0) -> bool:
        """The goal counts every bioticum up to Max Biotica Level, regardless of which biotica have
        locations. Under when_synergized a bioticum with synergy rules counts once every rule is active
        at once; every other bioticum counts once built.
        A nonzero Final Era goal also needs both Progressive Era items so Discovery can be selected."""
        biotica_reachable = (self.synergized_reachable(
            state, bioticum_goal, self.max_level)
                             if self.synergy_counts else self.builds_reachable(state, bioticum_goal))
        return biotica_reachable and (final_era_goal == 0 or self.final_era_reachable(state))

    def final_era_reachable(self, state: CollectionState) -> bool:
        """Discovery is the fourth era, unlocked by both Progressive Era items."""
        return state.has(self.era_item, self.player, len(self.data["eras"]["eras"]))

    def project_reachable(self, state: CollectionState, project) -> bool:
        """A project is available once its era has been unlocked.

        Project rules affect when the game sends a synergy check, but do not grant separate AP items;
        the location logic therefore models the era gate shared by projects of that level.
        """
        return state.has(self.era_item, self.player, max(0, int(project.get("level", 1)) - 1))

    def turning_point_reachable(self, state: CollectionState, turning_point_star) -> bool:
        eras = self.data["eras"]["base"] + self.data["eras"]["eras"]
        try:
            era_index = eras.index(turning_point_star["era"])
        except ValueError:
            return False
        needed_eras = max(0, era_index - len(self.data["eras"]["base"]) + 1)
        return state.has(self.era_item, self.player, needed_eras)

    def synergized_reachable(self, state: CollectionState, count: int, max_level: int = 3,
                             only_max_level: bool = False) -> bool:
        """`count` biotica working at full strength, optionally restricted to `max_level` only:
        a bioticum with synergy rules once every rule can be active at once, one without once built."""
        found = 0
        for bioticum in self.data["biotica"]:
            if bioticum["level"] > max_level or (only_max_level and bioticum["level"] != max_level):
                continue
            if self.can_combo_all(state, bioticum) if bioticum["rules"] else self.can_build(state, bioticum):
                found += 1
                if found >= count:
                    return True
        return False
