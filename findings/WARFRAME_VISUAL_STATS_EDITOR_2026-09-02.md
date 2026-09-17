# Warframe visual stats editor — 2026-09-02
## H1 — Warframe base stats require Lua polling

**Result: FALSE.** The composed powersuit metadata exposes native base Health,
Shields, Armor, maximum Energy, starting Energy, and movement-speed fields.
The visual editor changes only those existing metadata scalars.

Observed naming aliases are kept data-compatible (`MaxHealthOverride` versus
`MaxHealth`, `ArmourRatingOverride` versus `ArmourRating`, and equivalent
Energy/movement variants). The friendly UI label never replaces the emitted
native field name.

## H2 — Rank-30 growth is one repeated formula

**Result: FALSE.** `LevelUpgrades` is an ordered milestone array. In the shared
`PlayerPowerSuit` base record, Health, Shields, and Energy each have ten
non-zero milestone entries. Their shipped totals are +100 Health, +100 Shields,
and +50 Energy.

The visual editor presents each decoded total as “gained by rank 30.” Changing
that total scales only the already-existing milestone values proportionally;
it preserves milestone count, order, `UpgradeType`, operation, and rank
positions. It refuses a synthetic total when there is no non-zero native
milestone ladder to scale.

## H3 — The rank preview equals the final Arsenal value

**Result: FALSE.** The preview is deliberately labelled native/unmodded. It is:

`base metadata value + sum of existing LevelUpgrades milestones`

It does not include mods, Archon Shards, abilities, mission buffs, or other
runtime multipliers.

## Verification

- Core + WPF Release builds: 0 warnings, 0 errors.
- Metadata editor self-test: 95/95.
- Tests cover all six friendly base fields, rank-total aggregation, and exact
  proportional emission to the original `LevelUpgrades.N.Value` paths.
- The main raw metadata table remains the Advanced fallback; the visual editor
  stages changes into the same patch rows and does not create a second format.
