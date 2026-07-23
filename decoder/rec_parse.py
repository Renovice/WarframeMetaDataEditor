#!/usr/bin/env python3
"""
rec_parse.py - Parser for the RECORDS (type-graph) region of Packages.bin.dec

Region: [21094036 .. EOF]  (Packages.bin.dec, decompressed from
        Cache.Windows/H.Misc.cache -> /Packages.bin via Oodle).

FORMAT (all integers little-endian uint32, all strings latin1, length-prefixed,
NOT NUL-terminated):

The region is a flat sequence of DIRECTORY BLOCKS. Each block:

    [u32 dirPathLen][dirPath bytes]      # e.g. "/Lotus/Weapons/Grineer/Bows/GrnBow/"
    [u8  0x01]                           # constant separator / "has-children" marker
    [u32 typeCount]                      # number of type entries in this directory
    typeCount x TYPE ENTRY

Each TYPE ENTRY:

    [u32 nameLen][name bytes]            # short name, e.g. "GrnBowWeapon"
    [u8 c0][u8 c1][u8 c2]                # 3 CLASSIFIER / FLAG bytes (see below)
    [u32 basePathLen][basePath bytes]    # FULL path of the base type; len 0 = no base

The full path of a type = dirPath + name  (dirPath already ends in '/').

There is NO 16-byte hash anywhere in the RECORDS region. Records carry ONLY:
  full path, 3 flag bytes, base-type full path.
(The 16-byte hashes live in the HEADER [0..323664] and MID [1372093..21094036]
 regions, which key the POOL own-text blocks to types. Records are keyed
 POSITIONALLY / by full-path string, not by an inline hash.)

CLASSIFIER BYTES (empirical distribution over 461789 types):
  c0 : bitfield. Common values 0x81,0x01,0x09,0x03,0x49,0x91,0xA1,0x00.
       bit 0x80 set on ~58% (269191) - correlates with concrete asset/resource
       types (DiffuseMap/NormalMap/EmissiveMap etc.); bit 0x08 (0x09) ~80k.
       Low nibble 0x1 dominates. Exact semantics not required for compose.
  c1 : 0x00 (313k) dominant, then 0x04,0x20,0x02,... - secondary flags.
  c2 : 0x01 (356k) / 0x02 (104k) / 0x03 (1.4k) - small enum; near-constant 1.
  These three bytes are TYPE FLAGS (abstract/concrete, has-resource, category
  bucket), NOT a hash and NOT needed to walk the inheritance graph.

DEPENDENCIES: the sibling type names inside a directory block are exactly the
types the game groups together (what an earlier black-box read mistook for a
"dependency list" is simply the directory's own type list). True per-type asset
dependency edges are not stored in this region; the only cross-type edge here is
the single `base` pointer per type. Sibling names in the same dir are exposed
below as `dir_siblings` for convenience.

VALIDATION: parses the whole region byte-exact to EOF (0 bytes remaining,
0 errors). 2593/2596 ground-truth dump full-paths resolve to a parsed record;
the 3 exceptions are a leading-space dump artifact and 2 newer types. GrnBowWeapon
parses as /Lotus/Weapons/Grineer/Bows/GrnBow/GrnBowWeapon with base
/Lotus/Weapons/Tenno/Bows/LotusLongBow.
"""
import struct
import sys

REC_START = 21094036


def parse_records(data, start=REC_START):
    """Yield dict per type in file order.

    Each dict: {full_path, name, dir, base, flags:(c0,c1,c2), hash16:None,
                dep_count:int (# sibling types in same directory)}.
    """
    n = len(data)
    p = start
    u32 = lambda o: struct.unpack_from('<I', data, o)[0]
    while p < n:
        dl = u32(p); p += 4
        dpath = data[p:p + dl].decode('latin1'); p += dl
        sep = data[p]; p += 1                 # constant 0x01
        cnt = u32(p); p += 4
        entries = []
        for _ in range(cnt):
            nl = u32(p); p += 4
            name = data[p:p + nl].decode('latin1'); p += nl
            c0, c1, c2 = data[p], data[p + 1], data[p + 2]; p += 3
            bl = u32(p); p += 4
            base = data[p:p + bl].decode('latin1'); p += bl
            entries.append((name, base, (c0, c1, c2)))
        sib_names = [e[0] for e in entries]
        for name, base, flags in entries:
            yield {
                'full_path': dpath + name,
                'name': name,
                'dir': dpath,
                'base': base,
                'flags': flags,
                'hash16': None,          # no per-record hash in this region
                'dep_count': cnt - 1,    # sibling types in the same directory
                'dir_siblings': [s for s in sib_names if s != name],
            }


def main():
    path = sys.argv[1] if len(sys.argv) > 1 else r'C:\Users\Bartek\AppData\Local\Temp\Packages.bin.dec'
    out = sys.argv[2] if len(sys.argv) > 2 else \
        r'C:\Users\Bartek\OneDrive\Dokumenter\Warframe RE PROJECT RENOVICE\WarframeMetaDataEditor\decoder\rec_paths.txt'
    with open(path, 'rb') as f:
        data = f.read()
    recs = list(parse_records(data))
    with open(out, 'w', encoding='utf-8') as f:
        for r in recs:
            f.write(r['full_path'] + '\n')
    print('parsed types:', len(recs))
    print('wrote', out)
    for r in recs[:10]:
        print(r['full_path'], '<-', r['base'] or '(no base)', r['flags'])


if __name__ == '__main__':
    main()
