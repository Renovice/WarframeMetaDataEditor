# Warframe Metadata Editor

A tool to **read and patch Warframe's per-type metadata entirely offline** — every weapon /
warframe / mod stat (`reloadTime`, `AmmoCapacity`, `OmegaAttenuation`, …), decoded straight from
the game's local `Packages.bin`, with **no game running**.

## What lives where

| Folder | Contents |
|---|---|
| **`editor/`** | The C# WPF editor app (`src` → `App` + `Core`; compiled build in `editor/build/`). Internal assembly name is still `MetadataPatchEditor`. |
| **`decoder/`** | The offline `Packages.bin` decoder + cache tools. `pkg_decode_v46.py` is **the** decoder. See `decoder/README.md`. Exploratory RE history is parked in `decoder/_re_history/`. |
| **`data/`** | Decoded datasets + dumps — **generated locally, git-ignored** (not in the repo). The editor doesn't need them; regenerate with the decoder scripts if you want them. |
| **`reference/`** | Third-party parser source (Puxtril/LotusLib etc.) — **git-ignored**; external upstreams, see `EDITOR_NOTES.md` §7. |
| **`findings/`** | `FINDINGS.md` (the chronological RE log). The solved formats + all findings are consolidated in [`EDITOR_NOTES.md`](EDITOR_NOTES.md). |

## The data (`data/`)

| File | What it is |
|---|---|
| `packages_owntext.jsonl` | Every type's **raw decoded own-text** (201,546 types, full field text incl. nested blocks). |
| `packages_effective.jsonl` | **Composed** metadata (own + inherited top-level fields) — the offline equivalent of `get_effective_metadata`, 309,787 types. |
| `editor_dataset.json` | (Legacy) merged dataset for offline analysis. The **editor decodes live from the cache** and does not require it. |
| `dumps/` | Ground-truth `get_effective_metadata` dumps used to validate the decoder. |

## How it works (one line)

`Packages.bin` stores per-type property text as **magicless-ZSTD frames compressed against a 1 MB
dictionary embedded in the file**. The decoder decompresses each frame against that dictionary and
composes values up the inheritance chain. Full spec (both `Packages.bin` **and** `Languages.bin`) plus
all game-mechanics findings: [`EDITOR_NOTES.md`](EDITOR_NOTES.md).

## Building the editor (the Windows app)

**Prerequisites**
- **Windows** — the editor is a WPF app.
- **.NET SDK 9.0+** — `dotnet --version` should report ≥ 9.
- **`oo2core_9.dll`** (Oodle) — copy it from your Warframe install folder into `editor/lib/`. It's
  proprietary so it isn't in the repo. The project **builds without it**, but the app can't read the
  game cache until this DLL sits next to the exe.

**Build & run**
```
# run it directly:
dotnet run --project editor/App -c Release

# …or produce the standalone single-file exe (lands in the publish folder AND in Build/):
dotnet publish editor/App -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true
```
NuGet dependencies (`ZstdSharp.Port`) restore automatically. In the app: **Decode from Cache…** →
point it at your Warframe folder (or its `Cache.Windows`).

## Decoder scripts (Python, optional)

The standalone Python decoder — handy for bulk offline dumps; the editor doesn't need it.

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

## License

MIT — see [LICENSE](LICENSE). The code and docs in this repo are MIT-licensed.

**Not included / not covered by this license:** `oo2core_9.dll` (Oodle, RAD Game Tools / Epic) is
proprietary and must come from your own Warframe install — it is **not** redistributed here. The
third-party reference decoders this port learned from (Puxtril/LotusLib et al.) keep their own
licenses; see `EDITOR_NOTES.md` §7 for the upstreams. This is a fan-made tool, not affiliated with
or endorsed by Digital Extremes.
