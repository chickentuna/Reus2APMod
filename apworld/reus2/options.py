from dataclasses import dataclass

from Options import Choice, PerGameCommonOptions, Range, StartInventoryPool


class MaxBioticaLevel(Choice):
    """The highest biotica level that holds items and locations, for shorter games. Biotica above it
    are yours from the start and send no checks; the Progressive Eras stay in the pool to reach them.

    - **One Star:** 117 biotica with items and locations.
    - **Two Stars:** 229 biotica with items and locations.
    - **Three Stars:** all 353 biotica have items and locations."""
    display_name = "Max Biotica Level"
    option_one_star = 1
    option_two_stars = 2
    option_three_stars = 3
    default = 3


class GoalBuilds(Range):
    """Number of different biotica you must build to win. With Bioticum Checks on When Synergized,
    a bioticum that has synergy rules counts once its synergy is done, not when it is built.

    Only biotica up to Max Biotica Level count, so this is lowered to 117 at most with One Star
    and 229 with Two Stars."""
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
    """How many level 4 request rewards are checks in a given planet, on top of the 8 level 1 to 3
    ones, which always are.
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
    default = 1


class BioticumChecks(Choice):
    """Which locations a bioticum carries.

    - **Build only:** building it, so 1 check per biotica
    - **When Synergized:** one location, sent once every synergy rule has been triggered simultaneously, so 1 check per biotica.
    - **Build And Synergized:** on location for building it, and a second check for triggering every synergy it has simultaneously, so 2 checks per biotica, except those with no synergy rules.

    """
    display_name = "Bioticum Checks"
    option_build_only = 0
    option_when_synergized = 1
    option_build_and_synergized = 2
    default = 1


@dataclass
class Reus2Options(PerGameCommonOptions):
    start_inventory_from_pool: StartInventoryPool
    max_biotica_level: MaxBioticaLevel
    goal_builds: GoalBuilds
    starting_biotica: StartingBiotica
    starting_powers: StartingPowers
    starting_characters: StartingCharacters
    request_reward_checks: RequestRewardChecks
    shop_draft_checks: ShopDraftChecks
    bioticum_items: BioticumItems
    bioticum_checks: BioticumChecks
