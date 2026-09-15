from collections import Counter, defaultdict
from typing import Any, Dict, List

from BaseClasses import ItemClassification, Region
from worlds.AutoWorld import World

from .game_data import DATA
from .items import (Reus2Item, bioticum_pool_items, classifications, filler_items,
                    item_groups, item_table, items_granting_biotica)
from .locations import Reus2Location, location_table
from .options import BioticumItems, ComboChecks, MicroTypes, Reus2Options
from .rules import Reus2Rules

GOAL_LOCATION = "Thriving Planet"
VICTORY = "Victory"
ROLE_TYPES = ("Plant", "Animal", "Geo")


class Reus2World(World):
    """Reus 2 is a god game where giants shape a living planet for the humans who settle it.
    Every bioticum you build and every combo you trigger sends a check; drafts are gone, and all
    you have received is yours on every planet."""

    game = "Reus 2"
    options_dataclass = Reus2Options
    options: Reus2Options
    topology_present = False

    item_name_to_id = item_table()
    location_name_to_id = location_table()
    item_name_groups = item_groups()
    _classifications = classifications()
    _filler_items = filler_items()

    apex_biotica: bool
    starting_items: List[str]
    surplus_items: List[str]

    def generate_early(self) -> None:
        self.apex_biotica = self.options.bioticum_items.value == BioticumItems.option_apex
        self.starting_items = self._roll_starting_kit()
        self.surplus_items = []

    def _roll_starting_kit(self) -> List[str]:
        """One giant per role with their first biomes, some of their special powers, the items
        granting `starting_biotica` early zero-cost biotica they can place there, a few city
        leaders, and the Micro types the shop sells from the start."""
        rng = self.random
        giants_by_type = defaultdict(list)
        for giant in DATA["giants"]:
            giants_by_type[giant["type"]].append(giant)
        giants = [rng.choice(giants_by_type[t]) for t in ROLE_TYPES]
        biome_items = {b["name"]: b["item"] for b in DATA["biomes"]}
        biomes = sorted({g["first_biome"] for g in giants})
        kit = [g["item"] for g in giants] + [biome_items[b] for b in biomes]

        specials = sorted({g["special"] for g in giants if g["special"]})
        rng.shuffle(specials)
        kit += specials[: self.options.starting_powers.value]

        candidates = defaultdict(list)
        for bioticum in DATA["biotica"]:
            if bioticum["level"] == 1 and bioticum["cost"] == 0 and set(bioticum["biomes"]) & set(biomes):
                for bioticum_type in bioticum["types"]:
                    candidates[bioticum_type].append(bioticum["name"])
        for pile in candidates.values():
            rng.shuffle(pile)
        chosen: List[str] = []
        wanted = self.options.starting_biotica.value
        while len(chosen) < wanted and any(candidates[t] for t in ROLE_TYPES):
            for bioticum_type in ROLE_TYPES:
                pile = candidates[bioticum_type]
                while pile and pile[-1] in chosen:
                    pile.pop()
                if pile and len(chosen) < wanted:
                    chosen.append(pile.pop())
        granting = items_granting_biotica(self.apex_biotica)
        kit += list(dict.fromkeys(granting[name][0] for name in chosen))

        characters = [c["item"] for c in DATA["characters"]]
        kit += rng.sample(characters, min(self.options.starting_characters.value, len(characters)))

        if self.options.micro_types.value == MicroTypes.option_shuffled:
            micros = [m["item"] for m in DATA["micros"]]
            kit += rng.sample(micros, min(self.options.starting_micro_types.value, len(micros)))
        return kit

    def create_regions(self) -> None:
        menu = Region("Menu", self.player, self.multiworld)
        planet = Region("Planet", self.player, self.multiworld)

        combo_checks = self.options.combo_checks.value
        locations: Dict[str, int] = {}
        for bioticum in DATA["biotica"]:
            locations[bioticum["build_location"]] = bioticum["build_location_id"]
            if combo_checks == ComboChecks.option_per_rule:
                locations.update({rule["location"]: rule["location_id"] for rule in bioticum["rules"]})
            elif combo_checks == ComboChecks.option_per_bioticum and bioticum["rules"]:
                locations[bioticum["all_combos_location"]] = bioticum["all_combos_location_id"]
        for reward in DATA["request_rewards"][: self.options.request_reward_checks.value]:
            locations[reward["location"]] = reward["location_id"]
        for draft in DATA["shop_drafts"][: self.options.shop_draft_checks.value]:
            locations[draft["location"]] = draft["location_id"]
        planet.add_locations(locations, Reus2Location)

        goal = Reus2Location(self.player, GOAL_LOCATION, None, planet)
        goal.place_locked_item(Reus2Item(VICTORY, ItemClassification.progression, None, self.player))
        planet.locations.append(goal)

        self.multiworld.regions += [menu, planet]
        menu.connect(planet)

    def create_item(self, name: str) -> Reus2Item:
        return Reus2Item(name, self._classifications[name], self.item_name_to_id[name], self.player)

    def get_filler_item_name(self) -> str:
        return self.random.choice(self._filler_items)

    def create_items(self) -> None:
        for name in self.starting_items:
            self.multiworld.push_precollected(self.create_item(name))
        started = Counter(self.starting_items)

        pool: List[Reus2Item] = []

        def add(name: str, copies: int = 1) -> None:
            pool.extend(self.create_item(name) for _ in range(max(0, copies - started[name])))

        for name in bioticum_pool_items(self.apex_biotica):
            add(name)
        for key in ("giants", "biomes", "abilities", "characters"):
            for entry in DATA[key]:
                add(entry["item"])
        add(DATA["eras"]["item"], len(DATA["eras"]["eras"]))
        if self.options.micro_types.value == MicroTypes.option_shuffled:
            for micro in DATA["micros"]:
                add(micro["item"])

        slots = len(self.multiworld.get_unfilled_locations(self.player))
        if len(pool) > slots:
            surplus = set(self.random.sample(range(len(pool)), len(pool) - slots))
            kept: List[Reus2Item] = []
            for index, item in enumerate(pool):
                if index in surplus:
                    self.multiworld.push_precollected(item)
                    self.surplus_items.append(item.name)
                else:
                    kept.append(item)
            pool = kept
        pool += [self.create_filler() for _ in range(slots - len(pool))]
        self.multiworld.itempool += pool

    def set_rules(self) -> None:
        rules = Reus2Rules(self)
        combo_checks = self.options.combo_checks.value
        for bioticum in DATA["biotica"]:
            self.get_location(bioticum["build_location"]).access_rule = \
                lambda state, b=bioticum: rules.can_build(state, b)
            if combo_checks == ComboChecks.option_per_rule:
                for rule in bioticum["rules"]:
                    self.get_location(rule["location"]).access_rule = \
                        lambda state, b=bioticum, r=rule: rules.can_combo(state, b, r)
            elif combo_checks == ComboChecks.option_per_bioticum and bioticum["rules"]:
                self.get_location(bioticum["all_combos_location"]).access_rule = \
                    lambda state, b=bioticum: rules.can_combo_all(state, b)
        for reward in DATA["request_rewards"][: self.options.request_reward_checks.value]:
            self.get_location(reward["location"]).access_rule = lambda state, r=reward: rules.request_reward_ok(state, r)
        for draft in DATA["shop_drafts"][: self.options.shop_draft_checks.value]:
            self.get_location(draft["location"]).access_rule = lambda state, d=draft: rules.shop_draft_ok(state, d)

        goal = self.options.goal_builds.value
        self.get_location(GOAL_LOCATION).access_rule = lambda state: rules.builds_reachable(state, goal)
        self.multiworld.completion_condition[self.player] = lambda state: state.has(VICTORY, self.player)

    def fill_slot_data(self) -> Dict[str, Any]:
        slot_data = self.options.as_dict(
            "goal_builds", "request_reward_checks", "shop_draft_checks", "bioticum_items",
            "combo_checks", "micro_types", "starting_micro_types")
        slot_data["game_version"] = DATA["game_version"]
        slot_data["starting_items"] = self.starting_items
        slot_data["surplus_items"] = self.surplus_items
        return slot_data
