"""Generates apworld/reus2/data/reus2.json from the definition dump.

The file holds every item and location with its Archipelago id, plus the per-bioticum and per-rule
facts the logic reads. The mod embeds the same file, so both sides agree on names and ids.
Includes the base game and all four supported DLC packs. DLC records are marked so the world can
include each pack only when its option is enabled.

Usage:  python tools/build_ap_data.py [dump.json]
"""

import collections
import importlib.util
import json
import re
import sys
import zlib
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent
DEFAULT_DUMP = ROOT / "dump" / "reus2_definitions.json"
OUTPUT = ROOT / "apworld" / "reus2" / "data" / "reus2.json"

BASE_PACKS = {"Base", "None"}
DLC_PACK_NAMES = {
    "IceAge": "Ice Age",
    "Wetlands": "Everglades",
    "Grasslands": "Grasslands",
    "Jurassic": "Jurassic",
}
SUPPORTED_PACKS = BASE_PACKS | set(DLC_PACK_NAMES)
BASE_BIOMES = {"Desert", "Forest", "Ocean", "Rainforest", "Savanna", "Taiga"}
ROLE_TYPE = {"PlantRole": "Plant", "AnimalRole": "Animal", "GeoRole": "Geo"}
NOT_GIANTS = {"Test Giant", "EntityTentacleGiantDefinition"}
BASE_ERAS = ["Era_1_Genesis", "Era_2_Ancient"]
PROGRESSIVE_ERAS = ["Era_3_Medieval", "Era_4_Discovery"]
# Unique Archipelago request locations by level. Levels 1 to 3 can be collected across planets;
# level 4 locations are limited by the request_reward_checks option.
REQUEST_THRESHOLDS = [4, 6, 6, 6]
SHOP_DRAFTS = 512
FINAL_ERA_CHECKS = 22
# Micro1 to Micro3 buy the next Micro type of an era, and Micro types only come from items.
EXCLUDED_SHOP_OPTIONS = {"DailyChallengesAvailable", "Micro1", "Micro2", "Micro3"}
# Profile-level unlocks that are items. Every other shop option a profile level grants is given from
# the start.
UNLOCK_ITEMS = [
    ("Unlock: Micros", ["Aspect"]),
    ("Unlock: Secondary Biomes", ["SecondaryBiome"]),
    ("Shop: Upgrade Charges", ["Upgrade"]),
    ("Shop: Border Boosters", ["Borders"]),
    ("Shop: Terraform Charges", ["Mountain"]),
    ("Shop: Nomads", ["Nomad"]),
    ("Shop: Planet Expansion Charges", ["Expansion"]),
]
# Filler items: the player resource each one adds to, and by how much.
FILLER = [
    ("Eon Boost", "BonusEon", 5),
    ("Upgrade Charge", "UpgradePoints", 1),
    ("Border Booster", "BorderExpansion", 1),
    ("Terraform Charge", "MountainPoints", 1),
    ("Planet Expansion Charge", "CrevicePoints", 1),
    ("Nomad", "Nomads", 1),
]
# The share of a seed's filler slots a filler item holds, rounded down, for the ones kept rare; the
# rest of the slots are spread evenly over the other filler items.
FILLER_SHARE = {"Nomad": 0.05}
MICRO_RARITY_ORDER = {"Basic": 0, "Era1": 1, "Era2": 2, "Era3": 3}
# Rule counters that count across the whole planet; every other counter looks around the bioticum,
# so its candidates must share the bioticum's biome.
PLANET_WIDE_COUNTERS = {"Global", "GlobalExceptThisBiome"}

ABILITY_ITEMS = {
    "PlaceMountain_FrostGiant": "Terraform: Hills",
    "PlaceMountain_RockGiant": "Terraform: Mountain",
    "CreateOceanAnomaly": "Terraform: Ocean Anomaly",
    "CreateRiver": "Terraform: River",
    "CreateRiver (Use Sanctuary Animation)": "Terraform: River",
    "CreateSanctuary": "Terraform: Sanctuary",
    "CreateSanctuary (Use River Animation)": "Terraform: Sanctuary",
    "InvasiveSpecies": "Terraform: Invasive Patch",
    "Create Crevice": "Terraform: Expand Planet",
    "UpgradeSlots": "Terraform: Upgrade Slots",
}

# Ids are <category> << 40 | <bits>. The bits come from the definition GUID where there is one, so
# an id stays put when a game patch adds or reorders content. Micro Charge names no definition, so it
# takes bits 0, which no GUID produces.
ITEM_CATEGORY = {"bioticum": 1, "giant": 2, "biome": 3, "ability": 4, "character": 5, "micro": 6, "era": 7,
                 "filler": 8, "apex": 9, "charge": 10, "unlock": 11}
LOCATION_CATEGORY = {
    "build": 1, "combo": 2, "request": 3, "shop": 4, "all_combos": 5, "final_era": 6,
    "project": 7, "turning_point_star_1": 8, "turning_point_star_2": 9, "turning_point_star_3": 10,
}


def item_id(category, bits):
    return (ITEM_CATEGORY[category] << 40) | bits


def location_id(category, bits):
    return (LOCATION_CATEGORY[category] << 40) | bits


def guid_bits(guid, digits=9):
    return int(guid[:digits], 16)


def name_bits(name):
    return zlib.crc32(name.encode("utf-8"))


def is_supported(definition):
    return str(definition.get("installPack")) in SUPPORTED_PACKS


def dlc_name(definition):
    return DLC_PACK_NAMES.get(definition.get("installPack"))


def add_dlc_tag(entry, definition):
    dlc = dlc_name(definition)
    if dlc:
        entry["dlc"] = dlc
    return entry


def referenced_definitions(value, type_name):
    """Yield references of one definition type nested in an arbitrary definition."""
    if isinstance(value, dict):
        if value.get("type") == type_name and value.get("ref"):
            yield value["ref"]
        for nested in value.values():
            yield from referenced_definitions(nested, type_name)
    elif isinstance(value, list):
        for nested in value:
            yield from referenced_definitions(nested, type_name)


def build_projects(definitions):
    project_ids_used_by_turning_points = {
        project_id
        for turning_point in definitions["TurningPointDefinition"]
        for project_id in referenced_definitions(turning_point, "ProjectDefinition")
    }

    character_dlc_by_project = collections.defaultdict(set)
    character_base_projects = set()
    definitions_by_id = {
        definition["id"]: definition
        for entries in definitions.values()
        for definition in entries
        if isinstance(definition, dict) and definition.get("id")
    }
    for character in definitions["CharacterDefinition"]:
        if not is_supported(character) or not character.get("enabled") or character.get("isUnselectable"):
            continue
        dlc = dlc_name(character)
        project_ids = set(referenced_definitions(character, "ProjectDefinition"))
        for field in ("starterRequesters", "era1Requesters", "era2Requesters", "era3Requesters", "era4Requesters"):
            for requester_id in referenced_definitions(character.get(field), "RequestDefinition"):
                requester = definitions_by_id.get(requester_id)
                project_ids.update(referenced_definitions(requester, "ProjectDefinition"))
        for project_id in project_ids:
            if dlc:
                character_dlc_by_project[project_id].add(dlc)
            elif character.get("installPack") in BASE_PACKS:
                character_base_projects.add(project_id)

    projects = []
    for project in definitions["ProjectDefinition"]:
        if (not is_supported(project)
                or project["id"] in project_ids_used_by_turning_points
                or project.get("visualName") == "Construction Site"):
            continue

        entry = {
            "guid": project["id"],
            "name": project["name"],
            "visual_name": project.get("visualName"),
            "level": int(project.get("level") or 1),
            "rule_count": sum(
                1 for rule_set in project.get("ruleSets") or []
                for rule in rule_set.get("rules") or []
                if not str(rule.get("$type", "")).startswith("Fake")
            ),
            "location": f'Project: {project["name"]}',
            "location_id": location_id("project", guid_bits(project["id"])),
        }
        add_dlc_tag(entry, project)

        # A project carrying its own DLC pack uses that direct tag. Otherwise, requests generated
        # by characters identify DLC-only projects; a base-game character association keeps the
        # project available without a DLC.
        if (not entry.get("dlc") and project["id"] not in character_base_projects
                and character_dlc_by_project.get(project["id"])):
            dlcs = sorted(character_dlc_by_project[project["id"]])
            if len(dlcs) == 1:
                entry["dlc"] = dlcs[0]
            else:
                entry["dlcs"] = dlcs
        projects.append(entry)

    counts = collections.Counter(project["location"] for project in projects)
    for project in projects:
        if counts[project["location"]] > 1:
            project["location"] += f' ({project["guid"][:8]})'
    return sorted(projects, key=lambda project: (project["name"], project["guid"]))


def build_turning_point_stars(definitions):
    turning_points = {
        turning_point["id"]: turning_point
        for turning_point in definitions["TurningPointDefinition"]
        if is_supported(turning_point) and turning_point.get("enabled")
    }
    milestone_by_turning_point_and_star = {}
    for milestone in definitions["MilestoneDefinition"]:
        star = int(milestone.get("starsRequirement") or 0)
        requirements = milestone.get("turningPointRequirement") or []
        if (milestone.get("category") != "TurningPoint" or star not in (1, 2, 3)
                or milestone.get("name", "").startswith("TP_All") or len(requirements) != 1
                or not is_supported(milestone)):
            continue
        turning_point_id = requirements[0].get("ref")
        key = (turning_point_id, star)
        if turning_point_id in turning_points:
            if key in milestone_by_turning_point_and_star:
                raise ValueError(f"duplicate {star}-star milestone for {turning_points[turning_point_id]['name']}")
            milestone_by_turning_point_and_star[key] = milestone

    stars = []
    for turning_point_id, turning_point in turning_points.items():
        for star in (1, 2, 3):
            # The dump has per-turning-point milestones for stars 2 and 3; star 1 is the base
            # completion and is checked from CompleteTurningPoint when no individual milestone exists.
            milestone = milestone_by_turning_point_and_star.get((turning_point_id, star))
            stars.append(add_dlc_tag({
                "turning_point_guid": turning_point_id,
                "milestone_guid": milestone["id"] if milestone else None,
                "name": turning_point["name"],
                "era": turning_point["allowedEra"],
                "star": star,
                "location": f'Turning Point: {turning_point["name"]} - {star} Star',
                "location_id": location_id(
                    f"turning_point_star_{star}", guid_bits(turning_point_id)),
            }, turning_point))
    return sorted(stars, key=lambda star: (star["name"], star["star"]))


def build_biomes(definitions):
    return [
        add_dlc_tag({
            "guid": biome["id"], "name": biome["name"], "item": f"{biome['name']} Biome",
            "item_id": item_id("biome", guid_bits(biome["id"])),
        }, biome)
        for biome in sorted(definitions["BiomeDefinition"], key=lambda b: b["name"])
        if is_supported(biome) and biome.get("enabled")
        and (biome["name"] in BASE_BIOMES or dlc_name(biome))
    ]


def ref_name(value):
    return value.get("name") if isinstance(value, dict) else None


def ref_names(values):
    return [name for name in (ref_name(v) for v in values or []) if name]


def clean(name):
    return re.sub(r"^\d+\.?\s+", "", name).strip()


def candidates(groups, count=1, **extra):
    """A requirement met when every group has `count` other placeable biotica. An empty group can
    never be met, so the rule gets no location."""
    if not groups or any(not group for group in groups):
        return None
    return {"kind": "candidates", "groups": [sorted(set(g)) for g in groups], "count": count, **extra}


def rule_requirement(rule, pool, display, micro_names, biome_names):
    """What a combo rule needs besides its own bioticum being placed. None means the rule gets no
    location."""
    rule_type = rule.get("$type", "")

    def by_tags(tags):
        return [b["name"] for b in pool if set(tags) & set(b["tags"])]

    def by_type(bioticum_type, min_level=0):
        return [b["name"] for b in pool if bioticum_type in b["types"] and b["level"] >= min_level]

    tag = ref_name(rule.get("tag"))
    tags = ref_names(rule.get("tags"))
    bioticum_type = rule.get("bioticumType")
    bioticum_type = None if bioticum_type in (None, "Any") else bioticum_type
    count = max(1, int(rule.get("count") or 1))

    if rule_type in ("ForEachTagRule", "TagConditionRule") and tag:
        return candidates([by_tags([tag])], count if rule_type == "TagConditionRule" else 1)
    if rule_type in ("MulTagConditionRule", "ForEachMulTagRule") and tags:
        if rule.get("andOr") == "And":
            return candidates([by_tags([t]) for t in tags])
        return candidates([by_tags(tags)])
    if rule_type == "ExtraStatMultiplierRule":
        groups = ([by_tags([tag])] if tag else []) + ([by_type(bioticum_type)] if bioticum_type else [])
        return candidates(groups) if groups else {"kind": "none"}
    if rule_type in ("ForEachBioticumTypeRule", "ForEachUniqueTagRule", "YieldConditionRule", "ForEachYieldRule"):
        return candidates([by_type(bioticum_type)]) if bioticum_type else {"kind": "none"}
    if rule_type == "TypeConditionRule" and bioticum_type:
        return candidates([by_type(bioticum_type)], count)
    if rule_type == "ForEachLevelRule" and bioticum_type:
        return candidates([by_type(bioticum_type, int(rule.get("minLevel") or 0))])
    if rule_type == "ForEachSetRule":
        groups = [by_type(t) for t in rule.get("types") or [] if t != "Any"]
        groups += [by_tags([t]) for t in ref_names(rule.get("tags"))]
        groups += [[display[n]] for n in ref_names(rule.get("biotica")) if n in display]
        return candidates(groups) if groups else {"kind": "none"}
    if rule_type in ("ForEachSpecificRule", "SpecificConditionRule"):
        names = [display[n] for n in ref_names(rule.get("otherBiotica") or rule.get("biotica")) if n in display]
        # A specific rule can name its own bioticum, asking for another copy of it.
        return candidates([names], any_biome=True, allow_self=True)
    if rule_type == "ForEachPatchInBiomeRule":
        biome = ref_name(rule.get("biome"))
        return {"kind": "biome", "biome": biome} if biome in biome_names else None
    if rule_type in ("RuggedConditionRule", "ForEachRuggedRule"):
        return candidates([by_type("Geo") + by_tags(["Predator"])], require_biome="Taiga")
    if rule_type == "MicroSpreadRule":
        micro = ref_name(rule.get("micro"))
        return {"kind": "micro", "micro": micro} if micro in micro_names else {"kind": "none"}
    return {"kind": "none"}


def build_micros(definitions):
    """The Micro types a giant can be given. A Flat Micro is a giant's own, which it always has, so
    it is not an item."""
    micros = sorted(
        (m for m in definitions["BioticumAspectDefinition"]
         if is_supported(m) and m.get("rarity") != "Disabled" and m.get("boosterType") != "Flat"),
        key=lambda m: (MICRO_RARITY_ORDER.get(m.get("rarity"), 9), m["name"]),
    )
    return [
        add_dlc_tag({
            "guid": m["id"], "name": m["name"], "rarity": m.get("rarity"),
            "item": f"Micro: {m['name']}", "item_id": item_id("micro", guid_bits(m["id"])),
        }, m)
        for m in micros
    ]


def build_biotica(definitions, micro_names, biome_names_by_type):
    pool = []
    for raw in definitions["BioticumDefinition"]:
        if not is_supported(raw) or raw.get("isDebugAsset"):
            continue
        biomes = sorted({
            biome_names_by_type[biome_type]
            for biome_type, is_habitable in (raw.get("habitableBiomes") or {}).items()
            if is_habitable and biome_type in biome_names_by_type
        })
        if not biomes:
            continue
        pool.append(add_dlc_tag({
            "guid": raw["id"], "raw_name": raw["name"], "name": clean(raw["name"]),
            "level": int(raw.get("level") or 1), "types": list(raw.get("bioticumTypes") or []),
            "biomes": biomes, "cost": int(raw.get("biodiversityCost") or 0),
            "tags": ref_names(raw.get("tags")), "raw_rules": raw.get("rules") or [],
            "raw_cohorts": ref_names(raw.get("cohorts")),
        }, raw))

    for suffix in (lambda b: b["biomes"][0], lambda b: f"L{b['level']}"):
        counts = collections.Counter(b["name"] for b in pool)
        for b in pool:
            if counts[b["name"]] > 1:
                b["name"] = f"{b['name']} ({suffix(b)})"

    display = {b["raw_name"]: b["name"] for b in pool}
    skipped_rules = 0
    for b in pool:
        real = [(index, rule) for index, rule in enumerate(b.pop("raw_rules"))
                if isinstance(rule, dict) and not str(rule.get("$type", "")).startswith("Fake")]
        rules = []
        for index, rule in real:
            requirement = rule_requirement(rule, pool, display, micro_names, set(biome_names_by_type.values()))
            if requirement and requirement["kind"] == "candidates" and rule.get("ruleCounter") in PLANET_WIDE_COUNTERS:
                requirement["any_biome"] = True
            if requirement is None or requirement.get("require_biome", b["biomes"][0]) not in b["biomes"]:
                skipped_rules += 1
                continue
            rules.append({"rule_index": index, "type": rule["$type"], **requirement})
        b["rules"] = rules
        # An apex bioticum brings its cohorts with it, so one item can grant all of them.
        cohorts = sorted({display[n] for n in b.pop("raw_cohorts") if n in display})
        if cohorts:
            b["cohorts"] = cohorts
            b["apex_item"] = f"Apex: {b['name']} (Cohorts: {', '.join([cohort for cohort in cohorts])})"
            b["apex_item_id"] = item_id("apex", guid_bits(b["guid"]))
        b["item"] = b["name"]
        b["item_id"] = item_id("bioticum", guid_bits(b["guid"]))
        b["build_location"] = f"Build {b['name']}"
        b["build_location_id"] = location_id("build", guid_bits(b["guid"]))
        if rules:
            b["all_combos_location"] = f"{b['name']} Synergized"
            b["all_combos_location_id"] = location_id("all_combos", guid_bits(b["guid"]))
    for b in pool:
        del b["raw_name"]
    return sorted(pool, key=lambda b: b["name"]), skipped_rules


def build_giants(definitions):
    biome_names_by_type = {
        b["biomeType"]: b["name"] for b in definitions["BiomeDefinition"]
        if is_supported(b) and b.get("enabled")
        and (b["name"] in BASE_BIOMES or dlc_name(b))
    }
    biome_of_power = {
        p["name"]: biome_names_by_type.get(ref_name(p.get("biomeType")), ref_name(p.get("biomeType")))
        for p in definitions["PlaceBiomeAbilityDefinition"] if is_supported(p)
    }
    giants = []
    for raw in definitions["GiantDefinition"]:
        if not is_supported(raw) or raw["name"] in NOT_GIANTS or not raw.get("enabled"):
            continue
        role = (raw.get("validRoles") or [None])[0]
        specials = sorted({ABILITY_ITEMS[a] for a in ref_names(raw.get("abilities")) if a in ABILITY_ITEMS})
        name = raw["name"] if dlc_name(raw) else re.sub(r"\s*\(.*\)$", "", raw["name"])
        giants.append(add_dlc_tag({
            "guid": raw["id"], "name": name, "item": name, "item_id": item_id("giant", guid_bits(raw["id"])),
            "role": role, "type": ROLE_TYPE[role],
            "first_biome": biome_of_power.get(ref_name(raw.get("firstBiome"))),
            "second_biome": biome_of_power.get(ref_name(raw.get("secondBiome"))),
            "special": specials[0] if specials else None,
        }, raw))
    return sorted(giants, key=lambda g: g["name"])


def build_abilities(definitions):
    groups = collections.defaultdict(list)
    for entries in definitions.values():
        for definition in entries:
            if (isinstance(definition, dict) and is_supported(definition)
                    and definition.get("needsToBeUnlocked") and definition.get("name") in ABILITY_ITEMS):
                groups[ABILITY_ITEMS[definition["name"]]].append(definition["id"])
    return [{"item": name, "item_id": item_id("ability", name_bits(name)), "guids": sorted(guids)}
            for name, guids in sorted(groups.items())]


def main():
    path = Path(sys.argv[1]) if len(sys.argv) > 1 else DEFAULT_DUMP
    dump = json.loads(path.read_text(encoding="utf-8"))
    definitions = dump["definitions"]

    biome_names_by_type = {
        b["biomeType"]: b["name"] for b in definitions["BiomeDefinition"]
        if is_supported(b) and b.get("enabled")
        and (b["name"] in BASE_BIOMES or dlc_name(b))
    }
    micros = build_micros(definitions)
    biotica, skipped_rules = build_biotica(
        definitions, {m["name"] for m in micros}, biome_names_by_type)
    levels = definitions["MetaGameSettings"][0]["profileLevels"]

    data = {
        "game_version": dump["meta"]["gameVersion"],
        "biotica": biotica,
        "giants": build_giants(definitions),
        "biomes": build_biomes(definitions),
        "abilities": build_abilities(definitions),
        "characters": [
            add_dlc_tag({"guid": c["id"], "name": c["name"], "item": c["name"],
                         "item_id": item_id("character", guid_bits(c["id"]))}, c)
            for c in sorted(definitions["CharacterDefinition"], key=lambda c: c["name"])
            if is_supported(c) and c.get("enabled") and not c.get("isUnselectable")
        ],
        "micros": micros,
        "eras": {"item": "Progressive Era", "item_id": item_id("era", 1), "base": BASE_ERAS, "eras": PROGRESSIVE_ERAS},
        "unlocks": [{"item": name, "item_id": item_id("unlock", name_bits(name)), "shop_options": options}
                    for name, options in UNLOCK_ITEMS],
        # Micro Charge keeps the id Bonus Micro had.
        "filler": [{"item": "Micro Charge", "item_id": item_id("charge", 0), "resource": "AspectCharges", "amount": 1}]
                  + [{"item": name, "item_id": item_id("filler", index), "resource": resource, "amount": amount}
                     | ({"share": FILLER_SHARE[name]} if name in FILLER_SHARE else {})
                     for index, (name, resource, amount) in enumerate(FILLER, start=1)],
        "request_rewards": [
            {"location": f"Level {level + 1} Request Reward {index}",
             "location_id": location_id("request", level * 16 + index), "level": level, "index": index}
            for level, threshold in enumerate(REQUEST_THRESHOLDS) for index in range(1, threshold + 1)
        ],
        "shop_drafts": [{"location": f"Shop Draft {n}", "location_id": location_id("shop", n), "index": n}
                        for n in range(1, SHOP_DRAFTS + 1)],
        "final_era_checks": [
            {"location": f"Final Era: Discovery at 3 Stars #{n}",
             "location_id": location_id("final_era", n), "index": n}
            for n in range(1, FINAL_ERA_CHECKS + 1)
        ],
        "projects": build_projects(definitions),
        "turning_point_stars": build_turning_point_stars(definitions),
        "turning_points": sorted(t["id"] for t in definitions["TurningPointDefinition"]
                                 if str(t.get("installPack")) in BASE_PACKS and t.get("enabled")),
        "dlc_turning_points": [
            {"guid": t["id"], "dlc": dlc_name(t)}
            for t in sorted(definitions["TurningPointDefinition"], key=lambda t: t["id"])
            if dlc_name(t) and t.get("enabled")
        ],
        "shop_options": sorted({o for level in levels for o in level.get("unlockedShopOptions") or []}
                               - EXCLUDED_SHOP_OPTIONS - {o for _, options in UNLOCK_ITEMS for o in options}),
    }

    selector_path = ROOT / "apworld" / "reus2" / "dlc.py"
    selector_spec = importlib.util.spec_from_file_location("reus2_dlc_data", selector_path)
    selector = importlib.util.module_from_spec(selector_spec)
    selector_spec.loader.exec_module(selector)
    vanilla_data = selector.select_data(data)
    data["base_combo_rule_ids"] = sorted(
        f'{bioticum["guid"]}#{rule["rule_index"]}'
        for bioticum in vanilla_data["biotica"]
        for rule in bioticum["rules"]
    )

    items = [(b["item"], b["item_id"]) for b in data["biotica"]]
    items += [(b["apex_item"], b["apex_item_id"]) for b in data["biotica"] if "apex_item" in b]
    items += [(e["item"], e["item_id"]) for key in ("giants", "biomes", "abilities", "characters", "micros", "unlocks", "filler")
              for e in data[key]]
    items += [(data["eras"]["item"], data["eras"]["item_id"])]
    locations = [(b["build_location"], b["build_location_id"]) for b in data["biotica"]]
    locations += [(b["all_combos_location"], b["all_combos_location_id"]) for b in data["biotica"] if b["rules"]]
    locations += [(e["location"], e["location_id"]) for key in ("request_rewards", "shop_drafts") for e in data[key]]
    locations += [(e["location"], e["location_id"]) for e in data["final_era_checks"]]
    locations += [(e["location"], e["location_id"]) for key in ("projects", "turning_point_stars")
                  for e in data[key]]
    for label, table in (("item", items), ("location", locations)):
        names = collections.Counter(n for n, _ in table)
        ids = collections.Counter(i for _, i in table)
        assert all(c == 1 for c in names.values()), f"duplicate {label} names: {[n for n, c in names.items() if c > 1]}"
        assert all(c == 1 for c in ids.values()), f"duplicate {label} ids: {[i for i, c in ids.items() if c > 1]}"

    OUTPUT.parent.mkdir(parents=True, exist_ok=True)
    OUTPUT.write_text(json.dumps(data, indent=1, ensure_ascii=False) + "\n", encoding="utf-8")
    combos = sum(len(b["rules"]) for b in data["biotica"])
    apex = sum(1 for b in data["biotica"] if "apex_item" in b)
    print(f"wrote {OUTPUT.relative_to(ROOT)}  (game {data['game_version']})")
    print(f"  items: {len(items)}  locations: {len(locations)}")
    print(f"  biotica {len(data['biotica'])} (apex {apex})  combos {combos} (skipped {skipped_rules})"
          f"  giants {len(data['giants'])}"
          f"  biomes {len(data['biomes'])}  abilities {len(data['abilities'])}  characters {len(data['characters'])}"
          f"  micros {len(data['micros'])} projects {len(data['projects'])}"
          f"  Turning Point stars {len(data['turning_point_stars'])}")


if __name__ == "__main__":
    main()
