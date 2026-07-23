#!/usr/bin/env python3
r"""
lang_parse_b.py  --  APPROACH B: derive the v44 Languages.bin format bottom-up
from ground truth, then generalise to a full parser.

Method (what this file demonstrates):
  1. Take known (suffix, value) pairs from dict.en.json.
  2. Locate each suffix and each value inside Languages.en.dec.
  3. MEASURE the exact byte framing around them (leading ints, sfxLen,
     strOffset, packed len/flag; the -4 body offset for plaintext; the NUL
     delimiter) -- printed by measure_framing().
  4. Generalise the measured layout to parse() over all 444 sections and
     validate vs dict.en.json.

Findings (see measure_framing output; full spec in lang_parse.py docstring):

  SECTION   [u32 hsz][prefix][u32 lengthOfStrings][u32 secondCount]
            [ STRINGS sub-region ][ SUFFIX table (dense, to next section) ]

  SUFFIX    [u32 sfxLen][suffix][u32 strOffset][u32 packed]
            packed = (flag<<24) | valLen ;  flag 0x00 plaintext / 0x02 LZ

  VALUE slice in STRINGS = [u32 hash][body]
    plaintext : body @ strBase+strOffset-4 , read valLen , cut at first NUL
    compressed: body @ strBase+strOffset    , valLen LZ bytes (see lang_parse.py)

Measured constants proving the framing (section /Lotus/Language/1999/):
  * suffix "10000HollarsName" @ 308378 ; the 8 bytes before = u32(20) u32(16=len)
    -> [strOffset=?][packed] then suffix; the 12 bytes after = u32 u32 then next
    entry's [u32 sfxLen][suffix].
  * strOffset - trueBodyOffset == 4 for EVERY plaintext entry (constant).
  * packed high byte is 0x00 for plaintext (valLen == exact UTF-8 length) and
    0x02 for compressed (valLen == compressed size < uncompressed length).
"""
import struct
import json
import os
import re

DEC_PATH = r'C:/Users/Bartek/AppData/Local/Temp/Languages.en.dec'
GT_PATH  = r'C:/Users/Bartek/OneDrive/Dokumenter/OpenWF Server 08.07.2026/SpaceNinjaServer/node_modules/warframe-public-export-plus/dict.en.json'

BLOCK_REGION_START = 288000
FLAG_COMPRESSED = 0x02


# ---------------------------------------------------------------- generalised parser
def _section_starts(d):
    u32 = lambda o: struct.unpack_from('<I', d, o)[0]
    out = []
    for m in re.finditer(rb'/Lotus/Language/[^\x00]*?/', d):
        s = m.start()
        if s < 4 or s < BLOCK_REGION_START:
            continue
        hsz = u32(s - 4)
        if 15 < hsz < 120 and s + hsz <= len(d):
            prefix = d[s:s + hsz]
            if prefix.startswith(b'/Lotus/Language/') and prefix.endswith(b'/') and hsz == len(prefix):
                out.append((s - 4, hsz, prefix))
    return sorted(set(out))


def parse(path=DEC_PATH):
    with open(path, 'rb') as fh:
        d = fh.read()
    n = len(d)
    u32 = lambda o: struct.unpack_from('<I', d, o)[0]
    secs = _section_starts(d)
    out = {}
    stats = {'sections': len(secs), 'entries': 0, 'plaintext': 0, 'compressed': 0}

    for si, (hoff, hsz, prefix_b) in enumerate(secs):
        prefix = prefix_b.decode('utf-8', errors='replace')
        p = hoff + 4 + hsz
        length_of_strings = u32(p)
        str_base = p + 8
        suf_start = str_base + length_of_strings
        suf_end = secs[si + 1][0] if si + 1 < len(secs) else n

        o = suf_start
        while o < suf_end:
            if o + 4 > suf_end:
                break
            sfx_len = u32(o)
            if sfx_len > 250 or o + 4 + sfx_len + 8 > suf_end:
                break
            suffix = d[o + 4:o + 4 + sfx_len]
            str_offset = u32(o + 4 + sfx_len)
            packed = u32(o + 8 + sfx_len)
            o += 12 + sfx_len

            flag = packed >> 24
            val_len = packed & 0xFFFFFF
            full_tag = prefix + suffix.decode('latin1')
            stats['entries'] += 1
            if flag != 0x00:            # only 0x00 is literal UTF-8; 0x02 (+rare
                stats['compressed'] += 1   # 0x01/0x03/0x64/0x70) are non-literal
                continue
            raw = d[str_base + str_offset - 4: str_base + str_offset - 4 + val_len]
            z = raw.find(b'\x00')
            if z >= 0:
                raw = raw[:z]
            try:
                out[full_tag] = raw.decode('utf-8')
            except UnicodeDecodeError:
                out[full_tag] = raw.decode('latin1')
            stats['plaintext'] += 1
    return out, stats


# ---------------------------------------------------------------- Approach-B measurement
def measure_framing(sample_tags=None):
    """Locate known (suffix,value) pairs and print the measured byte framing.

    This is the empirical derivation that produced the parser above.
    """
    with open(DEC_PATH, 'rb') as fh:
        d = fh.read()
    gt = json.load(open(GT_PATH, encoding='utf-8'))
    u32 = lambda o: struct.unpack_from('<I', d, o)[0]

    if sample_tags is None:
        sample_tags = [
            '/Lotus/Language/1999/1999HubName',
            '/Lotus/Language/1999/AmirKissTitle',
            '/Lotus/Language/1999/10000HollarsName',
            '/Lotus/Language/Weapons/KuvaGrnBowName',
            '/Lotus/Language/Weapons/CarminePentaBarrel',
        ]

    secs = _section_starts(d)
    # map prefix -> (str_base)
    prefix_base = {}
    n = len(d)
    for si, (hoff, hsz, prefix_b) in enumerate(secs):
        p = hoff + 4 + hsz
        prefix_base[prefix_b.decode('utf-8', 'replace')] = p + 8

    print('=== Approach B: measured byte framing of known pairs ===')
    for tag in sample_tags:
        val = gt.get(tag)
        if val is None:
            print(tag, '(not in dict)'); continue
        # split prefix/suffix at last '/'
        cut = tag.rfind('/') + 1
        prefix, suffix = tag[:cut], tag[cut:].encode('latin1')
        # find the suffix entry: search for [u32 sfxLen][suffix] then read strOffset/packed
        pat = struct.pack('<I', len(suffix)) + suffix
        idx = d.find(pat)
        if idx < 0:
            print(tag, 'suffix not found'); continue
        str_offset = u32(idx + 4 + len(suffix))
        packed = u32(idx + 8 + len(suffix))
        flag, val_len = packed >> 24, packed & 0xFFFFFF
        str_base = prefix_base.get(prefix)
        body_off = str_base + str_offset - (4 if flag == 0 else 0)
        raw = d[body_off: body_off + val_len]
        shown = raw.split(b'\x00', 1)[0] if flag == 0 else raw
        kind = 'PLAINTEXT' if flag == 0 else 'COMPRESSED'
        print(f'\n{tag}')
        print(f'  suffix entry @ {idx-4}: [sfxLen={len(suffix)}][suffix]'
              f'[strOffset={str_offset}][packed=0x{packed:08x} -> flag=0x{flag:02x} valLen={val_len}] ({kind})')
        if flag == 0:
            print(f'  body @ strBase+strOffset-4 = {body_off}; read {val_len}, cut at NUL')
            print(f'  decoded = {shown.decode("utf-8","replace")!r}')
            print(f'  expected= {val!r}   MATCH={shown.decode("utf-8","replace")==val}')
        else:
            print(f'  body @ strBase+strOffset = {body_off}; {val_len} LZ bytes: {raw[:16].hex()}...')
            print(f'  expected (uncompressed) = {val[:48]!r}  (LZ decode not implemented)')


def main():
    measure_framing()
    result, stats = parse()
    print('\n=== generalised parse ===')
    print('sections parsed        :', stats['sections'])
    print('total entries          :', stats['entries'])
    print('plaintext decoded      :', stats['plaintext'])
    print('DE-compressed (skipped):', stats['compressed'])

    gb = '/Lotus/Language/Weapons/KuvaGrnBowName'
    print('GrnBow check: %r -> %r' % (gb, result.get(gb)))

    with open(GT_PATH, encoding='utf-8') as fh:
        gt = json.load(fh)
    ok = sum(1 for k, v in gt.items() if result.get(k) == v)
    present = sum(1 for k in gt if k in result)
    print('overall match rate     : %.4f  (%d/%d)' % (ok / len(gt), ok, len(gt)))
    print('match rate of decoded  : %.4f  (%d/%d)' % (ok / present, ok, present))


if __name__ == '__main__':
    main()
