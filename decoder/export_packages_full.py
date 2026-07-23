#!/usr/bin/env python3
"""Export the COMPLETE offline metadata from Packages.bin (v46) using the validated decoder.
Produces:
  data/packages_owntext.jsonl   -- every type's raw decoded OWN text (path, parent, ownText)
  data/packages_effective.jsonl -- composed (own + inherited top-level fields) for every type
                                    == offline get_effective_metadata replacement, no game.
"""
import json, os, re, importlib.util

HERE = os.path.dirname(os.path.abspath(__file__))
spec = importlib.util.spec_from_file_location("v46", os.path.join(HERE, "pkg_decode_v46.py"))
v46 = importlib.util.module_from_spec(spec); spec.loader.exec_module(v46)

DATA = os.path.join(os.path.dirname(HERE), "data")
OWN = os.path.join(DATA, "packages_owntext.jsonl")
EFF = os.path.join(DATA, "packages_effective.jsonl")

TOP = re.compile(r'^([A-Za-z_][A-Za-z0-9_:]*)=(.*)$')

def top_fields(text):
    """top-level key=value lines (depth 0 only), preserving first occurrence."""
    fields = {}; depth = 0
    for line in text.split('\n'):
        s = line.rstrip('\r')
        if depth == 0:
            m = TOP.match(s)
            if m and m.group(1) not in fields:
                fields[m.group(1)] = m.group(2)
        depth += s.count('{') - s.count('}')
        if depth < 0: depth = 0
    return fields

def main():
    data = open(v46.PATH, 'rb').read()
    P = v46.load_buffers(data)
    entities, stats = v46.decode_all(data, P)
    print("decode stats:", stats)
    assert stats['aligned'], "frame misalignment!"

    # 1) raw own-text
    n_own = 0
    with open(OWN, 'w', encoding='utf-8') as f:
        for path, e in entities.items():
            if e['ownText'] is not None:
                f.write(json.dumps({"path": path, "parent": e['parent'], "ownText": e['ownText']}, ensure_ascii=False) + "\n")
                n_own += 1
    print(f"wrote {n_own} own-text entities -> {OWN}")

    # 2) composed effective (own top-level fields unioned up the parent chain, child wins)
    own_fields = {}
    for path, e in entities.items():
        own_fields[path] = top_fields(e['ownText']) if e['ownText'] else {}

    cache = {}
    def compose(path, seen=None):
        if path in cache: return cache[path]
        if seen is None: seen = set()
        if path in seen: return {}
        seen.add(path)
        eff = {}
        parent = entities.get(path, {}).get('parent')
        if parent and parent in entities:
            eff.update(compose(parent, seen))
        eff.update(own_fields.get(path, {}))     # child overrides parent
        cache[path] = eff
        return eff

    n_eff = 0
    with open(EFF, 'w', encoding='utf-8') as f:
        for path in entities:
            eff = compose(path)
            if eff:
                f.write(json.dumps({"path": path, "fields": eff}, ensure_ascii=False) + "\n")
                n_eff += 1
    print(f"wrote {n_eff} composed-effective entities -> {EFF}")

if __name__ == "__main__":
    main()
