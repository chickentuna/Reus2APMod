from dataclasses import dataclass

from Options import Choice, PerGameCommonOptions, Range, StartInventoryPool


class GoalBuilds(Range):
    """Number of different biotica you must build to win."""
    display_name = "Goal: Biotica Built"
    range_start = 10
    range_end = 300
    default = 200


class StartingBiotica(Range):
    """Number of early biotica you start with, drawn from what your starting giants can place in
    their starting biomes.

    An item that does not fit the seed's locations is given to you at the start as well, so
    you can open with more than this."""
    display_name = "Starting Biotica"
    range_start = 4
    range_end = 30
    default = 4


class StartingPowers(Range):
    """Number of your starting giants' special terraforming powers you start with.

    An item that does not fit the seed's locations is given to you at the start as well, so
    you can open with more than this."""
    display_name = "Starting Powers"
    range_start = 0
    range_end = 3
    default = 0


class StartingCharacters(Range):
    """Number of city leaders you start with.

    An item that does not fit the seed's locations is given to you at the start as well, so
    you can open with more than this."""
    display_name = "Starting Characters"
    range_start = 1
    range_end = 5
    default = 1


class RequestRewardChecks(Range):
    """How many level 4 request rewards are checks in a given planet.
    Every new planet counts from its first level 4 request again, so if this is too high, the check isn't reachable."""
    display_name = "Level 4 Request Reward Checks"
    range_start = 0
    range_end = 6
    default = 0


class ShopDraftChecks(Range):
    """How many draft purchases within a single planet are checks. Each costs more than the last."""
    display_name = "Shop Draft Checks"
    range_start = 0
    range_end = 10
    default = 3


class BioticumItems(Choice):
    """How biotica are shuffled.

    - **Individual:** every bioticum is its own item. Puts a lot of items in the pool!
    - **Apex:** every apex bioticum is one item, granting it and the cohorts it brings; a bioticum
      no apex brings is still its own item."""
    display_name = "Biotica"
    option_individual = 0
    option_apex = 1
    default = 0


class BioticumChecks(Choice):
    """Which locations a bioticum carries.

    - **Build only:** building it, and nothing for its combos.
    - **Build and combos:** building it, and a second one for triggering every combo rule it has.
    - **Combos only:** one location, sent once every combo rule it has has been triggered, which
      takes building it along the way.
    - **Per rule:** building it, and one for each combo rule separately.
    - **Max combos:** the combos-only location, sent once every combo rule that stacks 6 times or
      fewer has reached its maximum stack and every other rule has been triggered.

    A bioticum with no combo rule carries its build in every case."""
    display_name = "Bioticum Checks"
    option_build_only = 0
    option_build_and_combos = 1
    option_combos_only = 2
    option_per_rule = 3
    option_max_combos = 4
    default = 2


@dataclass
class Reus2Options(PerGameCommonOptions):
    start_inventory_from_pool: StartInventoryPool
    goal_builds: GoalBuilds
    starting_biotica: StartingBiotica
    starting_powers: StartingPowers
    starting_characters: StartingCharacters
    request_reward_checks: RequestRewardChecks
    shop_draft_checks: ShopDraftChecks
    bioticum_items: BioticumItems
    bioticum_checks: BioticumChecks
