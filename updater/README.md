# OpenWF Metadata Updater

A compiled, preview-first synchronizer from an installed Warframe client's metadata to OpenWF.
It reuses `editor/Core` for cache extraction and `Packages.bin` decoding. It never writes into the
Warframe installation.

The updater is intentionally **semi-automatic**:

1. `scan` reads the game and OpenWF, then writes a versioned candidate, validation results, and a
   human-readable diff under `updater/runs/`. It does not change OpenWF.
2. You inspect `CHANGE_REPORT.md`.
3. `apply` separately verifies the candidate hash and validation status, backs up the current active
   snapshot, and atomically replaces only the generated metadata file.
4. OpenWF is never started or restarted by this application.
5. `rollback` restores the verified previous snapshot and refuses to overwrite a newer activation.

`apply` also refuses when a module has no integrated baseline target, because such a first activation
could not be rolled back safely.

## Current modules

The first end-to-end module extracts both authoritative client manifests:

- `/Lotus/Types/Challenges/PrimaryChallengeManifest`
- `/Lotus/Types/Items/SilentChallengeRewardManifest`

It normalizes every record, validates challenge and reward paths against the same decoded
`Packages.bin`, compares Primary coverage with OpenWF's installed `warframe-public-export-plus`, and
generates OpenWF `IChallenge.countedRewards` entries for the Silent manifest. Primary records remain
served by Public Export Plus because they include inbox messages and conditional upgrade semantics;
any new Primary entry missing from Public Export Plus becomes an explicit review warning.

The Nightwave synchronizer extracts the newest numbered `RadioLegionIntermission*Syndicate`
directly from the installed client's `Packages.bin`. It generates the season's rank rewards,
currency, vendor identity, featured rewards, and daily/weekly/elite challenge pools. OpenWF merges
those package-backed fields over the prior modern Nightwave presentation contract, so icon/colour
fields that were not proven by the new package are not invented.

When a generated season references a package reward that is missing from Public Export, the updater
extracts that bundle's exact `PackageComponents`. The cosmetic pass also scans every localized
package and admits a bundle only when its complete component graph resolves to exact cosmetics from
the same snapshot. Mixed gameplay/cosmetic packages, unresolved packages, and internal packages are
excluded. OpenWF registers the generated bundle contracts before acquisition.

Some authoritative tables are separate binary datafiles rather than composed Packages.bin text. The
updater invokes the pinned [Warframe-Exporter](https://github.com/Puxtril/Warframe-Exporter) and
`bin2json` tools under `vendor/upstream/warframe-public-export-plus-gen-senpai`. It extracts the current
Syndicates, DojoRecipeManifest, and complete VendorManifests families in a short temporary directory,
which avoids legacy Windows long-path omissions, then copies every raw/JSON artifact into the run with
its SHA-256. The current snapshot contains 120 records: 23 converted syndicate files plus Kahl's exact
raw-only known `bin2json` incompatibility, one Dojo manifest, and 96 vendor manifests.

The vendor module preserves DE's offers, currencies, prices, permanent flags, bins, quotas,
probabilities, durations, purchase limits, duplicate capacity, and dynamic scheduling fields. The
server runs those contracts through its deterministic scheduler. Offline execution proves 2,069
normal offers, 4,747 full-stock offers, 96 vendor OID lookups, and a Hex-standing purchase with zero
failures. `fullyStockedVendors` changes advertised inventory; the account-level
`noVendorPurchaseLimits` bypasses purchase history separately.

The current syndicate module emits 39 exact favour sets with 1,928 source rows and 1,927 active rows.
It rejects the single Zariman portrait reference whose StoreItem no longer exists in the same client
snapshot. The current Dojo module loss-accounts all 3,151 manifest recipes and adds 27 current research
recipes plus two grouped decorations missing from the older Public Export normalization.

The cosmetic catalog walks the same decoded `Packages.bin` and emits only exact, localized
inventory/store pairs in OpenWF's `WeaponSkins`, `FlavourItems`, and `ShipDecorations` bins. Both the
real type and its exact `/Lotus/StoreItems/` wrapper must exist in the installed client. Bare
placeholder localization identifiers, unlocalized helper types, internal art assets, and Railjack
armaments are rejected. Localized packages whose every leaf component belongs to this exact catalog
are exported as cosmetic bundles as well. This supplies current cosmetics that may not yet exist in
the npm Public Export, while OpenWF's acquisition audit remains the final server compatibility gate.

The complete item registry uses every decoded `/Lotus/StoreItems/` wrapper as its denominator. A
record is admitted only when the exact mirrored `/Lotus/` inventory type exists and the pair resolves
an effective `ProductCategory` plus an absolute `LocalizeTag`. The registry preserves the exact
metadata path that supplied each field. Every rejected wrapper is retained with one deterministic
reason, and the complete rejection list is SHA-256 pinned in `item-registry-audit.json`.

For each admitted item, the registry records whether an explicit OpenWF item record already exists in
one of the Public Export tables consumed by the acquisition dispatcher, whether the generated
cosmetic adapter owns it, or whether neither source exists. `None` means “no explicit server record”;
it does not claim that path-based fallback will fail, and it is not permission to synthesize behavior.
Acquisition, equipability, Arsenal visibility, commerce, and gameplay remain separate validation
layers. The OpenWF composite audit executes all 23,170 admitted StoreItem products plus 1,178 bundles
and unwrapped resources. It also validates pricing lookups, cosmetic compatibility classes,
syndicates, all current vendors, Dojo recipes, missions, rewards, enemy drops, bounties, relics, and
general recipes. That audit is run from the server with `npm run audit:metadata-end-to-end`.

## Commands

From `WarframeMetaDataEditor`:

```powershell
dotnet run --project updater -c Release -- self-test

dotnet run --project updater -c Release -- scan `
  --game "C:\Users\Bartek\OneDrive\Dokumenter\Warframe" `
  --server "C:\Users\Bartek\OneDrive\Dokumenter\OpenWF Server 08.07.2026\SpaceNinjaServer" `
  --reference-game "C:\path\to\previous-certified-Warframe" `
  --build-label "2026.07.11.15.28/7fwjVVacxcBzO-xahK2RZg"

dotnet run --project updater -c Release -- apply "<run-folder>"
dotnet run --project updater -c Release -- rollback "<run-folder>"

dotnet run --project updater -c Release -- client-audit `
  --game "C:\Program Files (x86)\Steam\steamapps\common\Warframe" `
  --reference "C:\Users\Bartek\OneDrive\Dokumenter\Warframe"

dotnet run --project updater -c Release -- inspect-type `
  --game "C:\Program Files (x86)\Steam\steamapps\common\Warframe" `
  --type "/Lotus/Syndicates/RadioLegionIntermission16Syndicate"
```

`scan` auto-detects the pinned vendor tools. For a standalone checkout they can be supplied explicitly
with `--vendor-exporter <Warframe-Exporter-CLI_Windows.exe>` and
`--vendor-bin2json <bin2json.exe>`.

`client-audit` is read-only. It classifies files as official build data, RENOVICE-owned overlay,
version-coupled generated OpenWF content, diagnostics, or unknown. It never interprets an absent
official file as permission to delete a custom file. It also detects the stale-unmanaged-manifest
risk that occurs when the official launcher updates caches below an existing `OpenWF/Content/0/UNMANAGED`.

`inspect-type` is also read-only. It prints the exact inheritance chain and composed metadata for one
package type, and exists specifically so new fields are investigated from evidence instead of guessed.

If validation emits warnings, `apply` refuses by default. After reviewing them, acknowledgement is
explicit: `apply "<run-folder>" --approve-warnings`.

## Generated artifacts

Every scan run contains:

- `candidate.challenge-metadata.json` — complete versioned challenge, Nightwave, cosmetic, item-registry,
  current-vendor, current-syndicate, and current-Dojo payload;
- `item-registry-audit.json` — exact store-wrapper denominator, admitted/rejected partition, rejection reasons,
  field provenance, explicit-server-record coverage, and deterministic hashes;
- `current-datafiles/` — preserved raw and normalized current Syndicate, Dojo, and vendor datafiles plus
  a manifest of source hashes and conversion status;
- `CHANGE_REPORT.md` — readable additions, removals, changes, coverage, and validation findings;
- `change-report.json` and `validation.json` — machine-readable equivalents;
- `run-manifest.json` — candidate hash and exact target contract;
- `update-signals.json` — review-only metadata paths associated with Nightwave, Amir/Fables,
  Deep Archimedea, and Prime Resurgence; detection is not backend implementation;
- `backup/` and `apply-state.json` after activation.

The active OpenWF file is:

`static/generated/openwf-metadata/challenge-metadata.json`

## Remaining live and review-only work

The complete offline audit is ready for one combined in-game acceptance pass. Live checks still need
to observe Arsenal discovery/equip presentation, Nightwave and ordinary vendor presentation, standing
purchase, current Dojo research, an ordinary item/bundle/resource acquisition, and representative
mission/reward/enemy-drop behavior on the selected build.

Prime Access windows, event scheduling, and any newly introduced mission/progression state machine
remain review-only because data extraction alone cannot implement server behavior. DE's official drop
page is retained as a hashed probability source, but its 25 June 2026 publication date and unresolved
display-name aliases are reported rather than converted into invented internal paths.

`SERVER_DATA_UPDATE_ARCHITECTURE.md` defines the source hierarchy, required module contract, complete
planned table families, cosmetic compatibility/Arsenal-discovery boundary, and hash-triggered update
workflow.
