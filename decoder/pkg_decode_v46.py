#!/usr/bin/env python3
"""
pkg_decode_v46.py -- COMPLETE v46 Packages.bin decoder + metadata composer + validator.

Mechanism (authoritative: LotusLib PackagesBin.cpp; v46 offsets located by loc_A/B/C.py):
  Packages.bin per-type property text is ZSTD-compressed against an EMBEDDED ZSTD
  DICTIONARY that lives inline at the front of the comZ buffer. Frames are MAGICLESS
  (LotusLib sets ZSTD_d_format = magicless). Each has-text entity gets one frame,
  consumed POSITIONALLY in entity-table iteration order, gated by:
    - comFlags: LSB-first bitstream, 1 hasText bit per entity + 1 isCompressed bit
                per has-text entity  (order: ht0,[ic0],ht1,[ic1],...).
    - comSize:  ULEB frame-byte-length per has-text entity (first u32 = dictSize).
    - comZ:     [dict(dictSize bytes)][frames]. Each frame = [ULEB declen][magicless zstd].

VERIFIED v46 layout (C:/Users/Bartek/AppData/Local/Temp/Packages.bin.dec, 52,710,200 B):
  header @16=20(hdrSize) @20=46(version) @24=1(flags); 50 refs [272:2320].
  comFlagsLen u32 @3154  = 82918     -> comFlags = [3158 : 86076]
  comSizeLen  u32 @86094 = 237009    -> comSize  = [86098 : 323107]  (comSize[0:4]=dictSize=1048576)
  comZLen     u32 @323513= 20770500  -> comZ     = [323517: 21094017]
  dict   = comZ[0:1048576]  (starts 0xEC30A437 ZSTD_MAGIC_DICTIONARY -> full dict)
  frames = comZ[1048576:]   (magicless zstd, entity-iteration order)
  entityCount u32 @21094017 = 461789 ; entity dir-block table @21094036 : EOF

Emits {fullPath: {parent, ownText}}, then COMPOSES full metadata per type by
UNION of ownText up the parent chain (child field overrides ancestor field).
Validates against ground-truth dumps: overall line-recall + distinctive-numeric recall.
"""
import struct, sys, os, re, random, glob, json
import zstandard

PATH = r'C:/Users/Bartek/AppData/Local/Temp/Packages.bin.dec'
DUMPS = r'C:/Users/Bartek/OneDrive/Dokumenter/Warframe RE PROJECT RENOVICE/WarframeMetaDataEditor/data/dumps'
GRNBOW = '/Lotus/Weapons/Grineer/Bows/GrnBow/GrnBowWeapon'

# ---- v46 buffer-header offsets (located + verified by loc_A/B/C) ----
COMFLAGS_LEN_OFF = 3154
COMSIZE_LEN_OFF  = 86094
COMZ_LEN_OFF     = 323513
ENTITY_TABLE_OFF = 21094036   # dir-block table start (15-byte gap after entityCount u32)

DISTINCTIVE = ['reloadTime', 'fireRate', 'AmmoCapacity', 'AmmoClipSize', 'OmegaAttenuation']


def u32(d, o):
    return struct.unpack_from('<I', d, o)[0]


def uleb(buf, o):
    r = 0; sh = 0
    while True:
        b = buf[o]; o += 1
        r |= (b & 0x7f) << sh
        if not (b & 0x80):
            break
        sh += 7
    return r, o


class BitReader:
    """LSB-first bit reader (matches LotusLib comFlags iteration)."""
    __slots__ = ('buf', 'byte', 'bit', 'pos')

    def __init__(self, buf):
        self.buf = buf
        self.byte = buf[0] if buf else 0
        self.bit = 0
        self.pos = 0

    def read(self):
        v = (self.byte >> self.bit) & 1
        self.bit += 1
        if self.bit > 7:
            self.pos += 1
            self.byte = self.buf[self.pos] if self.pos < len(self.buf) else 0
            self.bit -= 8
        return v


def load_buffers(data):
    assert u32(data, 16) == 20 and u32(data, 20) == 46 and u32(data, 24) == 1, 'not a v46 Packages.bin.dec'

    cf_len = u32(data, COMFLAGS_LEN_OFF)
    comFlags = data[COMFLAGS_LEN_OFF + 4: COMFLAGS_LEN_OFF + 4 + cf_len]

    cs_len = u32(data, COMSIZE_LEN_OFF)
    comSize = data[COMSIZE_LEN_OFF + 4: COMSIZE_LEN_OFF + 4 + cs_len]

    cz_len = u32(data, COMZ_LEN_OFF)
    cz_start = COMZ_LEN_OFF + 4
    comZ = data[cz_start: cz_start + cz_len]

    dictSize = u32(comSize, 0)                 # comSize[0:4] = dictSize
    dict_bytes = comZ[:dictSize]
    frames = comZ[dictSize:]
    entity_off = cz_start + cz_len             # 21094017
    entity_count = u32(data, entity_off)       # 461789

    return {
        'comFlags': comFlags, 'comSize': comSize, 'comZ': comZ,
        'dictSize': dictSize, 'dict_bytes': dict_bytes, 'frames': frames,
        'entity_off': entity_off, 'entity_count': entity_count,
        'cf_len': cf_len, 'cs_len': cs_len, 'cz_len': cz_len,
    }


def decode_all(data, P):
    """Walk the v46 dir-block entity table in order, consuming comFlags/comSize/comZ
    positionally. Returns (entities, stats).
      entities[fullPath] = {'parent': str, 'ownText': str or None}
    """
    entity_count = P['entity_count']
    o = ENTITY_TABLE_OFF

    flags = BitReader(P['comFlags'])
    comSize = P['comSize']
    size_pos = 4                               # skip leading dictSize u32
    frames = P['frames']
    frame_pos = 0

    dctx = zstandard.ZstdDecompressor(
        dict_data=zstandard.ZstdCompressionDict(P['dict_bytes'], dict_type=zstandard.DICT_TYPE_FULLDICT),
        format=zstandard.FORMAT_ZSTD1_MAGICLESS)

    entities = {}
    seen = 0
    n_text = n_comp = n_raw = n_notext = 0
    n = len(data)
    while seen < entity_count and o < n:
        dirLen = u32(data, o); o += 4
        directory = data[o:o + dirLen].decode('latin1'); o += dirLen
        o += 1                                 # 0x01 dir-block marker
        typeCount = u32(data, o); o += 4
        for _ in range(typeCount):
            nameLen = u32(data, o); o += 4
            name = data[o:o + nameLen].decode('latin1'); o += nameLen
            o += 3                             # 3 flag bytes
            baseLen = u32(data, o); o += 4
            base = data[o:o + baseLen].decode('latin1'); o += baseLen

            full = directory + name
            parent = base if (base and base[0] == '/') else (directory + base if base else '')

            hasText = flags.read()
            ownText = None
            if hasText:
                n_text += 1
                size, size_pos = uleb(comSize, size_pos)
                frame = frames[frame_pos: frame_pos + size]
                frame_pos += size
                isComp = flags.read()
                if isComp:
                    n_comp += 1
                    declen, fo = uleb(frame, 0)
                    ownText = dctx.decompress(frame[fo:], max_output_size=declen).decode('latin1')
                else:
                    n_raw += 1
                    ownText = frame.decode('latin1')
            else:
                n_notext += 1

            entities[full] = {'parent': parent, 'ownText': ownText}
            seen += 1
            if seen >= entity_count:
                break

    stats = {
        'entities': seen, 'text': n_text, 'compressed': n_comp, 'raw': n_raw,
        'no_text': n_notext, 'frame_end': frame_pos, 'frames_total': len(frames),
        'aligned': frame_pos == len(frames),
    }
    return entities, stats


# ---------------- metadata composition (child overrides parent) ----------------

_TOP_FIELD = re.compile(r'^([A-Za-z_][A-Za-z0-9_]*)=')


def parse_top_fields(text):
    """Return ordered dict {field: value_line_string} of TOP-LEVEL Field=... entries.

    A top-level field starts at column 0 (no leading whitespace) with `Name=`.
    Its value may span multiple lines (a `{ ... }` block); we capture the whole
    span by tracking brace depth so nested Field= lines are NOT treated as top-level.
    """
    fields = {}
    lines = text.split('\n')
    i = 0
    N = len(lines)
    while i < N:
        line = lines[i]
        m = _TOP_FIELD.match(line)
        if not m and (line == '' or line.startswith(' ') or line.startswith('\t')):
            i += 1
            continue
        if not m:
            i += 1
            continue
        name = m.group(1)
        depth = line.count('{') - line.count('}')
        block = [line]
        i += 1
        while depth > 0 and i < N:
            block.append(lines[i])
            depth += lines[i].count('{') - lines[i].count('}')
            i += 1
        fields[name] = '\n'.join(block)
    return fields


def compose(full, entities, cache=None, _stack=None):
    """Compose full metadata field-map for `full` = ancestor chain union, child wins.
    Returns ordered dict {field: value_line}. Root-most parent applied first, then
    each descendant overrides. Guards against cycles / missing parents.
    """
    if cache is None:
        cache = {}
    if full in cache:
        return cache[full]
    if _stack is None:
        _stack = set()
    if full in _stack:
        return {}
    e = entities.get(full)
    if e is None:
        return {}
    _stack.add(full)
    parent = e['parent']
    if parent and parent in entities and parent != full:
        merged = dict(compose(parent, entities, cache, _stack))
    else:
        merged = {}
    if e['ownText']:
        for k, v in parse_top_fields(e['ownText']).items():
            merged[k] = v          # child overrides ancestor
    _stack.discard(full)
    cache[full] = merged
    return merged


# ---------------- validation against ground-truth dumps ----------------

def load_dump(path):
    """Return (fullPath, list_of_field_lines_or_blocks, {distinctive_field: value})."""
    with open(path, 'r', encoding='latin1') as f:
        raw = f.read()
    lines = raw.split('\n')
    if not lines or not lines[0].startswith('>'):
        return None, None, None
    full = lines[0][1:].strip()
    body = '\n'.join(lines[1:])
    fields = parse_top_fields(body)
    distinct = {}
    for fld in DISTINCTIVE:
        if fld in fields:
            v = fields[fld]
            # only a single scalar line, e.g. "reloadTime=0.6"
            if '\n' not in v:
                distinct[fld] = v.split('=', 1)[1]
    return full, fields, distinct


def norm_val(s):
    """Normalize a scalar value for tolerant numeric comparison (2 vs 2.0 etc.)."""
    s = s.strip()
    try:
        f = float(s)
        # canonical float form
        if f == int(f):
            return str(int(f))
        return '%.6g' % f
    except ValueError:
        return s


def validate(entities, sample_paths, cache):
    line_hit = line_tot = 0
    dnum_hit = dnum_tot = 0
    per = []
    for dp in sample_paths:
        full, dump_fields, dump_distinct = load_dump(dp)
        if full is None:
            continue
        composed = compose(full, entities, cache)
        # ----- overall line recall: each top-level dump field present & matching -----
        d_hit = 0
        d_tot = 0
        missing = []
        for k, v in dump_fields.items():
            d_tot += 1
            cv = composed.get(k)
            if cv is not None and norm_multiline(cv) == norm_multiline(v):
                d_hit += 1
            elif cv is not None and '\n' not in v and '\n' not in cv and norm_val(v.split('=', 1)[1] if '=' in v else v) == norm_val(cv.split('=', 1)[1] if '=' in cv else cv):
                d_hit += 1
            else:
                missing.append(k)
        line_hit += d_hit
        line_tot += d_tot
        # ----- distinctive numeric recall -----
        dn_hit = 0
        dn_tot = 0
        dn_detail = {}
        for fld, dv in dump_distinct.items():
            dn_tot += 1
            cv = composed.get(fld)
            got = cv.split('=', 1)[1] if (cv and '\n' not in cv and '=' in cv) else None
            ok = got is not None and norm_val(got) == norm_val(dv)
            if ok:
                dn_hit += 1
            dn_detail[fld] = {'dump': dv, 'decoded': got, 'ok': ok}
        dnum_hit += dn_hit
        dnum_tot += dn_tot
        per.append({
            'path': full, 'dump_file': os.path.basename(dp),
            'line_recall': (d_hit, d_tot),
            'distinctive': dn_detail,
            'missing_sample': missing[:6],
        })
    overall_line = line_hit / line_tot if line_tot else 0.0
    overall_dnum = dnum_hit / dnum_tot if dnum_tot else 0.0
    return overall_line, overall_dnum, per, (line_hit, line_tot), (dnum_hit, dnum_tot)


def norm_multiline(block):
    """Normalize a field block for CONTENT comparison, ignoring pretty-printing.

    Ground-truth dumps indent nested blocks with 4-space steps; the raw decoded
    text carries no indentation. Both are the SAME content. So we compare after:
      - stripping leading/trailing whitespace on every line (kills the indent delta)
      - dropping blank lines
      - normalizing scalar numbers on `k=v` lines (2 vs 2.0, float rounding)
    """
    out = []
    for ln in block.split('\n'):
        s = ln.strip()
        if s == '':
            continue
        if '=' in s and '{' not in s and s.count('=') == 1:
            k, v = s.split('=', 1)
            out.append(k + '=' + norm_val(v))
        else:
            out.append(s)
    return '\n'.join(out)


def main():
    print('[*] reading', PATH)
    with open(PATH, 'rb') as f:
        data = f.read()
    P = load_buffers(data)
    print('[*] buffers: comFlags=%d comSize=%d comZ=%d dictSize=%d entityCount=%d' % (
        P['cf_len'], P['cs_len'], P['cz_len'], P['dictSize'], P['entity_count']))

    print('[*] decoding all entities (magicless zstd + embedded full dict)...')
    entities, stats = decode_all(data, P)
    print('[*] decoded: entities=%d text=%d compressed=%d raw=%d no_text=%d' % (
        stats['entities'], stats['text'], stats['compressed'], stats['raw'], stats['no_text']))
    print('[*] frame alignment: consumed=%d total=%d aligned=%s' % (
        stats['frame_end'], stats['frames_total'], stats['aligned']))

    cache = {}

    # ---- GrnBowWeapon decoded OWN text ----
    print('\n' + '=' * 70)
    print('GrnBowWeapon decoded OWN text (%s):' % GRNBOW)
    print('=' * 70)
    g = entities.get(GRNBOW)
    if g:
        print('parent:', g['parent'])
        print(g['ownText'])
    else:
        print('NOT FOUND')

    # ---- validate 30 random dumps ----
    all_dumps = glob.glob(os.path.join(DUMPS, '**', '*.txt'), recursive=True)
    all_dumps = [d for d in all_dumps if os.path.basename(d) not in ('errors.txt',)]
    random.seed(42)
    sample = random.sample(all_dumps, min(30, len(all_dumps)))
    # ensure GrnBow dump is in the sample for a distinctive proof
    kb = [d for d in all_dumps if os.path.basename(d) == 'Kuva Bramma.txt']
    if kb and kb[0] not in sample:
        sample[0] = kb[0]

    ol, od, per, (lh, lt), (dh, dt) = validate(entities, sample, cache)

    print('\n' + '=' * 70)
    print('VALIDATION on %d random dumps' % len(sample))
    print('=' * 70)
    print('OVERALL line recall     : %.4f  (%d/%d top-level fields matched)' % (ol, lh, lt))
    print('DISTINCTIVE num recall  : %.4f  (%d/%d reloadTime/fireRate/AmmoCapacity/AmmoClipSize/OmegaAttenuation)' % (od, dh, dt))
    print('-' * 70)
    for r in per:
        dl = r['distinctive']
        dstr = ' '.join('%s=%s%s' % (k, v['decoded'], '' if v['ok'] else ('!=%s' % v['dump'])) for k, v in dl.items())
        print('%-52s line %d/%d  %s' % (
            r['dump_file'][:52], r['line_recall'][0], r['line_recall'][1], dstr))

    # persist decoded corpus index + composed sample for downstream tooling
    outdir = os.path.dirname(os.path.abspath(__file__))
    with open(os.path.join(outdir, 'grnbow_decoded_own.txt'), 'w', encoding='latin1') as f:
        f.write(entities.get(GRNBOW, {}).get('ownText') or '')

    return ol, od


if __name__ == '__main__':
    main()
