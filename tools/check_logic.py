"""Evaluates every apworld location rule with a full inventory and lists the ones that still fail.

A location that fails here can never be reached, which Archipelago reports as "Location Accessibility
requirements not fulfilled". Runs without an Archipelago install by stubbing the modules the rules
import.

Usage:  python tools/check_logic.py
"""

import importlib.util
import sys
import types
from pathlib import Path

WORLD = Path(__file__).resolve().parent.parent / "apworld" / "reus2"


def load_world_modules():
    base = types.ModuleType("BaseClasses")
    base.CollectionState = object
    base.Item = type("Item", (), {})
    base.ItemClassification = type("ItemClassification", (), {"progression": 1, "useful": 2, "filler": 0})
    options = types.ModuleType("Options")
    for name in ("Choice", "Range", "PerGameCommonOptions", "StartInventoryPool", "Toggle", "DefaultOnToggle"):
        setattr(options, name, type(name, (), {}))
    sys.modules["BaseClasses"] = base
    sys.modules["Options"] = options

    package = types.ModuleType("reus2")
    package.__path__ = [str(WORLD)]
    sys.modules["reus2"] = package
    loaded = {}
    for name in ("game_data", "options", "items", "rules"):
        spec = importlib.util.spec_from_file_location(f"reus2.{name}", WORLD / f"{name}.py")
        module = importlib.util.module_from_spec(spec)
        sys.modules[f"reus2.{name}"] = module
        spec.loader.exec_module(module)
        loaded[name] = module
    return loaded


class FullInventory:
    def has(self, name, player, count=1):
        return True

    def has_any(self, names, player):
        return bool(names)

    def has_all(self, names, player):
        return True


def main():
    modules = load_world_modules()
    data = modules["game_data"].DATA

    checks = modules["options"].BioticumChecks
    values = (checks.option_build_only, checks.option_when_synergized, checks.option_build_and_synergized)

    failures = []
    modes = [(bioticum_items, bioticum_checks)
             for bioticum_items in (0, 1) for bioticum_checks in values]
    for bioticum_items, bioticum_checks in modes:
        mode = f"bioticum_items={bioticum_items} bioticum_checks={bioticum_checks}"
        world = types.SimpleNamespace(player=1, options=types.SimpleNamespace(
            bioticum_items=types.SimpleNamespace(value=bioticum_items),
            bioticum_checks=types.SimpleNamespace(value=bioticum_checks)))
        rules = modules["rules"].Reus2Rules(world)
        state = FullInventory()
        for bioticum in data["biotica"]:
            combos = bioticum["rules"]
            if not (combos and bioticum_checks == checks.option_when_synergized):
                if not rules.can_build(state, bioticum):
                    failures.append((mode, bioticum["build_location"], "build"))
            if combos and bioticum_checks != checks.option_build_only:
                if not rules.can_combo_all(state, bioticum):
                    failures.append((mode, bioticum["all_combos_location"], "all combos"))
        for reward in data["request_rewards"]:
            if not rules.request_reward_ok(state, reward):
                failures.append((mode, reward["location"], "request reward"))
        for draft in data["shop_drafts"]:
            if not rules.shop_draft_ok(state, draft):
                failures.append((mode, draft["location"], "shop draft"))
        if not rules.builds_reachable(state, len(data["biotica"])):
            failures.append((mode, "goal", "not every build reachable"))
        highest_goal = modules["options"].GoalBuilds.range_end
        if not rules.goal_reachable(state, highest_goal):
            failures.append((mode, "goal", f"the highest goal, {highest_goal}, is not reachable"))

    if failures:
        print(f"{len(failures)} unreachable:")
        for mode, location, reason in failures:
            print(f"  [{mode}] {location}  ({reason})")
        sys.exit(1)
    print(f"every location is reachable with a full inventory, in all {len(modes)} bioticum_items"
          " and bioticum_checks combinations")


if __name__ == "__main__":
    main()
