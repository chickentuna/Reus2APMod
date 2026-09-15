"""Generates apworld/reus2/data/reus2.json from the definition dump.

The file holds every item and location with its Archipelago id, plus the per-bioticum and per-rule
facts the logic reads. The mod embeds the same file, so both sides agree on names and ids.
Base game only.

Usage:  python tools/build_ap_data.py [dump.json]
"""

import collections
import json
import re
import sys
import zlib
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent
DEFAULT_DUMP = ROOT / "dump" / "reus2_definitions.json"
OUTPUT = ROOT / "apworld" / "reus2" / "data" / "reus2.json"

BASE_PACKS = {"Base", "None"}
BASE_BIOMES = ["Desert", "Forest", "Ocean", "Rainforest", "Savanna", "Taiga"]
ROLE_TYPE = {"PlantRole": "Plant", "AnimalRole": "Animal", "GeoRole": "Geo"}
NOT_GIANTS = {"Test Giant", "EntityTentacleGiantDefinition"}
BASE_ERAS = ["Era_1_Genesis", "Era_2_Ancient"]
PROGRESSIVE_ERAS = ["Era_3_Medieval", "Era_4_Discovery"]
REQUEST_THRESHOLDS = [2, 3, 3]
SHOP_DRAFTS = 10
EXCLUDED_SHOP_OPTIONS = {"DailyChallengesAvailable"}
MICRO_RARITY_ORDER = {"Basic": 0, "Era1": 1, "Era2": 2, "Era3": 3}
# Rule counters that count across the whole planet; every other counter looks around the bioticum,
# so its candidates must share the bioticum's biome.
PLANET_WIDE_COUNTERS = {"Global", "GlobalExceptThisBiome"}

ABILITY_ITEMS = {
    "PlaceMountain_FrostGiant": "Mountain (Frost Giant)",
    "PlaceMountain_RockGiant": "Mountain (Rock Giant)",
    "CreateOceanAnomaly": "Ocean Anomaly",
    "CreateRiver": "River",
    "CreateRiver (Use Sanctuary Animation)": "River",
    "CreateSanctuary": "Sanctuary",
    "CreateSanctuary (Use River Animation)": "Sanctuary",
    "InvasiveSpecies": "Invasive Species",
    "Create Crevice": "Crevice",
    "UpgradeSlots": "Upgrade Slots",
}

# Ids are <category> << 40 | <bits>. The bits come from the definition GUID where there is one, so
# an id stays put when a game patch adds or reorders content. Bonus Micro names no definition, so it
# takes bits 0, which no GUID produces.
ITEM_CATEGORY = {"bioticum": 1, "giant": 2, "biome": 3, "ability": 4, "character": 5, "micro": 6, "era": 7,
                 "filler": 8, "apex": 9, "charge": 10}
LOCATION_CATEGORY = {"build": 1, "combo": 2, "request": 3, "shop": 4, "all_combos": 5}


def item_id(category, bits):
    return (ITEM_CATEGORY[category] << 40) | bits


def location_id(category, bits):
    return (LOCATION_CATEGORY[category] << 40) | bits


def guid_bits(guid, digits=9):
    return int(guid[:digits], 16)


def name_bits(name):
    return zlib.crc32(name.encode("utf-8"))


def is_base(definition):
    return str(definition.get("installPack")) in BASE_PACKS


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


def rule_requirement(rule, pool, display, micro_names):
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
        return {"kind": "biome", "biome": biome} if biome in BASE_BIOMES else None
    if rule_type in ("RuggedConditionRule", "ForEachRuggedRule"):
        return candidates([by_type("Geo") + by_tags(["Predator"])], require_biome="Taiga")
    if rule_type == "MicroSpreadRule":
        micro = ref_name(rule.get("micro"))
        return {"kind": "micro", "micro": micro} if micro in micro_names else {"kind": "none"}
    return {"kind": "none"}


def build_micros(definitions):
    micros = sorted(
        (m for m in definitions["BioticumAspectDefinition"] if is_base(m) and m.get("rarity") != "Disabled"),
        key=lambda m: (MICRO_RARITY_ORDER.get(m.get("rarity"), 9), m["name"]),
    )
    return [
        {"guid": m["id"], "name": m["name"], "rarity": m.get("rarity"),
         "item": f"Micro: {m['name']}", "item_id": item_id("micro", guid_bits(m["id"]))}
        for m in micros
    ]


def build_biotica(definitions, micro_names):
    pool = []
    for raw in definitions["BioticumDefinition"]:
        if not is_base(raw) or raw.get("isDebugAsset"):
            continue
        biomes = [b for b in BASE_BIOMES if (raw.get("habitableBiomes") or {}).get(b) is True]
        if not biomes:
            continue
        pool.append({
            "guid": raw["id"], "raw_name": raw["name"], "name": clean(raw["name"]),
            "level": int(raw.get("level") or 1), "types": list(raw.get("bioticumTypes") or []),
            "biomes": biomes, "cost": int(raw.get("biodiversityCost") or 0),
            "tags": ref_names(raw.get("tags")), "raw_rules": raw.get("rules") or [],
            "raw_cohorts": ref_names(raw.get("cohorts")),
        })

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
        for number, (index, rule) in enumerate(real, 1):
            assert index < 32, f"rule index {index} on {b['name']} does not fit the combo id"
            requirement = rule_requirement(rule, pool, display, micro_names)
            if requirement and requirement["kind"] == "candidates" and rule.get("ruleCounter") in PLANET_WIDE_COUNTERS:
                requirement["any_biome"] = True
            if requirement is None or requirement.get("require_biome", b["biomes"][0]) not in b["biomes"]:
                skipped_rules += 1
                continue
            name = f"{b['name']} Combo" if len(real) == 1 else f"{b['name']} Combo {number}"
            rules.append({
                "rule_index": index, "type": rule["$type"], "location": name,
                "location_id": location_id("combo", (guid_bits(b["guid"], 8) << 5) | index),
                **requirement,
            })
        b["rules"] = rules
        # An apex bioticum brings its cohorts with it, so one item can grant all of them.
        cohorts = sorted({display[n] for n in b.pop("raw_cohorts") if n in display})
        if cohorts:
            b["cohorts"] = cohorts
            b["apex_item"] = f"Apex: {b['name']}"
            b["apex_item_id"] = item_id("apex", guid_bits(b["guid"]))
        b["item"] = b["name"]
        b["item_id"] = item_id("bioticum", guid_bits(b["guid"]))
        b["build_location"] = f"Build {b['name']}"
        b["build_location_id"] = location_id("build", guid_bits(b["guid"]))
        if rules:
            b["all_combos_location"] = f"{b['name']} All Combos"
            b["all_combos_location_id"] = location_id("all_combos", guid_bits(b["guid"]))
    for b in pool:
        del b["raw_name"]
    return sorted(pool, key=lambda b: b["name"]), skipped_rules


def build_giants(definitions):
    biome_of_power = {p["name"]: ref_name(p.get("biomeType")) for p in definitions["PlaceBiomeAbilityDefinition"]}
    giants = []
    for raw in definitions["GiantDefinition"]:
        if not is_base(raw) or raw["name"] in NOT_GIANTS:
            continue
        role = (raw.get("validRoles") or [None])[0]
        specials = sorted({ABILITY_ITEMS[a] for a in ref_names(raw.get("abilities")) if a in ABILITY_ITEMS})
        name = re.sub(r"\s*\(.*\)$", "", raw["name"])
        giants.append({
            "guid": raw["id"], "name": name, "item": name, "item_id": item_id("giant", guid_bits(raw["id"])),
            "role": role, "type": ROLE_TYPE[role],
            "first_biome": biome_of_power.get(ref_name(raw.get("firstBiome"))),
            "second_biome": biome_of_power.get(ref_name(raw.get("secondBiome"))),
            "special": specials[0] if specials else None,
        })
    return sorted(giants, key=lambda g: g["name"])


def build_abilities(definitions):
    groups = collections.defaultdict(list)
    for entries in definitions.values():
        for definition in entries:
            if isinstance(definition, dict) and definition.get("needsToBeUnlocked") and definition.get("name") in ABILITY_ITEMS:
                groups[ABILITY_ITEMS[definition["name"]]].append(definition["id"])
    return [{"item": name, "item_id": item_id("ability", name_bits(name)), "guids": sorted(guids)}
            for name, guids in sorted(groups.items())]


def main():
    path = Path(sys.argv[1]) if len(sys.argv) > 1 else DEFAULT_DUMP
    dump = json.loads(path.read_text(encoding="utf-8"))
    definitions = dump["definitions"]

    micros = build_micros(definitions)
    biotica, skipped_rules = build_biotica(definitions, {m["name"] for m in micros})

    data = {
        "game_version": dump["meta"]["gameVersion"],
        "biotica": biotica,
        "giants": build_giants(definitions),
        "biomes": [{"guid": b["id"], "name": b["name"], "item": f"{b['name']} Biome",
                    "item_id": item_id("biome", guid_bits(b["id"]))}
                   for b in sorted(definitions["BiomeDefinition"], key=lambda b: b["name"]) if b["name"] in BASE_BIOMES],
        "abilities": build_abilities(definitions),
        "characters": [{"guid": c["id"], "name": c["name"], "item": c["name"],
                        "item_id": item_id("character", guid_bits(c["id"]))}
                       for c in sorted(definitions["CharacterDefinition"], key=lambda c: c["name"])
                       if is_base(c) and c.get("enabled") and not c.get("isUnselectable")],
        "micros": micros,
        "bonus_micro": {"item": "Bonus Micro", "item_id": item_id("charge", 0)},
        "eras": {"item": "Progressive Era", "item_id": item_id("era", 1), "base": BASE_ERAS, "eras": PROGRESSIVE_ERAS},
        "filler": [{"item": "Eon Boost", "item_id": item_id("filler", 1)}],
        "request_rewards": [
            {"location": f"Level {level + 1} Request Reward {index}",
             "location_id": location_id("request", level * 16 + index), "level": level, "index": index}
            for level, threshold in enumerate(REQUEST_THRESHOLDS) for index in range(1, threshold + 1)
        ],
        "shop_drafts": [{"location": f"Shop Draft {n}", "location_id": location_id("shop", n), "index": n}
                        for n in range(1, SHOP_DRAFTS + 1)],
        "turning_points": sorted(t["id"] for t in definitions["TurningPointDefinition"] if is_base(t) and t.get("enabled")),
        "shop_options": sorted({o for level in definitions["MetaGameSettings"][0]["profileLevels"]
                                for o in level.get("unlockedShopOptions") or []} - EXCLUDED_SHOP_OPTIONS),
    }

    items = [(b["item"], b["item_id"]) for b in data["biotica"]]
    items += [(b["apex_item"], b["apex_item_id"]) for b in data["biotica"] if "apex_item" in b]
    items += [(e["item"], e["item_id"]) for key in ("giants", "biomes", "abilities", "characters", "micros", "filler") for e in data[key]]
    items += [(data["bonus_micro"]["item"], data["bonus_micro"]["item_id"]), (data["eras"]["item"], data["eras"]["item_id"])]
    locations = [(b["build_location"], b["build_location_id"]) for b in data["biotica"]]
    locations += [(r["location"], r["location_id"]) for b in data["biotica"] for r in b["rules"]]
    locations += [(b["all_combos_location"], b["all_combos_location_id"]) for b in data["biotica"] if b["rules"]]
    locations += [(e["location"], e["location_id"]) for key in ("request_rewards", "shop_drafts") for e in data[key]]
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
          f"  micros {len(data['micros'])}")


if __name__ == "__main__":
    main()
