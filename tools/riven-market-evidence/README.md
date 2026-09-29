# Riven market evidence (Impact / Puncture / Slash)

Builds a per-weapon table of which physical Riven traits really roll. It uses real Rivens listed on
warframe.market (read-only public API). It is independent evidence for checking PE+ `riven_unrollables`,
the server's errata and the decoded rule in `../riven-physical-rules`.
Background and accuracy: `work/research/riven-roll-rules-2026-09-29/THRESHOLD_25_PERCENT.md`.

Requirements: Python 3.9+ and nothing else (standard library only).

## Decision rule (per weapon, per trait)

- **Listing cleaning:** first, remove placeholder or typo listings. A listing is removed if any percent-unit attribute has `|value| <= 1.0`.
- **Counts:**
  - `n` is the number of cleaned listings for the weapon. warframe.market returns at most the 500 newest.
  - `seen` is the number of cleaned listings that carry the trait, as + or −.
- **Decision:**
  - `n >= --min-n` (default 100) and `seen == 0` → **unrollable**.
  - `seen >= --min-seen` (default 3) → **rollable**.
  - `n >= --min-n` and only 1–2 sightings where that few is very unlikely for a rollable trait
    (`P(X <= seen | n, rate) < --typo-p`, default 0.01) → **unrollable**. The sightings are seller typos,
    placeholders (e.g. Punch Through picked as Puncture) or legacy Rivens. `rate` is how often the weapon's own
    rollable physical traits are listed, or `--typical-rate` (default 0.121) when it has none.
  - Anything else is unknown, and the tag is omitted.

Limits:

- Listings are player-entered.
- Old Rivens keep stats that DE later removed, which can produce a false "rollable" for traits removed recently.
- Absence at n ≥ 100 is strong evidence, but not proof. An allowed physical trait appears on about 12 % of listings.

## Run after a client/server metadata update

```
# 1. build (only stale or missing weapons are fetched; ~4 s per request, backs off on HTTP 429)
python riven_market_evidence.py build ^
  --export  <SpaceNinjaServer>\static\generated\openwf-metadata\current-public-export.json ^
  --pe-plus <SpaceNinjaServer>\node_modules\warframe-public-export-plus ^
  --cache   <any scratch dir kept between runs> ^
  --out     <research folder>\inputs\riven_market_evidence.json

# 2. compare with PE+ riven_unrollables (+ server errata) and the decoded rule; writes Markdown to stdout
python riven_market_evidence.py compare ^
  --evidence <research folder>\inputs\riven_market_evidence.json ^
  --export   <SpaceNinjaServer>\static\generated\openwf-metadata\current-public-export.json ^
  --pe-plus  <SpaceNinjaServer>\node_modules\warframe-public-export-plus ^
  --errata /Lotus/Weapons/Lasria/LasGooAK/LasGooAKPlayerWeapon /Lotus/Weapons/Lasria/LasGooPistol/LasGooPistolPlayerWeapon ^
  --decoded <path>\weapon_rule.json > comparison.md
```

Weapons come from the union of `rivenContracts[*].compatibleItems` and `sentinelCompatibleItems`.

Slug mapping:

1. The warframe.market `gameRef` equal to the weapon path.
2. Otherwise, an exact English-name match through PE+ `ExportWeapons` and `dict.en.json`. This covers Plague Zaw strikes and Vinquibus (Melee).
3. Otherwise, a parent Riven entry is mapped through its child weapons (PE+ `parentName`), e.g.
   `QuadShotgunBase` → Hek, `DarkDaggerBase` → Dark Dagger.

Weapons that still don't map are listed under `unmapped`, with the reason.

`--max-age-days` (default 14) controls when cached listings are refetched. Use `--max-age-days 0` to force a full refresh, which takes about 30 minutes for about 420 weapons.

Feed the output to the server generator: `scripts/metadata-update/build-current-export.py ... --market-evidence
riven_market_evidence.json --weapon-rules weapon_rule.json`. It builds `rivenPhysicalRules` for every Riven weapon
(market evidence → PE+ `riven_unrollables` → decoded rule for new weapons), which is all the server reads.
