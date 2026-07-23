#!/usr/bin/env python3
"""Generate editor dump-format files from the OFFLINE decode, for player items
(public export) not already dumped in-game. Non-destructive: never overwrites an
existing dump (matched by the type PATH). Files: data/dumps/<Category>/<Name>.txt
  line 1: >/Lotus/....(full path)
  then:   composed Field=value / Field={block}  (own + inherited, child overrides)
"""
import json, os, re, glob

HERE = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.dirname(HERE)
DUMPS = os.path.join(ROOT, "data", "dumps")
OWN = os.path.join(ROOT, "data", "packages_owntext.jsonl")
EXP = r"C:/Users/Bartek/OneDrive/Dokumenter/OpenWF Server 08.07.2026/SpaceNinjaServer/node_modules/warframe-public-export-plus"

CATS = [("Weapons", "ExportWeapons"), ("Mods", "ExportUpgrades"), ("Arcanes", "ExportArcanes")]
INVALID = re.compile(r'[<>:"/\\|?*\r\n\t]')

def sanitize(n):
    return INVALID.sub(" ", n).strip().rstrip(".") or "unnamed"

# ---- top-level field extraction (scalars + whole blocks), brace-balance tracked ----
def top_fields(text):
    fields = {}          # key -> full field text (incl. key)
    if not text: return fields
    lines = text.split("\n")
    i = 0
    while i < len(lines):
        raw = lines[i].rstrip("\r")
        s = raw.strip()
        if not s or s.startswith(">"):
            i += 1; continue
        eq = s.find("=")
        if eq <= 0:
            i += 1; continue
        key = s[:eq]; val = s[eq+1:]
        net = val.count("{") - val.count("}")
        if net <= 0:
            fields[key] = key + "=" + val
            i += 1
        else:
            buf = [raw]; depth = net; i += 1
            while i < len(lines) and depth > 0:
                r = lines[i].rstrip("\r"); buf.append(r)
                depth += r.count("{") - r.count("}"); i += 1
            fields[key] = "\n".join(buf)
    return fields

def main():
    # 1) decode: path -> (parent, ownText)
    dec = {}
    for line in open(OWN, encoding="utf-8"):
        o = json.loads(line); dec[o["path"]] = (o.get("parent"), o.get("ownText"))
    print(f"decode loaded: {len(dec)} own-text types")

    # 2) friendly names + export item lists
    dic = {}
    dp = os.path.join(EXP, "dict.en.json")
    if os.path.exists(dp): dic = json.load(open(dp, encoding="utf-8"))
    def load(name):
        p = os.path.join(EXP, name + ".json")
        return json.load(open(p, encoding="utf-8")) if os.path.exists(p) else {}

    # 3) existing coverage: set of paths already dumped
    covered = set()
    for f in glob.glob(os.path.join(DUMPS, "**", "*.txt"), recursive=True):
        try:
            with open(f, encoding="utf-8", errors="replace") as fh:
                first = fh.readline()
            if first.startswith(">"): covered.add(first[1:].strip())
        except: pass
    print(f"already-dumped paths: {len(covered)}")

    # 4) compose + write
    def compose(path):
        chain, seen, p = [], set(), path
        while p and p in dec and p not in seen:
            seen.add(p); chain.append(p); p = dec[p][0]
        merged = {}
        for pp in reversed(chain):                       # root -> self, self overrides
            merged.update(top_fields(dec[pp][1]))
        return merged

    total = 0
    for cat, expname in CATS:
        exp = load(expname)
        folder = os.path.join(DUMPS, cat); os.makedirs(folder, exist_ok=True)
        existing_names = {os.path.splitext(os.path.basename(f))[0].lower()
                          for f in glob.glob(os.path.join(folder, "*.txt"))}
        written = skipped_covered = skipped_nodecode = 0
        for path, e in exp.items():
            if path in covered: skipped_covered += 1; continue
            if path not in dec: skipped_nodecode += 1; continue
            fields = compose(path)
            if not fields: continue
            leaf = path.rsplit("/", 1)[-1]
            nm = e.get("name") if isinstance(e, dict) else None
            friendly = sanitize(dic.get(nm, nm) if nm else leaf) or leaf
            name = friendly
            if name.lower() in existing_names: name = sanitize(f"{friendly} ({leaf})")
            existing_names.add(name.lower())
            body = ">" + path + "\n" + "\n".join(fields.values()) + "\n"
            open(os.path.join(folder, name + ".txt"), "w", encoding="utf-8").write(body)
            written += 1
        total += written
        print(f"[{cat}] wrote {written} new (skipped {skipped_covered} already-dumped, {skipped_nodecode} not-in-decode); export items={len(exp)}")
    print(f"\nTOTAL new decoded dump files: {total}  ->  {DUMPS}")

if __name__ == "__main__":
    main()
