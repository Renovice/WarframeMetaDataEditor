# Generic OpenWF server metadata patches — 2026-08-26

## Result

The RENOVICE Metadata Editor and SpaceNinjaServer now share a generic, removable server metadata
package format. It covers exact entries in every object/array `Export*` dataset exposed by the
installed `warframe-public-export-plus` package. It does not rewrite that dependency and it does not
touch inventory or save data.

## Hypotheses and evidence

| Hypothesis | Evidence | Verdict |
|---|---|---|
| One generic format can cover the server's Public Export-backed categories. | The installed package exposes 44 object/array `Export*` datasets; the loader resolves a named dataset plus a string/index JSON path and overlays selected fields. | **TRUE** |
| Every client metadata type has a corresponding server entry. | Client `Packages.bin` and Public Export Plus are different schemas and many client-only types/fields have no exact exported entry. The editor reports no mapping and refuses generation in that case. | **FALSE** |
| Client field names can be copied blindly to the server. | Octavia client metadata and its `ExportWarframes` entry use different field names and structures. The editor displays the actual server entry rather than projecting client fields. | **FALSE** |
| Exact mapping can be automated for supported entries. | Full installed-data probes resolved Octavia Prime to `ExportWarframes -> /Lotus/Powersuits/Bard/OctaviaPrime`; Amp resolved to nested `abilities -> 3` entries for Bard and Octavia Prime. | **TRUE** |
| Nested entries require a real path, not replacement of every runtime instance. | Amp is represented as an object inside an ability array. A dataset/path/field coordinate addresses that definition directly. | **TRUE** |
| The overlay edits account inventory or persistent saves. | Manifests require `inventoryMigration=false`; the implementation only mutates imported in-memory Public Export objects after validation. | **FALSE** |
| Adding an arbitrary new field guarantees a gameplay effect. | A field affects behavior only if an OpenWF route/service reads it. The loader can preserve the JSON value but cannot invent a consumer. | **FALSE** |
| Public Export JSON can be treated as duplicate-key-free. | The real full-data C# scan failed on duplicate `introducedAt` keys. JavaScript accepts these with last-key-wins behavior; the scanner was corrected to match that behavior. | **FALSE** |

## Package schema

Each package under `Metadata Patches/Enabled/<package>/` contains `patch.json` and either the existing
typed `upgrade-definitions.json`, the new generic `server-definitions.json`, or both. A generic file is:

```json
{
  "schemaVersion": 1,
  "overrides": [
    {
      "dataset": "ExportWarframes",
      "path": ["/Lotus/Powersuits/Bard/OctaviaPrime", "abilities", 3],
      "values": {
        "description": "Replacement server-side description"
      }
    }
  ]
}
```

Strings and non-negative integers are the only path segment types. Values may be normal finite JSON.
Existing non-null fields must retain their JSON kind. `__proto__`, `prototype`, and `constructor` are
forbidden as path or value-object keys. Package IDs are stable per dataset/path, enabling safe updates
and folder-based disable/restore.

## Validation results

- Metadata editor core harness: **46/46 PASS**.
- WPF Release build: **0 warnings, 0 errors**.
- Full installed-data mapping: Octavia Prime **1 exact match**; Bard Amplify **2 exact nested matches**.
- OpenWF TypeScript verify and generated reward audit: **exit 0**, **60/60 PASS**.
- OpenWF lint: **exit 0**.
- OpenWF production build: **exit 0**.
- OpenWF application self-test: **exit 0**.
- OpenWF application self-test applies a validated generic Octavia override to the shared imported
  object, asserts both changed fields and the returned field count, then restores the exact original
  properties in a `finally` block: **PASS**.
- Dev launcher syntax check: **exit 0**.
- Active metadata-package census after testing: **0 Enabled**; the former Stretch experiment remains in `Disabled`.

The system is therefore proven at schema, mapping, validation, build, and package-lifecycle level.
Actual effects remain field-specific: a changed server value must still be consumed by the relevant
OpenWF code path, and live gameplay remains the final test for that behavior.
