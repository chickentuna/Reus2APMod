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
    options = types.ModuleType("Options")
    for name in ("Choice", "Range", "PerGameCommonOptions", "StartInventoryPool", "Toggle", "DefaultOnToggle"):
        setattr(options, name, type(name, (), {}))
    sys.modules["BaseClasses"] = base
    sys.modules["Options"] = options

    package = types.ModuleType("reus2")
    package.__path__ = [str(WORLD)]
    sys.modules["reus2"] = package
    loaded = {}
    for name in ("game_data", "options", "rules"):
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


def main():
    modules = load_world_modules()
    data = modules["game_data"].DATA

    failures = []
    for micro_mode in (0, 1):
        world = types.SimpleNamespace(
            player=1, options=types.SimpleNamespace(micro_mode=types.SimpleNamespace(value=micro_mode)))
        rules = modules["rules"].Reus2Rules(world)
        state = FullInventory()
        for bioticum in data["biotica"]:
            if not rules.can_build(state, bioticum):
                failures.append((micro_mode, bioticum["build_location"], "build"))
            for rule in bioticum["rules"]:
                if not rules.can_combo(state, bioticum, rule):
                    failures.append((micro_mode, rule["location"], rule["type"]))
        if not rules.builds_reachable(state, len(data["biotica"])):
            failures.append((micro_mode, "goal", "not every build reachable"))

    if failures:
        print(f"{len(failures)} unreachable:")
        for micro_mode, location, reason in failures:
            print(f"  [micro_mode={micro_mode}] {location}  ({reason})")
        sys.exit(1)
    print("every location is reachable with a full inventory, in both micro modes")


if __name__ == "__main__":
    main()
