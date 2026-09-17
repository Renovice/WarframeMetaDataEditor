# Repeatable Warframe-to-OpenWF data update architecture

## Goal

One update run should identify every server-relevant data change exposed by a new installed Warframe
build, normalize the changes into versioned modules, and produce a reviewable OpenWF candidate. It is
semi-automatic: extraction and comparison are automatic; activation remains gated by referential and
semantic validation. New backend behavior is reported as unresolved rather than invented.

## Hypotheses and current evidence

| Hypothesis                                                                             | Evidence                                                                                                                                                                                                              | Result                                                                                      |
| -------------------------------------------------------------------------------------- | --------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- | ------------------------------------------------------------------------------------------- |
| A normal client login can synchronize the complete game schema                         | Login requests expose build and account state, but do not upload decoded package inheritance, localization, StoreItem relationships, package components, or reward datafiles.                                         | False without a custom protocol.                                                            |
| Installed data can drive repeatable server updates                                     | The updater decodes `Packages.bin`; extracts current Syndicate, Dojo, and all vendor datafiles; records every input hash; diffs snapshots; validates candidates; applies atomically; and rolls back.                  | True for implemented modules.                                                               |
| Public Export is a complete current denominator                                        | It can lag the installed client and omits some package-backed/current records.                                                                                                                                        | False. It is a useful typed reference and compatibility source.                             |
| Every client store product can be loss-accounted without guessing acquisition behavior | Every decoded `/Lotus/StoreItems/` wrapper has a deterministic mirrored inventory path; exact type/category/localization gates partition the complete wrapper set, while server-record evidence is stored separately. | True for the implemented item-registry reference layer.                                     |
| DE's official drop-table page replaces installed metadata                              | It publishes player-facing reward locations and probabilities but does not provide the complete item, localization, compatibility, commerce, mission-state, or package graph.                                         | False. It is an authoritative reward-probability input.                                     |
| The VFX/metadata work reduces format-discovery cost                                    | The metadata editor already understands package composition and the VFX toolchain already resolves cache resources, dependencies, native paths, and several binary payload families.                                  | True as reusable parsing infrastructure; each new table still needs its own semantic proof. |
| Warframe-Exporter replaces the updater                                                 | Warframe-Exporter can list and extract current cache files, but it does not normalize OpenWF schemas, reconcile Public Export, prove acquisition/purchase behavior, activate a hash-pinned snapshot, or roll back.    | False. It is the raw current-client datafile layer used by the updater.                     |

## Source hierarchy

Every generated record stores source identity, source hash, extraction method, and the exact fields used.
No source silently overwrites a contradictory value.

1. Installed-client `Packages.bin` and package/cache datafiles: current client-known types, inheritance,
   relationships, localization paths, and native binary tables.
2. DE official drop tables: published reward locations and probabilities.
3. Warframe Public Export and `warframe-public-export-plus`: stable public item/vendor contracts and
   existing OpenWF adapters.
4. Existing OpenWF constants/database schemas: the server behavior and persistence contract that a
   candidate must satisfy.
5. Public documentation: player-facing name confirmation only unless a field has no stronger source.

Disagreements are emitted as validation findings with both values and both provenance records.

## Required module contract

Every module must implement these stages before activation:

1. **Extract** from one or more pinned inputs.
2. **Normalize** into a versioned deterministic schema.
3. **Enumerate the denominator**: decoded candidates, admitted records, and every rejection with reason.
4. **Diff** added, removed, and field-level changed records against the active snapshot.
5. **Validate references** against the same installed build.
6. **Validate semantics** needed by the corresponding OpenWF adapter.
7. **Adapt** only proven fields into server structures.
8. **Audit production paths** such as acquisition, purchase, reward, or mission lookup.
9. **Apply atomically** only after the existing hash and validation gates pass.
10. **Rollback** only to the verified previous activation.

Repeated extraction from unchanged inputs must produce a zero-record-change fixed point. Generated
timestamps may change; semantic records may not.

## Implemented pipeline

The current pipeline deliberately keeps four jobs separate:

1. [Warframe-Exporter](https://github.com/Puxtril/Warframe-Exporter) reads raw files from the installed
   client cache. The updater currently requests Syndicates, DojoRecipeManifest, and every
   VendorManifest; it runs extraction and `bin2json` in a short temporary directory to avoid legacy
   Windows path truncation, then copies and hashes all evidence into the scan run.
2. The pinned `warframe-public-export-plus` generator normalizes DE Public Export, package metadata,
   current datafiles, wiki mappings, and DE's official drop table into the 44 structures OpenWF already
   consumes. It is a normalization reference, not the current-client denominator.
3. The updater overlays only exact current-client deltas with provenance: complete items/cosmetics,
   challenges/Nightwave, 96 vendors, 39 syndicate favour sets, and 3,151 Dojo recipes.
4. OpenWF validates the resulting production paths by executing acquisition, commerce, vendor,
   standing, recipe, mission/reward-reference, enemy-drop, bounty, and relic audits.

The applied Public Export Plus 0.6.8 snapshot for the 10 September 2026 audit is SHA-256
`f40f41aade7c9f0b63cd09517f16b5584925b9d8cdbc7e3c9448192b04143844`. A repeated scan against the
same inputs reported zero additions, removals, or changes in every generated section and
`requiresReview=false`.

## Planned normalized modules

| Module                               | Extracted data                                                                                                                             | Proof required before apply                                                                                                                                                                                                                 |
| ------------------------------------ | ------------------------------------------------------------------------------------------------------------------------------------------ | ------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| Item registry                        | Warframes, weapons, companions, vehicles, resources, currencies, blueprints, Mods, Arcanes, inventory categories, StoreItems, localization | Implemented: all 23,593 wrappers are partitioned; 23,170 admitted items and 1,178 non-store definitions have complete isolated acquisition classifications with zero failures.                                                              |
| Cosmetic acquisition                 | Cosmetic inventory products and pure cosmetic packages                                                                                     | Implemented: exact StoreItem/inventory relationship and successful server acquisition.                                                                                                                                                      |
| Cosmetic compatibility and discovery | Applicable frame/weapon/companion/vehicle, cosmetic slot, Arsenal/Market discovery flags, platform and seasonal gates                      | Metadata graph implemented for all 8,685 cosmetics with zero unclassified records. Current-client Arsenal presentation remains a live acceptance gate.                                                                                      |
| Commerce and pricing                 | Platinum price, Cred price, standing, event/vendor currency, bundle price, platform/TennoGen commerce                                      | Implemented for exact package fields and current vendor contracts. Records with no explicit price remain unpriced; no fallback currency or amount is invented.                                                                              |
| Syndicates                           | Tags, ranks, thresholds, caps, currencies, prerequisites, offerings                                                                        | Current favour extraction and OpenWF integration implemented for 39 sets and 1,927 active rows; one stale missing-client definition is explicitly rejected.                                                                                 |
| Vendors                              | Offers, bins, quotas, rotation periods, permanent flags, prices, limits                                                                    | Implemented for all 96 current manifests; deterministic normal/full schedules, OID lookup, and standing purchase pass.                                                                                                                      |
| Missions and starchart               | Nodes, planets, mission type, level/tileset/faction configuration, prerequisites                                                           | All 46 normalized mission types and 354 nodes pass structural/reference audit. Existing generic/specialized behavior remains server code; a newly introduced state machine is review-only.                                                  |
| Rewards and drops                    | Mission rotations, enemy drops, relics, reward manifests, official probabilities                                                           | 1,102 reward manifests, 400 enemy drop tables, 3,089 relics, and DE's 52,266 official probability rows pass structural validation. The older official publication date and unresolved display-name aliases remain explicit evidence limits. |
| Events and seasons                   | Schedules, currencies, ranks, goals, reward tables                                                                                         | Metadata-only changes may apply; progression/backend changes remain review-only.                                                                                                                                                            |
| Prime catalogs                       | Prime Access, Resurgence, vault state, packages, Varzia offers                                                                             | Exact package contents, currency, availability window, and server purchase contract.                                                                                                                                                        |

## Current proof ledger

The complete offline server audit currently reports:

- 23,170/23,170 admitted StoreItem records classified, with zero failed or empty mutations;
- 1,178/1,178 bundle and unwrapped-resource acquisitions supported;
- 8,685/8,685 cosmetics assigned an exact target/slot or proven global-category class;
- 96/96 vendor OID lookups and 4,666 current native offer definitions validated;
- 39 current syndicate sets, 1,927 active favour rows, and a successful Hex-standing purchase;
- 3,151 current Dojo recipes, including 29 exact current-client supplements over Public Export;
- 46 mission types, 354 nodes, 1,102 reward manifests, 11,510 reward entries, 54 bounties,
  3,089 relics, 400 enemy drop tables, and 2,000 recipes with zero structural audit failures;
- 52,266 rows parsed from DE's official PC drop page with zero invalid probability values;
- zero errors and zero warnings in the composite `audit:metadata-end-to-end` run.

These results prove local extraction, normalization, reference integrity, and offline OpenWF execution.
They do not prove current-client Arsenal rendering, shop presentation, mission gameplay, or newly added
backend state machines until the combined in-game acceptance pass observes them.

## Cosmetic coverage boundary

The current cosmetic module proves acquisition products and metadata compatibility classes. It does
not yet prove that every appearance is presented by the current Arsenal UI. Every client-present
appearance is classified independently across:

- client presence;
- compatible equipment and cosmetic slot;
- server grant support;
- account ownership;
- Arsenal visibility;
- Market/vendor visibility;
- platform, Steam, season, quest, or event gate;
- exact price/currency source.

The combined in-game pass must distinguish a successful ownership/equip mutation from Arsenal and
Market visibility. If a client-present skin remains hidden, a later patch must target the game's
authoritative availability/catalog decision after that evidence is captured; it must not use per-frame
polling or one-off support scripts.

## Update trigger

The update trigger is a changed installed `Packages.bin` or relevant cache/datafile hash. A scheduled or
manual command can run `scan` after the official launcher finishes. A normal game login is not used as
the data transport. The report may recommend server code or schema work, but only modules whose exact
adapter and validators already exist can be applied automatically.
