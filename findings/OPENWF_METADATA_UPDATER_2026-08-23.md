# OpenWF Metadata Updater — first implementation and challenge module

Date: 2026-08-23 local / 2026-08-22 UTC.

## H1 — the existing C# Metadata Editor Core can support a production updater

**Result: TRUE.** The updater references `editor/Core` directly. It extracted the installed
`/Packages.bin`, decoded 52,710,200 bytes into 461,788 types, and ended aligned. No Python decoder or
full JSONL export is involved in the production workflow.

Installed snapshot:

- Packages SHA-256: `3e1eef08e56c5f8f7ea8c825e4f7566909c43d6461b7cbcd7836fa4f3aeaf502`
- snapshot id: `3e1eef08e56c5f8f`
- executable file version: `2026.07.11.15.28`
- requested client build label: `2026.07.11.15.28/7fwjVVacxcBzO-xahK2RZg`

## H2 — both challenge manifests can be extracted and validated automatically

**Result: TRUE.** The current installed client contains 150 Primary records and 10 Silent records.
Every referenced challenge and reward path exists in the same decoded package. Validation completed
with 0 errors, 0 warnings, and 2 informational findings.

## H3 — Public Export Plus covers the whole current challenge reward surface

**Result: FALSE.** OpenWF's installed Public Export Plus 0.6.5 covers 150/150 Primary entries and 0/10
Silent entries. The updater therefore audits Primary coverage but generates OpenWF fallback rewards
only from SilentChallengeRewardManifest. Missing future Primary entries are review warnings because
their inbox and conditional-upgrade semantics must not be guessed.

## H4 — OpenWF's normal compiled build automatically propagates a changed generated snapshot

**Result: FALSE before the fix; TRUE after it.** The incremental compiler left the previous JSON in
`build/static/generated`. All four build scripts now explicitly copy `static/generated` into
`build/static/generated`. The source and built active snapshots now have the identical SHA-256:
`87fc304a6f048b812e603cb830e188f433f6c064d78a9cd2fed2d3b44b85822b`.

## H5 — repeated scans are stable

**Result: TRUE.** After activation, a new scan reported 0 added, 0 removed, and 0 changed records for
generated challenges, Primary manifest entries, and Silent manifest entries.

## Safety properties verified

- `scan` is read-only with respect to Warframe and OpenWF.
- `apply` checks the candidate hash and validation counts before writing.
- warnings require explicit acknowledgement.
- the prior active snapshot is backed up before an atomic replacement.
- activation is refused if no rollback baseline exists.
- rollback checks both current and backup hashes and refuses to overwrite a newer activation.
- a compiled temporary-filesystem regression test proves apply followed by rollback restores the exact
  prior bytes.
- the tool never starts or restarts OpenWF.

## Verification

- updater Release build: 0 warnings, 0 errors;
- updater deterministic self-tests: 6/6 pass;
- `dotnet format --verify-no-changes`: pass;
- live-cache scan: aligned, 0 validation errors, 0 validation warnings;
- OpenWF `npm run verify`: pass;
- focused ESLint on `itemDataService.ts` and `inventoryService.ts`: pass;
- OpenWF `build:dev`: pass;
- active candidate hash equals applied and compiled-build hashes;
- `git diff --check`: pass.

OpenWF was not restarted during this work. The already-running process continues using whatever was
loaded at its last start; the new generated loader becomes active on the user's next normal restart.
