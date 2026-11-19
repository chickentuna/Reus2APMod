from dataclasses import dataclass

from Options import Choice, PerGameCommonOptions, Range, StartInventoryPool, Toggle


MAX_FINAL_ERA_GOAL = 15


class MaxBioticaLevel(Choice):
    """The highest biotica level that holds items and locations, for shorter games. Biotica above it
    are yours from the start and send no checks; the Progressive Eras stay in the pool to reach them.
    DLC options add that pack's biotica to the level totals."""
    display_name = "Max Biotica Level"
    option_one_star = 1
    option_two_stars = 2
    option_three_stars = 3
    default = 3


class GoalBuilds(Range):
    """Number of different biotica you must build to meet the biotica part of the goal. The Final
    Era goal must also be met when its count is nonzero. With Bioticum Checks on When Synergized,
    a bioticum that has synergy rules counts once its synergy is done, not when it is built.

    Only biotica up to Max Biotica Level count. The goal is lowered to the available biotica count
    for the selected DLCs and Max Biotica Level."""
    display_name = "Goal: Biotica Built"
    range_start = 10
    range_end = 600
    default = 200


class GoalFinalEras(Range):
    """Number of different Era 4: Discovery sessions to complete at three stars, up to 15, in
    addition to the biotica goal. At least six Final Era checks are available, with two more than
    the goal."""
    display_name = "Goal: Final Eras at 3 Stars"
    range_start = 0
    range_end = MAX_FINAL_ERA_GOAL
    default = 4


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
    """How many level 4 request rewards are checks in a given planet, on top of the 16 level 1 to 3
    locations, which are available across planets.
    Every new planet counts from its first level 4 request again, so if this is too high, the check isn't reachable."""
    display_name = "Level 4 Request Reward Checks"
    range_start = 0
    range_end = 6
    default = 6


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


class OnlyMaxLevelBiotica(Toggle):
    """Only biotica at Max Biotica Level have locations, except Evolve biotica, which always have a
    build location and also a synergy location when this option is off.

    Caution: enable other location options as well, or there may not be enough locations and
    progression can lock very quickly."""
    display_name = "Only Max-Level Biotica Locations"
    default = True


class ProjectChecks(Choice):
    """Which project locations are included.

    - **None:** projects are not locations.
    - **Build only:** a project check is sent when the project is built.
    - **When Synergized:** a project check is sent when all of its active rule-set rules are active.

    Projects without synergy rules are checked when built in either enabled mode.
    """
    display_name = "Project Checks"
    option_none = 0
    option_build_only = 1
    option_when_synergized = 2
    default = option_build_only


class TurningPointChecks(Toggle):
    """Include locations for the one-, two-, and three-star milestones of each Turning Point."""
    display_name = "Turning Point Star Checks"
    default = True


class IceAge(Toggle):
    """Include Ice Age biotica, characters, biome, and giant. Enable only if the game has this DLC."""
    display_name = "Ice Age DLC"
    default = False


class Everglades(Toggle):
    """Include Everglades biotica, characters, biome, and giant. Enable only if the game has this DLC."""
    display_name = "Everglades DLC"
    default = False


class Grasslands(Toggle):
    """Include Grasslands biotica, characters, biome, and giant. Enable only if the game has this DLC."""
    display_name = "Grasslands DLC"
    default = False


class Jurassic(Toggle):
    """Include Jurassic biotica, characters, biome, and giant. Enable only if the game has this DLC."""
    display_name = "Jurassic DLC"
    default = False


@dataclass
class Reus2Options(PerGameCommonOptions):
    start_inventory_from_pool: StartInventoryPool
    max_biotica_level: MaxBioticaLevel
    goal_builds: GoalBuilds
    goal_final_eras: GoalFinalEras
    starting_biotica: StartingBiotica
    starting_powers: StartingPowers
    starting_characters: StartingCharacters
    request_reward_checks: RequestRewardChecks
    bioticum_items: BioticumItems
    bioticum_checks: BioticumChecks
    only_max_level_biotica: OnlyMaxLevelBiotica
    project_checks: ProjectChecks
    turning_point_checks: TurningPointChecks
    ice_age: IceAge
    everglades: Everglades
    grasslands: Grasslands
    jurassic: Jurassic
