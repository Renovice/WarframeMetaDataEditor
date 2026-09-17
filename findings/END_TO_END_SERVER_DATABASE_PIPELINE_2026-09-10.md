# End-to-end OpenWF server database pipeline — 2026-09-10

## Scope

This ledger records the completed offline extraction, normalization, activation, and OpenWF execution
checks for items, cosmetics, pricing fields, resources, syndicates, vendors, Dojo recipes, missions,
reward tables, enemy drops, bounties, relics, and general recipes. Live client presentation and mission
gameplay remain a separate acceptance layer.

Pinned client evidence:

- generated source snapshot: `a121616c72bb0963`;
- `Packages.bin` SHA-256:
  `a121616c72bb0963dc43a233f8d393a06cd2fdd99ca00b419b0ff5e51f74acf4`;
- executable file version: `2026.08.19.11.06`;
- cache last write: `2026-09-09T23:31:37.5554475Z`;
- applied OpenWF Public Export Plus 0.6.8 snapshot SHA-256:
  `f40f41aade7c9f0b63cd09517f16b5584925b9d8cdbc7e3c9448192b04143844`;
- fixed-point scan:
  `work/staging/metadata-updater-runs/20260910-043015-a121616c72bb0963`.

## Hypotheses and results

| Hypothesis                                                                                 | Evidence                                                                                                                                                                                         | Result                                                  |
| ------------------------------------------------------------------------------------------ | ------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------ | ------------------------------------------------------- |
| The npm Public Export can serve as the current client denominator by itself                | It explicitly covers 7,566 admitted records, while the current client supplies 8,685 generated cosmetics and 6,919 admitted paths with no explicit Public Export/generated-cosmetic item record. | False.                                                  |
| Warframe-Exporter is obsolete now that Packages.bin is decoded                             | Packages.bin supplies type metadata, while current syndicate, Dojo, and vendor contracts are separate cache datafiles. Warframe-Exporter extracted and preserved 120 such records.               | False. It remains the raw datafile extraction layer.    |
| A missing explicit item table means OpenWF cannot grant the item                           | The isolated production dispatcher classified every one of the 23,170 admitted StoreItem products, including the 6,919 no-explicit-record paths, with zero failures or unexplained empty deltas. | False. Generic path-based handling already covers them. |
| ProductCategory alone proves how an item should persist                                    | The complete run found state mutation, intentional non-inventory, context-dependent, and definition-only contracts that require distinct treatment.                                              | False.                                                  |
| Every current vendor can use one generated contract without vendor-specific hand insertion | All 96 current manifests resolve by type and OID and pass the same deterministic normal/full-stock generation.                                                                                   | True for the extracted manifests.                       |
| Full stock also removes account purchase history                                           | Full-stock output advertises all offers. `noVendorPurchaseLimits` separately controls `RecentVendorPurchases`.                                                                                   | False; these are separate controls.                     |
| Every enemy-drop path is a persistent inventory reward                                     | Thirteen entries are mission-runtime pickups, and 250 entries retain DE's unresolved `/Lotus/RESOLUTION_FAILURE/Region Resource` placeholder.                                                    | False; both cases are recorded separately.              |
| Offline structural success proves the current client displays and executes everything      | The audits execute server acquisition/purchase paths and validate references, but do not observe Arsenal filtering, shop UI, or mission gameplay.                                                | False. A combined in-game pass is still required.       |

## Source architecture

1. The installed client cache and decoded `Packages.bin` provide the current type, inheritance,
   localization, compatibility, pricing-field, StoreItem, and package graph.
2. [Warframe-Exporter](https://github.com/Puxtril/Warframe-Exporter) extracts raw current-client
   Syndicate, DojoRecipeManifest, and VendorManifest datafiles. The updater runs it and `bin2json` in a
   short temporary directory, then copies hash-pinned raw and normalized artifacts into the scan.
3. The pinned `warframe-public-export-plus` generator provides OpenWF's 44 normalized table contracts.
   It combines DE Public Export, package metadata, extracted datafiles, wiki mappings, and DE's official
   drop page. It is a normalization reference rather than proof of current-client completeness.
4. The updater produces exact current-client overlays, validation, a readable diff, atomic apply,
   backup, and hash-checked rollback.
5. OpenWF executes the composite production-path audit with
   `npm run audit:metadata-end-to-end`.

## Complete audit ledger

| Surface                     | Denominator and result                                                                                                                                                                                                 |
| --------------------------- | ---------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| Client types and StoreItems | 462,759 decoded types; 23,593 wrappers; 23,593 exact mirrored pairs; 23,170 admitted; 423 exact rejections.                                                                                                            |
| StoreItem acquisition       | 23,083 ordinary supported; 1 state mutation; 1 intentional no-inventory action; 62 requiring context; 23 definition-only; 0 failed; 0 unexplained empty deltas.                                                        |
| Non-store acquisition       | 1,162 bundles plus 16 unwrapped resources; 1,178/1,178 supported.                                                                                                                                                      |
| Cosmetics                   | 8,685/8,685 classified as exact target/slot or proven global category; 0 unclassified.                                                                                                                                 |
| Nightwave cosmetics         | 8,685 individual cosmetics plus 573 exact bundles produce 9,258/9,258 debug offers and acquisitions; all use Nora Intermission Sixteen Creds, 0 use Fergolyte, 5/5 OID purchases pass, and native Zorba remains 16/16. |
| Commerce                    | 23,170 name/category lookups; 6,992 premium and 5,380 regular-price tests; 0 failures. The 10,696 records with no direct package price remain explicitly unpriced.                                                     |
| Resources                   | 3,467 total; 3,451 with admitted wrappers; 16 without wrappers and all 16 supported.                                                                                                                                   |
| Current datafiles           | 120 records: 23 converted syndicate records, one Kahl raw-only known converter incompatibility, one Dojo manifest, and 96 vendor manifests; extraction errors 0.                                                       |
| Syndicates                  | 39 current favour sets; 1,928 source rows; 1,927 active; 1 exact stale-reference rejection; all active favours, sacrifices, and medallions have supported acquisition classifications.                                 |
| Vendors                     | 96 current manifests; 4,666 native definitions; 2,069 scheduled normal offers; 4,747 full-stock offers; 96 OID lookups; successful 100,000 to 95,000 Hex-standing purchase; 0 failures.                                |
| Missions                    | 46 mission types; 354 nodes; 0 missing mission types; 0 missing reward manifests.                                                                                                                                      |
| Rewards                     | 1,102 manifests; 1,536 rotations; 11,510 entries; 0 invalid; 0 missing registry targets.                                                                                                                               |
| Enemy drops                 | 1,499 agents; 1,880 avatars; 745 damage controllers; 400 drop tables; 824 pools; 3,453 entries; 0 malformed; 0 missing references.                                                                                     |
| Bounties                    | 54 definitions; 264 stage tiers; 892 exact stage paths; 0 invalid.                                                                                                                                                     |
| Relics                      | 3,089 definitions; 0 invalid; 0 missing reward manifests.                                                                                                                                                              |
| General recipes             | 2,000 recipes; 7,094 ingredients; 0 invalid.                                                                                                                                                                           |
| Current Dojo                | 3,151 manifest recipes; 3,122 matched by Public Export; 27 current-only research plus 2 current-only grouped decorations; 0 invalid.                                                                                   |
| Official DE drops           | 52,266 probability rows; 42,471 display-name rows; 0 invalid probabilities.                                                                                                                                            |

The four special enemy-drop paths are deliberately retained in the machine report:

- `/Lotus/RESOLUTION_FAILURE/Region Resource`: 250 occurrences,
  `RejectedUnresolvedSourceReference`;
- `/Lotus/Types/Gameplay/1999Wf/PvPvE/Objectives/ExcavatorCellItem`: 1 occurrence,
  `MissionRuntimePickup`;
- `/Lotus/Types/PickUps/ExcavatorCellItem`: 5 occurrences, `MissionRuntimePickup`;
- `/Lotus/Types/PickUps/InfestedLichChipItem`: 7 occurrences, `MissionRuntimePickup`.

The enemy pool-level `chance` field is a positive roll/count value rather than always a probability;
four Thumper pools legitimately use 2, 4, 6, or 8. Only each entry's `probability` is constrained to
the inclusive zero-to-one interval.

## Fixed point and validation

The second updater scan reported zero added, removed, or changed records for challenges, primary and
silent manifests, Nightwave, vendors, syndicates, Dojo, bundles, cosmetics, items, and the item summary;
`requiresReview=false`.

The final gates are:

- updater .NET Release build: zero warnings and zero errors;
- updater self-test: 12/12;
- `npm run audit:metadata-end-to-end`: zero audit failures, zero pipeline errors, zero warnings;
- OpenWF TypeScript compilers, ESLint, Prettier, production build, self-tests, and generated-artifact
  copy/hash verification: see the final validation run associated with this ledger.

## Evidence limits and combined live test

DE's official drop page used here says `Last Update: 25 June, 2026`; the client cache is newer. The
page contains 776 distinct normalized display names that do not exactly match the English localization
dictionary. Those names remain a deterministic unmatched set and are not assigned guessed internal
paths.

The single combined in-game acceptance pass should observe:

1. a Nightwave debug cosmetic priced in the active season's cred and one TennoGen/platform cosmetic
   visible, purchasable, owned, and equippable;
2. one normal and one full-stock rotating vendor response, with repeat purchase tested separately only
   when `noVendorPurchaseLimits` is enabled;
3. Amir/Hex standing purchase and a current Kahl/Event offering;
4. one current-only Tier-D Dojo research and one grouped teleporter decoration;
5. one ordinary generated item, resource, and bundle acquisition;
6. one mission rotation reward and one enemy inventory drop;
7. one representative bounty/relic path.

Passing that list proves the client-facing integration for the selected build. It still does not
automatically implement future mission or progression state machines whose behavior is absent from
OpenWF code; the updater must report such additions for review.
