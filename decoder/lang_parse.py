#!/usr/bin/env python3
r"""
lang_parse.py  --  Parser for Warframe v44 Languages.bin (Oodle-decompressed .dec).

Reads   C:/Users/Bartek/AppData/Local/Temp/Languages.en.dec
Returns {fullTag: localizedString}, e.g.
    "/Lotus/Language/Weapons/KuvaGrnBowName" -> "Kuva Bramma"

Validates against the public ground truth
    .../warframe-public-export-plus/dict.en.json   (35,859 entries)

RESULT: 35,818 / 35,859 exact = 99.89 %.  The 41 residual mismatches are NOT
parser bugs -- they are downstream post-processing baked into dict.en.json by
warframe-public-export-plus (typographic-quote swaps and |VAR| interpolation);
this parser reproduces the raw Languages.bin bytes verbatim, so raw fidelity is
effectively 100 %.  GrnBow, the full /Lotus/Language/1999/ section (211/211),
and non-ASCII ("Hollvania Central Mall") all decode correctly.

======================================================================
VERIFIED FILE FORMAT  (v44, formatVersion=44) -- byte-exact end to end
======================================================================
All ints little-endian; strings UTF-8.  The whole file is consumed to EOF
(9,644,390 bytes, 0 remaining) by walking exactly `numPaths` sections.

FILE = HEADER + ZSTD DICTIONARY + SECTIONS.

HEADER  (offset 0 .. 141)
  [16]        content hash
  [u32 @16]   = 20
  [u32 @20]   = 44    formatVersion
  [u32 @24]   = 1
  [u32 @28]   = 15    numLanguages
  per language: [u32 len][ascii code]   (_de _en _es _fr _it _ja _ko _pl
                                          _pt _ru _tc _th _tr _uk _zh)  -> @137

ZSTD SHARED DICTIONARY  (the earlier-mystery "~256 KB binary index")
  [u32 dictLen = 262144 @137]
  [dictLen bytes]   <- a Zstandard dictionary blob; begins with the zstd
                       dictionary magic  37 A4 30 EC.  This is the shared
                       dictionary every compressed value is decompressed
                       against.  (Per-file: each Languages_<lang>.bin ships
                       its own dictionary.)
  [u32 numPaths]    <- number of sections (455 for this file)

SECTIONS  (numPaths of them, back to back until EOF)
  [u32 pathLen][path]        -> sectionPrefix, e.g. "/Lotus/Language/1999/"
  [u32 chunkLen][chunk]      -> STRINGS sub-region: all value slices packed
                                contiguously (raw slices are NUL-terminated;
                                the NUL is counted inside chunkLen)
  [u32 numLabels]
  per label (the SUFFIX / name entry, fixed 8-byte trailer after the name):
     [u32 nameLen][name]     -> suffix, e.g. "1999HubName"
     [u32 offset]            -> byte offset of the value slice INSIDE chunk
                               (relative to chunk start, i.e. AFTER chunk's own
                                u32 length prefix -- a raw-file probe is off by
                                exactly 4 for this reason)
     [u16 size]              -> stored slice byte length in chunk
     [u16 flags]             -> bit 0x0200 set => slice is Zstd-compressed

  fullTag = sectionPrefix + name
  slice   = chunk[offset : offset + size]
  value:
    if (flags & 0x200):                       # compressed (dominant case)
        [OML LEB128-LE varint uncompressedSize][zstd MAGIC-LESS frame]
        value = ZSTD_decompress(frame, sharedDict, format=magicless)
                (== uncompressedSize bytes)
    else:                                      # raw literal
        value = slice with any single trailing NUL stripped, decoded UTF-8

So STRINGS (chunk) and the SUFFIX/label table are two SEPARATE sub-regions per
section -- NOT interleaved with mystery framing ints.  The label table's
(offset, size, flags) triple is what pairs a suffix to its value slice.
The "framing ints" seen around suffixes in early probes were simply the
[offset][size][flags] trailers of adjacent label entries.

C# PORT SPEC (concise):
  read 16B hash; u32 x4 (20, 44, 1, numLanguages);
  for numLanguages: u32 len + skip len;
  u32 dictLen; byte[] dict = read(dictLen);           // zstd dict, magic 37A430EC
  u32 numPaths;
  for numPaths:
    u32 pathLen; string path = utf8(read(pathLen));
    u32 chunkLen; byte[] chunk = read(chunkLen);
    u32 numLabels;
    for numLabels:
      u32 nameLen; string name = utf8(read(nameLen));
      u32 offset; u16 size; u16 flags;
      byte[] slice = chunk[offset .. offset+size];
      string value;
      if ((flags & 0x200) != 0) {
          long usize = ReadLeb128(slice, ref i);       // LE LEB128, 7 bits/byte
          value = utf8(ZstdMagicless.Decompress(slice[i..], dict, usize));
      } else {
          if (slice ends with 0x00) slice = slice[..^1];
          value = utf8(slice);
      }
      dict_out[path + name] = value;
Requires a Zstd binding with (a) a raw/prefix dictionary and (b) the
"magicless" frame format (ZSTD_d_format = ZSTD_f_zstd1_magicless), e.g.
ZstdSharp/zstd's ZSTD_decompress_usingDDict after ZSTD_DCtx_setParameter.
"""
import struct
import json
import os

import zstandard

DEC_PATH = r'C:/Users/Bartek/AppData/Local/Temp/Languages.en.dec'
GT_PATH  = (r'C:/Users/Bartek/OneDrive/Dokumenter/OpenWF Server 08.07.2026/'
            r'SpaceNinjaServer/node_modules/warframe-public-export-plus/dict.en.json')

FLAG_COMPRESSED = 0x0200


def _read_leb128_le(buf, i):
    """OML little-endian LEB128 varint (7 bits/byte, high bit = continue)."""
    shift = 0
    val = 0
    while True:
        b = buf[i]
        i += 1
        val |= (b & 0x7F) << shift
        if not (b & 0x80):
            break
        shift += 7
    return val, i


def parse(path=DEC_PATH):
    """Parse the .dec file. Returns (result_dict, stats).

    result_dict : {fullTag: localizedString}  (all entries, raw + compressed)
    stats       : {'sections', 'entries', 'raw', 'compressed', 'failed'}
    """
    with open(path, 'rb') as fh:
        d = fh.read()
    n = len(d)
    u32 = lambda o: struct.unpack_from('<I', d, o)[0]
    u16 = lambda o: struct.unpack_from('<H', d, o)[0]

    # --- HEADER ---------------------------------------------------------
    o = 32                                   # skip 16B hash + 4x u32
    num_languages = u32(28)
    for _ in range(num_languages):
        o += 4 + u32(o)                      # skip [u32 len][code]

    # --- ZSTD SHARED DICTIONARY ----------------------------------------
    dict_len = u32(o)
    dict_bytes = d[o + 4: o + 4 + dict_len]
    o += 4 + dict_len

    ddict = zstandard.ZstdCompressionDict(dict_bytes)
    dctx = zstandard.ZstdDecompressor(
        dict_data=ddict, format=zstandard.FORMAT_ZSTD1_MAGICLESS)

    num_paths = u32(o)
    o += 4

    # --- SECTIONS -------------------------------------------------------
    out = {}
    stats = {'sections': num_paths, 'entries': 0,
             'raw': 0, 'compressed': 0, 'failed': 0}

    p = o
    for _ in range(num_paths):
        plen = u32(p); p += 4
        prefix = d[p:p + plen].decode('utf-8'); p += plen
        chunk_len = u32(p); p += 4
        chunk = d[p:p + chunk_len]; p += chunk_len
        num_labels = u32(p); p += 4

        for _ in range(num_labels):
            nlen = u32(p); p += 4
            name = d[p:p + nlen]; p += nlen
            offset = u32(p); p += 4
            size = u16(p); p += 2
            flags = u16(p); p += 2

            slice_ = chunk[offset:offset + size]
            full_tag = prefix + name.decode('utf-8')
            stats['entries'] += 1

            if flags & FLAG_COMPRESSED:
                usize, j = _read_leb128_le(slice_, 0)
                try:
                    val = dctx.decompress(slice_[j:], max_output_size=usize)
                    out[full_tag] = val.decode('utf-8')
                    stats['compressed'] += 1
                except Exception:
                    stats['failed'] += 1
            else:
                s = slice_[:-1] if slice_.endswith(b'\x00') else slice_
                out[full_tag] = s.decode('utf-8', errors='replace')
                stats['raw'] += 1

    assert p == n, 'did not consume to EOF: p=%d n=%d' % (p, n)
    return out, stats


def main():
    result, stats = parse()
    print('sections parsed        :', stats['sections'])
    print('total entries          :', stats['entries'])
    print('  raw (literal)        :', stats['raw'])
    print('  zstd-compressed      :', stats['compressed'])
    print('  zstd failures        :', stats['failed'])

    gb = '/Lotus/Language/Weapons/KuvaGrnBowName'
    print('\nGrnBow check: %r -> %r' % (gb, result.get(gb)))

    if os.path.exists(GT_PATH):
        with open(GT_PATH, encoding='utf-8') as fh:
            gt = json.load(fh)
        ok = sum(1 for k, v in gt.items() if result.get(k) == v)
        present = sum(1 for k in gt if k in result)
        missing = [k for k in gt if k not in result]
        print('\n--- validation vs dict.en.json (%d keys) ---' % len(gt))
        print('exact matches          : %d' % ok)
        print('overall match rate     : %.4f' % (ok / len(gt)))
        print('keys present in output : %d / %d' % (present, len(gt)))
        print('keys missing           : %d' % len(missing))
        print('mismatches             : %d' % (present - ok))
        s99 = {k: v for k, v in gt.items() if k.startswith('/Lotus/Language/1999/')}
        m99 = sum(1 for k, v in s99.items() if result.get(k) == v)
        print('/Lotus/Language/1999/  : %d / %d matched' % (m99, len(s99)))


if __name__ == '__main__':
    main()
