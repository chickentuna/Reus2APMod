from dataclasses import dataclass

from Options import Choice, DefaultOnToggle, PerGameCommonOptions, Range, StartInventoryPool


class GoalBuilds(Range):
    """Number of different biotica you must build to win."""
    display_name = "Goal: Biotica Built"
    range_start = 10
    range_end = 300
    default = 200


class StartingBiotica(Range):
    """Number of early biotica you start with, drawn from what your starting giants can place in
    their starting biomes."""
    display_name = "Starting Biotica"
    range_start = 4
    range_end = 30
    default = 12


class StartingPowers(Range):
    """Number of your starting giants' special terraforming powers you start with."""
    display_name = "Starting Powers"
    range_start = 0
    range_end = 3
    default = 1


class StartingCharacters(Range):
    """Number of city leaders you start with."""
    display_name = "Starting Characters"
    range_start = 1
    range_end = 5
    default = 2


class RequestRewardChecks(Range):
    """How many of a planet's draft-paying request rewards are checks, in the order they are earned.
    A planet pays at most 8, and later ones need later eras."""
    display_name = "Request Reward Checks"
    range_start = 0
    range_end = 8
    default = 6


class ShopDraftChecks(Range):
    """How many draft purchases within a single planet are checks. Each costs more than the last."""
    display_name = "Shop Draft Checks"
    range_start = 0
    range_end = 10
    default = 3


class BioticumItems(Choice):
    """How biotica are shuffled.

    - **Individual:** every bioticum is its own item.
    - **Apex:** every apex bioticum is one item, granting it and the cohorts it brings; a bioticum
      no apex brings is still its own item."""
    display_name = "Biotica"
    option_individual = 0
    option_apex = 1
    default = 0


class ComboChecks(DefaultOnToggle):
    """Whether triggering a bioticum's combo is a check. Off leaves only the builds, the request
    rewards and the shop drafts."""
    display_name = "Combo Checks"


class MicroUnlocks(Choice):
    """Which Micro types the shop sells.

    - **Off:** the ones the game draws for the run's eras.
    - **Individual:** the ones you have received, every Micro being its own item.
    - **Progressive:** the ones you have received, Micros being identical items that each grant the
      next one.

    A Micro Charge item grants one charge of its Micro either way, and the shop sells a Micro for
    charges."""
    display_name = "Micro Unlocks"
    option_off = 0
    option_individual = 1
    option_progressive = 2
    default = 0


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
    combo_checks: ComboChecks
    micro_unlocks: MicroUnlocks
