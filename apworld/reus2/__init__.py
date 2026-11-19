from collections import Counter, defaultdict
from typing import Any, Dict, List, Optional

from BaseClasses import ItemClassification, Region
from worlds.AutoWorld import World

from .game_data import selected_data
from .items import (Reus2Item, biotica_up_to, bioticum_pool_items, classifications, common_filler_items,
                    item_groups, item_table, items_granting_biotica, rare_filler_counts)
from .locations import (Reus2Location, biotica_with_locations, final_era_location_count, location_table,
                        mark_locations_excluded, progressive_era_early_locations,
                        restrict_item_to_locations, shop_draft_location_count, turning_point_checks)
from .options import BioticumChecks, BioticumItems, MAX_FINAL_ERA_GOAL, ProjectChecks, Reus2Options
from .rules import Reus2Rules

GOAL_LOCATION = "Thriving Planet"
VICTORY = "Victory"
ROLE_TYPES = ("Plant", "Animal", "Geo")
# Request levels are stored from 0, so level 4 requests are level 3.
LEVEL_4 = 3
# The options slot data carries, which decide the seed's locations and logic.
SLOT_OPTIONS = ("max_biotica_level", "goal_builds", "request_reward_checks",
                "bioticum_items", "bioticum_checks", "goal_final_eras", "ice_age", "everglades",
                "grasslands", "jurassic", "project_checks", "turning_point_checks",
                "only_max_level_biotica")


class Reus2World(World):
    """Reus 2 is a god game where giants shape a living planet for the humans who settle it.
    Biotica builds, synergies, request rewards and shop drafts send checks; items received are yours
    on every planet. Victory requires the biotica goal and the three-star Discovery goal."""

    game = "Reus 2"
    options_dataclass = Reus2Options
    options: Reus2Options
    topology_present = False

    item_name_to_id = item_table()
    location_name_to_id = location_table()
    item_name_groups = item_groups()
    _classifications = classifications()
    _filler_items = common_filler_items()

    # Universal Tracker rebuilds a slot's logic from its slot data, without the player's yaml.
    ut_can_gen_without_yaml = True

    apex_biotica: bool
    max_level: int
    data: Dict[str, Any]
    starting_items: List[str]
    surplus_items: List[str]
    final_era_check_count: int
    shop_draft_check_count: int
    replayed_shop_draft_count: Optional[int] = None
    replayed_surplus: Optional[List[str]] = None

    @staticmethod
    def interpret_slot_data(slot_data: Dict[str, Any]) -> Dict[str, Any]:
        return slot_data

    def generate_early(self) -> None:
        # Universal Tracker passes the slot data back; the seed's options and random rolls come from it.
        replay = getattr(self.multiworld, "re_gen_passthrough", {}).get(self.game)
        self.replayed_shop_draft_count = replay.get("shop_draft_checks") if replay else None
        if replay:
            for name in SLOT_OPTIONS:
                if name in replay:
                    getattr(self.options, name).value = replay[name]
            if "goal_final_eras" not in replay:
                self.options.goal_final_eras.value = 0
            for name in ("ice_age", "everglades", "grasslands", "jurassic"):
                if name not in replay:
                    getattr(self.options, name).value = False
        self.options.goal_final_eras.value = min(
            max(0, self.options.goal_final_eras.value), MAX_FINAL_ERA_GOAL)
        self.data = selected_data(
            bool(self.options.ice_age.value), bool(self.options.everglades.value),
            bool(self.options.grasslands.value), bool(self.options.jurassic.value))
        self._classifications = classifications(self.data)
        self._filler_items = common_filler_items(self.data)
        self.apex_biotica = self.options.bioticum_items.value == BioticumItems.option_apex
        self.max_level = self.options.max_biotica_level.value
        self.options.goal_builds.value = min(
            self.options.goal_builds.value, len(biotica_up_to(self.max_level, self.data)))
        if replay:
            self.final_era_check_count = min(
                len(self.data["final_era_checks"]), replay.get("final_era_check_count", 0))
        else:
            self.final_era_check_count = final_era_location_count(self.options.goal_final_eras.value)
        self.starting_items = list(replay["starting_items"]) if replay else self._roll_starting_kit()
        self.replayed_surplus = list(replay["surplus_items"]) if replay else None
        self.surplus_items = []

    def _roll_starting_kit(self) -> List[str]:
        """One giant per role with their first biomes, some of their special powers, the items
        granting `starting_biotica` early zero-cost biotica they can place there, and a few city
        leaders."""
        rng = self.random
        giants_by_type = defaultdict(list)
        for giant in self.data["giants"]:
            giants_by_type[giant["type"]].append(giant)
        giants = [rng.choice(giants_by_type[t]) for t in ROLE_TYPES]
        biome_items = {b["name"]: b["item"] for b in self.data["biomes"]}
        biomes = sorted({g["first_biome"] for g in giants})
        kit = [g["item"] for g in giants] + [biome_items[b] for b in biomes]

        specials = sorted({g["special"] for g in giants if g["special"]})
        rng.shuffle(specials)
        kit += specials[: self.options.starting_powers.value]

        candidates = defaultdict(list)
        for bioticum in self.data["biotica"]:
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
        granting = items_granting_biotica(self.apex_biotica, self.max_level, self.data)
        kit += list(dict.fromkeys(granting[name][0] for name in chosen))

        characters = [c["item"] for c in self.data["characters"]]
        kit += rng.sample(characters, min(self.options.starting_characters.value, len(characters)))
        return kit

    def create_regions(self) -> None:
        menu = Region("Menu", self.player, self.multiworld)
        planet = Region("Planet", self.player, self.multiworld)

        checks = self.options.bioticum_checks.value
        locations: Dict[str, int] = {}
        excluded_location_names = {
            check["location"] for check in self.data["final_era_checks"][:self.final_era_check_count]
        }
        only_max_level = bool(self.options.only_max_level_biotica.value)
        active_biotica = biotica_with_locations(self.max_level, only_max_level, self.data)
        for bioticum in active_biotica:
            combos = bioticum["rules"]
            if combos and checks != BioticumChecks.option_build_only:
                locations[bioticum["all_combos_location"]] = bioticum["all_combos_location_id"]
            # Under when_synergized a bioticum that has combo rules carries its Synergized location alone.
            if not (combos and checks == BioticumChecks.option_when_synergized):
                locations[bioticum["build_location"]] = bioticum["build_location_id"]
        request_rewards = self._request_rewards()
        for reward in request_rewards:
            locations[reward["location"]] = reward["location_id"]
        for check in self.data["final_era_checks"][: self.final_era_check_count]:
            locations[check["location"]] = check["location_id"]
        if self.options.project_checks.value != ProjectChecks.option_none:
            for project in self.data["projects"]:
                locations[project["location"]] = project["location_id"]
        active_turning_points = (turning_point_checks(self.data, self.options.goal_final_eras.value)
                                 if bool(self.options.turning_point_checks.value) else [])
        for check in active_turning_points:
            locations[check["location"]] = check["location_id"]
            if check["era"] == "Era_4_Discovery":
                excluded_location_names.add(check["location"])
        if self.replayed_shop_draft_count is None:
            started = Counter(self.starting_items)
            item_count = sum(max(0, count - started[name])
                             for name, count in self._item_pool_counts().items())
            self.shop_draft_check_count = shop_draft_location_count(
                item_count, len(locations), len(self.data["shop_drafts"]), len(excluded_location_names))
        else:
            self.shop_draft_check_count = min(
                len(self.data["shop_drafts"]), max(0, int(self.replayed_shop_draft_count)))
        active_shop_drafts = self.data["shop_drafts"][:self.shop_draft_check_count]
        early_location_names = progressive_era_early_locations(
            active_turning_points, request_rewards, active_shop_drafts, active_biotica, checks)
        for draft in active_shop_drafts:
            locations[draft["location"]] = draft["location_id"]
        planet.add_locations(locations, Reus2Location)
        restrict_item_to_locations(
            (location for location in planet.locations if location.name != GOAL_LOCATION),
            self.data["eras"]["item"], early_location_names)
        mark_locations_excluded(planet.locations, excluded_location_names)

        goal = Reus2Location(self.player, GOAL_LOCATION, None, planet)
        goal.place_locked_item(Reus2Item(VICTORY, ItemClassification.progression, None, self.player))
        planet.locations.append(goal)

        self.multiworld.regions += [menu, planet]
        menu.connect(planet)

    def _request_rewards(self) -> List[Dict[str, Any]]:
        """The 16 level 1 to 3 request rewards, and the first `request_reward_checks` level 4 ones."""
        base = [r for r in self.data["request_rewards"] if r["level"] < LEVEL_4]
        level_4 = [r for r in self.data["request_rewards"] if r["level"] == LEVEL_4]
        return base + level_4[: self.options.request_reward_checks.value]

    def create_item(self, name: str) -> Reus2Item:
        return Reus2Item(name, self._classifications[name], self.item_name_to_id[name], self.player)

    def _item_pool_counts(self) -> Counter:
        counts = Counter(bioticum_pool_items(self.apex_biotica, self.max_level, self.data))
        for key in ("giants", "biomes", "abilities", "characters", "micros", "unlocks"):
            counts.update(entry["item"] for entry in self.data[key])
        counts[self.data["eras"]["item"]] += len(self.data["eras"]["eras"])
        return counts

    def get_filler_item_name(self) -> str:
        return self.random.choice(self._filler_items)

    def create_items(self) -> None:
        for name in self.starting_items:
            self.multiworld.push_precollected(self.create_item(name))
        started = Counter(self.starting_items)

        pool: List[Reus2Item] = []

        def add(name: str, copies: int = 1) -> None:
            pool.extend(self.create_item(name) for _ in range(max(0, copies - started[name])))

        for name, copies in self._item_pool_counts().items():
            add(name, copies)

        slots = len(self.multiworld.get_unfilled_locations(self.player))
        if self.replayed_surplus is not None:
            wanted = Counter(self.replayed_surplus)
            surplus = set()
            for index, item in enumerate(pool):
                if wanted[item.name] > 0:
                    wanted[item.name] -= 1
                    surplus.add(index)
        elif len(pool) > slots:
            surplus = set(self.random.sample(range(len(pool)), len(pool) - slots))
        else:
            surplus = set()
        if surplus:
            kept: List[Reus2Item] = []
            for index, item in enumerate(pool):
                if index in surplus:
                    self.multiworld.push_precollected(item)
                    self.surplus_items.append(item.name)
                else:
                    kept.append(item)
            pool = kept
        filler_slots = slots - len(pool)
        for name, count in rare_filler_counts(filler_slots, self.data).items():
            pool += [self.create_item(name) for _ in range(count)]
        pool += [self.create_filler() for _ in range(slots - len(pool))]
        era_item = self.data["eras"]["item"]
        early_era_count = sum(item.name == era_item for item in pool)
        if early_era_count:
            self.multiworld.early_items[self.player][era_item] = early_era_count
        self.multiworld.itempool += pool

    def set_rules(self) -> None:
        rules = Reus2Rules(self)
        checks = self.options.bioticum_checks.value
        only_max_level = bool(self.options.only_max_level_biotica.value)
        for bioticum in biotica_with_locations(self.max_level, only_max_level, self.data):
            combos = bioticum["rules"]
            if combos and checks != BioticumChecks.option_build_only:
                self.get_location(bioticum["all_combos_location"]).access_rule = \
                    lambda state, b=bioticum: rules.can_combo_all(state, b)
            if not (combos and checks == BioticumChecks.option_when_synergized):
                self.get_location(bioticum["build_location"]).access_rule = \
                    lambda state, b=bioticum: rules.can_build(state, b)
        for reward in self._request_rewards():
            self.get_location(reward["location"]).access_rule = lambda state, r=reward: rules.request_reward_ok(state, r)
        for draft in self.data["shop_drafts"][:self.shop_draft_check_count]:
            self.get_location(draft["location"]).access_rule = lambda state, d=draft: rules.shop_draft_ok(state, d)
        for check in self.data["final_era_checks"][: self.final_era_check_count]:
            self.get_location(check["location"]).access_rule = rules.final_era_reachable
        if self.options.project_checks.value != ProjectChecks.option_none:
            for project in self.data["projects"]:
                self.get_location(project["location"]).access_rule = \
                    lambda state, p=project: rules.project_reachable(state, p)
        if bool(self.options.turning_point_checks.value):
            for check in turning_point_checks(self.data, self.options.goal_final_eras.value):
                self.get_location(check["location"]).access_rule = \
                    lambda state, c=check: rules.turning_point_reachable(state, c)

        bioticum_goal = self.options.goal_builds.value
        final_era_goal = self.options.goal_final_eras.value
        self.get_location(GOAL_LOCATION).access_rule = \
            lambda state: rules.goal_reachable(state, bioticum_goal, final_era_goal)
        self.multiworld.completion_condition[self.player] = lambda state: state.has(VICTORY, self.player)

    def fill_slot_data(self) -> Dict[str, Any]:
        slot_data = self.options.as_dict(*SLOT_OPTIONS)
        slot_data["game_version"] = self.data["game_version"]
        slot_data["shop_draft_checks"] = self.shop_draft_check_count
        slot_data["final_era_check_count"] = self.final_era_check_count
        slot_data["starting_items"] = self.starting_items
        slot_data["surplus_items"] = self.surplus_items
        slot_data["combo_rule_ids"] = sorted(
            f'{bioticum["guid"]}#{rule["rule_index"]}'
            for bioticum in self.data["biotica"]
            for rule in bioticum["rules"]
        )
        # The server sends the start inventory without classification flags; the client reads them here.
        slot_data["starting_item_flags"] = {name: int(self._classifications[name])
                                            for name in self.starting_items + self.surplus_items}
        return slot_data
