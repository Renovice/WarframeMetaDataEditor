# Coordinated client/server metadata patches — 2026-08-26

## Outcome

The mod identity editor now treats client metadata, OpenWF server definitions, and inventory as three
independent layers. Client-only is the default. A server definition is an explicit, removable package;
inventory migration is not implemented implicitly and is recorded as `false` in every generated manifest.

## Hypothesis ledger

| Hypothesis | Evidence | Verdict |
|---|---|---|
| A client rarity/rank-tier patch requires a server definition override to persist an owned mod. | OpenWF stores owned rank in `UpgradeFingerprint`; the former definition override only changed the shared `ExportUpgrades` definition used by exports and definition consumers. | **FALSE**. |
| The editor can author client metadata without changing OpenWF's WebUI/Public Export definition. | Server-package creation is unchecked by default and no server path is required in client-only mode. | **TRUE**. |
| The same editor can optionally override both ends. | Enabling the server component writes `patch.json` and `upgrade-definitions.json` under the selected server's `Metadata Patches/Enabled`. | **TRUE**. |
| Removing a server definition requires rewriting inventory or the original Public Export. | Disable moves the package to `Metadata Patches/Disabled`; the baseline export and inventory are untouched. | **FALSE**. |
| Folder order is a safe way to resolve two patches changing the same field. | Silent last-writer behavior is ambiguous and hard to audit. The loader rejects field-level conflicts before applying any package. | **FALSE**. |
| The client `QA_*` enum is an exact numeric maximum-rank encoding. | The cache tier is not one-to-one with numeric Public Export `fusionLimit`. | **FALSE**. |

## Package contract

```text
SpaceNinjaServer/
└── Metadata Patches/
    ├── Enabled/
    │   └── Stretch Server Definition/
    │       ├── patch.json
    │       └── upgrade-definitions.json
    └── Disabled/
```

`patch.json` declares schema version, stable ID, display name, package version, presence of client
metadata, `serverDefinitions=true`, and `inventoryMigration=false`. `upgrade-definitions.json` maps
known `/Lotus/Upgrades/...` paths to optional `rarity` and `fusionLimit` fields.

The server enumerates package directories deterministically, validates the complete plan, detects
duplicate IDs and per-field ownership conflicts, and only then mutates the shared in-memory export.
Disabled folders are never scanned. Logs distinguish no-enabled-patch startup from `PATCH LOAD PASS`.

## Editor behavior

- Client rarity: `COMMON`, `UNCOMMON`, `RARE`, or `LEGENDARY`.
- Client FusionLimit: inherit/no patch or any of `QA_NONE`, `QA_LOW`, `QA_MEDIUM`, `QA_HIGH`,
  `QA_VERY_HIGH`.
- Server package: opt-in. Server rarity and numeric fusion limit 0–10 are independent inputs.
- Inventory: fixed to “Do not modify owned mods or save data.”
- Disable: safely moves the matching package from `Enabled` to `Disabled` and requests an OpenWF restart.

## Verification

- Metadata editor Core self-test: **41 passed, 0 failed**.
- WPF Release build: **0 warnings, 0 errors**.
- OpenWF TypeScript verify plus generated-reward audit: **exit 0**, 60/60 unique reward references passed.
- OpenWF lint: **exit 0**, zero warnings/errors.
- OpenWF application self-test: **exit 0**.
- Active server-package census after migration: **0 enabled packages, 0 applied definitions**.

The former global Stretch override was migrated to a valid package under `Disabled`, so the current
effective OpenWF definition is vanilla until the user explicitly enables a package.
