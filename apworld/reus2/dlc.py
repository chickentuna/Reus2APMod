def select_data(data, ice_age: bool = False, everglades: bool = False,
                grasslands: bool = False, jurassic: bool = False):
    """Return game data filtered to the DLC packs enabled by a seed."""
    enabled_dlcs = set()
    if ice_age:
        enabled_dlcs.add("Ice Age")
    if everglades:
        enabled_dlcs.add("Everglades")
    if grasslands:
        enabled_dlcs.add("Grasslands")
    if jurassic:
        enabled_dlcs.add("Jurassic")

    selected = dict(data)

    def available(entry):
        if entry.get("dlc"):
            return entry["dlc"] in enabled_dlcs
        if entry.get("dlcs"):
            return bool(set(entry["dlcs"]) & enabled_dlcs)
        return True

    for key in ("giants", "biomes", "characters", "micros", "abilities"):
        selected[key] = [entry for entry in data[key] if available(entry)]
    for key in ("projects", "turning_point_stars"):
        selected[key] = [entry for entry in data[key] if available(entry)]

    biome_names = {entry["name"] for entry in selected["biomes"]}
    micro_names = {entry["name"] for entry in selected["micros"]}
    candidates = []
    for bioticum in data["biotica"]:
        if not available(bioticum):
            continue
        biomes = [biome for biome in bioticum["biomes"] if biome in biome_names]
        if biomes:
            candidates.append({**bioticum, "biomes": biomes})

    bioticum_names = {bioticum["name"] for bioticum in candidates}
    biotica = []
    for bioticum in candidates:
        rules = []
        for rule in bioticum["rules"]:
            if rule.get("require_biome") and rule["require_biome"] not in biome_names:
                continue
            kind = rule["kind"]
            if kind == "biome" and rule["biome"] not in biome_names:
                continue
            if kind == "micro" and rule["micro"] not in micro_names:
                continue
            if kind == "candidates":
                groups = [[name for name in group if name in bioticum_names]
                          for group in rule["groups"]]
                if any(not group for group in groups):
                    continue
                rules.append({**rule, "groups": groups})
            else:
                rules.append(rule)
        biotica.append({**bioticum, "rules": rules})
    selected["biotica"] = biotica
    return selected
