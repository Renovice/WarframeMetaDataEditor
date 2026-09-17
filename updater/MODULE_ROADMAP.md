# Semi-automatic updater module checklist

The updater is never allowed to infer and activate unknown backend behavior. Every module must provide
an extractor, normalized versioned schema, diff, referential validator, OpenWF adapter, explicit apply,
and rollback before it can be marked complete.

## Shared foundation

- [x] Detect/accept the installed game and OpenWF locations.
- [x] Extract and decode the installed `/Packages.bin` through the existing compiled Core library.
- [x] Invoke the pinned Warframe-Exporter and `bin2json` tools in a short temporary path for current
      Syndicate, DojoRecipeManifest, and all VendorManifests datafiles; preserve raw and normalized hashes.
- [x] Identify snapshots by SHA-256 and record game/cache provenance.
- [x] Write isolated versioned scan runs without changing OpenWF.
- [x] Compare against the active OpenWF snapshot.
- [x] Emit machine-readable and human-readable change reports.
- [x] Block activation on validation errors.
- [x] Require explicit acknowledgement for warnings.
- [x] Verify candidate hashes before activation.
- [x] Back up and atomically replace only the generated target.
- [x] Refuse unsafe rollback over a newer activation.
- [x] Copy generated metadata into compiled OpenWF builds.
- [x] Prove repeated scans reach a zero-change fixed point.

## Modules

- [x] Challenge rewards
  - [x] PrimaryChallengeManifest extraction and exact Public Export Plus coverage audit.
  - [x] SilentChallengeRewardManifest extraction.
  - [x] Challenge path and StoreItem referential validation.
  - [x] Generated OpenWF challenge reward adapter.
  - [x] Hard-coded Silent table removed from OpenWF.
- [x] Nightwave syndicate synchronization
  - [x] Newest numbered RadioLegion syndicate extraction from installed Packages.bin.
  - [x] Rank rewards, currency, featured rewards, and challenge-pool validation.
  - [x] Generated OpenWF supplement merged over a known modern Nightwave presentation contract.
  - [x] Build-label, season-number, and activation mapping for Amir's Shockwave.
  - [x] Explicit currency-correct prior-season vendor fallback when current offers are unavailable.
  - [x] Authoritative current rotating offers extracted from the installed vendor datafile when available.
- [x] Client cosmetic catalog
  - [x] Exact inventory type plus StoreItem-wrapper existence gate.
  - [x] Effective ProductCategory and absolute localization-path gate.
  - [x] `WeaponSkins`, `FlavourItems`, and `ShipDecorations` server adapter.
  - [x] Recursive all-cosmetic package extraction from exact `PackageComponents`.
  - [x] Nightwave debug consumes the generated current-client catalog; native Zorba remains unchanged.
  - [x] Full individual-plus-bundle offer-set and acquisition audit in OpenWF.
- [x] Complete client item registry
  - [x] Every decoded `/Lotus/StoreItems/` wrapper is the fixed denominator across weapons, Warframes,
        companions, vehicles, resources, currencies, blueprints, Mods, Arcanes, cosmetics, and other products.
  - [x] Exact mirrored inventory type, effective category, absolute localization, and field-owner paths.
  - [x] Every rejection retained with one reason and deterministic SHA-256.
  - [x] Public Export and generated-cosmetic explicit-server-record evidence reported separately.
  - [x] Added/removed/changed diff and zero-change repeated-scan proof.
- [x] Complete server acquisition compatibility
  - [x] Execute all 23,170 admitted StoreItem pairs through isolated OpenWF inventory mutation.
  - [x] Classify 23,083 ordinary successes, 1 state mutation, 1 intentional non-inventory action,
        62 context-dependent definitions, and 23 definition-only products; zero failures and zero empty deltas.
  - [x] Execute 1,162 bundles and 16 resources without StoreItem wrappers; 1,178/1,178 supported.
  - [x] Keep every non-ordinary result visible; do not infer persistence from `ProductCategory` alone.
- [x] Cosmetic compatibility metadata graph
  - [x] Classify all 8,685 admitted cosmetics by exact target/slot fields or proven global category.
  - [x] Preserve Market flags, platform gates, TennoGen/platform records, and price fields independently.
  - [x] Zero unclassified compatibility records.
  - [ ] Confirm current-client Arsenal discovery and equip presentation in the combined in-game pass.
- [x] Commerce and pricing
  - [x] Preserve exact Platinum, regular-credit, selling-price, Market, and platform fields with owners.
  - [x] Run 23,170 name/category lookups, 6,992 premium-price tests, and 5,380 regular-price tests.
  - [x] Preserve 10,696 products with no explicit direct price instead of inventing one.
  - [x] Preserve each current vendor's native credits, Platinum, standing, item currency, focus XP,
        random-cost, duplicate-capacity, and purchase-limit contract.
- [x] Prime Access definition and bundle coverage
  - [x] Include every client-present Prime Access item/package definition in the complete StoreItem and
        non-store bundle acquisition audits.
  - [x] Treat active real-money storefront windows as external live-service state when they are absent
        from the installed client and Public Export; record that boundary instead of inventing a window.
- [x] Prime Resurgence vendor stock
  - [x] Current Varzia manifests use the same current-client extraction, native scheduling, and full-stock audit.
  - [x] Keep Prime Access storefront windows distinct from the current-client Varzia vendor contract.
- [x] Syndicates and standing
  - [x] Current client supplied 39 exact favour sets, 1,928 source rows, and 1,927 active rows.
  - [x] Preserve the one stale Zariman portrait reference as an exact rejected row because the current
        client has no matching StoreItem definition.
  - [x] Audit rank thresholds, offerings, sacrifices, medallions, standing purchase mutation, and runtime lookup.
- [x] General vendor inventories and rotations
  - [x] Extract all 96 current vendor manifests and 4,666 native offer definitions.
  - [x] Verify 2,069 normal scheduled offers, 4,747 full-stock offers, 96 OID lookups, and a real
        Hex-standing purchase; zero failures.
  - [x] Keep `fullyStockedVendors` (advertised stock) separate from account `noVendorPurchaseLimits`.
- [x] Mission, bounty, relic, enemy, recipe, and Starchart structures
  - [x] Audit 46 mission types and 354 nodes with zero missing mission or reward-manifest references.
  - [x] Audit 54 bounty definitions, 264 stage tiers, and 892 exact stage paths.
  - [x] Audit 3,089 relic definitions and all reward-manifest references.
  - [x] Audit 2,000 recipes with 7,094 ingredients.
  - [x] Import 3,151 current Dojo recipes and supplement 27 current research plus 2 grouped decorations
        absent from the older Public Export normalization.
- [x] Drop and reward tables
  - [x] Audit 1,102 reward manifests, 1,536 rotations, and 11,510 entries with no invalid record or
        missing acquisition target.
  - [x] Audit 400 enemy drop tables, 824 pools, and 3,453 entries with no malformed probability or
        missing avatar/damage-controller reference.
  - [x] Keep 250 `/Lotus/RESOLUTION_FAILURE/Region Resource` occurrences explicitly rejected and
        classify 13 mission-runtime pickups separately from persistent inventory rewards.
  - [x] Parse and hash DE's official drop-table export: 52,266 probability rows, zero invalid.
  - [x] Treat the official page's 25 June 2026 publication date and 776 unmatched display-name aliases
        as a freshness/reconciliation boundary; do not manufacture exact internal paths from display names.
- [x] Event metadata and backend-change detector
  - [x] Include current event VendorManifests plus normalized event rewards and bounties in the same
        production-path audits.
  - [x] Emit `update-signals.json` for added/removed client types and keep behavior-bearing signal
        categories review-required so a new server state machine is never fabricated from metadata.

The evidence/source contract and repeatable update workflow are specified in
`SERVER_DATA_UPDATE_ARCHITECTURE.md`.

## Review-only changes

These may be detected and scaffolded but must never be activated automatically:

- new or changed HTTP payloads/endpoints;
- account or database migrations;
- new mission/game-mode state machines;
- server-authoritative event schedules and progression logic;
- Lua/native behavior changes;
- metadata whose referenced backend semantics cannot be proved from the client data.
