# Mod rarity and exact maximum rank — 2026-08-25

> **Superseded architecture notice (2026-08-26):** the original global
> `static/generated/openwf-metadata/upgrade-overrides.json` design coupled client presentation to the
> server's published definition and is no longer active. The corrected package architecture and the
> negative persistence finding are recorded in
> `COORDINATED_CLIENT_SERVER_METADATA_PATCHES_2026-08-26.md`. This file remains as the historical
> experiment record; statements below about mandatory coordination are not current behavior.

## Hypothesis ledger

| Hypothesis | Evidence | Verdict |
|---|---|---|
| Visible rarity exists in patchable client metadata. | Decoded upgrade types carry top-level `Rarity=COMMON/UNCOMMON/RARE/LEGENDARY`; the editor already emits proven top-level prepend overrides. | TRUE at the metadata/schema level; live visual test pending. |
| Cache `FusionLimit=QA_*` uniquely determines exact rank. | Normal rank-3 Reach and Quick Return use the same coarse family seen on rank-5 mods; the cache enum is not a numeric rank. | FALSE. |
| Exact maximum rank exists in OpenWF's served data. | `warframe-public-export-plus/ExportUpgrades.json` records numeric `fusionLimit`; OpenWF's `/Manifest/ExportUpgrades_<lang>.json` route serializes that object. | TRUE. |
| One coordinated editor action can control both values without editing dependencies. | The editor emits a metadata patch plus a generated override document; OpenWF validates and mutates the shared imported export object at startup. | TRUE offline; live client confirmation pending. |
| Every integer rank 0–10 works in the current client. | Stock data proves 0/3/5/10. No controlled live evidence yet proves the remaining integers. | UNPROVEN; allowed only as an experiment. |

## Authoritative field separation

`Rarity` in `Packages.bin` controls the client-side upgrade type metadata. Numeric `fusionLimit` comes
from Public Export and is served by OpenWF. The similarly named cache `FusionLimit=QA_*` is retained as
a separately labeled coarse hint; it is never translated blindly into an exact rank.

The coordinated override schema is:

```json
{
  "schemaVersion": 1,
  "upgrades": {
    "/Lotus/Upgrades/Mods/Warframe/AvatarAbilityRangeMod": {
      "rarity": "LEGENDARY",
      "fusionLimit": 10
    }
  }
}
```

OpenWF refuses startup for unknown upgrade paths, unsupported keys, invalid rarity values, non-integer
ranks, or ranks outside 0–10. Existing override entries are preserved by the editor's merge.

## Controlled test

The staged test changes normal Stretch from `UNCOMMON`, rank 5 to `LEGENDARY`, rank 10. Its client
patch also opts into `QA_VERY_HIGH`, explicitly testing the coarse hint alongside the authoritative
numeric override. Check after a full OpenWF/client restart:

1. Stretch uses the Legendary card rarity treatment and rarity filters.
2. Stretch displays ten rank pips and can be fused to rank 10.
3. Rank 6–10 upgrade values continue the normal linear `Value × (rank + 1)` rule.
4. The upgraded fingerprint persists after relogging.
5. Removing both test files restores stock rarity and rank.

Do not infer arbitrary-rank support from the rank-10 result; a later rank-7 test isolates that claim.
