#!/usr/bin/env python3
# Offline metadata extractor for Warframe Cache.Windows.
# TOC tree per LotusLib: parentDirIndex[28-31] indexes the DIRECTORY-ONLY array
# (m_dirs, synthetic root at 0). Decompression per LotusLib: big-endian 0x80 block
# format (same as SHCC) -> reuse wf_content.py's Oodle path.
import struct, sys
sys.setrecursionlimit(2_000_000)

WF_DIR = r"C:\Users\Bartek\OneDrive\Dokumenter\Warframe RE PROJECT RENOVICE\Patched BootStrapper\rebuild-2026.07.11"
sys.path.insert(0, WF_DIR)
import wf_content as wf  # noqa: E402

CW = r"C:\Users\Bartek\OneDrive\Dokumenter\Warframe\Cache.Windows"

def parse_toc(toc_path):
    data = open(toc_path, "rb").read()
    magic, ver = struct.unpack_from("<II", data, 0)
    if magic != 0x1867C64E:
        raise SystemExit(f"bad magic {magic:#x}")
    n = (len(data) - 8) // 96
    dir_name = [""]; dir_parent = [0]      # m_dirs[0] = synthetic root
    raw = []
    for i in range(n):
        b = 8 + i * 96
        off, ts = struct.unpack_from("<qq", data, b)
        comp, dec, _res, par = struct.unpack_from("<iiii", data, b + 16)
        name = data[b + 32:b + 96].split(b"\x00", 1)[0].decode("utf-8", "replace")
        raw.append((off, ts, comp, dec, par, name))
        if off == -1:                      # directory -> append to m_dirs
            dir_name.append(name); dir_parent.append(par)
    # resolve directory full paths (parentDirIndex indexes m_dirs; may forward-ref)
    dpath = [None] * len(dir_name); dpath[0] = ""
    def build(k):
        if dpath[k] is not None:
            return dpath[k]
        p = dir_parent[k]
        dpath[k] = ("/" + dir_name[k]) if (p < 0 or p >= len(dir_name) or p == k) else build(p) + "/" + dir_name[k]
        return dpath[k]
    for k in range(len(dir_name)):
        build(k)
    # file entries: path = parentDir path + "/" + name  (include dupes: ts==0)
    files = {}
    for (off, ts, comp, dec, par, name) in raw:
        if off >= 0 and 0 <= par < len(dpath):
            files[dpath[par] + "/" + name] = (off, comp, dec)
    return ver, files

def decompress_entry(cache_path, off, comp, dec):
    with open(cache_path, "rb") as f:
        f.seek(off); blob = f.read(comp)
    if comp == dec:
        return blob                                    # stored uncompressed
    if len(blob) >= 8 and blob[0] == 0x80 and (blob[7] & 0x0F) == 0x01:
        return wf.decompress_oodle_chunk(blob, 0, dec)  # BE 0x80 block loop + Oodle
    if blob and blob[0] == 0x8C:
        return wf.oodle_decompress(blob, dec)           # single unwrapped Oodle block
    return blob                                         # fallback: raw

def classify(raw):
    printable = sum(1 for b in raw[:400] if 9 <= b <= 13 or 32 <= b <= 126)
    return "TEXT" if printable > 340 else "BINARY"

if __name__ == "__main__" and "--grep" in sys.argv:
    import time
    KEYWORDS = [b"AmmoClipSize", b"OperationType", b"AddChargeConditions", b"MasteryReq",
                b"NumChargesToAdd", b"UpgradeType=WEAPON", b"Behaviors="]
    PREFIXES = ("/Lotus/Weapons/", "/Lotus/Powersuits/", "/Lotus/Upgrades/", "/Lotus/Types/")
    for pkg in ("H.Misc", "B.Misc", "F.Misc"):
        ver, files = parse_toc(f"{CW}\\{pkg}.toc")
        cand = [p for p in files if p.startswith(PREFIXES) and "." not in p.rsplit("/", 1)[-1]]
        print(f"{pkg}: {len(cand)} candidate no-ext gameplay entries; decompressing + grepping...")
        hits = 0; done = 0; t0 = time.time()
        for p in cand:
            off, comp, dec = files[p]
            if dec > 300_000:      # skip huge (metadata is small)
                continue
            try:
                raw = decompress_entry(f"{CW}\\{pkg}.cache", off, comp, dec)
            except Exception:
                continue
            done += 1
            for kw in KEYWORDS:
                if kw in raw:
                    hits += 1
                    print(f"  HIT {kw.decode()} in {p}")
                    break
            if time.time() - t0 > 90:
                print(f"  (time cap after {done} decompressions)"); break
        print(f"  {pkg}: decompressed {done}, keyword hits {hits}")
    sys.exit(0)

if __name__ == "__main__" and "--probe" in sys.argv:
    # decompress a handful of real entries and see if any are Field=value text
    ver, fH = parse_toc(f"{CW}\\H.Misc.toc")
    picks = ["/Lotus/Powersuits/Rhino/IronBody",
             "/Lotus/Powersuits/Rhino/Rhino",
             "/Lotus/Weapons/Bow/EnergyArrowPBR"]
    picks += [p for p in fH if p.startswith("/Lotus/Powersuits/") and "." not in p.rsplit("/",1)[-1]][:3]
    for p in picks:
        if p not in fH:
            print(f"{p}: NOT PRESENT"); continue
        off, comp, dec = fH[p]
        try:
            raw = decompress_entry(f"{CW}\\H.Misc.cache", off, comp, dec)
            print(f"\n{p}  ({classify(raw)}, {len(raw)}B)")
            print("  " + repr(raw[:220]))
        except Exception as e:
            print(f"{p}: decompress err {e}")
    sys.exit(0)

if __name__ == "__main__":
    import glob, os
    targets = ["/Lotus/Weapons/Tenno/Rifle/Rifle", "/Lotus/Powersuits/Excalibur/Excalibur",
               "/Lotus/Upgrades/Mods/Rifle/WeaponDamageAmountMod"]
    scanned = 0; total_files = 0; any_found = 0
    for toc in sorted(glob.glob(f"{CW}\\*.toc")):
        pkg = os.path.basename(toc)[:-4]
        if os.path.getsize(toc) < 2000:
            continue
        try:
            ver, files = parse_toc(toc)
        except Exception as e:
            print(f"{pkg}: parse err {e}"); continue
        scanned += 1; total_files += len(files)
        found = [t for t in targets if t in files]
        any_found += len(found)
        if found:
            print(f"*** {pkg}: HAS {found}")
            t = found[0]; off, comp, dec = files[t]
            print(f"    {t}  off={off} comp={comp} dec={dec}")
            try:
                txt = decompress_entry(f"{CW}\\{pkg}.cache", off, comp, dec).decode("utf-8", "replace")
                print("    head:", repr(txt[:200]))
            except Exception as e:
                print("    decompress err:", e)
    print(f"\nscanned {scanned} packages, {total_files} total file entries, targets found: {any_found}")
    # also: how many /Lotus/Weapons and /Lotus/Powersuits *type-ish* (no-ext leaf) entries exist total?
    ver, fB = parse_toc(f"{CW}\\B.Misc.toc")
    ver, fH = parse_toc(f"{CW}\\H.Misc.toc")
    allf = set(fB) | set(fH)
    wep_types = [p for p in allf if p.startswith("/Lotus/Weapons/") and "." not in p.rsplit("/",1)[-1]]
    print(f"B+H.Misc: {len(wep_types)} no-ext /Lotus/Weapons entries; sample: {sorted(wep_types)[:5]}")
