# Warframe Metadata Editor

A tool to **read and patch Warframe's per-type metadata entirely offline** — every weapon /
warframe / mod stat (`reloadTime`, `AmmoCapacity`, `OmegaAttenuation`, …), decoded straight from
the game's local `Packages.bin`, with **no game running**.

## What lives where

| Folder | Contents |
|---|---|
| **`editor/`** | The C# WPF editor app (`src` → `App` + `Core`; compiled build in `editor/build/`). Internal assembly name is still `MetadataPatchEditor`. |
| **`decoder/`** | The offline `Packages.bin` decoder + cache tools. `pkg_decode_v46.py` is **the** decoder. See `decoder/README.md`. Exploratory RE history is parked in `decoder/_re_history/`. |
| **`data/`** | The decoded datasets + ground-truth dumps. See below. |
| **`reference/`** | Third-party parser source, incl. **Puxtril/LotusLib** — the authoritative decoder this port is based on. |
| **`findings/`** | `FINDINGS.md` (the chronological RE log). The solved formats + all findings are consolidated in [`EDITOR_NOTES.md`](EDITOR_NOTES.md). |

## The data (`data/`)

| File | What it is |
|---|---|
| `packages_owntext.jsonl` | Every type's **raw decoded own-text** (201,546 types, full field text incl. nested blocks). |
| `packages_effective.jsonl` | **Composed** metadata (own + inherited top-level fields) — the offline equivalent of `get_effective_metadata`, 309,787 types. |
| `editor_dataset.json` | Merged dataset the editor consumes (dumps + public export). |
| `dumps/` | Ground-truth `get_effective_metadata` dumps used to validate the decoder. |

## How it works (one line)

`Packages.bin` stores per-type property text as **magicless-ZSTD frames compressed against a 1 MB
dictionary embedded in the file**. The decoder decompresses each frame against that dictionary and
composes values up the inheritance chain. Full spec (both `Packages.bin` **and** `Languages.bin`) plus
all game-mechanics findings: [`EDITOR_NOTES.md`](EDITOR_NOTES.md).

## Quick start

```
# 1) (re)extract the current Packages.bin from the game cache -> %TEMP%\Packages.bin.dec
python decoder/cache_extract.py           # or point pkg_decode_v46.PATH at an existing .dec

# 2) decode + validate against the dumps
python decoder/pkg_decode_v46.py

# 3) produce the full offline datasets in data/
python decoder/export_packages_full.py
```

Requires Python 3 + `zstandard` (`pip install zstandard`).

## Editing values

The decoded value is the **static base** DE ships. To change what the game uses, write an
**OpenWF metadata patch** (`OpenWF/Metadata Patches/*.txt`) — patches override the base on next
launch. The editor helps author these; the decoder gives you the true base to start from.
