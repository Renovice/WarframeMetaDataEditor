# Complete client item registry — 2026-09-10

## Scope and evidence boundary

This module is the deterministic client-product reference for later acquisition, pricing, syndicate,
mission, reward, and Arsenal-discovery work. It does not claim that client metadata alone proves an
OpenWF inventory mutation, purchase path, equip slot, UI visibility rule, or gameplay implementation.

Source snapshot:

- installed `Packages.bin` SHA-256:
  `a121616c72bb0963dc43a233f8d393a06cd2fdd99ca00b419b0ff5e51f74acf4`;
- decoded types: 462,759, aligned frame boundary;
- OpenWF Public Export Plus: 0.6.8;
- active generated snapshot SHA-256 after the final current-vendor/syndicate/Dojo apply:
  `f40f41aade7c9f0b63cd09517f16b5584925b9d8cdbc7e3c9448192b04143844`.

## Hypotheses and results

| Hypothesis                                                                        | Evidence                                                                                                                                                                     | Result                                                                                       |
| --------------------------------------------------------------------------------- | ---------------------------------------------------------------------------------------------------------------------------------------------------------------------------- | -------------------------------------------------------------------------------------------- |
| Every decoded client store wrapper has an exact mirrored inventory type           | 23,593 `/Lotus/StoreItems/` wrappers and 23,593 exact mirrored `/Lotus/` types.                                                                                              | True for this snapshot.                                                                      |
| Every wrapper can be admitted as a user-facing product                            | 421 pairs lack an absolute localization path and 2 lack a ProductCategory.                                                                                                   | False: 23,170 admitted and 423 rejected.                                                     |
| Public Export alone represents the current client catalog                         | 7,566 admitted items have a record in an OpenWF-consumed Public Export table; 8,685 use the generated cosmetic record; 6,919 have neither explicit record.                   | False.                                                                                       |
| An explicit metadata record alone proves complete acquisition or gameplay support | OpenWF also has path-based dispatch and category-specific persistence rules. The separate isolated audit executed all admitted products and classified non-ordinary results. | False for metadata alone; acquisition is now proven separately for the complete denominator. |
| Every authoritative field-owner path begins with `/Lotus/`                        | Representative `FlavourItems` inherit `ProductCategory` from `/EE/Types/Engine/UIFlavourItem`. The decoded path exists and is absolute, but it is outside `/Lotus/`.         | False. Proven field owners may be any absolute decoded package path.                         |
| Repeating extraction from unchanged inputs changes semantic records               | The second scan reported zero added, removed, or changed records in every snapshot section and `requiresReview=false`.                                                       | False; the module reached a zero-change fixed point.                                         |

## Exact partition

- Store-wrapper denominator: **23,593**.
- Exact mirrored pairs: **23,593**.
- Admitted: **23,170**.
- Rejected: **423**.
- Rejection partition:
  - `MISSING_ABSOLUTE_LOCALIZATION`: 421;
  - `MISSING_PRODUCT_CATEGORY`: 2.
- Rejection-list SHA-256:
  `00947db257e79b19bebff08dac3ed58fbe6025f98ed47ff385ab22782fcd9afa`.
- No-explicit-server-record paths: **6,919**.
- No-explicit-record path-list SHA-256:
  `4f5ac808faf91c9aced7eb42170e70800516151b722912b5905ced01626e11e7`.

The two missing-category wrappers are:

- `/Lotus/StoreItems/Types/Weapon/LotusCustomAimWeapon`;
- `/Lotus/StoreItems/Types/Weapon/LotusHandCompass`.

The full 423-row rejection list and full 6,919-row no-explicit-record list are preserved in the scan's
`item-registry-audit.json`. Its SHA-256 is identical across both scans:
`068160a4d01849801db32d11968ce404f808dc7fddd57d5a248d825282aa4659`.

## Validation

- .NET Release build: 0 warnings, 0 errors.
- Updater self-test: 12/12.
- OpenWF TypeScript verification: pass.
- Generated reward acquisition audit: 60/60.
- Generated item-registry audit: 0 failures.
- Complete admitted-item acquisition: 23,170/23,170 classified; 23,083 ordinary successes,
  1 state mutation, 1 intentional no-inventory action, 62 context-dependent definitions,
  23 definition-only products, zero failed, and zero unexplained empty deltas.
- Bundle and non-StoreItem resource acquisition: 1,178/1,178 supported.
- Cosmetic compatibility classification: 8,685/8,685; zero unclassified.
- Native Zorba manifest: 16/16 native offers, no generated additions.
- Nightwave cosmetic currency and Fergolyte checks: zero failures.
- Generated current vendors: 96 manifests, 4,666 definitions, 2,069 normal offers,
  4,747 full-stock offers, 96 OID lookups, one successful Hex-standing purchase, zero failures.
- Current syndicates: 39 sets, 1,928 source rows, 1,927 active rows, one exact rejected stale reference.
- Current Dojo manifest: 3,151 recipes; 27 research and 2 decoration supplements over Public Export.
- Server data pipeline: zero errors and zero warnings across resources, commerce, syndicates,
  vendors, missions, rewards, enemy drops, bounties, relics, general recipes, and Dojo recipes.
- ESLint and Prettier checks for the new server code: pass.

The first live restart exposed and rejected an overly narrow server validator that required field-owner
paths to begin with `/Lotus/`. The updater had correctly preserved decoded `/EE/` inheritance owners.
The server and command-line audit now require an absolute path and execute the same runtime self-test.

Two simultaneous `npm run dev` watchers also attempted to restart the same database and produced a
`DBPathInUse` error. Both exact OpenWF watcher trees were stopped, one verified watcher was started,
and the fresh server returned HTTP 200 on port 443. This was a process-ownership fault rather than a
metadata or database-content fault.

## Remaining acceptance boundary

The 6,919 `serverRecordSource=None` records are no longer an unresolved acquisition set. They were
executed through the same path-based dispatcher as every other admitted product and are included in
the complete classification above. No category adapter was required merely because an explicit table
record was absent.

One combined in-game pass still needs to observe current-client Arsenal presentation/equip behavior,
Nightwave and rotating-vendor presentation, standing purchases, current Dojo research, representative
item/bundle/resource acquisition, and representative mission reward/enemy-drop behavior. Those live
observations remain separate from the complete offline registry and server-path audit.
