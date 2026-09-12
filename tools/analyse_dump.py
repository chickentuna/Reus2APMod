"""Step 2 of the Archipelago scoping: turn the definition dump into location/logic facts.

Reports, for the dumped catalogue:
  * how many milestones and era feats name concrete content versus a bare number;
  * which of them depend on content the run supplies at random, and so cannot be attempted
    on demand;
  * for every bioticum filter in an era feat, the set of biotica that satisfies it, so thin
    or empty candidate sets show up before any logic is written.

Usage:  python tools/analyse_dump.py [path-to-dump.json]
"""

import collections
import json
import sys
from pathlib import Path

DEFAULT_DUMP = Path(__file__).resolve().parent.parent / "dump" / "reus2_definitions.json"

# Content the run deals out rather than the player choosing. A check that needs one of these
# is a check the player farms restarts for.
RANDOM_CONTENT = {"LuxuryDefinition", "RequestDefinition", "ProjectDefinition"}

# Characters are listed separately: a leader is picked by the player when settling nomads,
# but only from the ones unlocked, so these become item-gated rather than luck-gated.
CHOSEN_IF_UNLOCKED = {"CharacterDefinition"}

THIN_CANDIDATE_SET = 6


def refs_in(node, out):
    if isinstance(node, dict):
        if "ref" in node and "type" in node:
            out.append(node)
        for value in node.values():
            refs_in(value, out)
    elif isinstance(node, list):
        for value in node:
            refs_in(value, out)
    return out


def requirement_refs(milestone):
    requirements = {k: v for k, v in milestone.items() if k.endswith("Requirement")}
    return refs_in(requirements, [])


def milestone_census(milestones):
    print(f"\n=== milestones ({len(milestones)}) ===")
    by_category = collections.Counter(m.get("category") for m in milestones)
    for category, count in by_category.most_common():
        print(f"  {count:4}  {category}")

    named, amount_only, random_gated, character_gated = [], [], [], []
    for milestone in milestones:
        refs = requirement_refs(milestone)
        types = {r["type"] for r in refs}
        (named if refs else amount_only).append(milestone)
        if types & RANDOM_CONTENT:
            random_gated.append(milestone)
        if types & CHOSEN_IF_UNLOCKED:
            character_gated.append(milestone)

    print(f"\n  name concrete content : {len(named)}")
    print(f"  pure numeric target   : {len(amount_only)}")
    print(f"  need random content   : {len(random_gated)}  <- cannot be attempted on demand")
    print(f"  need a character      : {len(character_gated)}  <- gated by an unlock, so targetable")
    for milestone in random_gated:
        print(f"      {milestone['name']}  ({milestone.get('category')})")


def feat_census(feats):
    print(f"\n=== era feats ({len(feats)}) ===")
    objectives = collections.Counter()
    referenced = collections.Counter()
    random_gated = []
    for feat in feats:
        refs = refs_in(feat.get("objectives"), [])
        for ref in refs:
            referenced[ref["type"]] += 1
        if {r["type"] for r in refs} & RANDOM_CONTENT:
            random_gated.append(feat["name"])

        def count(node):
            if isinstance(node, dict):
                if "$type" in node:
                    objectives[node["$type"]] += 1
                for value in node.values():
                    count(value)
            elif isinstance(node, list):
                for value in node:
                    count(value)

        count(feat.get("objectives"))

    print("  objective types:")
    for name, total in objectives.most_common(12):
        print(f"    {total:4}  {name}")
    print("  referenced definitions:")
    for name, total in referenced.most_common():
        print(f"    {total:4}  {name}")
    print(f"  need random content: {len(random_gated)} -> {random_gated}")


def resolve_filter(slot_filter, biotica):
    """Biotica satisfying a BioticumSlotFilter. Apex is taken to mean 'has cohorts'."""
    explicit = slot_filter.get("definition")
    if isinstance(explicit, dict) and explicit.get("ref"):
        return [explicit.get("name")]

    wanted_types = set(slot_filter.get("bioticumTypes") or [])
    wanted_tags = {t["name"] for t in slot_filter.get("tags") or [] if isinstance(t, dict)}
    type_and = slot_filter.get("typeAndOr") == "And"
    tag_and = slot_filter.get("tagAndOr") == "And"
    apex_only = slot_filter.get("apexOnly")

    matches = []
    for bioticum in biotica:
        if apex_only and not bioticum.get("cohorts"):
            continue
        types = set(bioticum.get("bioticumTypes") or [])
        if wanted_types:
            if type_and and not wanted_types <= types:
                continue
            if not type_and and not wanted_types & types:
                continue
        tags = {t["name"] for t in bioticum.get("tags") or [] if isinstance(t, dict)}
        if wanted_tags:
            if tag_and and not wanted_tags <= tags:
                continue
            if not tag_and and not wanted_tags & tags:
                continue
        matches.append(bioticum["name"])
    return matches


def filter_report(feats, biotica):
    print(f"\n=== bioticum filters in era feats (against {len(biotica)} biotica) ===")
    found = []

    def walk(node, feat_name):
        if isinstance(node, dict):
            if node.get("$type") == "BioticumSlotFilter":
                found.append((feat_name, node))
            for value in node.values():
                walk(value, feat_name)
        elif isinstance(node, list):
            for value in node:
                walk(value, feat_name)

    for feat in feats:
        walk(feat.get("objectives"), feat["name"])

    print(f"  {len(found)} filters")
    thin = []
    for feat_name, slot_filter in found:
        matches = resolve_filter(slot_filter, biotica)
        if len(matches) <= THIN_CANDIDATE_SET:
            thin.append((feat_name, len(matches), matches[:6], slot_filter))

    if not thin:
        print(f"  none resolve to {THIN_CANDIDATE_SET} candidates or fewer")
        return
    print(f"  {len(thin)} resolve to {THIN_CANDIDATE_SET} or fewer — these drive the tightest logic:")
    for feat_name, size, sample, slot_filter in thin:
        criteria = {
            "types": slot_filter.get("bioticumTypes"),
            "tags": [t["name"] for t in slot_filter.get("tags") or [] if isinstance(t, dict)],
            "apexOnly": slot_filter.get("apexOnly"),
        }
        print(f"    {feat_name:26} {size:3} candidates  {criteria}  e.g. {sample}")


def main():
    path = Path(sys.argv[1]) if len(sys.argv) > 1 else DEFAULT_DUMP
    dump = json.loads(path.read_text(encoding="utf-8"))
    definitions = dump["definitions"]
    print(f"dump: {path.name}  game {dump['meta']['gameVersion']}  "
          f"{dump['meta']['definitionCount']} definitions  depth {dump['meta']['maxDepth']}")

    milestone_census(definitions["MilestoneDefinition"])
    feat_census(definitions["EraFeatDefinition"])
    filter_report(definitions["EraFeatDefinition"], definitions["BioticumDefinition"])


if __name__ == "__main__":
    main()
