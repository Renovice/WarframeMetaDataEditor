#!/usr/bin/env python3
"""Full OFFLINE editor dataset — the practical 'full decoder'.

Combines every offline source so the editor has maximal per-item metadata with NO game:
  - data/dumps/**        : 2536 raw get_effective_metadata composed Field=value (FULL fields,
                           incl. the trimmed ones the public export drops: Attenuation, AmmoCapacity, Incarnon...)
  - warframe-public-export-plus/Export*.json : ALL 2746 gameplay items + common editable stats
                           (fills the 210 items with no full dump, esp. all 125 Warframes)
  - offline/records_full.json / inherit_from_records.json : 462k-type inheritance graph
Output: data/editor_dataset.json  = { path: { source, fields{...}, raw[...], base } }
"""
import os, json, glob, re
from pathlib import Path

HERE = Path(__file__).parent
DUMPS = HERE.parent / "data" / "dumps"
EXP = Path("C:/Users/Bartek/OneDrive/Dokumenter/OpenWF Server 08.07.2026/SpaceNinjaServer/node_modules/warframe-public-export-plus")
OUT = HERE.parent / "data" / "editor_dataset.json"

def load_export(name):
    p = EXP / (name + ".json")
    if not p.exists(): return {}
    d = json.load(open(p, encoding="utf-8"))
    return d if isinstance(d, dict) else {}

def parse_dump_fields(lines):
    fields, depth = {}, 0
    for raw in lines:
        s = raw.strip()
        if depth == 0 and re.match(r"^[A-Za-z_][\w:]*=", s):
            k, v = s.split("=", 1); fields.setdefault(k, v)
        depth += raw.count("{") - raw.count("}")
        if depth < 0: depth = 0
    return fields

def main():
    # 1) dumps (full raw)
    dumps = {}
    for f in glob.glob(str(DUMPS / "**" / "*"), recursive=True):
        if not os.path.isfile(f): continue
        try: t = open(f, encoding="utf-8", errors="replace").read()
        except: continue
        lines = t.splitlines()
        if not lines or not lines[0].startswith(">"): continue
        dumps[lines[0][1:].strip()] = lines[1:]

    # 2) public export (all gameplay items)
    exports = {n: load_export(n) for n in
               ["ExportWeapons","ExportUpgrades","ExportWarframes","ExportArcanes",
                "ExportRecipes","ExportGear","ExportSentinels","ExportAbilities"]}
    export_by_path = {}
    for name, d in exports.items():
        cat = name.replace("Export","")
        for path, e in d.items():
            if isinstance(e, dict):
                export_by_path.setdefault(path, {})["_category"] = cat
                export_by_path[path].update({k:v for k,v in e.items()})

    # 3) inheritance
    inh = {}
    try: inh = json.load(open(HERE/"inherit_from_records.json"))
    except: pass

    # 4) merge — dump wins (full), export fills
    ds = {}
    all_paths = set(dumps) | set(export_by_path)
    full = common = 0
    for p in all_paths:
        entry = {"path": p, "base": inh.get(p.rsplit("/",1)[-1])}
        if p in dumps:
            entry["source"] = "dump_full"
            entry["fields"] = parse_dump_fields(dumps[p])
            entry["raw"] = dumps[p]
            if p in export_by_path:  # attach export stats too (typed/normalized)
                entry["export"] = export_by_path[p]
            full += 1
        else:
            entry["source"] = "export_only"
            entry["fields"] = {k:v for k,v in export_by_path[p].items()}
            entry["export"] = export_by_path[p]
            common += 1
        ds[p] = entry

    json.dump(ds, open(OUT, "w"), ensure_ascii=False)
    # report
    cats = {}
    for p,e in ds.items():
        c = (e.get("export") or {}).get("_category") or "other"
        cats.setdefault(c, [0,0])
        cats[c][0 if e["source"]=="dump_full" else 1] += 1
    print(f"editor_dataset.json written: {len(ds)} items")
    print(f"  full raw (dumps, all fields): {full}")
    print(f"  export-only (common stats):   {common}")
    print("  by category [full / export-only]:")
    for c,(a,b) in sorted(cats.items()): print(f"    {c:14} {a:5} / {b:5}")
    print(f"  output: {OUT}")

if __name__ == "__main__":
    main()
