#!/usr/bin/env python3
"""
lang_parse_c.py  --  APPROACH C: suffix-region enumeration + strings pairing.

Same verified v44 format as lang_parse.py, but implemented in the spirit of
"Approach C": enumerate ALL suffix entries in order (they are dense and
self-delimiting), and pair each with its value slice in the STRINGS
sub-region via the (strOffset, valLen) fields carried in the suffix entry.

Per-SECTION byte layout (byte-exact, walker consumes file to EOF):
    [u32 hsz][prefix]
    [u32 lengthOfStrings][ strings sub-region ]
    [u32 numberOfStrings]
    numberOfStrings x SUFFIX entry:
        [u32 sfxLen][suffix][u32 strOffset][u16 valLen][u16 flags]

  fullTag = prefix + suffix
  value   = strings_sub_region[strOffset : strOffset+valLen]
            (literal UTF-8 when flags & 0x200 == 0; DE-compressed otherwise)

This confirms the two lists (suffixes vs value-slices) align POSITIONALLY
through the per-entry strOffset -- no separate count juggling is needed
because each suffix entry directly carries the offset/length of its value.
"""
import struct
import json
import os

DEC_PATH = r'C:/Users/Bartek/AppData/Local/Temp/Languages.en.dec'
GT_PATH  = r'C:/Users/Bartek/OneDrive/Dokumenter/OpenWF Server 08.07.2026/SpaceNinjaServer/node_modules/warframe-public-export-plus/dict.en.json'

BLOCK_REGION_START = 288000
COMPRESSED_FLAG = 0x0200


def iter_entries(d):
    """Yield (fullTag, valueBytes, flags) for every entry, in file order."""
    n = len(d)
    u32 = lambda o: struct.unpack_from('<I', d, o)[0]
    u16 = lambda o: struct.unpack_from('<H', d, o)[0]

    o = BLOCK_REGION_START
    while o < n:
        if o + 4 > n:
            break
        hsz = u32(o)
        if hsz <= 0 or hsz > 200 or o + 4 + hsz > n:
            break
        o += 4
        prefix = d[o:o + hsz].decode('utf-8', errors='replace')
        o += hsz

        length_of_strings = u32(o); o += 4
        str_base = o
        o += length_of_strings

        if o + 4 > n:
            break
        number_of_strings = u32(o); o += 4

        for _ in range(number_of_strings):
            if o + 4 > n:
                break
            sfx_len = u32(o); o += 4
            if o + sfx_len + 8 > n:
                break
            suffix = d[o:o + sfx_len]; o += sfx_len
            str_offset = u32(o); val_len = u16(o + 4); flags = u16(o + 6)
            o += 8
            full_tag = prefix + suffix.decode('latin1')
            value = d[str_base + str_offset: str_base + str_offset + val_len]
            yield full_tag, value, flags


def build(path=DEC_PATH):
    with open(path, 'rb') as fh:
        d = fh.read()
    result = {}
    n_plain = n_comp = n_total = 0
    for full_tag, value, flags in iter_entries(d):
        n_total += 1
        if flags & COMPRESSED_FLAG:
            n_comp += 1
            continue
        try:
            result[full_tag] = value.decode('utf-8')
        except UnicodeDecodeError:
            result[full_tag] = value.decode('latin1')
        n_plain += 1
    return result, dict(total=n_total, plaintext=n_plain, compressed=n_comp)


def main():
    result, stats = build()
    print('total entries          :', stats['total'])
    print('plaintext decoded      :', stats['plaintext'])
    print('DE-compressed (skipped):', stats['compressed'])

    gb = '/Lotus/Language/Weapons/KuvaGrnBowName'
    print('\nGrnBow check: %r -> %r' % (gb, result.get(gb)))

    if os.path.exists(GT_PATH):
        with open(GT_PATH, encoding='utf-8') as fh:
            gt = json.load(fh)
        ok = sum(1 for k, v in gt.items() if result.get(k) == v)
        print('\n--- validation vs dict.en.json (%d keys) ---' % len(gt))
        print('exact matches          : %d' % ok)
        print('overall match rate     : %.4f' % (ok / len(gt)))
        s99 = {k: v for k, v in gt.items() if k.startswith('/Lotus/Language/1999/')}
        m99 = sum(1 for k, v in s99.items() if result.get(k) == v)
        print('/Lotus/Language/1999/  : %d / %d matched' % (m99, len(s99)))


if __name__ == '__main__':
    main()
