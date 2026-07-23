# Warframe Metadata Editor — Complete Notes

Everything about how this editor works and what we've reverse-engineered, in one place.
The editor reads Warframe's per-type metadata **entirely offline** (no game running) and helps you
author OpenWF metadata patches to change any stat.

- **Stats** (reloadTime, AmmoCapacity, mod Values, warframe MaxEnergy…) come from `Packages.bin` — §2.
- **Names** ("Kuva Bramma", "Octavia") come from `Languages.bin` — §3.
- **What you can edit and how a patch is applied** — §4.
- **Game-mechanics findings** (mod formulas, mod max-rank truth, warframe scaling, rivens) — §5.

Decoders: `decoder/pkg_decode_v46.py` + `decoder/lang_parse.py` (reference Python), ported to
`editor/Core/PackagesBinDecoder.cs` + `editor/Core/LanguagesBin.cs` (what the exe runs).
Authoritative upstream: **Puxtril/LotusLib** (`reference/LotusLib/`).

---

## 1. The one insight (both files share it)

Both `Packages.bin` and `Languages.bin` store their payload as **magicless-ZSTD frames compressed
against a dictionary embedded in the file itself** (dict magic `37 A4 30 EC` = `0xEC30A437`). Early
attempts failed because the data looked like an unknown bit-packed codec — the numbers/strings
"weren't there as raw bytes" because they were *compressed*, not absent. Once you feed the embedded
dictionary to a magicless ZSTD decoder, everything falls out.

The two files use the **same trick** but **different framing and constants** — do not mix them:

| | `Packages.bin` (stats) | `Languages.bin` (names) |
|---|---|---|
| Version | 46 | 44 |
| Embedded dict | 1 MiB (1,048,576) | 256 KiB (262,144) |
| Framing | one concatenated frame region + separate size/flag arrays | per-section value "chunk" + label table |
| Cache file | `H.Misc.cache` → `/Packages.bin` | `H.Misc_en.cache` → `/Languages.bin` |

Cache extraction is identical for both: pull the entry out of the `.cache`/`.toc` pair and
Oodle-decompress it (needs `oo2core_9.dll`, bundled with the exe). Full TOC + Oodle format in **§6**;
how the whole thing was reverse-engineered — the wrong maps, the dead ends, the unlock — in **§7**.

---

## 2. Packages.bin — stats (version 46)

Fully recoverable from `Cache.Windows/H.Misc.cache → /Packages.bin`. Validated byte-exact against
2,536 `get_effective_metadata` dumps. Offsets below are the values for the 52,710,200-byte
decompressed file — **illustrative, not hardcoded**: `PackagesBinDecoder.cs` now **locates the four
regions structurally** (comZ via the ZSTD-dict magic, comSize via its ULEB-sum, comFlags via its
bit-count, the entity table via the first dir-block), so it survives content updates (which shift
everything after the growing comFlags/comSize buffers) and version bumps that keep the buffer shape.
If the format truly changes, location fails loudly rather than returning garbage.

```
[0:16]    magic hash
@16 u32 = 20     header size
@20 u32 = 46     version
@24 u32 = 1      flags
[28:268]  240-byte block (20 x 12B) — v46 addition (stock LotusLib skips only 4 here)
@268 u32 = 50    referenceCount ; 50 refs {u32 len; ascii path; u16 tag} span [272:2320]
comFlagsLen u32 @3154   = 82918     -> comFlags = [3158 : 86076]
comSizeLen  u32 @86094  = 237009    -> comSize  = [86098 : 323107]
comZLen     u32 @323513 = 20770500  -> comZ     = [323517 : 21094017]
entityCount u32 @21094017 = 461789
entity (dir-block) table  = [21094036 : EOF]
```

- **comFlags**: LSB-first bitstream. Per entity in table order: 1 `hasText` bit; per has-text
  entity 1 `isCompressed` bit. (201,547 have text; 198,870 compressed, 2,677 raw.)
- **comSize**: `comSize[0:4]` = `dictSize` = 1,048,576; then one ULEB frame-byte-length per
  has-text entity (201,547 sizes summing to 19,721,924 = the frame region exactly).
- **comZ** = `[dict (1,048,576 B)][frames]`. `dict` starts with `0xEC30A437` (full dict). Each
  frame = `[ULEB declen][magicless ZSTD payload]`.
- **entity table** (dir-blocks): `[u32 dirLen][dir][u8 0x01][u32 typeCount]` then `typeCount ×
  {[u32 nameLen][name][3 flag bytes][u32 baseLen][base]}`. `fullPath = dir + name`;
  `parent = base if base[0]=='/' else dir + base`.

**Decode** each has-text entity in table order (one comFlags bit-group, one comSize ULEB, one comZ
frame):

```python
dctx = zstandard.ZstdDecompressor(
    dict_data=zstandard.ZstdCompressionDict(dict_bytes, dict_type=zstandard.DICT_TYPE_FULLDICT),
    format=zstandard.FORMAT_ZSTD1_MAGICLESS)
declen, off = uleb(frame, 0)
own_text = dctx.decompress(frame[off:], max_output_size=declen)   # if isCompressed
# else: own_text = frame  (raw plaintext)
```

Full metadata = a type's own-text **unioned with its ancestors'** own-text up the `parent` chain
(child overrides parent) — the offline equivalent of `get_effective_metadata`.

**Validation:** 461,789 entities, frame alignment exact (19,721,924 / 19,721,924); vs 2,536 dumps
~99.6% on actual field content. Distinctive numerics byte-exact (GrnBow `reloadTime=0.60000002`,
`fireRate=40`, `AmmoCapacity=5`). `OmegaAttenuation` note: the decoder returns the **static base**
DE ships (GrnBow `0.65`); dumps showing `1.55` are a metadata *patch* applied at compose time, not a
runtime computation — the static decode is ground truth.

---

## 3. Languages.bin — names (version 44)

Localized strings (item names + descriptions) from `Cache.Windows/H.Misc_en.cache → /Languages.bin`
(English; other langs in their own `H.Misc_<lang>` shard, and a parallel `B.Misc_<lang>` shard also
carries it). Live-verified against the 9,644,390-byte English file. All ints little-endian; the
language table and dict lengths are read dynamically.

```
[0:16]    16-byte content hash
@16  u32 = 20     (fixed; unused)
@20  u32 = 44     formatVersion   ← "v44". Read but NOT validated at runtime (documentary only)
@24  u32 = 1      (fixed; unused)
@28  u32 = 15     numLanguages
@32  ...          language table: numLanguages × { u32 len; `len` ascii code } — SKIPPED
                  (codes: _de _en _es _fr _it _ja _ko _pl _pt _ru _tc _th _tr _uk _zh); ends @137
@137 u32 = 262144 dictLen                                   (256 KiB)
[141 : 262285]    ZSTD dictionary (full dict, magic 37 A4 30 EC)
@262285 u32 = 455 numPaths (sections)
[262289 : EOF]    455 back-to-back SECTION records (consumes to EOF exactly)
```

**Section record** (× numPaths):
```
u32 pathLen ; `pathLen` bytes UTF-8 prefix          (e.g. "/Lotus/Language/Suits/")
u32 chunkLen ; `chunkLen` bytes value-chunk          (this section's string bytes, packed)
u32 numLabels ; then numLabels label records
```
Label offsets are relative to the **start of this section's chunk** (`chunkStart`).

**Label record** (× numLabels): fixed 8-byte trailer after the name.
```
u32 nameLen ; `nameLen` bytes UTF-8 name             (e.g. "KuvaGrnBowName")
u32 offset  ; byte offset of the value slice INSIDE the chunk
u16 size    ; slice byte length
u16 flags   ; bit 0x0200 (FLAG_COMPRESSED) ⇒ slice is zstd-compressed
```

**Decode** one label → string (`tag = prefix + name`, `slice = chunk[offset : offset+size]`):
```python
if flags & 0x0200:                                   # compressed
    declen, j = uleb(slice, 0)                        # LEB128 uncompressed length
    text = dctx.decompress(slice[j:], max_output_size=declen)   # magicless, against the full dict
else:                                                 # raw literal
    text = slice[:-1] if slice[-1:] == b"\x00" else slice       # strip ONE trailing NUL
out[tag] = text.decode("utf-8")
```

Output = a flat `{tag → string}` map. The editor keys off `…Name` tags for friendly labels.

**Validation (live):** 455 sections, **136,052 entries** (49,285 raw + 86,767 compressed, 0 failed),
full consume to EOF. Byte-exact round-trips: `/Lotus/Language/Suits/BardName` → `Octavia`;
`/Lotus/Language/Weapons/KuvaGrnBowName` → `Kuva Bramma`; a compressed `…OctaviaDeluxeSkinBundleDesc`
(93 B stored → LEB128 declen 217 → magicless frame → 217 B of text).

---

## 4. Editing — how a patch is applied (OpenWF)

The decoder gives you the **static base** DE ships. To change what the game uses, the editor emits an
**OpenWF metadata patch** (`OpenWF/Metadata Patches/*.txt`). The consumer is the **OpenWF Bootstrapper**
(`wtsapi32.dll` client proxy — *not* the server); it rewrites the client's effective metadata on read.
Applies **client-side on restart** (or hot-reload via `GET http://localhost:6900/reload_metadata_patches`).

Patch ops (documented in OpenWF's `Reference Manual.html`, proven in-game):
- **`Field=value`** — bare line, *prepended* to a type's metadata; the game keeps the first duplicate,
  so this overrides (or **adds**) a **top-level** field. The proven, reliable lever.
- **`q|<dot.path>|value`** — query-assignment into the nested structure by index, e.g.
  `q|Upgrades.0.Value|123` or `q|Upgrades.0.OperationType|MULTIPLY`. **Proven in-game**
  (`ModDropChanceBooster.txt` uses `q|Upgrades.0.Value|2`; the game reads Value=2 back). Works on
  **index-addressable** arrays like `Upgrades`; does **not** reach anonymous `{...}` lists (riven
  `ItemCompatibilities` — those need `s|`).
- **`s|regex|replacement`** — Soup-regex substitution (anchored). Used for the riven per-weapon
  strength (the compat list isn't index-addressable).

The editor surfaces, per item: top-level scalar fields as `Field` rows, `Upgrades.N.*` as `q|` rows
(with `Operation / formula` and `Gain per rank` clearly labelled), warframe `LevelUpgrades` as
per-level `q|` rows, riven strength as an anchored `s|` row, and numeric arrays as `arr` rows.
It validates `OperationType`/`FusionLimit` against their allowed enum sets before saving.

---

## 5. Game-mechanics findings

### 5.1 A mod's "formula" = OperationType + Value + linear rank scaling
Across all 1,977 `/Lotus/Upgrades/Mods/` types:
- **Rank scaling is uniform + linear.** `effective = Value × (rank + 1)`. No mod stores a per-rank
  table (0 `LevelStats` in the cache); curve fields (`FusionEnergyCurve`/`FusionPowerCurve`) are
  always `QC_NONE`. (Genuinely non-linear cases like Warding Thurible live in the **ability script**,
  not mod metadata.) Stretch: `0.075 × (5+1) = 45%`. Serration: `0.15 × (10+1) = 165%`.
- **The operation differs per mod** (`OperationType`, editable via `q|Upgrades.0.OperationType`):
  `STACKING_MULTIPLY` (1045; +%-of-base, additive within group) · `ADD` (758; flat add) ·
  `MULTIPLY` (115; separate final multiplier, e.g. damage-taken ×0.833) · `SET` (67; overwrite) ·
  `ADD_BASE` (4; add to base pre-multipliers). Semantics confirmed vs wiki + game logic.

### 5.2 A mod's MAX RANK is NOT in the client cache
Key finding, verified by diffing rank-5 **Reach** vs rank-3 **Quick Return**: same `FusionLimit`
(`QA_MEDIUM`), same `BaseDrain`, **every rank-relevant field identical** — two mods with different
max ranks are cache *twins*. So the 3-vs-5 cap is decided **outside** the patchable `Packages.bin`
(DE public-export / server side). Cache `FusionLimit` is only a **coarse correlate**:
- `QA_VERY_HIGH` ⟺ rank 10 (reliable) · **absent** ⟺ rank 5 (reliable) · `QA_MEDIUM` → **both 3 and 5**.

Confirmed real max ranks (wiki, after stripping Beginner/Intermediate/Expert **training-variant**
duplicates that share names): Serration/Vitality/Redirection = 10, Stretch/Intensify/Continuity = 5,
Quick Return = 3, Transmute Cores / Astral Autopsy = 0. **Editing `FusionLimit` is therefore
experimental** — it may nudge the tier flag but likely won't move the usable max rank. The editor
offers it (with an "experimental" label) but the base `Value`/`OperationType` are the reliable levers.

### 5.3 Warframe stats — base (reliable) + per-level (reliable)
Base stats are **top-level scalar fields** (edited via the proven `Field` prepend), friendly-labelled
in the editor. Octavia example:

| Editor label | Field | Value |
|---|---|---:|
| Base Energy (max) | `MaxEnergy` | 175 |
| Starting Energy | `InitialEnergy` | 100 |
| Base Health | `MaxHealthOverride` | 270 |
| Base Shield | `MaxShieldOverride` | 180 |
| Base Armor | `ArmourRatingOverride` | 160 |
| Sprint Speed (×) | `MovementSpeedMultiplier` | 1.05 |

Per-level scaling is `LevelUpgrades` (`AVATAR_HEALTH_MAX`/`SHIELD_MAX`/`POWER_MAX` with `ADD_BASE`
per-level increment; Octavia +10 health / +10 shield / +5 energy per level), exposed as
`Health / level` etc. `q|` rows. So you control **both** the base and the per-level gain.

### 5.4 Riven strength (individual weapon)
Real riven strength = the per-weapon **`Attenuation`** inside the riven mod's `ItemCompatibilities`
(uncapped, real multiplier), **not** `OmegaAttenuation` (that's the cosmetic dots, 0.5–1.55 on the
weapon). The editor patches strength with an **anchored `s|`** so it hits **only that weapon**
(the compat list isn't `q|`-indexable). `Scale ×` multiplies strength (+ tied dots) for rivens, or
the per-rank/per-level `Value`s for mods/warframes.

---

## 6. Cache container layer — opening the game's cache files

Before any ZSTD decode you have to pull the entry out of the `.cache`/`.toc` pair. This layer was
**ported from Puxtril/LotusLib** (`reference/LotusLib/`), the authoritative C++ reader for DE's
Evolution-Engine caches — not hand-reversed. Editor: `Core/Cache.cs` + `Core/Oodle.cs`; Python mirror:
`decoder/cache_toc.py` + `cache_extract.py`.

**`.toc` (the index).** Header = two little-endian u32: magic `0x1867C64E` @0, `archiveVersion` @4
(16 or 20). Then a packed array of fixed **96-byte records**, `count = (len-8)/96`:

```
+0x00 int64  cacheOffset     byte offset into .cache  (== -1 marks a DIRECTORY entry)
+0x08 int64  timeStamp       Windows FILETIME         (== 0 marks a deleted/duplicate stub)
+0x10 int32  compressedLen   bytes to read from .cache
+0x14 int32  length          decompressed size
+0x18 int32  reserved
+0x1C int32  parentDirIndex  indexes the DIRECTORY-only array (synthetic root = 0)
+0x20 char[64] name          NUL-terminated, Latin-1
```

The subtlety that matters: **`parentDirIndex` indexes a directory-only array, not the flat record
list.** Build a separate `m_dirs` list (root pre-seeded as `""`), append every `cacheOffset==-1`
record to it, then resolve full paths recursively (`path[k] = path[parent] + "/" + name`) — it can
forward-reference a parent, so memoise with a cycle/out-of-range guard. A file's full path =
`dir[parentDirIndex] + "/" + name`. To find `/Packages.bin` / `/Languages.bin`, exact-match the full
path; `timeStamp==0` duplicates resolve to the same path so **last valid wins**. Result =
`(cacheOffset, compressedLen, length)`.

**`.cache` (the blobs).** Headerless concatenation. Seek `cacheOffset`, read `compressedLen` bytes.

**Decompression** — dispatch on `(comp,dec)` + the blob's first bytes (there is no per-entry flag):
1. `comp == dec` → stored uncompressed.
2. `blob[0]==0x80 && (blob[7]&0x0F)==0x01` → **BE `0x80`-block loop** (Warframe's post-"Ensmallening"
   multi-block Oodle container). Each block: 8-byte header of two **big-endian** u32 (`num1,num2` — the
   one endian flip in an otherwise LE format); `blockCompLen = (num1>>2)&0xFFFFFF`,
   `blockDecompLen = (num2>>5)&0xFFFFFF`; payload starts `0x8C` (one wrapped Oodle stream);
   `OodleLZ_Decompress(payload) → blockDecompLen` bytes; loop until `dec` bytes emitted. (A `{0,0}`
   header = single unwrapped block.) This `0x80`/`0x8C` scheme is the same inner container as the SHCC
   content-catalog chunks — which is how it was cross-recognized.
3. `blob[0]==0x8C` → single-shot `OodleLZ_Decompress`.

**Oodle** = `OodleLZ_Decompress` from `oo2core_9.dll` (fallbacks `oo2core_9_win64`/`oo2core_8*`),
called with `fuzzSafe=No, checkCRC=No, verbosity=None, …, threadPhase=All`; success ⇔ return ==
expected `dec`. DLL resolved: explicit override → next to the exe → the Warframe `GameDir` (parent of
`Cache.Windows`) → dev fallback.

---

## 7. How these formats were reverse-engineered (the RE process)

### 7.1 The container layer — ported, not hand-cracked
The `.toc`/`.cache` + Oodle layer (§6) came straight from **LotusLib**: the magic, the 96-byte
`RawTOCEntry` geometry, and the `parentDirIndex`-into-directory-array trick were read from
`CachePair.h` / `DirectoryTree.cpp` / `RawEntry.h` and confirmed on the real files. The only genuinely
reverse-engineered piece was recognizing that Warframe's post-"Ensmallening" blobs wrap `0x8C` Oodle
streams in a BE `0x80`-block header — cross-identified against the SHCC content-catalog chunk format.

### 7.2 Packages.bin — the wrong map, and the one signature that fixed it
The instructive one: **the first map was entirely wrong**, built from Shannon-entropy + text-density
region profiling. Three regions were mislabeled:

| Old (wrong) label | Reality |
|---|---|
| "POOL" (low entropy 5.45, "de-duped own-text pool") | the **1 MiB embedded ZSTD dictionary**. The entropy guess started ~158 bytes late and **clipped the `ZSTD_MAGIC_DICTIONARY` off the front** — which is exactly why it wasn't recognized as a dictionary. |
| "MID" (entropy 7.91, "bespoke LZ codec + 16B content hashes + hash→offset index") | the array of **magicless ZSTD frames**. The "literal ASCII runs between back-refs" were zstd frames; the "repeating 16-byte hashes" were frame boundaries. |
| "RECORDS" ("dependency / directory tables") | the **entity + inheritance table** (closest to right — the base-chains were correctly reversed). |

That wrong map spawned two big dead ends:
- **The "keying" hunt.** Assuming each type indexed into the pool via some hash — FNV/CRC/MD5/SHA1
  links, header offset-tables, alphabetical order — all proven random. It was a **non-problem**: frames
  are consumed **positionally**, in entity-table order. No index exists or is needed.
- **The exe-RE rabbit hole.** A full Ghidra stack byte-verified the game's `read_bits`/`read_float`
  dequantizers at their RVAs, a runtime `[ser+0x198]` nbits table, a `{0,1,3}` mode discriminator — all
  **correct machine code, wrong layer.** That's the game's *runtime* notation reader, irrelevant to the
  on-disk container. It even produced a persuasive "bit-budget impossibility" proof (a 192-bit
  GLAPistol payload can't hold its overrides) and the observation that "reloadTime / OmegaAttenuation
  appear **zero** times as raw f32" — a real fact with the wrong conclusion. The values weren't raw
  floats because they were **zstd-compressed**, not because they were runtime-only.

**The unlock:** porting **LotusLib `PackagesBin.cpp`** and noticing `comZ` begins with `37 A4 30 EC`
(`ZSTD_MAGIC_DICTIONARY`). That one signature reframed everything: `comZ = [full dict][magicless
frames]`, `dictSize` is the first u32 of `comSize`, and the three length-prefixed buffers
(`comFlags`/`comSize`/`comZ`) are located by forward-scanning for a length header followed by exactly
that many bytes (they're hidden amid hash data, not contiguous as stock LotusLib assumes — this is v46).

**Why the decode is provably right — frame alignment is self-verifying.** Consuming exactly one
`comSize` ULEB + one frame per has-text entity, across all **461,789** entities, lands the frame
pointer **exactly** on `comZ_end` (21,094,017): the 201,547 ULEBs sum to 19,721,924, the `comFlags`
bit-walk ends at byte 86,075, and `dict 1,048,576 + frames 19,721,924 = 20,770,500 = comZLen`. A single
off-by-one anywhere desyncs every downstream frame, so exact end-alignment is a strong global proof.
Then: byte-exact spot checks vs `get_effective_metadata` dumps (GrnBow `reloadTime=0.60000002`,
`fireRate=40`, `AmmoCapacity=5`), an independent adversarial pass on disjoint random dumps (seed 1337
vs 42), and a non-echo proof (a 120-char decoded chunk appears in **no** dump; dict sha256 printed) —
the very floats previously "proven un-recoverable offline" now emerge byte-exact. (The old
`OFFLINE_DECODER_VERDICT.json` in `decoder/_re_history/` still asserts the refuted "blocked offline"
conclusion — kept only as the RE record, superseded by `pkg_decode_v46.py`.)

### 7.3 Languages.bin — a transplant of the Packages.bin insights
Once Packages.bin was solved, Languages.bin was mostly recognition. (Note: the `loc_A/B/C.py` history
files are actually about the **Packages.bin** compression derivation, despite living in `_re_history`;
the Languages framing is documented in `lang_parse.py`'s docstring.) Three transplants:
- the earlier "~256 KB binary index" mystery → the **shared ZSTD dictionary** (`u32 dictLen 262144` +
  a blob with the same `37 A4 30 EC` magic);
- the "mystery framing ints" scattered around the suffix strings → the fixed **8-byte
  `[u32 offset][u16 size][u16 flags]` label trailers**; each section is `[pathLen/prefix]` +
  `[chunkLen/value-chunk]` + `[numLabels + label table]`, two separate sub-regions, not interleaved;
- per compressed value, the same `[LEB128 declen][magicless zstd]` against the shared dict; `flags &
  0x0200` is the compressed/raw discriminator.

**The one genuine probe — off-by-4.** A label offset checked against the raw file landed **exactly 4
bytes early**. The fix: the offset is relative to the **chunk start** (after the chunk's own `u32`
length prefix) — and 4 is precisely that `chunkLen` u32's width. Correcting it made
`slice = chunk[offset:offset+size]` hit the right bytes.

**Validation:** the parser consumes the whole 9,644,390-byte file and asserts `p == n` (0 bytes left)
after exactly 455 sections — a structural proof the framing is exact — plus **99.89%** (35,818 /
35,859) against `warframe-public-export-plus/dict.en.json`. The 41 residual mismatches were shown to be
downstream export post-processing (typographic-quote swaps, `|VAR|` interpolation), not parser errors.

---

## 8. Caveats (version-specific)

- **Both decoders now self-locate — no baked-in offsets.** `PackagesBinDecoder.cs` derives the four
  regions structurally (see §2); `LanguagesBin.cs` already read every length dynamically. So both
  survive content updates and version bumps that keep the same buffer shape, and neither hard-requires
  a version number. A genuine *format* change (new buffer layout) makes location fail loudly — re-derive
  per §7.2; LotusLib is the reference, and v46 framing already differs from stock LotusLib.
- **Illustrative offsets in §2/§3** (`comFlagsLen @3154`, `dictLen @137`, …) are the current build's
  values for reference, not constants the code depends on.
- **`FusionLimit` editing is unproven** (see §5.2) — treat as experimental.
- **Names are per-language** — only `H.Misc_en` is wired up.

---

*Format specs live-verified against the current cache. The chronological RE log is `findings/FINDINGS.md`.*
