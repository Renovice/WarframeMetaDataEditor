# decoder/

Offline `Packages.bin` decoder + cache tools. Requires Python 3 + `zstandard`.

## The essential scripts

| Script | Purpose |
|---|---|
| **`pkg_decode_v46.py`** | **THE decoder.** Parses v46 `Packages.bin`, decompresses every type's ZSTD frame against the embedded dictionary, composes inheritance, and validates against `../data/dumps`. Run it directly to see per-type validation. |
| **`export_packages_full.py`** | Runs the decoder over all types and writes `../data/packages_owntext.jsonl` (raw own-text) + `../data/packages_effective.jsonl` (composed = offline `get_effective_metadata`). |
| **`rec_parse.py`** | Parses the entity/records region into the type inheritance graph → `rec_paths.txt` (461,789 types in file order). |
| **`cache_toc.py`** | Reads a `Cache.Windows/*.toc` index (offset/size/path tree). |
| **`cache_extract.py`** | Extracts + Oodle-decompresses a cache entry (e.g. `/Packages.bin` from `H.Misc.cache`) → the `.dec` the decoder reads. |
| **`decode_editor.py`** | Builds `../data/editor_dataset.json` (merges dumps + public export). |
| **`pkg_decoder_lotus.py`** | Earlier stock-LotusLib port (reference; the stock header layout, not v46). |

## Input

The decoder reads `%TEMP%\Packages.bin.dec` (the Oodle-decompressed `/Packages.bin`). Regenerate it
from the live cache with `cache_extract.py`, or repoint `pkg_decode_v46.PATH`.

## Key layout constants (v46, verified)

```
header @16=20(hdrSize) @20=46(version) @24=1(flags); 50 refs [272:2320]
comFlagsLen u32 @3154  = 82918     -> comFlags [3158:86076]   (LSB-first: hasText + isCompressed bits)
comSizeLen  u32 @86094 = 237009    -> comSize  [86098:323107] (comSize[0:4]=dictSize=1048576, then ULEB sizes)
comZLen     u32 @323513= 20770500  -> comZ     [323517:21094017]
dict = comZ[0:1048576] (0xEC30A437 full dict) ; frames = comZ[1048576:] (magicless zstd)
entityCount @21094017 = 461789 ; dir-block entity table @21094036:EOF
```

Decode a frame: `ZstdDecompressor(dict_data=ZstdCompressionDict(dict, DICT_TYPE_FULLDICT),
format=FORMAT_ZSTD1_MAGICLESS).decompress(frame[ulebLen:], max_output_size=declen)`.

## `_re_history/`

~240 exploratory scripts + intermediate JSON from the reverse-engineering effort. Kept for
provenance; **not needed to run the decoder**. Many still contain old absolute paths.
